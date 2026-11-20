using Punto_de_Venta.Datos;
using Punto_de_Venta.Servicios;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Punto_de_Venta
{
    public class ProductoRapidoConfig
    {
        public int ProductoId { get; set; }
        public string NombreProducto { get; set; } = string.Empty;
        public string Tecla { get; set; } = string.Empty;
        public decimal Cantidad { get; set; } = 1m;
    }

    public class AppConfig
    {
        public bool PantallaReposoHabilitada { get; set; } = true;
        public int PantallaReposoMinutos { get; set; } = 5;
        public bool ModoOscuro { get; set; } = false;
        public bool ImprimirTicketAutomatico { get; set; } = false;
        public bool PreguntarAntesDeImprimir { get; set; } = true;
        public string ImpresoraSeleccionada { get; set; } = string.Empty;
        public int AnchoTicketCaracteres { get; set; } = 32;
        public string RutaRespaldos { get; set; } = string.Empty;
        public string ColorAcentoHex { get; set; } = "#007AFF"; 
        public string NombreNegocio { get; set; } = string.Empty;
        public string GiroNegocio { get; set; } = string.Empty;
        public bool SolicitarPinAdmin { get; set; } = false;
        public string DireccionNegocio { get; set; } = string.Empty;
        public string MensajePie { get; set; } = string.Empty;
        public string MensajeWhatsAppRecordatorio { get; set; } = "Hola {CLIENTE}, te recordamos de {NEGOCIO} que tienes un saldo pendiente de {SALDO} por concepto de: {CONCEPTO}. ¡Agradecemos tu pronto pago!";
        public bool BasculaHabilitada { get; set; } = false;
        public string BasculaPuerto { get; set; } = "COM1";
        public int BasculaBaudRate { get; set; } = 9600;
        public int BasculaDataBits { get; set; } = 8;
        public string BasculaParity { get; set; } = "None";
        public string BasculaStopBits { get; set; } = "One";
        public string BasculaNewLine { get; set; } = "\r\n";
        public string BasculaProtocolo { get; set; } = "Torrey / Rhino (Peticion P)"; 
        public bool BasculaModoPrecio { get; set; } = false;
        public bool ContarMonedasIndividualmente { get; set; } = false;
        public DateTime? UltimoRespaldoDriveFecha { get; set; }
        public int MaxRespaldosLocales { get; set; } = 5;
        public int MaxRespaldosDrive { get; set; } = 5;
        public int IntervaloRespaldoMinutos { get; set; } = 60;
        public bool ServidorLocalHabilitado { get; set; } = false;
        public string ModoVisualizacionMetricas { get; set; } = "Graficas";
        public string Idioma { get; set; } = "es";
        public bool GuiaInicioMostrada { get; set; } = false;
        public string GeminiApiKeyCifrada { get; set; } = string.Empty;
        public string GeminiModelo { get; set; } = "gemini-3.5-flash-lite";
        public bool SegundaPantallaHabilitada { get; set; } = false;
        public string SegundaPantallaModo { get; set; } = "CobroCliente";
        public string GeminiProxyUrl { get; set; } = string.Empty;
        public System.Collections.Generic.List<ProductoRapidoConfig> ProductosRapidos { get; set; } = [];

        public string ObtenerGeminiApiKey()
        {
            if (string.IsNullOrWhiteSpace(GeminiApiKeyCifrada)) return string.Empty;
            try
            {
                byte[] cifrado = Convert.FromBase64String(GeminiApiKeyCifrada);
                byte[] plano = ProtectedData.Unprotect(cifrado, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plano);
            }
            catch (Exception ex)
            {
                PosLogger.Error("AppConfig.ObtenerGeminiApiKey", ex);
                return string.Empty;
            }
        }

        public void GuardarGeminiApiKey(string apiKeyPlana)
        {
            if (string.IsNullOrWhiteSpace(apiKeyPlana))
            {
                GeminiApiKeyCifrada = string.Empty;
                return;
            }
            byte[] plano = Encoding.UTF8.GetBytes(apiKeyPlana.Trim());
            byte[] cifrado = ProtectedData.Protect(plano, null, DataProtectionScope.CurrentUser);
            GeminiApiKeyCifrada = Convert.ToBase64String(cifrado);
        }

        public string PinAdminHash { get; set; } = "03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4";

        private static string ConfigPath
        {
            get
            {
                string baseDir = Datos.DatabaseHelper.DbFolder;
                string subConfig = Path.Combine(baseDir, "Configuracion", "config.json");
                if (File.Exists(subConfig)) return subConfig;
                return Path.Combine(baseDir, "config.json");
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        public static bool ExisteArchivoConfiguracion() => File.Exists(ConfigPath);

        private static AppConfig? _cachedConfig;
        private static readonly object _configLock = new();

        public static void InvalidarCache()
        {
            lock (_configLock)
            {
                _cachedConfig = null;
            }
        }

        public static AppConfig Load(bool forzarLectura = false)
        {
            lock (_configLock)
            {
                if (!forzarLectura && _cachedConfig != null)
                {
                    return _cachedConfig;
                }

                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        var json = File.ReadAllText(ConfigPath);
                        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                        if (config != null)
                        {
                            if (string.IsNullOrWhiteSpace(config.NombreNegocio))
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(json);
                                    foreach (var prop in doc.RootElement.EnumerateObject())
                                    {
                                        var propName = prop.Name.ToLowerInvariant();
                                        if (propName == "nombre" || propName == "tienda" || propName == "negocio" || propName == "businessname")
                                        {
                                            var valor = prop.Value.GetString();
                                            if (!string.IsNullOrWhiteSpace(valor))
                                            {
                                                config.NombreNegocio = valor;
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch (Exception exDoc)
                                {
                                    PosLogger.Warn("AppConfig.Load.FallbackNombre", exDoc.Message);
                                }
                            }
                            _cachedConfig = config;
                            return config;
                        }
                    }
                }
                catch (Exception ex)
                {
                    PosLogger.Error("AppConfig.Load", ex);
                    if (_cachedConfig != null) return _cachedConfig;
                    if (File.Exists(ConfigPath)) return new AppConfig();
                }

                _cachedConfig = new AppConfig();
                return _cachedConfig;
            }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this, JsonOptions);
                SafeFileStore.WriteAllLinesAtomic(ConfigPath, new[] { json });
                lock (_configLock)
                {
                    _cachedConfig = this;
                }
            }
            catch (Exception ex)
            {
                PosLogger.Error("AppConfig.Save", ex);
            }
        }

        private const string DefaultPinLegacySha256 = "03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4";
        private const string Pbkdf2Prefix = "PBKDF2$";
        private const int Pbkdf2Iterations = 100_000;

        public bool ValidarPin(string pinIngresado)
        {
            if (string.IsNullOrEmpty(pinIngresado)) return false;

            string hashAlmacenado = string.IsNullOrWhiteSpace(PinAdminHash)
                ? DefaultPinLegacySha256
                : PinAdminHash.Trim();

            if (hashAlmacenado.StartsWith(Pbkdf2Prefix, StringComparison.Ordinal))
            {
                return VerificarPinPbkdf2(pinIngresado, hashAlmacenado);
            }

            // Retrocompatibilidad con hashes SHA-256 sin sal (incluyendo el PIN por defecto "1234")
            string hashLegacyCalculado = CalcularSha256(pinIngresado);
            byte[] bytesEsperados = Encoding.UTF8.GetBytes(hashAlmacenado.ToLowerInvariant());
            byte[] bytesCalculados = Encoding.UTF8.GetBytes(hashLegacyCalculado.ToLowerInvariant());

            if (bytesEsperados.Length == bytesCalculados.Length &&
                CryptographicOperations.FixedTimeEquals(bytesEsperados, bytesCalculados))
            {
                try
                {
                    PinAdminHash = GenerarHashPinPbkdf2(pinIngresado);
                    Save();
                }
                catch
                {
                }
                return true;
            }

            return false;
        }

        public void EstablecerNuevoPin(string nuevoPin)
        {
            PinAdminHash = GenerarHashPinPbkdf2(nuevoPin);
            Save();
        }

        public static string GenerarHashPinPbkdf2(string pin)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] subkey = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(pin),
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                32);
            return $"{Pbkdf2Prefix}{Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}";
        }

        private static bool VerificarPinPbkdf2(string pinIngresado, string hashFormateado)
        {
            try
            {
                string[] partes = hashFormateado.Split('$');
                if (partes.Length != 4) return false;
                if (!int.TryParse(partes[1], out int iteraciones) || iteraciones < 10_000) return false;

                byte[] salt = Convert.FromBase64String(partes[2]);
                byte[] esperado = Convert.FromBase64String(partes[3]);
                if (salt.Length < 8 || esperado.Length < 16) return false;

                byte[] calculado = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(pinIngresado),
                    salt,
                    iteraciones,
                    HashAlgorithmName.SHA256,
                    esperado.Length);

                return CryptographicOperations.FixedTimeEquals(esperado, calculado);
            }
            catch
            {
                return false;
            }
        }

        public static string CalcularSha256(string texto)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(texto));
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}