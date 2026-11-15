using Punto_de_Venta.Servicios;
using Xunit;

namespace Punto_de_Venta.Tests
{
    public class BasculaServiceTests
    {
        [Theory]
        [InlineData("0.500 kg", 0.500)]
        [InlineData("  1.250 KG", 1.250)]
        [InlineData("ST,GS,+  0.750kg", 0.750)]
        [InlineData("US,NT,+  1.500 kg", 1.500)]
        [InlineData("S S     2.345 kg", 2.345)]
        [InlineData("S D     0.850 kg", 0.850)]
        [InlineData("+0.350", 0.350)]
        [InlineData("1,250 kg", 1.250)]
        public void TryParsePeso_TramasKilogramosValidas_RetornaPesoCorrecto(string trama, double esperadoDouble)
        {
            decimal esperado = (decimal)esperadoDouble;

            bool ok = BasculaService.TryParsePeso(trama, out decimal peso);

            Assert.True(ok);
            Assert.Equal(esperado, peso);
        }

        [Theory]
        [InlineData("500 g", 0.500)]
        [InlineData("750 gr", 0.750)]
        [InlineData("1250 grs", 1.250)]
        [InlineData("350 gramos", 0.350)]
        public void TryParsePeso_TramasEnGramos_ConvierteAKilogramos(string trama, double esperadoDouble)
        {
            decimal esperado = (decimal)esperadoDouble;

            bool ok = BasculaService.TryParsePeso(trama, out decimal peso);

            Assert.True(ok);
            Assert.Equal(esperado, peso);
        }

        [Theory]
        [InlineData("2.000 lb", 0.907)]
        [InlineData("1.5 lbs", 0.680)]
        public void TryParsePeso_TramasEnLibras_ConvierteAKilogramos(string trama, double esperadoDouble)
        {
            decimal esperado = (decimal)esperadoDouble;

            bool ok = BasculaService.TryParsePeso(trama, out decimal peso);

            Assert.True(ok);
            Assert.Equal(esperado, peso);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("-0.500 kg")]
        [InlineData(".500")]
        [InlineData("ERR")]
        [InlineData("OVERLOAD")]
        public void TryParsePeso_TramasInvalidasONegativas_RetornaFalse(string trama)
        {
            bool ok = BasculaService.TryParsePeso(trama, out decimal peso);

            Assert.False(ok);
            Assert.Equal(0m, peso);
        }

        [Fact]
        public void IntentarExtraerUltimaTramaCompletaValida_ModoContinuo_DescartaPrimeraTruncadaYTomaUltima()
        {
            string buffer = "500 kg\r\nST,GS,+ 1.200 kg\r\nST,GS,+ 1.250 kg\r\nST,GS,+ 1.";

            bool ok = BasculaService.IntentarExtraerUltimaTramaCompletaValida(
                buffer,
                descartarPrimeraTramaIncompleta: true,
                terminadorConfigurado: "\r\n",
                out decimal peso);

            Assert.True(ok);
            Assert.Equal(1.250m, peso);
        }

        [Fact]
        public void IntentarExtraerUltimaTramaCompletaValida_SinTerminador_RetornaFalse()
        {
            bool ok = BasculaService.IntentarExtraerUltimaTramaCompletaValida(
                "1.250 kg",
                descartarPrimeraTramaIncompleta: false,
                terminadorConfigurado: "\r\n",
                out _);

            Assert.False(ok);
        }

        [Fact]
        public void IntentarExtraerUltimaTramaCompletaValida_TerminadorPersonalizado_FuncionaCorrectamente()
        {
            bool ok = BasculaService.IntentarExtraerUltimaTramaCompletaValida(
                "0.400 kg;0.850 kg;",
                descartarPrimeraTramaIncompleta: false,
                terminadorConfigurado: ";",
                out decimal peso);

            Assert.True(ok);
            Assert.Equal(0.850m, peso);
        }

        [Theory]
        [InlineData("Continuo", true)]
        [InlineData("Continuous (Auto)", true)]
        [InlineData("Torrey / Rhino (Peticion P)", false)]
        [InlineData("", false)]
        public void EsProtocoloContinuo_DetectaCorrectamente(string protocolo, bool esperado)
        {
            Assert.Equal(esperado, BasculaService.EsProtocoloContinuo(protocolo));
        }
    }
}

