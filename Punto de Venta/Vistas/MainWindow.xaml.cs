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

            // Configuración de Licencia de QuestPDF (Requerido para la versión actual)
            QuestPDF.Settings.License = LicenseType.Community;

            // =========================================================
            // APLICAR NOMBRE DEL NEGOCIO DINÁMICO AL DASHBOARD
            // =========================================================
            try
            {
                var config = AppConfig.Load();
                if (!string.IsNullOrWhiteSpace(config.NombreNegocio))
                {
                    // Asegúrate de haberle puesto estos x:Name a tus TextBlocks en MainWindow.xaml
                    if (FindName("TxtMiniLogo") is TextBlock miniLogo)
                        miniLogo.Text = config.NombreNegocio;

                    if (FindName("TxtTituloDashboard") is TextBlock tituloDash)
                        tituloDash.Text = config.NombreNegocio;
                }
            }
            catch { } // Evita que un fallo al leer el archivo impida que la ventana abra

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

            // REPARACIÓN DEL PANTALLAZO EN BLANCO: 
            // Forzar que el UserControl vuelva a ser visible, ya que el evento ESC lo oculta internamente.
            _vistasCache[nombreVista].Visibility = Visibility.Visible;

            GridDashboard.Visibility = Visibility.Collapsed;
            ContenedorVistas.Content = _vistasCache[nombreVista];
            ContenedorVistas.Visibility = Visibility.Visible;

            // Foco retardado para asegurar que la vista activa reciba los eventos del teclado
            Dispatcher.BeginInvoke(new Action(() => {
                _vistasCache[nombreVista].Focus();
            }), DispatcherPriority.Loaded);
        }

        public void RegresarAlMenu()
        {
            // 1. Ocultar y limpiar el contenedor de la vista modular
            ContenedorVistas.Visibility = Visibility.Collapsed;
            ContenedorVistas.Content = null;

            // 2. Volver a mostrar el panel de inicio (Dashboard Bento)
            if (GridDashboard != null)
            {
                GridDashboard.Visibility = Visibility.Visible;
            }

            // 3. Devolver el foco a la ventana principal
            this.Focus();
            Keyboard.Focus(this);
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
            // REPARACIÓN CLAVE DEL ESC: Si el modal de exportar está abierto, lo cierra.
            if (ExportarOverlay != null && ExportarOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    CerrarExportar();
                    e.Handled = true;
                }
                return;
            }

            // REPARACIÓN CLAVE DEL ESC: Si un módulo está abierto, dejamos que el UserControl 
            // y sus modales internos manejen el ESC por sí mismos.
            if (ContenedorVistas.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    // Dejamos pasar el evento para que llegue al UserControl activo
                    return;
                }
                // Retornamos para evitar que estando dentro de un módulo, presiones teclas del menú general.
                return;
            }

            // Atajos directos desde el menú de inicio
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

        #region Importación de Base de Datos (Txt)
        private void BtnSubirLista_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar lista de productos para importar a SQLite"
            };

            if (openFileDialog.ShowDialog() != true) return;

            try
            {
                var lineasNuevas = File.ReadAllLines(openFileDialog.FileName, Encoding.UTF8);
                var productosExistentes = ProductoDB.Load();
                var catalogoPorId = productosExistentes.ToDictionary(p => p.Id);

                int maxId = catalogoPorId.Keys.DefaultIfEmpty(0).Max();
                int importadosCount = 0;

                foreach (var linea in lineasNuevas)
                {
                    if (string.IsNullOrWhiteSpace(linea)) continue;
                    var prod = ParsearLineaProducto(linea);
                    if (prod == null) continue;

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

                ProductoDB.Save(catalogoPorId.Values);
                ToastNotification.Show($"Se importaron y guardaron {importadosCount} productos en SQLite correctamente", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al importar archivo: {ex.Message}", ToastType.Error);
            }
        }

        private static Producto? ParsearLineaProducto(string linea)
        {
            var partes = linea.Split(new[] { "//" }, StringSplitOptions.None);
            if (partes.Length < 4) return null;

            int.TryParse(partes[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
            var codigo = partes[1].Trim();
            var nombre = partes[2].Trim();
            if (string.IsNullOrWhiteSpace(nombre)) return null;

            var categoria = "N/A";
            decimal costo = 0m, ganancia = 0m, precio = 0m;
            int stock = 0;
            bool pedirCantidad = false, seVendePorPeso = false;

            if (partes.Length >= 10)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3].Trim();
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
                int.TryParse(partes[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stock);
                bool.TryParse(partes[8].Trim(), out pedirCantidad);
                bool.TryParse(partes[9].Trim(), out seVendePorPeso);
            }
            else if (partes.Length == 9)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3].Trim();
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
                int.TryParse(partes[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stock);
                bool.TryParse(partes[8].Trim(), out pedirCantidad);
            }
            else if (partes.Length == 8)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3].Trim();
                costo = ParseDecimalFlexible(partes[4]);
                ganancia = ParseDecimalFlexible(partes[5]);
                precio = ParseDecimalFlexible(partes[6]);
                int.TryParse(partes[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stock);
            }
            else if (partes.Length == 7)
            {
                categoria = string.IsNullOrWhiteSpace(partes[3]) ? "N/A" : partes[3].Trim();
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
                categoria = string.IsNullOrWhiteSpace(partes[4]) ? "N/A" : partes[4].Trim();
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
            var dialog = new SaveFileDialog
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

        // --- MÉTODOS DE DIBUJO DE QUESTPDF ---

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
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Lato));

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
                            columns.ConstantColumn(30); // ID
                            columns.ConstantColumn(90); // Codigo
                            columns.RelativeColumn();   // Nombre
                            columns.ConstantColumn(40); // Stock
                            columns.ConstantColumn(55); // Costo
                            columns.ConstantColumn(55); // Ganancia
                            columns.ConstantColumn(60); // Precio
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
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(3).Text(product.Stock.ToString());
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
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Lato));

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
                            columns.ConstantColumn(30); // ID
                            columns.RelativeColumn();   // Cliente/Concepto
                            columns.ConstantColumn(70); // Original
                            columns.ConstantColumn(70); // Abonado
                            columns.ConstantColumn(80); // Restante
                            columns.ConstantColumn(70); // Estado
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
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Lato));

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

                                var itemsDelDia = new List<(string Nombre, int Cantidad, decimal Subtotal)>();
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
                                            var match = System.Text.RegularExpressions.Regex.Match(part, @"^(?<name>.+?)\s+x(?<qty>\d+)$");
                                            if (match.Success) itemsDelDia.Add((match.Groups["name"].Value.Trim(), int.Parse(match.Groups["qty"].Value), 0m));
                                            else itemsDelDia.Add((part.Trim(), 1, 0m));
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

                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten4).PaddingVertical(2).Text(cant.ToString());
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