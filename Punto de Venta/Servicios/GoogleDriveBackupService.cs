using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Punto_de_Venta.Servicios
{
    public static class GoogleDriveBackupService
    {
        private const string ApplicationName = "ChipAndChapaPOS";
        private const string BackupFolderName = "Respaldos Chip&Chapa POS";
        private static readonly string[] Scopes = { DriveService.Scope.DriveFile };

        /// <summary>
        /// Comprime la carpeta de datos local organizando los archivos en subcarpetas
        /// y sube el .zip mediante ResumableUpload a Google Drive.
        /// En modo silencioso (segundo plano), nunca lanza ventanas interactivas de navegador.
        /// </summary>
        public static async Task<bool> RespaldarEnDriveAsync(IProgress<int>? progreso, bool modoSilencioso = false)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string sourceFolder = Path.Combine(appData, "POS_DataStorage");

            if (!Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException($"No se encontro la carpeta de origen: {sourceFolder}");
            }

            string tempZipPath = Path.Combine(Path.GetTempPath(), $"POS_Backup_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.zip");

            try
            {
                try
                {
                    using var conn = Punto_de_Venta.Datos.DatabaseHelper.GetConnection();
                    using var walCmd = conn.CreateCommand();
                    walCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    walCmd.ExecuteNonQuery();
                }
                catch { }

                // 1. Compresion usando System.IO.Compression.ZipArchive en tarea separada
                await Task.Run(() =>
                {
                    if (File.Exists(tempZipPath))
                    {
                        File.Delete(tempZipPath);
                    }

                    var dirInfo = new DirectoryInfo(sourceFolder);
                    using var zipArchive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create);

                    foreach (var fileInfo in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        string relativePath = fileInfo.FullName.Length > sourceFolder.Length
                            ? fileInfo.FullName.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            : fileInfo.Name;

                        // Omitir respaldos previos, tokens de sesion y archivos temporales
                        if (relativePath.StartsWith("Backups", StringComparison.OrdinalIgnoreCase) ||
                            relativePath.StartsWith("GoogleDriveTokenStore", StringComparison.OrdinalIgnoreCase) ||
                            relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try
                        {
                            string entryName = ObtenerRutaEntradaZip(sourceFolder, fileInfo);
                            using var sourceStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            var entry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);
                            using var entryStream = entry.Open();
                            sourceStream.CopyTo(entryStream);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[GoogleDriveBackup] Archivo omitido por bloqueo: {fileInfo.FullName} ({ex.Message})");
                        }
                    }
                });

                var zipFileInfo = new FileInfo(tempZipPath);
                if (!zipFileInfo.Exists || zipFileInfo.Length == 0)
                {
                    throw new InvalidOperationException("No se pudo generar el archivo de respaldo comprimido.");
                }

                progreso?.Report(5);

                // 2. Autenticacion OAuth2 con almacenamiento de sesion protegido mediante DPAPI
                string tokenStorePath = Path.Combine(sourceFolder, "GoogleDriveTokenStore_Secure");
                var dpapiStore = new DpapiDataStore(tokenStorePath);

                if (modoSilencioso)
                {
                    // En ejecucion silenciosa en segundo plano, validar que exista un token previo descifrable.
                    // Si no existe, abortar sin abrir ventanas de navegador en medio de la operacion del cajero.
                    if (!dpapiStore.TieneTokenValido("user"))
                    {
                        System.Diagnostics.Debug.WriteLine("[GoogleDriveBackup] Modo silencioso: Sin token previo descifrable. Respaldo omitido.");
                        return false;
                    }
                }

                UserCredential credential;
                var assembly = Assembly.GetExecutingAssembly();
                using (Stream? stream = assembly.GetManifestResourceStream("Punto_de_Venta.credentials.json"))
                {
                    if (stream == null)
                    {
                        throw new FileNotFoundException("No se encontro 'credentials.json' incrustado en el proyecto.");
                    }

                    // En modo silencioso, usar timeout de 15 segundos para no colgar el hilo si requiere login
                    using var authCts = modoSilencioso ? new CancellationTokenSource(TimeSpan.FromSeconds(15)) : null;
                    var tokenCancel = authCts?.Token ?? CancellationToken.None;

                    try
                    {
                        credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                            GoogleClientSecrets.FromStream(stream).Secrets,
                            Scopes,
                            "user",
                            tokenCancel,
                            dpapiStore
                        );
                    }
                    catch (OperationCanceledException) when (modoSilencioso)
                    {
                        System.Diagnostics.Debug.WriteLine("[GoogleDriveBackup] Modo silencioso: La autorizacion requeria interaccion de usuario. Omitido de forma segura.");
                        return false;
                    }
                }

                // 3. Inicializacion del cliente de Google Drive v3
                var driveService = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName
                });
                string? folderId = await ObtenerOCrearCarpetaDriveAsync(driveService, BackupFolderName);

                // 5. Metadatos del archivo a subir
                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = $"Respaldo_POS_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip",
                    MimeType = "application/zip",
                    Description = "Copia de seguridad automatica de Chip&Chapa POS"
                };

                if (!string.IsNullOrEmpty(folderId))
                {
                    fileMetadata.Parents = new List<string> { folderId };
                }

                // 6. Carga reanudable con reporte de progreso
                using var uploadStream = new FileStream(tempZipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long totalBytes = uploadStream.Length;

                var uploadRequest = driveService.Files.Create(fileMetadata, uploadStream, "application/zip");
                uploadRequest.Fields = "id, name, size";

                uploadRequest.ProgressChanged += (IUploadProgress progress) =>
                {
                    switch (progress.Status)
                    {
                        case UploadStatus.Starting:
                            progreso?.Report(5);
                            break;

                        case UploadStatus.Uploading:
                            if (totalBytes > 0)
                            {
                                int porcentaje = (int)((progress.BytesSent * 100) / totalBytes);
                                progreso?.Report(Math.Clamp(porcentaje, 5, 99));
                            }
                            break;

                        case UploadStatus.Completed:
                            progreso?.Report(100);
                            break;

                        case UploadStatus.Failed:
                            break;
                    }
                };

                var uploadResult = await uploadRequest.UploadAsync();
                if (uploadResult.Status == UploadStatus.Failed)
                {
                    throw uploadResult.Exception ?? new Exception("La subida a Google Drive no se completo.");
                }

                bool completado = uploadResult.Status == UploadStatus.Completed;
                if (completado)
                {
                    RegistrarRespaldoExitoso();
                }

                return completado;
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    try
                    {
                        File.Delete(tempZipPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GoogleDriveBackup] No se pudo eliminar el ZIP temporal: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Comprueba si existe la sesion guardada y si DPAPI puede descifrar el token en esta PC.
        /// </summary>
        public static bool UsuarioEstaAutenticado()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string tokenStorePath = Path.Combine(appData, "POS_DataStorage", "GoogleDriveTokenStore_Secure");

                if (!Directory.Exists(tokenStorePath))
                {
                    return false;
                }

                var store = new DpapiDataStore(tokenStorePath);
                return store.TieneTokenValido("user");
            }
            catch
            {
                return false;
            }
        }

        public static void RegistrarRespaldoExitoso()
        {
            try
            {
                var config = AppConfig.Load();
                config.UltimoRespaldoDriveFecha = DateTime.Now;
                config.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GoogleDriveBackup] Error guardando fecha de respaldo: {ex.Message}");
            }
        }

        public static DateTime? ObtenerUltimaFechaRespaldo()
        {
            try
            {
                var config = AppConfig.Load();
                return config.UltimoRespaldoDriveFecha;
            }
            catch
            {
                return null;
            }
        }

        public static string ObtenerTextoUltimoRespaldo()
        {
            var fecha = ObtenerUltimaFechaRespaldo();
            if (!fecha.HasValue)
            {
                return "Ultimo respaldo: Sin respaldos registrados";
            }

            var transcurrido = DateTime.Now - fecha.Value;
            if (transcurrido.TotalSeconds < 0 || transcurrido.TotalMinutes < 1)
            {
                return "Ultimo respaldo hace un momento";
            }
            if (transcurrido.TotalMinutes < 60)
            {
                int minutos = Math.Max(1, (int)transcurrido.TotalMinutes);
                return minutos == 1 ? "Ultimo respaldo hace 1 minuto" : $"Ultimo respaldo hace {minutos} minutos";
            }
            if (transcurrido.TotalHours < 24)
            {
                int horas = (int)transcurrido.TotalHours;
                return horas == 1 ? "Ultimo respaldo hace 1 hora" : $"Ultimo respaldo hace {horas} horas";
            }
            if (transcurrido.TotalDays < 30)
            {
                int dias = (int)transcurrido.TotalDays;
                return dias == 1 ? "Ultimo respaldo hace 1 dia" : $"Ultimo respaldo hace {dias} dias";
            }

            return $"Ultimo respaldo: {fecha.Value:dd/MM/yyyy HH:mm}";
        }

        private static string ObtenerRutaEntradaZip(string sourceFolder, FileInfo fileInfo)
        {
            string relative = fileInfo.FullName.Length > sourceFolder.Length
                ? fileInfo.FullName.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fileInfo.Name;

            string normalized = relative.Replace('\\', '/');
            if (normalized.StartsWith("Tickets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Tickets", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            string ext = fileInfo.Extension.ToLowerInvariant();
            string name = fileInfo.Name;

            if (name.StartsWith("Ticket_", StringComparison.OrdinalIgnoreCase) && ext == ".txt")
            {
                return $"Tickets/{name}";
            }

            if (ext == ".db" || ext == ".db-wal" || ext == ".db-shm")
            {
                return $"BaseDatos/{name}";
            }

            if (ext == ".json" || ext == ".key")
            {
                return $"Configuracion/{name}";
            }

            if (ext == ".log" || (ext == ".txt" && name.Contains("log", StringComparison.OrdinalIgnoreCase)))
            {
                return $"Logs/{name}";
            }

            return $"Otros/{name}";
        }

        private static async Task<string?> ObtenerOCrearCarpetaDriveAsync(DriveService service, string nombreCarpeta)
        {
            try
            {
                var listRequest = service.Files.List();
                listRequest.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{nombreCarpeta}' and trashed = false";
                listRequest.Spaces = "drive";
                listRequest.Fields = "files(id, name)";
                var result = await listRequest.ExecuteAsync();

                if (result.Files != null && result.Files.Count > 0)
                {
                    return result.Files[0].Id;
                }

                var folderMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = nombreCarpeta,
                    MimeType = "application/vnd.google-apps.folder",
                    Description = "Carpeta de respaldos de Chip&Chapa POS"
                };

                var createRequest = service.Files.Create(folderMetadata);
                createRequest.Fields = "id";
                var folder = await createRequest.ExecuteAsync();
                return folder.Id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GoogleDriveBackup] No se pudo crear o localizar la carpeta '{nombreCarpeta}': {ex.Message}");
                return null;
            }
        }
    }
}