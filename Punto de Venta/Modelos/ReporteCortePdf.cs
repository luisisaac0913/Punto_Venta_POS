using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Creamos alias para evitar que QuestPDF y WPF choquen por los nombres "Colors" y "Fonts"
using PdfColors = QuestPDF.Helpers.Colors;
using PdfFonts = QuestPDF.Helpers.Fonts;

namespace Punto_de_Venta.Reportes
{
    public static class ReporteDiarioPdf
    {
        public static void GenerarYabrir(CorteCaja corte, List<MovimientoCaja> movimientos, string rutaLogo, byte[] graficaBytes, string rutaArchivo)
        {
            var mx = new CultureInfo("es-MX");

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(PdfColors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(PdfFonts.SegoeUI));

                    // 1. ENCABEZADO
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Miscelánea Jaz").FontSize(28).Bold().FontColor(PdfColors.Black);
                            col.Item().PaddingTop(2).LineHorizontal(2).LineColor("#1597a7");

                            col.Item().PaddingTop(5).Text($"Reporte de Operaciones y Corte de Caja").FontSize(14).SemiBold().FontColor(PdfColors.Grey.Darken3);
                            col.Item().Text($"Turno #{corte.Id} | Fecha: {corte.FechaHora.ToString("dd 'de' MMMM 'del' yyyy", mx)}").FontSize(11).FontColor(PdfColors.Grey.Darken2);
                        });

                        if (!string.IsNullOrEmpty(rutaLogo) && File.Exists(rutaLogo))
                        {
                            row.ConstantItem(90).AlignRight().Image(rutaLogo);
                        }
                    });

                    // 2. CONTENIDO PRINCIPAL
                    page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                    {
                        // SECCIÓN A: Arqueo de Caja
                        col.Item().PaddingBottom(10).Row(row =>
                        {
                            // Columna Izquierda
                            row.RelativeItem().Border(1).BorderColor(PdfColors.Grey.Lighten2).Padding(10).Column(inner =>
                            {
                                // PaddingBottom movido ANTES del Text
                                inner.Item().PaddingBottom(5).Text("Ingresos Declarados").FontSize(12).SemiBold().FontColor("#1597a7");
                                inner.Item().Text($"Fondo Inicial: {corte.FondoInicial.ToString("C2", mx)}");
                                inner.Item().Text($"Ventas en Efectivo: {corte.VentasEfectivo.ToString("C2", mx)}");
                                inner.Item().Text($"Otras Entradas: {corte.EntradasEfectivo.ToString("C2", mx)}");
                            });

                            row.ConstantItem(10);

                            // Columna Derecha
                            row.RelativeItem().Border(1).BorderColor(PdfColors.Grey.Lighten2).Padding(10).Column(inner =>
                            {
                                // PaddingBottom movido ANTES del Text
                                inner.Item().PaddingBottom(5).Text("Egresos y Resultados").FontSize(12).SemiBold().FontColor("#1597a7");
                                inner.Item().Text($"Gastos/Salidas: {corte.SalidasEfectivo.ToString("C2", mx)}");
                                inner.Item().PaddingTop(5).Text($"Total Esperado en Caja: {corte.TotalEsperado.ToString("C2", mx)}").SemiBold().FontSize(11);
                                inner.Item().Text($"Total Contado Físico: {corte.TotalContado.ToString("C2", mx)}").SemiBold().FontSize(11);

                                var difColor = corte.Diferencia < 0 ? PdfColors.Red.Medium : (corte.Diferencia > 0 ? PdfColors.Orange.Medium : PdfColors.Green.Medium);
                                string difTexto = corte.Diferencia == 0 ? "EXACTO" : (corte.Diferencia < 0 ? "FALTANTE" : "SOBRANTE");
                                inner.Item().PaddingTop(5).Text($"Diferencia: {corte.Diferencia.ToString("C2", mx)} ({difTexto})").SemiBold().FontColor(difColor).FontSize(12);
                            });
                        });

                        // SECCIÓN B: Gráfica
                        if (graficaBytes != null && graficaBytes.Length > 0)
                        {
                            col.Item().PaddingVertical(15).AlignCenter().Height(180).Image(graficaBytes);
                        }

                        // SECCIÓN C: Tabla
                        col.Item().PaddingTop(10).Background("#1597a7").Padding(8).Text("Detalle de Movimientos de Caja").FontSize(12).Bold().FontColor(PdfColors.White);

                        col.Item().Table(tabla =>
                        {
                            tabla.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(80);
                                columns.RelativeColumn();
                                columns.ConstantColumn(90);
                            });

                            tabla.Header(header =>
                            {
                                header.Cell().Background("#dbf0f2").Padding(5).Text("Hora").SemiBold();
                                header.Cell().Background("#dbf0f2").Padding(5).Text("Tipo").SemiBold();
                                header.Cell().Background("#dbf0f2").Padding(5).Text("Concepto").SemiBold();
                                header.Cell().Background("#dbf0f2").Padding(5).AlignRight().Text("Monto").SemiBold();
                            });

                            bool alternate = false;
                            foreach (var mov in movimientos.OrderBy(m => m.FechaHora))
                            {
                                var bgColor = alternate ? PdfColors.Grey.Lighten4 : PdfColors.White;

                                tabla.Cell().Background(bgColor).BorderBottom(1).BorderColor(PdfColors.Grey.Lighten3).Padding(5).Text(mov.FechaHora.ToString("hh:mm tt"));
                                tabla.Cell().Background(bgColor).BorderBottom(1).BorderColor(PdfColors.Grey.Lighten3).Padding(5).Text(mov.Tipo);
                                tabla.Cell().Background(bgColor).BorderBottom(1).BorderColor(PdfColors.Grey.Lighten3).Padding(5).Text(mov.Concepto);
                                tabla.Cell().Background(bgColor).BorderBottom(1).BorderColor(PdfColors.Grey.Lighten3).Padding(5).AlignRight().Text(mov.Monto.ToString("C2", mx));

                                alternate = !alternate;
                            }
                        });

                        // SECCIÓN D: Firmas
                        col.Item().EnsureSpace(80).PaddingTop(40).Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Column(inner =>
                            {
                                inner.Item().LineHorizontal(1).LineColor(PdfColors.Black);
                                inner.Item().PaddingTop(5).Text("Entregó (Nombre y Firma)").FontSize(11).SemiBold();
                                inner.Item().Text("Cajero en Turno").FontSize(9).FontColor(PdfColors.Grey.Medium);
                            });

                            row.ConstantItem(50);

                            row.RelativeItem().AlignCenter().Column(inner =>
                            {
                                inner.Item().LineHorizontal(1).LineColor(PdfColors.Black);
                                inner.Item().PaddingTop(5).Text("Recibió (Nombre y Firma)").FontSize(11).SemiBold();
                                inner.Item().Text("Administrador / Gerente").FontSize(9).FontColor(PdfColors.Grey.Medium);
                            });
                        });
                    });

                    // 3. PIE DE PÁGINA
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("☺");
                        x.CurrentPageNumber();
                        x.Span(" de ");
                        x.TotalPages();
                    });
                });
            })
            .GeneratePdf(rutaArchivo);

            Process.Start(new ProcessStartInfo(rutaArchivo) { UseShellExecute = true });
        }

        public static byte[] CapturarElementoComoImagen(UIElement elementoGrafica)
        {
            if (elementoGrafica == null) return null;
            elementoGrafica.UpdateLayout();

            int width = (int)Math.Max(1, elementoGrafica.RenderSize.Width);
            int height = (int)Math.Max(1, elementoGrafica.RenderSize.Height);

            var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(elementoGrafica);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }
}