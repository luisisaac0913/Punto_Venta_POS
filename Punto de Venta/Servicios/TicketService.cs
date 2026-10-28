using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Text;
using Punto_de_Venta.Modelos;

namespace Punto_de_Venta.Servicios
{
    public static class TicketService
    {
        #region Rutas y Directorios
        private static readonly string TicketsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POS_DataStorage",
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
            if (!string.IsNullOrWhiteSpace(config.GiroNegocio)) sb.AppendLine(CentrarTexto(config.GiroNegocio.Trim(), ancho));
            if (!string.IsNullOrWhiteSpace(config.DireccionNegocio)) sb.AppendLine(CentrarTexto(config.DireccionNegocio.Trim(), ancho));
            sb.AppendLine(new string('-', ancho));
            sb.AppendLine($"Folio: #{venta.Id}");
            sb.AppendLine($"Fecha: {venta.FechaHora:dd/MM/yyyy hh:mm tt}");
            sb.AppendLine($"Método: {venta.MetodoPago}");
            sb.AppendLine(new string('-', ancho));
            sb.AppendLine(ancho == 42 ? "CANT  DESCRIPCION            PRECIO   IMPORTE" : "CANT DESCRIPCION       PRECIO IMPORTE");
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
            if (venta.MetodoPago == "Fiado")
            {
                sb.AppendLine(AlinearDerecha("CUENTA POR COBRAR (FIADO)", ancho));
            }
            else if (venta.MetodoPago == "Mixto")
            {
                sb.AppendLine(AlinearDerecha("PAGO MIXTO DESGLOSADO", ancho));
                if (venta.PagoEfectivo > 0) sb.AppendLine(AlinearDerecha($"Efectivo: {venta.PagoEfectivo.ToString("C2", culture)}", ancho));
                if (venta.PagoTarjeta > 0) sb.AppendLine(AlinearDerecha($"Tarjeta: {venta.PagoTarjeta.ToString("C2", culture)}", ancho));
                if (venta.PagoTransferencia > 0) sb.AppendLine(AlinearDerecha($"Transfer.: {venta.PagoTransferencia.ToString("C2", culture)}", ancho));
                sb.AppendLine(AlinearDerecha($"CAMBIO: {cambio.ToString("C2", culture)}", ancho));
            }
            else
            {
                sb.AppendLine(AlinearDerecha($"PAGO CON: {pagoCon.ToString("C2", culture)}", ancho));
                sb.AppendLine(AlinearDerecha($"CAMBIO: {cambio.ToString("C2", culture)}", ancho));
            }
            sb.AppendLine(new string('-', ancho));
            if (!string.IsNullOrWhiteSpace(config.MensajePie))
            {
                foreach (var linea in config.MensajePie.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
                {
                    sb.AppendLine(CentrarTexto(linea.Trim(), ancho));
                }
            }
            sb.AppendLine("\n\n");
            return sb.ToString();
        }
        #endregion

        #region Almacenamiento e Impresion Fusica
        public static void GuardarCopiaDigital(Venta venta, IEnumerable<DetalleVenta> items, decimal pagoCon, decimal cambio, AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(TicketsFolder);
                File.WriteAllText(Path.Combine(TicketsFolder, $"Ticket_{venta.Id}.txt"), GenerarTextoTicket(venta, items, pagoCon, cambio, config), Encoding.UTF8);
            }
            catch { }
        }
        public static void ImprimirFisico(Venta venta, IEnumerable<DetalleVenta> items, decimal pagoCon, decimal cambio, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.ImpresoraSeleccionada) || config.ImpresoraSeleccionada.Contains("Ninguna", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var textoTicket = GenerarTextoTicket(venta, items, pagoCon, cambio, config);
                var printDoc = new PrintDocument { PrinterSettings = { PrinterName = config.ImpresoraSeleccionada } };
                printDoc.PrintPage += (sender, e) =>
                {
                    using var font = new Font("Consolas", 8, FontStyle.Regular);
                    e.Graphics?.DrawString(textoTicket, font, Brushes.Black, new PointF(0, 0));
                };
                printDoc.Print();
            }
            catch { }
        }
        public static string GenerarTicketMuestra(AppConfig config)
        {
            var ventaPrueba = new Venta { Id = 101, FechaHora = DateTime.Now, Total = 45.00m, MetodoPago = "Efectivo" };
            var itemsPrueba = new List<DetalleVenta>
            {
                new() { Nombre = "Refresco Coca 600", Cantidad = 1, Precio = 18.00m },
                new() { Nombre = "Galletas Chokis", Cantidad = 2, Precio = 17.00m }
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