using Google.Apis.Util.Store;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Punto_de_Venta.Servicios
{
    public class DpapiDataStore : IDataStore
    {
        private readonly string _folderPath;

        public DpapiDataStore(string folderPath)
        {
            _folderPath = folderPath;
            if (!Directory.Exists(_folderPath))
            {
                Directory.CreateDirectory(_folderPath);
            }
        }

        public Task ClearAsync()
        {
            if (Directory.Exists(_folderPath))
            {
                var files = Directory.GetFiles(_folderPath);
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("La clave no puede estar vacia", nameof(key));
            var filePath = GetFilePath(key);
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch { }
            }
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("La clave no puede estar vacia", nameof(key));
            var filePath = GetFilePath(key);

            if (File.Exists(filePath))
            {
                try
                {
                    var encryptedBytes = File.ReadAllBytes(filePath);
                    if (encryptedBytes.Length > 0)
                    {
                        var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                        var json = Encoding.UTF8.GetString(decryptedBytes);
                        var value = JsonConvert.DeserializeObject<T>(json);

                        return Task.FromResult(value);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DPAPI] Error al descifrar el token: {ex.Message}");
                }
            }
            return Task.FromResult(default(T));
        }

        public Task StoreAsync<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("La clave no puede estar vacia", nameof(key));
            var filePath = GetFilePath(key);

            var json = JsonConvert.SerializeObject(value);
            var plainBytes = Encoding.UTF8.GetBytes(json);

            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            // Escritura atomica para proteger el archivo ante cortes de energia repentinos
            string tempPath = filePath + ".tmp";
            File.WriteAllBytes(tempPath, encryptedBytes);
            File.Move(tempPath, filePath, true);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Comprueba si existe un token almacenado y si DPAPI puede descifrarlo exitosamente
        /// sin lanzar excepciones al hilo principal.
        /// </summary>
        public bool TieneTokenValido(string key)
        {
            try
            {
                var filePath = GetFilePath(key);
                if (!File.Exists(filePath)) return false;

                var encryptedBytes = File.ReadAllBytes(filePath);
                if (encryptedBytes.Length == 0) return false;

                var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                if (decryptedBytes == null || decryptedBytes.Length == 0) return false;

                var json = Encoding.UTF8.GetString(decryptedBytes);
                return !string.IsNullOrWhiteSpace(json);
            }
            catch
            {
                return false;
            }
        }

        private string GetFilePath(string key)
        {
            return Path.Combine(_folderPath, key + ".secure");
        }
    }
}