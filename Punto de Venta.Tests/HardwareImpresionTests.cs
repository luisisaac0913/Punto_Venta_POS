using System.Text;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using Xunit;

namespace Punto_de_Venta.Tests
{
    public class HardwareImpresionTests
    {
        [Theory]
        [InlineData("EPSON TM-T20III", true)]
        [InlineData("Xprinter XP-58", true)]
        [InlineData("POS-80", true)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("(Ninguna)", false)]
        [InlineData("Ninguna impresora", false)]
        [InlineData("Microsoft Print to PDF", false)]
        [InlineData("Microsoft XPS Document Writer", false)]
        [InlineData("OneNote (Desktop)", false)]
        [InlineData("Fax", false)]
        public void RawPrinterHelper_EsImpresoraFisicaValida_FiltraVirtualesYNulas(string nombre, bool esperado)
        {
            Assert.Equal(esperado, RawPrinterHelper.EsImpresoraFisicaValida(nombre));
        }

        [Fact]
        public void EtiquetaPrinter_GenerarBytesEtiqueta_IncluyeComandosEscPosYCode128()
        {
            byte[] bytes = EtiquetaPrinter.GenerarBytesEtiqueta(
                "Mi Tienda",
                "Refresco Cola 600ml",
                "7501055300075",
                18.50m,
                copias: 1);

            Assert.NotEmpty(bytes);
            // ESC @ (inicializar) + ESC t 2 (CP850)
            Assert.Equal(0x1B, bytes[0]);
            Assert.Equal(0x40, bytes[1]);
            Assert.Equal(0x1B, bytes[2]);
            Assert.Equal(0x74, bytes[3]);
            Assert.Equal(0x02, bytes[4]);

            // Debe contener prefijo CODE128 Subset B "{B7501055300075"
            string ascii = Encoding.ASCII.GetString(bytes);
            Assert.Contains("{B7501055300075", ascii);
            Assert.Contains("$18.50", ascii);
        }

        [Fact]
        public void EtiquetaPrinter_GenerarBytesEtiqueta_EscapaLlaveAbiertaEnCode128()
        {
            byte[] bytes = EtiquetaPrinter.GenerarBytesEtiqueta("Tienda", "Producto", "AB{12", 10m);
            string ascii = Encoding.ASCII.GetString(bytes);

            Assert.Contains("{BAB{{12", ascii);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void EtiquetaPrinter_GenerarBytesEtiqueta_CodigoVacio_LanzaArgumentException(string codigo)
        {
            Assert.Throws<ArgumentException>(() =>
                EtiquetaPrinter.GenerarBytesEtiqueta("Tienda", "Producto", codigo, 10m));
        }

        [Fact]
        public void EtiquetaPrinter_CodificarTextoTermicoCp850_CodificaEspanolSinPerderCaracteres()
        {
            byte[] bytes = EtiquetaPrinter.CodificarTextoTermicoCp850("Piña ÁÉÍÓÚ ñÑ ¿¡");

            Assert.NotEmpty(bytes);
            Assert.DoesNotContain((byte)'?', bytes);
        }

        [Fact]
        public void TicketService_GenerarTextoTicket_RespetaAncho58mmY80mm()
        {
            var venta = new Venta
            {
                Id = 42,
                FechaHora = new DateTime(2026, 11, 15, 10, 30, 0),
                Total = 35.00m,
                MetodoPago = "Efectivo",
                Estado = "Completada"
            };
            var items = new List<DetalleVenta>
            {
                new() { Nombre = "Agua Natural 1L", Cantidad = 2, Precio = 17.50m }
            };

            var config58 = new AppConfig { NombreNegocio = "Chip & Chapa", AnchoTicketCaracteres = 32 };
            string ticket58 = TicketService.GenerarTextoTicket(venta, items, 50m, 15m, config58);

            Assert.Contains("#42", ticket58);
            Assert.Contains("TOTAL:", ticket58);
            Assert.Contains("35.00", ticket58);
        }

        [Theory]
        [InlineData(32)]
        [InlineData(42)]
        public void TicketService_GenerarTicketMuestra_NingunaLineaExcedeAnchoConfigurado(int ancho)
        {
            var config = new AppConfig
            {
                NombreNegocio = "Mi Tienda",
                AnchoTicketCaracteres = ancho
            };

            string ticket = TicketService.GenerarTicketMuestra(config);
            var lineas = ticket.Replace("\r\n", "\n").Split('\n');

            foreach (string linea in lineas)
            {
                Assert.True(linea.Length <= ancho, $"Linea excede {ancho} chars ({linea.Length}): '{linea}'");
            }
        }
    }
}

