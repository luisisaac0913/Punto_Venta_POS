using System;

namespace Punto_de_Venta.Servicios
{
    public static class CajonService
    {
        public static bool AbrirCajon(string? nombreImpresora = null)
        {
            try
            {
                var printer = nombreImpresora;
                if (string.IsNullOrWhiteSpace(printer))
                {
                    var config = AppConfig.Load();
                    printer = config.ImpresoraSeleccionada;
                }
                if (!RawPrinterHelper.EsImpresoraFisicaValida(printer))
                {
                    return false;
                }
                byte[] pulsoCajon = [0x1B, 0x70, 0x00, 0x19, 0xFA, 0x1B, 0x70, 0x01, 0x19, 0xFA];
                return RawPrinterHelper.SendBytesToPrinter(printer!, pulsoCajon, "Apertura Cajon POS");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CajonService] No se pudo abrir el cajon: {ex.Message}");
                return false;
            }
        }
    }
}