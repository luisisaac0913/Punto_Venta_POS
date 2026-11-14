using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace Punto_de_Venta.Servicios
{
    public static class LicenciaService
    {
        private static readonly string AzureFunctionVerificarBaseUrl = "https://chipandchapa-d5bsach3emeka6h0.mexicocentral-01.azurewebsites.net/api/verificar/";
        private static readonly HttpClient _httpClient = CrearHttpClientSeguro(12);

        private static HttpClient CrearHttpClientSeguro(int timeoutSegundos)
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                Proxy = null,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
                }
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSegundos) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) POS/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        }

        private static HttpClient CrearHttpClientFallback(int timeoutSegundos)
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                Proxy = null,
                ServerCertificateCustomValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSegundos) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) POS/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        }
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
        private static readonly string SALT = "POS_Premium_Vitalicio_SecureSalt_2026_v2";
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

        public static void DesactivarLicencia()
        {
            LimpiarLicenciaLegacy();
            try
            {
                if (File.Exists(LicenciaPath))
                {
                    File.Delete(LicenciaPath);
                }
            }
            catch
            {
            }
            _isPremiumCached = false;
        }

        public static bool InterpretarRespuestaPagado(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;

            try
            {
                var data = JObject.Parse(json);
                if (data["pagado"] != null)
                {
                    if (data["pagado"]!.Type == JTokenType.Boolean)
                    {
                        return data["pagado"]!.Value<bool>();
                    }
                    if (data["pagado"]!.Type == JTokenType.String)
                    {
                        string val = data["pagado"]!.Value<string>()?.Trim() ?? string.Empty;
                        return bool.TryParse(val, out bool b) ? b : string.Equals(val, "pagado", StringComparison.OrdinalIgnoreCase) || string.Equals(val, "approved", StringComparison.OrdinalIgnoreCase);
                    }
                    return data["pagado"]?.Value<bool>() ?? false;
                }

                if (data["estatus"] != null || data["status"] != null)
                {
                    string? st = (data["estatus"] ?? data["status"])?.ToString();
                    return string.Equals(st, "pagado", StringComparison.OrdinalIgnoreCase) || string.Equals(st, "approved", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
            }

            string jsonLimpio = json.ToLowerInvariant();
            return jsonLimpio.Contains("\"pagado\": true") ||
                   jsonLimpio.Contains("\"pagado\":true") ||
                   jsonLimpio.Contains("\"pagado\":\"true\"") ||
                   jsonLimpio.Contains("\"pagado\": \"true\"") ||
                   jsonLimpio.Contains("\"estatus\":\"pagado\"") ||
                   jsonLimpio.Contains("\"estatus\": \"pagado\"") ||
                   jsonLimpio.Contains("\"status\":\"approved\"") ||
                   jsonLimpio.Contains("\"status\": \"approved\"");
        }

        public static async Task<bool?> VerificarEnNubeAsync()
        {
            string hwid = ObtenerIdentificadorHardware();
            if (string.IsNullOrWhiteSpace(hwid)) return null;

            string urlVerificacion = AzureFunctionVerificarBaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(hwid);
            string json = string.Empty;

            try
            {
                using var response = await _httpClient.GetAsync(urlVerificacion).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    DesactivarLicencia();
                    return false;
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using var fallbackClient = CrearHttpClientFallback(12);
                    using var response = await fallbackClient.GetAsync(urlVerificacion).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        DesactivarLicencia();
                        return false;
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    json = await EjecutarGetConCurlAsync(urlVerificacion).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(json))
            {
                bool jsonValido = false;
                try
                {
                    JObject.Parse(json);
                    jsonValido = true;
                }
                catch
                {
                    jsonValido = false;
                }

                if (!jsonValido)
                {
                    return null;
                }

                bool pagado = InterpretarRespuestaPagado(json);
                if (pagado)
                {
                    if (!ValidarLicencia())
                    {
                        ActivarLicencia();
                    }
                    return true;
                }
                else
                {
                    DesactivarLicencia();
                    return false;
                }
            }

            return null;
        }

        private static async Task<string> EjecutarGetConCurlAsync(string url)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "curl.exe",
                    Arguments = $"-s -f --connect-timeout 8 --max-time 15 -k -H \"Accept: application/json\" \"{url}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return string.Empty;
                string output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await proc.WaitForExitAsync().ConfigureAwait(false);
                return proc.ExitCode == 0 ? output : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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