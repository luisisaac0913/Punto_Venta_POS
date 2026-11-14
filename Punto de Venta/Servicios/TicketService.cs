using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using Punto_de_Venta.Modelos;

namespace Punto_de_Venta.Servicios
{
    public static class TicketService
    {
        #region Rutas y Directorios
        private static string TicketsFolder => Path.Combine(
            Datos.DatabaseHelper.DbFolder,
            "Tickets"
        );
        #endregion

        #region Generacion de Texto del Ticket
        public static string GenerarTextoTicket(Venta venta, IEnumerable<DetalleVenta> items, decimal pagoCon, decimal cambio, AppConfig config)
        {
            var sb = new StringBuilder();
            var culture = CultureInfo.GetCultureInfo("es-MX");
            int ancho = config.AnchoTicketCaracteres > 0 ? config.AnchoTicketCaracteres : 32;
            if (!string.IsNullOrWhiteSpace(config.NombreNegocio)) sb.AppendLine(CentrarTexto(config.NombreNegocio.Trim(), ancho));
            if (LicenciaService.EsPremium())
            {
                if (!string.IsNullOrWhiteSpace(config.GiroNegocio)) sb.AppendLine(CentrarTexto(config.GiroNegocio.Trim(), ancho));
                if (!string.IsNullOrWhiteSpace(config.DireccionNegocio)) sb.AppendLine(CentrarTexto(config.DireccionNegocio.Trim(), ancho));
            }
            bool en = IdiomaService.EsIngles();
            sb.AppendLine(new string('-', ancho));
            sb.AppendLine($"{(en ? "Receipt" : "Folio")}: #{venta.Id}");
            sb.AppendLine($"{(en ? "Date" : "Fecha")}: {venta.FechaHora:dd/MM/yyyy hh:mm tt}");
            string metodo = en ? IdiomaService.TraducirTexto(venta.MetodoPago) : venta.MetodoPago;
            if (en && string.Equals(metodo, "Efectivo", StringComparison.OrdinalIgnoreCase)) metodo = "Cash";
            else if (en && string.Equals(metodo, "Tarjeta", StringComparison.OrdinalIgnoreCase)) metodo = "Card";
            else if (en && string.Equals(metodo, "Transferencia", StringComparison.OrdinalIgnoreCase)) metodo = "Transfer";
            else if (en && string.Equals(metodo, "Mixto", StringComparison.OrdinalIgnoreCase)) metodo = "Mixed";
            else if (en && string.Equals(metodo, "Fiado", StringComparison.OrdinalIgnoreCase)) metodo = "Credit";
            sb.AppendLine($"{(en ? "Method" : "Metodo")}: {metodo}");
            sb.AppendLine(new string('-', ancho));
            if (en)
                sb.AppendLine(ancho >= 42 ? "QTY   DESCRIPTION              PRICE  AMOUNT" : "QTY  DESCRIPTION      PRICE AMOUNT");
            else
                sb.AppendLine(ancho >= 42 ? "CANT  DESCRIPCION            PRECIO   IMPORTE" : "CANT DESCRIPCION       PRECIO IMPORTE");
            sb.AppendLine(new string('-', ancho));
            foreach (var item in items)
            {
                string cantDesc = $"{item.Cantidad}x {item.Nombre}";
                decimal subtotal = item.Precio * item.Cantidad;
                string subtotalStr = subtotal.ToString("0.00", culture);

                if (cantDesc.Length + subtotalStr.Length + 1 > ancho)
                {
                    if (cantDesc.Length > ancho - subtotalStr.Length - 1)
                        cantDesc = cantDesc.Substring(0, Math.Max(1, ancho - subtotalStr.Length - 2)) + ".";
                }

                int espacios = ancho - cantDesc.Length - subtotalStr.Length;
                sb.AppendLine($"{cantDesc}{new string(' ', Math.Max(1, espacios))}{subtotalStr}");
            }
            sb.AppendLine(new string('-', ancho));
            sb.AppendLine(AlinearDerecha($"TOTAL: {venta.Total.ToString("C2", culture)}", ancho));
            if (venta.MetodoPago == "Fiado" || venta.Estado == "Fiado")
            {
                sb.AppendLine(AlinearDerecha(en ? "ACCOUNTS RECEIVABLE (CREDIT)" : "CUENTA POR COBRAR (FIADO)", ancho));
            }
            else if (venta.MetodoPago == "Mixto")
            {
                sb.AppendLine(AlinearDerecha(en ? "MIXED PAYMENT BREAKDOWN" : "PAGO MIXTO DESGLOSADO", ancho));
                if (venta.PagoEfectivo > 0) sb.AppendLine(AlinearDerecha($"{(en ? "Cash" : "Efectivo")}: {venta.PagoEfectivo.ToString("C2", culture)}", ancho));
                if (venta.PagoTarjeta > 0) sb.AppendLine(AlinearDerecha($"{(en ? "Card" : "Tarjeta")}: {venta.PagoTarjeta.ToString("C2", culture)}", ancho));
                if (venta.PagoTransferencia > 0) sb.AppendLine(AlinearDerecha($"Transfer.: {venta.PagoTransferencia.ToString("C2", culture)}", ancho));
                sb.AppendLine(AlinearDerecha($"{(en ? "CHANGE" : "CAMBIO")}: {cambio.ToString("C2", culture)}", ancho));
            }
            else
            {
                sb.AppendLine(AlinearDerecha($"{(en ? "PAID WITH" : "PAGO CON")}: {pagoCon.ToString("C2", culture)}", ancho));
                sb.AppendLine(AlinearDerecha($"{(en ? "CHANGE" : "CAMBIO")}: {cambio.ToString("C2", culture)}", ancho));
            }
            sb.AppendLine(new string('-', ancho));
            string defaultFooter = en ? "THANK YOU FOR YOUR PURCHASE\nCome back soon" : "GRACIAS POR SU COMPRA\nVuelva pronto";
            string pieTicket = LicenciaService.EsPremium()
                ? (string.IsNullOrWhiteSpace(config.MensajePie) ? defaultFooter : config.MensajePie)
                : defaultFooter;

            foreach (var linea in pieTicket.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                sb.AppendLine(CentrarTexto(linea.Trim(), ancho));
            }
            sb.AppendLine("\n");
            return sb.ToString();
        }
        #endregion

