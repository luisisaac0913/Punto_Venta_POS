using System;

namespace Punto_de_Venta.Servicios
{
    public static class CajonService
    {
        public static bool AbrirCajon(string? nombreImpresora = null)
        {
            return AbrirCajon(out _, nombreImpresora);
        }

        public static bool AbrirCajon(out string error, string? nombreImpresora = null)
        {
            error = string.Empty;
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
                    error = "No hay una impresora fisica configurada para el cajon de dinero.";
                    return false;
                }

                var estadoImpresora = TicketService.VerificarEstadoImpresora(printer);
                if (!estadoImpresora.Instalada)
                {
                    error = estadoImpresora.MensajeEstado;
                    PosLogger.Warn("CajonService.AbrirCajon", error);
                    return false;
                }

                if (estadoImpresora.TieneAdvertenciaCola)
                {
                    error = estadoImpresora.MensajeEstado;
                    PosLogger.Warn("CajonService.AbrirCajon.AdvertenciaImpresora", error);
                }

                byte[] pulsoCajon = [0x1B, 0x70, 0x00, 0x19, 0xFA, 0x1B, 0x70, 0x01, 0x19, 0xFA];
                bool enviado = RawPrinterHelper.SendBytesToPrinter(printer!, pulsoCajon, "Apertura Cajon POS");
                if (!enviado)
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        error = $"No se pudo enviar el pulso de apertura de cajon a '{printer}'.";
                    }
                    PosLogger.Warn("CajonService.AbrirCajon", error);
                }

                return enviado;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                PosLogger.Error("CajonService.AbrirCajon", ex, $"Impresora={nombreImpresora}");
                return false;
            }
        }
    }
}