using Punto_de_Venta.Datos;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Punto_de_Venta
{
    public class AppConfig
    {
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

        // ================= CONFIGURACIÓN BÁSCULA DIGITAL =================
        public bool BasculaHabilitada { get; set; } = false;
        public string BasculaPuerto { get; set; } = "COM1";
        public int BasculaBaudRate { get; set; } = 9600;
        public string BasculaProtocolo { get; set; } = "Torrey / Rhino (Petición P)"; // Opciones: "Torrey / Rhino (Petición P)", "Modo Continuo"
        public bool BasculaModoPrecio { get; set; } = false;

        // Hash SHA-256 de "1234" por defecto
        public string PinAdminHash { get; set; } = "03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4";

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POS_DataStorage",
            "config.json"
        );

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
            }
            catch { }

            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                SafeFileStore.WriteAllLinesAtomic(ConfigPath, new[] { json });
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