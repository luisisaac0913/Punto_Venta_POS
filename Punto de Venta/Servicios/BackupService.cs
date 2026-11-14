using Microsoft.Data.Sqlite;
using Punto_de_Venta.Datos;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Punto_de_Venta.Servicios
{
    public static class BackupService
    {
        public class RespaldoItem
        {
            public string Nombre { get; set; } = string.Empty;
            public string Origen { get; set; } = "Local";
            public DateTime Fecha { get; set; }
            public string RutaLocal { get; set; } = string.Empty;
            public string DriveFileId { get; set; } = string.Empty;
            public long TamanoBytes { get; set; }
            public string DisplayTexto => $"[{Origen}] {Fecha:dd/MM/yyyy HH:mm:ss} - {Nombre} ({TamanoBytes / 1024.0:0.0} KB)";
            public override string ToString() => DisplayTexto;
        }

        public class ResumenDiferenciasBackup
        {
            public int ProductosActuales { get; set; }
            public int ProductosEnRespaldo { get; set; }
            public int ProductosAEliminar { get; set; }
            public int ProductosAAgregar { get; set; }
        }

        public static string AppDataFolder => DatabaseHelper.DbFolder;

        public static string ObtenerCarpetaRespaldos(string? rutaPersonalizada = null)
        {
            if (string.IsNullOrWhiteSpace(rutaPersonalizada))
            {
                try
                {
                    var config = AppConfig.Load();
                    rutaPersonalizada = config.RutaRespaldos;
                }
                catch (Exception ex)
                {
                    PosLogger.Warn("BackupService.ObtenerCarpetaRespaldos", ex.Message);
                    rutaPersonalizada = null;
                }
            }
            if (!string.IsNullOrWhiteSpace(rutaPersonalizada))
            {
                try
                {
                    if (!Directory.Exists(rutaPersonalizada))
                    {
                        Directory.CreateDirectory(rutaPersonalizada);
                    }
                    return rutaPersonalizada;
                }
                catch (Exception ex)
                {
                    PosLogger.Warn("BackupService.CrearCarpetaPersonalizada", $"No se pudo usar '{rutaPersonalizada}': {ex.Message}");
                }
            }

            return Path.Combine(AppDataFolder, "Backups");
        }

        public static bool GenerarRespaldo(string prefijo = "Auto", string? rutaPersonalizada = null)
        {
            string tempDbSnapshot = Path.Combine(Path.GetTempPath(), $"pos_snapshot_{Guid.NewGuid():N}.db");
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    return false;
                }
                var destinoCarpeta = ObtenerCarpetaRespaldos(rutaPersonalizada);
                Directory.CreateDirectory(destinoCarpeta);
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                var zipName = $"Backup_{prefijo}_{timestamp}.zip";
                var destinationZip = Path.Combine(destinoCarpeta, zipName);
                if (File.Exists(destinationZip))
                {
                    zipName = $"Backup_{prefijo}_{timestamp}_{Guid.NewGuid().ToString("N")[..4]}.zip";
                    destinationZip = Path.Combine(destinoCarpeta, zipName);
                }

                string dbPath = DatabaseHelper.DbPath;
                bool tieneDbSnapshot = false;
                if (File.Exists(dbPath))
                {
                    using (var sourceConn = DatabaseHelper.GetConnection())
                    using (var destConn = new SqliteConnection($"Data Source={tempDbSnapshot}"))
                    {
                        destConn.Open();
                        sourceConn.BackupDatabase(destConn);
                    }
                    using (var clearConn = new SqliteConnection($"Data Source={tempDbSnapshot}"))
                    {
                        SqliteConnection.ClearPool(clearConn);
                    }
                    tieneDbSnapshot = File.Exists(tempDbSnapshot);
                }

                var extensionesExtra = new[] { ".txt", ".json" };
                var archivos = Directory.GetFiles(AppDataFolder, "*", SearchOption.AllDirectories)
                    .Where(file =>
                    {
                        string rel = file.Length > AppDataFolder.Length
                            ? file.Substring(AppDataFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            : Path.GetFileName(file);
                        if (rel.StartsWith("Backups", StringComparison.OrdinalIgnoreCase) ||
                            rel.StartsWith("GoogleDriveTokenStore", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        return extensionesExtra.Contains(Path.GetExtension(file).ToLowerInvariant());
                    })
                    .ToList();

                if (!tieneDbSnapshot && archivos.Count == 0)
                {
                    return false;
                }

                using (var zipArchive = ZipFile.Open(destinationZip, ZipArchiveMode.Create))
                {
                    if (tieneDbSnapshot)
                    {
                        string dbEntryName = Path.GetRelativePath(AppDataFolder, dbPath).Replace('\\', '/');
                        if (dbEntryName.StartsWith("..", StringComparison.Ordinal))
                        {
                            dbEntryName = Path.GetFileName(dbPath);
                        }
                        using var dbStream = new FileStream(tempDbSnapshot, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var dbEntry = zipArchive.CreateEntry(dbEntryName, CompressionLevel.Optimal);
                        using var entryStream = dbEntry.Open();
                        dbStream.CopyTo(entryStream);
                    }

                    foreach (var rutaArchivo in archivos)
                    {
                        var nombreArchivo = Path.GetRelativePath(AppDataFolder, rutaArchivo).Replace('\\', '/');
                        using var fileStream = new FileStream(rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var entry = zipArchive.CreateEntry(nombreArchivo, CompressionLevel.Optimal);
                        using var entryStream = entry.Open();
                        fileStream.CopyTo(entryStream);
                    }
                }
                LimpiarRespaldosAntiguos(destinoCarpeta);
                return true;
            }
            catch (Exception ex)
            {
                PosLogger.Error("BackupService.GenerarRespaldo", ex, $"Prefijo={prefijo}");
                System.Diagnostics.Debug.WriteLine($"[BackupService] Error al generar respaldo: {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempDbSnapshot)) File.Delete(tempDbSnapshot);
                }
                catch (Exception exDel)
                {
                    PosLogger.Warn("BackupService.GenerarRespaldo.Cleanup", exDel.Message);
                }
            }
        }
        public static void LimpiarRespaldosAntiguos(string carpeta)
        {
            try
            {
                if (!Directory.Exists(carpeta))
                {
                    return;
                }

                int maxRespaldos = 5;
                try
                {
                    var config = AppConfig.Load();
                    if (config.MaxRespaldosLocales > 0)
                    {
                        maxRespaldos = config.MaxRespaldosLocales;
                    }
                }
                catch (Exception exCfg)
                {
                    PosLogger.Warn("BackupService.LimpiarRespaldosAntiguos.Config", exCfg.Message);
                }

                var archivosZip = Directory.GetFiles(carpeta, "Backup_*.zip")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                if (archivosZip.Count > maxRespaldos)
                {
                    var aEliminar = archivosZip.Skip(maxRespaldos);
                    foreach (var archivo in aEliminar)
                    {
                        try
                        {
                            archivo.Delete();
                        }
                        catch (Exception exFile)
                        {
                            PosLogger.Warn("BackupService.LimpiarRespaldosAntiguos.Delete", $"{archivo.FullName}: {exFile.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PosLogger.Error("BackupService.LimpiarRespaldosAntiguos", ex);
            }
        }

        public static List<RespaldoItem> ListarRespaldosLocales(string? rutaPersonalizada = null)
        {
            var resultado = new List<RespaldoItem>();
            var carpetas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ObtenerCarpetaRespaldos(rutaPersonalizada),
                Path.Combine(AppDataFolder, "Backups")
            };

            foreach (string carpeta in carpetas)
            {
                if (!Directory.Exists(carpeta)) continue;

                foreach (string file in Directory.GetFiles(carpeta, "*.zip"))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        resultado.Add(new RespaldoItem
                        {
                            Nombre = info.Name,
                            Origen = "Local",
                            Fecha = info.LastWriteTime,
                            RutaLocal = info.FullName,
                            TamanoBytes = info.Length
                        });
                    }
                    catch { }
                }
            }

            return resultado
                .GroupBy(r => r.RutaLocal, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(r => r.Fecha)
                .ToList();
        }

        public static ResumenDiferenciasBackup AnalizarDiferenciasZip(string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                throw new FileNotFoundException("El archivo de respaldo seleccionado no existe.", zipPath);
            }

            string tempDb = Path.Combine(Path.GetTempPath(), $"pos_diff_{Guid.NewGuid():N}.db");
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var dbEntry = archive.Entries
                        .Where(e => !string.IsNullOrEmpty(e.Name) && e.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(e => e.Name.Equals("pos_data.db", StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault();

                    if (dbEntry == null)
                    {
                        throw new InvalidDataException("El respaldo ZIP no contiene una base de datos SQLite (.db) valida.");
                    }

                    dbEntry.ExtractToFile(tempDb, overwrite: true);
                }

                var actuales = LeerClavesProductos(DatabaseHelper.DbPath);
                var respaldo = LeerClavesProductos(tempDb);

                int aEliminar = actuales.Count(c => !respaldo.Contains(c));
                int aAgregar = respaldo.Count(c => !actuales.Contains(c));

                return new ResumenDiferenciasBackup
                {
                    ProductosActuales = actuales.Count,
                    ProductosEnRespaldo = respaldo.Count,
                    ProductosAEliminar = aEliminar,
                    ProductosAAgregar = aAgregar
                };
            }
            finally
            {
                using (var clearConn = new SqliteConnection($"Data Source={tempDb}"))
                {
                    SqliteConnection.ClearPool(clearConn);
                }
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch (Exception exDel) { PosLogger.Warn("BackupService.AnalizarDiferenciasZip.Cleanup", exDel.Message); }
            }
        }

        private static HashSet<string> LeerClavesProductos(string dbPath)
        {
            var claves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dbPath)) return claves;

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, COALESCE(Codigo, '') FROM Productos;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                string codigo = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                claves.Add(string.IsNullOrEmpty(codigo) ? $"ID:{id}" : codigo);
            }
            return claves;
        }

        public static void RestaurarDesdeZip(string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                throw new FileNotFoundException("El archivo de respaldo seleccionado no existe.", zipPath);
            }

            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"pos_restore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempExtractDir);
            bool servidorEstabaActivo = App.EsServidorLocalActivo();

            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    string fullExtractRoot = Path.GetFullPath(tempExtractDir);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string destPath = Path.GetFullPath(Path.Combine(tempExtractDir, entry.FullName));
                        if (!destPath.StartsWith(fullExtractRoot, StringComparison.OrdinalIgnoreCase)) continue;
                        string? dir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                }

                string? dbExtraida = Directory.GetFiles(tempExtractDir, "*.db", SearchOption.AllDirectories)
                    .OrderByDescending(f => Path.GetFileName(f).Equals("pos_data.db", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(dbExtraida) || !File.Exists(dbExtraida))
                {
                    throw new InvalidDataException("El respaldo ZIP no contiene una base de datos SQLite (.db) valida.");
                }

                using (var testConn = new SqliteConnection($"Data Source={dbExtraida};Mode=ReadOnly"))
                {
                    testConn.Open();
                    using var cmd = testConn.CreateCommand();
                    cmd.CommandText = "PRAGMA integrity_check;";
                    string? check = cmd.ExecuteScalar()?.ToString();
                    if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"La base de datos del respaldo fallo la verificacion de integridad: {check}");
                    }
                }
                using (var clearTestConn = new SqliteConnection($"Data Source={dbExtraida};Mode=ReadOnly"))
                {
                    SqliteConnection.ClearPool(clearTestConn);
                }

                GenerarRespaldo("PreRestauracion");

                if (servidorEstabaActivo)
                {
                    try
                    {
                        App.DetenerServidorLocalAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception exSrv)
                    {
                        PosLogger.Warn("BackupService.RestaurarDesdeZip.DetenerServidor", exSrv.Message);
                    }
                }

                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                string dbDestino = DatabaseHelper.DbPath;
                string? dbDestinoDir = Path.GetDirectoryName(dbDestino);
                if (!string.IsNullOrEmpty(dbDestinoDir)) Directory.CreateDirectory(dbDestinoDir);

                foreach (string suffix in new[] { "-wal", "-shm" })
                {
                    string walPath = dbDestino + suffix;
                    if (File.Exists(walPath))
                    {
                        try { File.Delete(walPath); }
                        catch (Exception exWal) { PosLogger.Warn("BackupService.RestaurarDesdeZip.DeleteWal", $"{walPath}: {exWal.Message}"); }
                    }
                }

                File.Copy(dbExtraida, dbDestino, overwrite: true);

                foreach (string suffix in new[] { "-wal", "-shm" })
                {
                    string walPath = dbDestino + suffix;
                    if (File.Exists(walPath))
                    {
                        try { File.Delete(walPath); }
                        catch (Exception exWal) { PosLogger.Warn("BackupService.RestaurarDesdeZip.DeleteWalPost", $"{walPath}: {exWal.Message}"); }
                    }
                }

                string fullAppDataRoot = Path.GetFullPath(AppDataFolder);
                foreach (string archivo in Directory.GetFiles(tempExtractDir, "*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(archivo).ToLowerInvariant();
                    if (ext != ".json" && ext != ".txt") continue;
                    string nombre = Path.GetFileName(archivo);
                    if (nombre.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                        nombre.Equals("credentials.json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relPath = Path.GetRelativePath(tempExtractDir, archivo);
                    string normalizedRel = relPath.Replace('\\', '/');
                    if (normalizedRel.StartsWith("Otros/", StringComparison.OrdinalIgnoreCase))
                    {
                        relPath = relPath.Substring("Otros".Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }

                    string destinoExtra = Path.GetFullPath(Path.Combine(AppDataFolder, relPath));
                    if (!destinoExtra.StartsWith(fullAppDataRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        string? destDir = Path.GetDirectoryName(destinoExtra);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                        File.Copy(archivo, destinoExtra, overwrite: true);

                        // Si se restauro config.json en Configuracion/config.json y existe tambien en raiz (o viceversa), sincronizar ambos
                        if (nombre.Equals("config.json", StringComparison.OrdinalIgnoreCase))
                        {
                            string rootConfig = Path.Combine(AppDataFolder, "config.json");
                            string subConfig = Path.Combine(AppDataFolder, "Configuracion", "config.json");
                            if (!string.Equals(destinoExtra, rootConfig, StringComparison.OrdinalIgnoreCase) && File.Exists(rootConfig))
                            {
                                File.Copy(archivo, rootConfig, overwrite: true);
                            }
                            else if (!string.Equals(destinoExtra, subConfig, StringComparison.OrdinalIgnoreCase) && File.Exists(subConfig))
                            {
                                File.Copy(archivo, subConfig, overwrite: true);
                            }
                        }
                    }
                    catch (Exception exExtra)
                    {
                        PosLogger.Warn("BackupService.RestaurarDesdeZip.ExtraFile", $"{destinoExtra}: {exExtra.Message}");
                    }
                }

                AppConfig.InvalidarCache();
                SqliteConnection.ClearAllPools();
                DatabaseHelper.InicializarBaseDeDatos();
            }
            catch (Exception ex)
            {
                PosLogger.Error("BackupService.RestaurarDesdeZip", ex, zipPath);
                throw;
            }
            finally
            {
                if (servidorEstabaActivo || AppConfig.Load(forzarLectura: true).ServidorLocalHabilitado)
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await App.IniciarServidorLocalAsync();
                        }
                        catch (Exception exSrvStart)
                        {
                            PosLogger.Warn("BackupService.RestaurarDesdeZip.ReiniciarServidor", exSrvStart.Message);
                        }
                    });
                }

                try
                {
                    if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, recursive: true);
                }
                catch (Exception exClean)
                {
                    PosLogger.Warn("BackupService.RestaurarDesdeZip.Cleanup", exClean.Message);
                }
            }
        }
    }
}