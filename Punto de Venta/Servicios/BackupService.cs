using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Punto_de_Venta.Servicios
{
    public static class BackupService
    {
        public static string AppDataFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POS_DataStorage"
        );

        public static string ObtenerCarpetaRespaldos(string? rutaPersonalizada = null)
        {
            if (string.IsNullOrWhiteSpace(rutaPersonalizada))
            {
                try
                {
                    var config = AppConfig.Load();
                    rutaPersonalizada = config.RutaRespaldos;
                }
                catch
                {
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
                catch
                {
                }
            }

            return Path.Combine(AppDataFolder, "Backups");
        }

        public static void GenerarRespaldo(string prefijo = "Auto", string? rutaPersonalizada = null)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    return;
                }
                var destinoCarpeta = ObtenerCarpetaRespaldos(rutaPersonalizada);
                Directory.CreateDirectory(destinoCarpeta);
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
                var zipName = $"Backup_{prefijo}_{timestamp}.zip";
                var destinationZip = Path.Combine(destinoCarpeta, zipName);
                if (File.Exists(destinationZip))
                {
                    return;
                }
                try
                {
                    using var conn = Punto_de_Venta.Datos.DatabaseHelper.GetConnection();
                    using var walCmd = conn.CreateCommand();
                    walCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    walCmd.ExecuteNonQuery();
                }
                catch { }

                var extensionesValidas = new[] { ".txt", ".json", ".db", ".db-wal", ".db-shm" };
                var archivos = Directory.GetFiles(AppDataFolder)
                    .Where(file => extensionesValidas.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .ToList();
                var dbLocal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pos_data.db");
                if (File.Exists(dbLocal) && !archivos.Contains(dbLocal))
                {
                    archivos.Add(dbLocal);
                }

                if (archivos.Count == 0)
                {
                    return;
                }
                using (var zipArchive = ZipFile.Open(destinationZip, ZipArchiveMode.Create))
                {
                    foreach (var rutaArchivo in archivos)
                    {
                        var nombreArchivo = Path.GetFileName(rutaArchivo);
                        using var fileStream = new FileStream(rutaArchivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var entry = zipArchive.CreateEntry(nombreArchivo, CompressionLevel.Optimal);
                        using var entryStream = entry.Open();
                        fileStream.CopyTo(entryStream);
                    }
                }
                LimpiarRespaldosAntiguos(destinoCarpeta);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupService] Error al generar respaldo: {ex.Message}");
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
                catch
                {
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
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}