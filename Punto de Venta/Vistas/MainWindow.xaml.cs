using Microsoft.Win32;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using Punto_de_Venta.Vistas;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public partial class MainWindow : Window
    {
        #region Campos Privados y Caché
        private readonly DispatcherTimer _clockTimer;
        private bool _isShuttingDown;
        private readonly Dictionary<string, UserControl> _vistasCache = new Dictionary<string, UserControl>();
        #endregion

        #region Inicialización
        public MainWindow()
        {
            InitializeComponent();

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = true;

            try
            {
                var config = AppConfig.Load();
                if (!string.IsNullOrWhiteSpace(config.NombreNegocio))
                {
                    if (FindName("TxtMiniLogo") is TextBlock miniLogo)
                        miniLogo.Text = config.NombreNegocio;

                    if (FindName("TxtTituloDashboard") is TextBlock tituloDash)
                        tituloDash.Text = config.NombreNegocio;
                }
            }
            catch { }

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (_, _) => UpdateClock();

            Closing += (_, _) =>
            {
                if (!_isShuttingDown)
                {
                    _isShuttingDown = true;

                    try
                    {
                        var config = AppConfig.Load();
                        BackupService.GenerarRespaldo("Cierre", config.RutaRespaldos);
                    }
                    catch
                    {
                        BackupService.GenerarRespaldo("Cierre");
                    }

                    Application.Current.Shutdown();
                }
            };

            UpdateClock();
            _clockTimer.Start();
        }
        #endregion

        #region Gestión de Ventana y Reloj
        private void ModernTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateClock()
        {
            var now = DateTime.Now;
            var spanishCulture = CultureInfo.GetCultureInfo("es-MX");

            if (FindName("ClockText") is TextBlock clockBlock)
            {
                clockBlock.Text = now.ToString("hh:mm:ss tt", CultureInfo.GetCultureInfo("en-US"));
            }

            if (FindName("DateText") is TextBlock dateBlock)
            {
                dateBlock.Text = now.ToString("dddd, d 'de' MMMM 'de' yyyy", spanishCulture);
            }
        }
        #endregion

        #region Navegación y Rutas (Vistas Modulares)
        private void AbrirVista<T>() where T : UserControl, new()
        {
            string nombreVista = typeof(T).Name;

            if (!_vistasCache.ContainsKey(nombreVista))
            {
                _vistasCache[nombreVista] = new T();
            }

            _vistasCache[nombreVista].Visibility = Visibility.Visible;

            GridDashboard.Visibility = Visibility.Collapsed;
            ContenedorVistas.Content = _vistasCache[nombreVista];
            ContenedorVistas.Visibility = Visibility.Visible;

            Dispatcher.BeginInvoke(new Action(() => {
                _vistasCache[nombreVista].Focus();
            }), DispatcherPriority.Loaded);
        }

        public void RegresarAlMenu()
        {
            ContenedorVistas.Visibility = Visibility.Collapsed;
            ContenedorVistas.Content = null;

            if (GridDashboard != null)
            {
                GridDashboard.Visibility = Visibility.Visible;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                this.Focus();
                Keyboard.Focus(this);
            }), DispatcherPriority.Input);
        }

        private void Cobro_Click(object sender, RoutedEventArgs e) => AbrirVista<Cobro>();
        private void Prestamos_Click(object sender, RoutedEventArgs e) => AbrirVista<Prestamos>();
        private void CodigosDeBarras_Click(object sender, RoutedEventArgs e) => AbrirVista<CodigosBarrasWindow>();

        private void Inventario_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Inventario>();
        }

        private void Ventas_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Ventas>();
        }

        private void Graficas_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Graficas>();
        }

        private void Configuracion_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Configuracion>();
        }

        private void Promociones_Click(object sender, RoutedEventArgs e)
        {
            if (SolicitarAutorizacion()) AbrirVista<Promociones>();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ExportarOverlay != null && ExportarOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    CerrarExportar();
                    e.Handled = true;
                }
                return;
            }

            if (ImportarOverlay != null && ImportarOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    CerrarImportar();
                    e.Handled = true;
                }
                return;
            }

            if (ContenedorVistas.Visibility == Visibility.Visible)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.D1:
                    Cobro_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D2:
                    Ventas_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D3:
                    Inventario_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D4:
                    Prestamos_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D5:
                    Graficas_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D6:
                    Configuracion_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D7:
                    Exportar_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.D8:
                    Promociones_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }

        private bool SolicitarAutorizacion()
        {
            var config = AppConfig.Load();
            if (!config.SolicitarPinAdmin) return true;

            var modal = new AutorizacionModal { Owner = this };
            modal.ShowDialog();
            return modal.Autorizado;
        }
        #endregion

        #region Importación de Base de Datos (Archivos CSV)
        private void AbrirModalImportar_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("ImportarOverlay") is Grid overlay)
            {
                overlay.Visibility = Visibility.Visible;
                overlay.Focus();
            }
        }

        private void ImportarOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FindName("ImportarOverlay") is Grid overlay && ReferenceEquals(e.OriginalSource, overlay))
            {
                CerrarImportar();
            }
        }

        private void CerrarImportar()
        {
            if (FindName("ImportarOverlay") is Grid overlay) overlay.Visibility = Visibility.Collapsed;
        }

        private void EjecutarImportacion_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar lista de productos para importar a SQLite"
            };

            if (openFileDialog.ShowDialog() != true) return;

            try
            {
                var lineasNuevas = File.ReadAllLines(openFileDialog.FileName, Encoding.UTF8);

                if (lineasNuevas.Length == 0)
                {
                    ToastNotification.Show("El archivo seleccionado está completamente vacío.", ToastType.Warning);
                    return;
                }

                var productosExistentes = ProductoDB.Load();
                var catalogoPorId = productosExistentes.ToDictionary(p => p.Id);

                int maxId = catalogoPorId.Keys.DefaultIfEmpty(0).Max();
                int importadosCount = 0;
                int omitidosCount = 0;

                foreach (var linea in lineasNuevas)
                {
                    if (string.IsNullOrWhiteSpace(linea)) continue;

                    var prod = ParsearLineaProducto(linea);
                    if (prod == null)
                    {
                        omitidosCount++;
                        continue;
                    }

                    if (prod.Id <= 0 || !catalogoPorId.ContainsKey(prod.Id))
                    {
                        var existentePorCodigo = catalogoPorId.Values.FirstOrDefault(p => !string.IsNullOrEmpty(p.Codigo) && p.Codigo == prod.Codigo);
                        if (existentePorCodigo != null)
                        {
                            prod.Id = existentePorCodigo.Id;
                            catalogoPorId[prod.Id] = prod;
                        }
                        else
                        {
                            maxId++;
                            prod.Id = maxId;
                            catalogoPorId[prod.Id] = prod;
                        }
                    }
                    else
                    {
                        catalogoPorId[prod.Id] = prod;
                    }
                    importadosCount++;
                }

                if (importadosCount > 0)
                {
                    ProductoDB.Save(catalogoPorId.Values);
                    CerrarImportar();

                    if (omitidosCount > 1)
                    {
                        ToastNotification.Show($"Se importaron {importadosCount} productos. Se omitieron {omitidosCount - 1} filas con formato incorrecto.", ToastType.Info);
                    }
                    else
                    {
                        ToastNotification.Show($"Se importaron y guardaron {importadosCount} productos exitosamente.", ToastType.Success);
                    }
                }
                else
                {
                    ToastNotification.Show("No se detectó ningún producto válido. Verifica que el archivo sea un CSV con el orden de columnas correcto.", ToastType.Warning);
                }
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error crítico al leer el archivo: {ex.Message}", ToastType.Error);
            }
        }

        private static Producto? ParsearLineaProducto(string linea)
        {
            var partes = linea.Split(',');
            if (partes.Length < 4)
            {
                partes = linea.Split(';');
                if (partes.Length < 4) return null;
            }

            for (int i = 0; i < partes.Length; i++)
            {
                partes[i] = partes[i].Trim(' ', '"');
            }

            int.TryParse(partes[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
            var codigo = partes[1];
            var nombre = partes[2];

            if (string.IsNullOrWhiteSpace(nombre) || nombre.Equals("Nombre", StringComparison.OrdinalIgnoreCase))
                return null;

            var categoria = "N/A";
            decimal costo = 0m, ganancia = 0m, precio = 0m;
            decimal stock = 0m;
            bool pedirCantidad = false, seVendePorPeso = false;

            if (partes.Length >= 10)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3];
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
                decimal.TryParse(partes[7], NumberStyles.Any, CultureInfo.InvariantCulture, out stock);
                bool.TryParse(partes[8], out pedirCantidad);
                bool.TryParse(partes[9], out seVendePorPeso);
            }
            else if (partes.Length == 9)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3];
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
                decimal.TryParse(partes[7], NumberStyles.Any, CultureInfo.InvariantCulture, out stock);
                bool.TryParse(partes[8], out pedirCantidad);
            }
            else if (partes.Length == 8)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3];
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
                decimal.TryParse(partes[7], NumberStyles.Any, CultureInfo.InvariantCulture, out stock);
            }
            else if (partes.Length == 7)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3];
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
            }
            else if (partes.Length == 6)
            {
                costo = ParseDecimalFlexible(partes[3]);
                ganancia = ParseDecimalFlexible(partes[4]);
                precio = ParseDecimalFlexible(partes[5]);
            }
            else if (partes.Length == 5)
            {
                precio = ParseDecimalFlexible(partes[3]);
                categoria = string.IsNullOrWhiteSpace(partes[4]) ? "N/A" : partes[4];
                ganancia = precio;
            }
            else if (partes.Length == 4)
            {
                precio = ParseDecimalFlexible(partes[3]);
                ganancia = precio;
            }

            if (precio == 0m && (costo > 0m || ganancia > 0m))
            {
                precio = costo + ganancia;
            }

            return new Producto
            {
                Id = id,
                Codigo = codigo,
                Nombre = nombre,
                Categoria = categoria,
                Costo = costo,
                Ganancia = ganancia,
                Precio = precio,
                Stock = stock,
                PedirCantidadAlCobrar = pedirCantidad,
                SeVendePorPeso = seVendePorPeso
            };
        }

        private static decimal ParseDecimalFlexible(string text)
        {
            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            if (decimal.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out var val) ||
                decimal.TryParse(text.Trim(), styles, CultureInfo.GetCultureInfo("es-MX"), out val))
            {
                return val;
            }
            return 0m;
        }
        #endregion

        #region Exportación (Archivos PDF)
        private void Exportar_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("ExportarOverlay") is Grid overlay)
            {
                overlay.Visibility = Visibility.Visible;
                overlay.Focus();
            }
        }

        private void ExportarOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FindName("ExportarOverlay") is Grid overlay && ReferenceEquals(e.OriginalSource, overlay))
            {
                CerrarExportar();
            }
        }

        private void ExportarOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CerrarExportar();
                e.Handled = true;
            }
        }

        private void CerrarExportar()
        {
            if (FindName("ExportarOverlay") is Grid overlay) overlay.Visibility = Visibility.Collapsed;
        }

        private void MostrarErrorExportacion(Exception exception)
        {
            CerrarExportar();
            ToastNotification.Show($"No se pudo generar el reporte: {exception.Message}", ToastType.Error);
        }

        private void ExportarPdf(string suggestedName, Action<string> generatePdfAction)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar reporte en PDF",
                Filter = "Archivo PDF (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName = suggestedName
            };

            try
            {
                if (dialog.ShowDialog(this) == true)
                {
                    generatePdfAction(dialog.FileName);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                    ToastNotification.Show("Reporte PDF generado exitosamente", ToastType.Success);
                }
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
            finally
            {
                CerrarExportar();
            }
        }

        private void ExportarVentas_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var hoy = DateTime.Today;
                var inicioSemana = hoy.AddDays(-6);
                var sales = VentaDB.Load().Where(sale =>
                {
                    var fechaLocal = sale.FechaHora.ToLocalTime().Date;
                    return sale.Estado != "Cancelada" && fechaLocal >= inicioSemana && fechaLocal <= hoy;
                }).ToList();

                ExportarPdf("Reporte_Ventas_Semanal.pdf", path => GenerarPdfVentas(sales, path));
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
        }

        private void ExportarProductos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportarPdf("Catalogo_Productos.pdf", path => GenerarPdfProductos(ProductoDB.Load(), path));
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
        }

        private void ExportarPrestamos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportarPdf("Libreta_Deudores.pdf", path => GenerarPdfPrestamos(PrestamoDB.Load(), path));
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
        }
        private static void GenerarPdfProductos(IEnumerable<Producto> products, string filePath)
        {
            var productList = products.OrderBy(p => p.Id).ToList();
            var culture = CultureInfo.GetCultureInfo("es-MX");

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.SegoeUI));

                    page.Header().Column(c =>
                    {
                        c.Item().Text("CATÁLOGO DE PRODUCTOS").SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);
                        c.Item().Text($"Total de productos registrados: {productList.Count}").FontSize(11).FontColor(Colors.Grey.Darken1);
                        c.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    page.Content().PaddingVertical(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(30);
                            columns.ConstantColumn(90);
                            columns.RelativeColumn();
                            columns.ConstantColumn(40);
                            columns.ConstantColumn(55);
                            columns.ConstantColumn(55);
                            columns.ConstantColumn(60);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("ID").SemiBold();
                            header.Cell().Text("CÓDIGO").SemiBold();
                            header.Cell().Text("NOMBRE").SemiBold();
                            header.Cell().Text("STOCK").SemiBold();
                            header.Cell().Text("COSTO").SemiBold().AlignRight();
                            header.Cell().Text("GANANCIA").SemiBold().AlignRight();
                            header.Cell().Text("PRECIO").SemiBold().AlignRight();
                        });

                        foreach (var product in productList)
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3).Text(product.Id.ToString());
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3).Text(product.Codigo ?? "");
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3).Text(product.Nombre);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3).Text(product.Stock.ToString("0.###"));
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3).Text(product.Costo.ToString("C2", culture)).AlignRight();
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3).Text(product.Ganancia.ToString("C2", culture)).AlignRight();
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3).Text(product.Precio.ToString("C2", culture)).AlignRight().SemiBold();
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Página ");
                        x.CurrentPageNumber();
                        x.Span(" de ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf(filePath);
        }

        private static void GenerarPdfPrestamos(IEnumerable<Deudor> debtors, string filePath)
        {
            var debtorList = debtors.OrderBy(d => d.Id).ToList();
            var culture = CultureInfo.GetCultureInfo("es-MX");

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.SegoeUI));

                    page.Header().Column(c =>
                    {
                        c.Item().Text("REPORTE DE PRÉSTAMOS Y DEUDAS").SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);
                        c.Item().Text($"TOTAL SALDO PENDIENTE POR COBRAR: {debtorList.Sum(d => d.SaldoRestante).ToString("C2", culture)}").FontSize(12).FontColor(Colors.Red.Darken2).SemiBold();
                        c.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    page.Content().PaddingVertical(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(30);
                            columns.RelativeColumn();
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(70);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("ID").SemiBold();
                            header.Cell().Text("CLIENTE / CONCEPTO").SemiBold();
                            header.Cell().Text("DEUDA").SemiBold().AlignRight();
                            header.Cell().Text("ABONADO").SemiBold().AlignRight();
                            header.Cell().Text("RESTANTE").SemiBold().AlignRight();
                            header.Cell().Text("ESTADO").SemiBold().AlignCenter();
                        });

                        foreach (var debtor in debtorList)
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).Text(debtor.Id.ToString());

                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).Column(col =>
                            {
                                col.Item().Text(debtor.Nombre).SemiBold();
                                col.Item().Text(debtor.Concepto ?? "").FontSize(9).FontColor(Colors.Grey.Darken2);
                            });

                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).Text(debtor.MontoOriginal.ToString("C2", culture)).AlignRight();
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).Text(debtor.TotalAbonado.ToString("C2", culture)).AlignRight();
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).Text(debtor.SaldoRestante.ToString("C2", culture)).AlignRight().SemiBold();

                            var estadoColor = debtor.EstaSaldada ? Colors.Green.Medium : Colors.Orange.Medium;
                            var estadoTexto = debtor.EstaSaldada ? "SALDADA" : "PENDIENTE";
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).Text(estadoTexto).FontColor(estadoColor).SemiBold().AlignCenter();
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Página ");
                        x.CurrentPageNumber();
                        x.Span(" de ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf(filePath);
        }

        private static void GenerarPdfVentas(IReadOnlyCollection<Venta> sales, string filePath)
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var today = DateTime.Today;
            var weekStart = today.AddDays(-6);

            var totalWeek = 0m;
            var totalProfitWeek = 0m;

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.SegoeUI));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("RESUMEN DE VENTAS (SEMANAL)").SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);
                        col.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        for (var day = weekStart; day <= today; day = day.AddDays(1))
                        {
                            var dailySales = sales.Where(s => s.FechaHora.ToLocalTime().Date == day).ToList();
                            var dailyTotal = dailySales.Sum(s => s.Total);
                            var dailyProfit = dailySales.Sum(s => s.GananciaTotal);

                            totalWeek += dailyTotal;
                            totalProfitWeek += dailyProfit;

                            col.Item().PaddingTop(10).Text($"Día: {day.ToString("dd/MM/yyyy", culture)}").Bold().FontSize(12);

                            col.Item().PaddingTop(5).PaddingBottom(5).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(40);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(80);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Text("Cant").SemiBold();
                                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Text("Producto").SemiBold();
                                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Text("Subtotal").SemiBold().AlignRight();
                                });

                                var itemsDelDia = new List<(string Nombre, decimal Cantidad, decimal Subtotal)>();
                                foreach (var sale in dailySales)
                                {
                                    if (sale.Productos.Count > 0)
                                    {
                                        itemsDelDia.AddRange(sale.Productos.Select(p => (p.Nombre, p.Cantidad, p.Subtotal)));
                                    }
                                    else if (!string.IsNullOrWhiteSpace(sale.ProductosResumen))
                                    {
                                        foreach (var part in sale.ProductosResumen.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            var match = System.Text.RegularExpressions.Regex.Match(part, @"^(?<name>.+?)\s+x(?<qty>[\d\.]+)$");
                                            if (match.Success && decimal.TryParse(match.Groups["qty"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedQty))
                                                itemsDelDia.Add((match.Groups["name"].Value.Trim(), parsedQty, 0m));
                                            else itemsDelDia.Add((part.Trim(), 1m, 0m));
                                        }
                                    }
                                }

                                var agrupados = itemsDelDia.GroupBy(i => i.Nombre, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key);
                                bool hasItems = false;
                                foreach (var g in agrupados)
                                {
                                    hasItems = true;
                                    var cant = g.Sum(x => x.Cantidad);
                                    var sub = g.Sum(x => x.Subtotal);

                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten4).PaddingVertical(2).Text(cant.ToString("0.###", culture));
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten4).PaddingVertical(2).Text(g.Key);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten4).PaddingVertical(2).Text(sub > 0 ? sub.ToString("C2", culture) : "-").AlignRight();
                                }

                                if (!hasItems)
                                {
                                    table.Cell().ColumnSpan(3).Text("(Sin ventas registradas)").Italic().FontColor(Colors.Grey.Medium);
                                }
                            });

                            col.Item().PaddingBottom(10).AlignRight().Text($"Total del día: {dailyTotal.ToString("C2", culture)} | Ganancia: {dailyProfit.ToString("C2", culture)}").SemiBold().FontColor(Colors.Green.Darken2);
                            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten3);
                        }

                        col.Item().PaddingTop(20).Background(Colors.Grey.Lighten4).Padding(10).Column(totals =>
                        {
                            totals.Item().Text($"GRAN TOTAL SEMANAL: {totalWeek.ToString("C2", culture)}").Bold().FontSize(14);
                            totals.Item().Text($"GANANCIA TOTAL SEMANAL: {totalProfitWeek.ToString("C2", culture)}").Bold().FontSize(14).FontColor(Colors.Green.Darken2);
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Página ");
                        x.CurrentPageNumber();
                        x.Span(" de ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf(filePath);
        }
        #endregion
    }
}