        #region Verificacion de Cola de Impresion de Windows
        public static (bool Instalada, bool TieneAdvertenciaCola, string MensajeEstado) VerificarEstadoImpresora(string? printerName)
        {
            if (!EsImpresoraConfiguradaValida(printerName))
            {
                return (false, false, "No hay una impresora configurada.");
            }

            string nombreLimpio = printerName!.Trim();
            bool encontradaEnWindows = false;

            try
            {
                foreach (string installed in PrinterSettings.InstalledPrinters)
                {
                    if (string.Equals(installed, nombreLimpio, StringComparison.OrdinalIgnoreCase))
                    {
                        encontradaEnWindows = true;
                        nombreLimpio = installed;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                PosLogger.Warn("TicketService.VerificarEstadoImpresora.InstalledPrinters", ex);
            }

            if (!encontradaEnWindows)
            {
                return (false, true, $"La impresora '{nombreLimpio}' no se encuentra instalada en Windows.");
            }

            try
            {
                string escapedName = nombreLimpio.Replace("\\", "\\\\").Replace("'", "\\'");
                var query = new ObjectQuery($"SELECT WorkOffline, PrinterStatus, DetectedErrorState, PrinterState FROM Win32_Printer WHERE Name = '{escapedName}'");
                var options = new System.Management.EnumerationOptions
                {
                    Timeout = TimeSpan.FromMilliseconds(1200),
                    ReturnImmediately = true,
                    Rewindable = false
                };

                using var searcher = new ManagementObjectSearcher( new ManagementScope(@"\\.\root\cimv2"), query, options);
                using var results = searcher.Get();

                foreach (ManagementObject printerObj in results)
                {
                    using (printerObj)
                    {
                        bool workOffline = printerObj["WorkOffline"] is bool b && b;
                        uint detectedError = printerObj["DetectedErrorState"] != null ? Convert.ToUInt32(printerObj["DetectedErrorState"]) : 0u;
                        uint printerState = printerObj["PrinterState"] != null ? Convert.ToUInt32(printerObj["PrinterState"]) : 0u;

                        // Sin papel (DetectedErrorState == 4 o bit 0x10 en PrinterState)
                        if (detectedError == 4 || (printerState & 0x00000010u) != 0)
                        {
                            return (true, true, $"La impresora '{nombreLimpio}' reporta que no tiene papel.");
                        }

                        // Papel atascado (DetectedErrorState == 8 o bit 0x08 en PrinterState)
                        if (detectedError == 8 || (printerState & 0x00000008u) != 0)
                        {
                            return (true, true, $"La impresora '{nombreLimpio}' tiene un atasco de papel.");
                        }

                        // Tapa abierta (DetectedErrorState == 6 o bit 0x800000 en PrinterState)
                        if (detectedError == 6 || (printerState & 0x00800000u) != 0)
                        {
                            return (true, true, $"La tapa de la impresora '{nombreLimpio}' está abierta.");
                        }

                        // Offline / Fuera de linea
                        if (workOffline || detectedError == 9 || (printerState & 0x00000080u) != 0 || (printerState & 0x00000400u) != 0)
                        {
                            return (true, true, $"La impresora '{nombreLimpio}' está fuera de línea (Offline) o desconectada.");
                        }

                        // Otro estado de error de hardware
                        if (detectedError == 2 || detectedError == 10 || detectedError == 11 || (printerState & 0x00000002u) != 0)
                        {
                            return (true, true, $"La impresora '{nombreLimpio}' reporta un estado de error en la cola de Windows.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Si el servicio WMI falla o tarda, no bloqueamos el cobro ni rompemos drivers genericos
                PosLogger.Warn("TicketService.VerificarEstadoImpresora.WMI", ex);
            }

            return (true, false, $"Impresora lista: {nombreLimpio}");
        }
        #endregion

        #region Almacenamiento e Impresion Fisica
        public static void GuardarCopiaDigital(Venta venta, IEnumerable<DetalleVenta> items, decimal pagoCon, decimal cambio, AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(TicketsFolder);
                File.WriteAllText(Path.Combine(TicketsFolder, $"Ticket_{venta.Id}.txt"), GenerarTextoTicket(venta, items, pagoCon, cambio, config), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                PosLogger.Warn("TicketService.GuardarCopiaDigital", ex);
            }
        }

        public static bool EsImpresoraConfiguradaValida(string? printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return false;
            string p = printerName.Trim();
            return !p.StartsWith("(", StringComparison.Ordinal) &&
                   !p.Contains("Ninguna", StringComparison.OrdinalIgnoreCase) &&
                   !p.Contains("None", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ImprimirFisico(Venta venta, IEnumerable<DetalleVenta> items, decimal pagoCon, decimal cambio, AppConfig config)
        {
            return ImprimirFisico(venta, items, pagoCon, cambio, config, out _);
        }

        public static bool ImprimirFisico(Venta venta, IEnumerable<DetalleVenta> items, decimal pagoCon, decimal cambio, AppConfig config, out string advertenciaOError)
        {
            advertenciaOError = string.Empty;
            if (!EsImpresoraConfiguradaValida(config.ImpresoraSeleccionada))
            {
                advertenciaOError = "No hay una impresora configurada.";
                return false;
            }

            var textoTicket = GenerarTextoTicket(venta, items, pagoCon, cambio, config);
            return ImprimirTextoFisico(config.ImpresoraSeleccionada, textoTicket, out advertenciaOError, config.AnchoTicketCaracteres);
        }

        public static bool ImprimirTextoFisico(string? printerName, string textoTicket, out string error, int anchoCaracteresConfig = 0)
        {
            error = string.Empty;
            if (!EsImpresoraConfiguradaValida(printerName))
            {
                error = "No hay una impresora configurada.";
                return false;
            }

            var estadoCola = VerificarEstadoImpresora(printerName);
            if (!estadoCola.Instalada)
            {
                error = estadoCola.MensajeEstado;
                PosLogger.Warn("TicketService.ImprimirTextoFisico", error);
                return false;
            }

            if (estadoCola.TieneAdvertenciaCola)
            {
                error = estadoCola.MensajeEstado;
                PosLogger.Warn("TicketService.ImprimirTextoFisico.AdvertenciaCola", estadoCola.MensajeEstado);
            }

            try
            {
                string textoNormalizado = (textoTicket ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
                string[] lineas = string.IsNullOrEmpty(textoNormalizado) ? Array.Empty<string>() : textoNormalizado.Split('\n');
                int indiceLinea = 0;

                int anchoCaracteres = anchoCaracteresConfig > 0
                    ? anchoCaracteresConfig
                    : (AppConfig.Load().AnchoTicketCaracteres > 0 ? AppConfig.Load().AnchoTicketCaracteres : 32);

                // Ancho real de rollo en centesimas de pulgada (1 pulgada = 25.4 mm = 100 unidades):
                // - Rollo 58mm (<= 32 cols): papel 58mm (~228), area imprimible real 48mm (~188)
                // - Rollo 80mm (> 32 cols):  papel 80mm (~315), area imprimible real 72mm (~283)
                bool esRollo58mm = anchoCaracteres <= 32;
                int anchoPapelCentesimas = esRollo58mm ? 228 : 315;
                float anchoImprimibleObjetivo = esRollo58mm ? 188f : 283f;

                using var printDoc = new PrintDocument
                {
                    PrintController = new StandardPrintController(),
                    OriginAtMargins = false,
                    PrinterSettings = { PrinterName = printerName!.Trim() }
                };

                try
                {
                    printDoc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

                    // Si el driver en Windows trae Tamano Carta (850x1100) o A4 (827x1169) por defecto,
                    // ajustamos el PaperSize al ancho real del rollo y al alto dinamico del ticket
                    // para no recortar ni expulsar 28 cm de papel en blanco.
                    var paperActual = printDoc.DefaultPageSettings.PaperSize;
                    if (paperActual == null || paperActual.Width > 350 || paperActual.Height > 950)
                    {
                        int altoEstimadoCentesimas = Math.Max(140, (int)Math.Ceiling((lineas.Length + 3) * 14.0f));
                        printDoc.DefaultPageSettings.PaperSize = new PaperSize("TicketPOS", anchoPapelCentesimas, altoEstimadoCentesimas);
                    }
                }
                catch (Exception exPaper)
                {
                    PosLogger.Warn("TicketService.ImprimirTextoFisico.PaperSize", exPaper);
                }

                printDoc.PrintPage += (sender, e) =>
                {
                    if (e.Graphics == null || lineas.Length == 0)
                    {
                        e.HasMorePages = false;
                        return;
                    }

                    using var formatoTexto = new StringFormat(StringFormat.GenericTypographic)
                    {
                        FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap
                    };

                    float anchoPaginaDriver = e.PageBounds.Width > 40 ? e.PageBounds.Width : anchoImprimibleObjetivo;
                    float anchoVisibleClip = e.Graphics.VisibleClipBounds.Width > 40 ? e.Graphics.VisibleClipBounds.Width : anchoPaginaDriver;
                    float anchoDisponible = Math.Min(anchoImprimibleObjetivo, anchoVisibleClip) - 2f;
                    if (anchoDisponible < 120f)
                    {
                        anchoDisponible = anchoImprimibleObjetivo;
                    }

                    // Seleccionamos y escalamos dinamicamente la fuente monoespaciada para que jamas se recorte a la derecha
                    float fontSize = esRollo58mm ? 7.75f : 8.25f;
                    string patronMedicion = new string('M', anchoCaracteres);
                    Font font = new Font("Consolas", fontSize, FontStyle.Regular);
                    try
                    {
                        while (fontSize > 5.75f)
                        {
                            SizeF medida = e.Graphics.MeasureString(patronMedicion, font, int.MaxValue, formatoTexto);
                            if (medida.Width <= anchoDisponible)
                            {
                                break;
                            }
                            font.Dispose();
                            fontSize -= 0.25f;
                            font = new Font("Consolas", fontSize, FontStyle.Regular);
                        }

                        float altoLinea = font.GetHeight(e.Graphics) + 1.0f;
                        float x = 1f;
                        float y = 2f;
                        float altoMaximo = e.PageBounds.Height > 60 ? e.PageBounds.Height - 6f : 3200f;

                        while (indiceLinea < lineas.Length && y + altoLinea <= altoMaximo)
                        {
                            e.Graphics.DrawString(lineas[indiceLinea], font, Brushes.Black, new PointF(x, y), formatoTexto);
                            y += altoLinea;
                            indiceLinea++;
                        }

                        e.HasMorePages = indiceLinea < lineas.Length;
                    }
                    finally
                    {
                        font.Dispose();
                    }
                };

                printDoc.Print();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                PosLogger.Error("TicketService.ImprimirTextoFisico", ex, $"Impresora={printerName}");
                return false;
            }
        }

        public static string GenerarTicketMuestra(AppConfig config)
        {
            bool en = IdiomaService.EsIngles();
            var ventaPrueba = new Venta { Id = 101, FechaHora = DateTime.Now, Total = 45.00m, MetodoPago = en ? "Cash" : "Efectivo" };
            var itemsPrueba = new List<DetalleVenta>
            {
                new() { Nombre = en ? "Cola Soda 600ml" : "Refresco Coca 600", Cantidad = 1, Precio = 18.00m },
                new() { Nombre = en ? "Chokis Cookies" : "Galletas Chokis", Cantidad = 2, Precio = 17.00m }
            };
            return GenerarTextoTicket(ventaPrueba, itemsPrueba, 50.00m, 5.00m, config);
        }
        #endregion

        #region Utilidades de Formato y Alineacion
        public static string CentrarTexto(string texto, int ancho) =>
            texto.Length >= ancho ? texto.Substring(0, ancho) : new string(' ', (ancho - texto.Length) / 2) + texto;

        public static string AlinearDerecha(string texto, int ancho) =>
            texto.Length >= ancho ? texto : new string(' ', ancho - texto.Length) + texto;
        #endregion
    }
}