using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
        /// </summary>
        public static async Task<bool> RespaldarEnDriveAsync(IProgress<int> progreso)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string sourceFolder = Path.Combine(appData, "POS_DataStorage");

            if (!Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException($"No se encontro la carpeta de origen: {sourceFolder}");
            }

            // Localizar archivo de credenciales de Google
            string credentialsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials.json");
            if (!File.Exists(credentialsPath))
            {
                credentialsPath = Path.Combine(sourceFolder, "credentials.json");
            }

            if (!File.Exists(credentialsPath))
            {
                throw new FileNotFoundException("No se encontro el archivo 'credentials.json'. Colocalo junto al ejecutable o en POS_DataStorage.");
            }

            // Generar ruta de archivo comprimido temporal unico
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"POS_Backup_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.zip");

            try
            {
                // Sincronizar WAL de SQLite antes de empaquetar si existe conexion abierta
                try
                {
                    using var conn = Punto_de_Venta.Datos.DatabaseHelper.GetConnection();
                    using var walCmd = conn.CreateCommand();
                    walCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    walCmd.ExecuteNonQuery();
                }
                catch { }

                // 1. Compresion usando System.IO.Compression.ZipFile
                // Se empaqueta en una tarea separada para no bloquear el hilo de interfaz
                // organizando los tickets, base de datos y configuraciones en carpetas dedicadas.
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

                        // Omitir respaldos previos, archivos temporales y token de sesion
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

                // 2. Autenticacion OAuth2 con almacenamiento de sesion en FileDataStore
                string tokenStorePath = Path.Combine(sourceFolder, "GoogleDriveTokenStore");
                UserCredential credential;

                using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(tokenStorePath, true)
                    );
                }

                // 3. Inicializacion del cliente Drive v3
                var driveService = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName
                });

                // 4. Localizar o crear carpeta dedicada en Google Drive
                string? folderId = await ObtenerOCrearCarpetaDriveAsync(driveService, BackupFolderName);

                // 5. Metadatos del archivo en Google Drive
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

                // 6. Carga reanudable (ResumableUpload) con calculo de progreso en tiempo real
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
                                // Mapeamos de 5% a 99% durante la transferencia de bytes
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

                return uploadResult.Status == UploadStatus.Completed;
            }
            finally
            {
                // Limpieza garantizada del archivo temporal
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
        /// Mapea la ruta interna de cada archivo dentro del archivo ZIP
        /// asegurando separadores estandar '/' y organizacion por carpetas tematicas.
        /// </summary>
        private static string ObtenerRutaEntradaZip(string sourceFolder, FileInfo fileInfo)
        {
            string relative = fileInfo.FullName.Length > sourceFolder.Length
                ? fileInfo.FullName.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fileInfo.Name;

            string normalized = relative.Replace('\\', '/');

            // Si el archivo ya esta dentro de la carpeta Tickets, conservar su jerarquia
            if (normalized.StartsWith("Tickets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Tickets", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            string ext = fileInfo.Extension.ToLowerInvariant();
            string name = fileInfo.Name;

            // Tickets individuales en raiz
            if (name.StartsWith("Ticket_", StringComparison.OrdinalIgnoreCase) && ext == ".txt")
            {
                return $"Tickets/{name}";
            }

            // Archivos de base de datos SQLite
            if (ext == ".db" || ext == ".db-wal" || ext == ".db-shm")
            {
                return $"BaseDatos/{name}";
            }

            // Configuracion y licencias
            if (ext == ".json" || ext == ".key")
            {
                return $"Configuracion/{name}";
            }

            // Archivos de registro y logs
            if (ext == ".log" || (ext == ".txt" && name.Contains("log", StringComparison.OrdinalIgnoreCase)))
            {
                return $"Logs/{name}";
            }

            return $"Otros/{name}";
        }

        /// <summary>
        /// Busca si ya existe la carpeta de respaldos en Google Drive.
        /// Si no existe, la crea y retorna su identificador unico.
        /// </summary>
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
