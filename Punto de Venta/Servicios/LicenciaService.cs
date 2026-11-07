using System;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace Punto_de_Venta.Servicios
{
    public static class LicenciaService
    {
        private static readonly string LicenciaPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POS_DataStorage",
            "licencia.key"
        );
        private const string SALT = "POS_DataStorage_SecureKey_2026";
        private static string? _cachedHardwareId;
        private static bool? _isPremiumCached;

        public static void InvalidarCache()
        {
            _isPremiumCached = null;
        }

        public static string ObtenerIdentificadorHardware()
        {
            if (!string.IsNullOrEmpty(_cachedHardwareId))
            {
                return _cachedHardwareId;
            }

            string motherboardId = string.Empty;
            string cpuId = string.Empty;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in searcher.Get())
                {
                    motherboardId = obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                    break;
                }
            }
            catch { }
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    cpuId = obj["ProcessorId"]?.ToString()?.Trim() ?? "";
                    break;
                }
            }
            catch { }

            string hardwareId = $"{motherboardId}-{cpuId}";
            if (hardwareId == "-") hardwareId = Environment.MachineName;

            _cachedHardwareId = hardwareId;
            return _cachedHardwareId;
        }
        private static string GenerarHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input + SALT));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
        public static bool LicenciaExiste() => File.Exists(LicenciaPath);
        public static bool EsPremium()
        {
            if (_isPremiumCached.HasValue)
            {
                return _isPremiumCached.Value;
            }
            return ValidarLicencia();
        }

        public static void MostrarMensajePremium(string caracteristica = "")
        {
            string detalle = string.IsNullOrWhiteSpace(caracteristica) ? "Esta funcion" : $"La funcion '{caracteristica}'";
            var resultado = Punto_de_Venta.Vistas.CustomMessageBox.Show(
                $"{detalle} es exclusiva de la version Premium.\n\nDeseas ver las opciones de activacion de la licencia?",
                "Funcion Premium",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information
            );

            if (resultado == System.Windows.MessageBoxResult.Yes)
            {
                MainWindow.NavegarAActivacion();
            }
        }

        public static void ActivarLicencia()
        {
            string hwId = ObtenerIdentificadorHardware();
            string hash = GenerarHash(hwId);
            Directory.CreateDirectory(Path.GetDirectoryName(LicenciaPath)!);
            File.WriteAllText(LicenciaPath, hash);
            _isPremiumCached = true;
        }
        public static bool ValidarLicencia()
        {
            if (!LicenciaExiste())
            {
                _isPremiumCached = false;
                return false;
            }
            try
            {
                string hashGuardado = File.ReadAllText(LicenciaPath).Trim();
                string hwIdActual = ObtenerIdentificadorHardware();
                string hashActual = GenerarHash(hwIdActual);
                bool esValida = hashGuardado == hashActual;
                _isPremiumCached = esValida;
                return esValida;
            }
            catch
            {
                _isPremiumCached = false;
                return false;
            }
        }
    }
}