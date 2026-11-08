using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Punto_de_Venta.Servicios
{
    public static class LicenciaService
    {
        private static readonly string LicenciaPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POS_DataStorage",
            "licencia_premium.key"
        );
        private static readonly string LegacyLicenciaPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POS_DataStorage",
            "licencia.key"
        );
        private const string SALT = "POS_Premium_Vitalicio_SecureSalt_2026_v2";
        private static string? _cachedHardwareId;
        private static bool? _isPremiumCached;

        private static readonly HashSet<string> InvalidPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "",
            "Default string",
            "To be filled by O.E.M.",
            "To Be Filled By O.E.M.",
            "None",
            "System Serial Number",
            "Base Board Serial Number",
            "Not Applicable",
            "Not Specified",
            "00000000-0000-0000-0000-000000000000",
            "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"
        };

        public static void InvalidarCache()
        {
            _isPremiumCached = null;
        }

        private static bool EsValorHardwareValido(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return false;
            string limpio = valor.Trim();
            if (InvalidPlaceholders.Contains(limpio)) return false;

            bool soloCerosOGuiones = true;
            bool soloEfesOGuiones = true;
            foreach (char c in limpio)
            {
                if (c != '0' && c != '-') soloCerosOGuiones = false;
                if (c != 'F' && c != 'f' && c != '-') soloEfesOGuiones = false;
            }
            if (soloCerosOGuiones || soloEfesOGuiones) return false;

            return true;
        }

        private static string ObtenerMachineGuidRegistro()
        {
            try
            {
                using var baseKey64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var cryptoKey64 = baseKey64.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                string? guid64 = cryptoKey64?.GetValue("MachineGuid")?.ToString()?.Trim();
                if (EsValorHardwareValido(guid64)) return guid64!;
            }
            catch { }

            try
            {
                using var baseKeyDefault = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
                using var cryptoKeyDefault = baseKeyDefault.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                string? guidDefault = cryptoKeyDefault?.GetValue("MachineGuid")?.ToString()?.Trim();
                if (EsValorHardwareValido(guidDefault)) return guidDefault!;
            }
            catch { }

            return string.Empty;
        }

        public static string ObtenerIdentificadorHardware()
        {
            if (!string.IsNullOrEmpty(_cachedHardwareId))
            {
                return _cachedHardwareId;
            }

            string machineGuid = ObtenerMachineGuidRegistro();
            string systemUuid = string.Empty;
            string motherboardId = string.Empty;
            string cpuId = string.Empty;

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string? rawUuid = obj["UUID"]?.ToString()?.Trim();
                    if (EsValorHardwareValido(rawUuid))
                    {
                        systemUuid = rawUuid!;
                        break;
                    }
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string? rawSerial = obj["SerialNumber"]?.ToString()?.Trim();
                    if (EsValorHardwareValido(rawSerial))
                    {
                        motherboardId = rawSerial!;
                        break;
                    }
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string? rawCpu = obj["ProcessorId"]?.ToString()?.Trim();
                    if (EsValorHardwareValido(rawCpu))
                    {
                        cpuId = rawCpu!;
                        break;
                    }
                }
            }
            catch { }

            var partes = new List<string>();
            if (!string.IsNullOrEmpty(machineGuid)) partes.Add($"MG:{machineGuid}");
            if (!string.IsNullOrEmpty(systemUuid)) partes.Add($"UUID:{systemUuid}");
            if (!string.IsNullOrEmpty(motherboardId)) partes.Add($"MB:{motherboardId}");
            if (!string.IsNullOrEmpty(cpuId)) partes.Add($"CPU:{cpuId}");

            if (partes.Count == 0)
            {
                partes.Add($"HOST:{Environment.MachineName}");
            }

            string semillaHardware = string.Join("|", partes);
            using SHA256 sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(semillaHardware));
            StringBuilder hex = new StringBuilder(32);
            for (int i = 0; i < 8; i++)
            {
                hex.Append(bytes[i].ToString("X2"));
            }

            string h = hex.ToString();
            _cachedHardwareId = $"POS-{h.Substring(0, 4)}-{h.Substring(4, 4)}-{h.Substring(8, 4)}-{h.Substring(12, 4)}";
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

        private static void LimpiarLicenciaLegacy()
        {
            try
            {
                if (File.Exists(LegacyLicenciaPath))
                {
                    File.Delete(LegacyLicenciaPath);
                }
            }
            catch { }
        }

        public static bool LicenciaExiste()
        {
            LimpiarLicenciaLegacy();
            return File.Exists(LicenciaPath);
        }

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
            LimpiarLicenciaLegacy();
            string hwId = ObtenerIdentificadorHardware();
            string hash = GenerarHash(hwId);
            Directory.CreateDirectory(Path.GetDirectoryName(LicenciaPath)!);
            File.WriteAllText(LicenciaPath, hash);
            _isPremiumCached = true;
        }

        public static bool ValidarLicencia()
        {
            LimpiarLicenciaLegacy();
            if (!File.Exists(LicenciaPath))
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