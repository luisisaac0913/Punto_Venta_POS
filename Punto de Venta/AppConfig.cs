using Punto_de_Venta.Datos;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Punto_de_Venta
{
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
        public string BasculaProtocolo { get; set; } = "Torrey / Rhino (Peticion P)"; 
        public bool BasculaModoPrecio { get; set; } = false;
        public bool ContarMonedasIndividualmente { get; set; } = false;
        public DateTime? UltimoRespaldoDriveFecha { get; set; }
        public int MaxRespaldosLocales { get; set; } = 5;
        public int MaxRespaldosDrive { get; set; } = 5;
        public int IntervaloRespaldoMinutos { get; set; } = 60;
        public bool ServidorLocalHabilitado { get; set; } = false;
        public string ModoVisualizacionMetricas { get; set; } = "Graficas";

        public string PinAdminHash { get; set; } = "03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4";

        private static string ConfigPath
        {
            get
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "POS_DataStorage");
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
                                catch { }
                            }
                            _cachedConfig = config;
                            return config;
                        }
                    }
                }
                catch { }

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
            catch { }
        }

        public bool ValidarPin(string pinIngresado)
        {
            if (string.IsNullOrEmpty(pinIngresado)) return false;
            var hashIngresado = CalcularSha256(pinIngresado);
            return string.Equals(PinAdminHash, hashIngresado, StringComparison.OrdinalIgnoreCase);
        }

        public void EstablecerNuevoPin(string nuevoPin)
        {
            PinAdminHash = CalcularSha256(nuevoPin);
            Save();
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