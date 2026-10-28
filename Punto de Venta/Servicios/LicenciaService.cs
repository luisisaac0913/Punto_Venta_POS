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
        public static string ObtenerIdentificadorHardware()
        {
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
            return hardwareId;
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
        public static void ActivarLicencia()
        {
            string hwId = ObtenerIdentificadorHardware();
            string hash = GenerarHash(hwId);
            Directory.CreateDirectory(Path.GetDirectoryName(LicenciaPath)!);
            File.WriteAllText(LicenciaPath, hash);
        }
        public static bool ValidarLicencia()
        {
            if (!LicenciaExiste()) return false;
            string hashGuardado = File.ReadAllText(LicenciaPath).Trim();
            string hwIdActual = ObtenerIdentificadorHardware();
            string hashActual = GenerarHash(hwIdActual);
            return hashGuardado == hashActual;
        }
    }
}