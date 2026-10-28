using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using Punto_de_Venta.Vistas;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public partial class Ventas : UserControl
    {
        private readonly DispatcherTimer _clockTimer;
        private List<Producto> _catalog = [];
        private List<MovimientoCaja> _movimientos = [];
        private Venta? _selectedSale;
        private bool _loadFailed;
        private string _periodoSeleccionado = "Dia";
        private DateTime _filtroInicio;
        private DateTime _filtroFin;

        public ObservableCollection<Venta> Sales { get; } = [];
        public ICollectionView SalesView { get; }

        public Ventas()
        {
            InitializeComponent();
            Language = XmlLanguage.GetLanguage("es-MX");

            SalesView = CollectionViewSource.GetDefaultView(Sales);
            SalesView.SortDescriptions.Clear();
            SalesView.SortDescriptions.Add(new SortDescription(nameof(Venta.FechaHora), ListSortDirection.Descending));
            SalesView.SortDescriptions.Add(new SortDescription(nameof(Venta.Id), ListSortDirection.Descending));
            SalesView.Filter = MatchesFilter;
            DataContext = this;

            ActualizarRangoPeriodo("Dia");
            ReturnButton.IsEnabled = false;

            ActualizarEstiloBotonesPeriodo();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => { ClockText.Text = DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US")); };
            _clockTimer.Start();
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility == Visibility.Visible)
            {
                RecargarDatosDesdeBD();
                FocusSearch();
                this.Focus();
                Keyboard.Focus(this);
            }
        }

        private void RecargarDatosDesdeBD()
        {
            try
            {
                Sales.Clear();
                _loadFailed = false;
                try { _catalog = ProductoDB.Load(); } catch { _catalog = []; }
                CargarMovimientosCaja();
                var loadedSales = VentaDB.Load().OrderByDescending(s => s.FechaHora).ThenByDescending(s => s.Id);
                foreach (var sale in loadedSales)
                {
                    HydrateSaleProfit(sale);
                    Sales.Add(sale);
                }
                SalesView.Refresh();
                RefreshMetrics();
            }
            catch (Exception exception)
            {
                _loadFailed = true;
                CustomMessageBox.Show(Application.Current.MainWindow, exception.Message, "No se pudo cargar el historial", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CargarMovimientosCaja()
        {
            try { _movimientos = MovimientoCajaDB.Load() ?? []; }
            catch { _movimientos = []; }
        }

        public void BtnPeriodo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string periodo)
            {
                _periodoSeleccionado = periodo;
                ActualizarRangoPeriodo(periodo);
                ActualizarEstiloBotonesPeriodo();
                CustomDatePanel.Visibility = periodo == "Custom" ? Visibility.Visible : Visibility.Collapsed;
                SalesView.Refresh();
                RefreshMetrics();
            }
        }

        public void DatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_periodoSeleccionado == "Custom")
            {
                _filtroInicio = DatePickerDesde.SelectedDate?.Date ?? DateTime.Today;
                _filtroFin = (DatePickerHasta.SelectedDate?.Date ?? DateTime.Today).AddDays(1).AddTicks(-1);
                SalesView.Refresh();
                RefreshMetrics();
            }
        }

        private void ActualizarRangoPeriodo(string periodo)
        {
            var ahora = DateTime.Now;
            switch (periodo)
            {
                case "Dia": _filtroInicio = DateTime.Today; _filtroFin = DateTime.Today.AddDays(1).AddTicks(-1); break;
                case "Semana": var diff = (7 + (ahora.DayOfWeek - DayOfWeek.Monday)) % 7; _filtroInicio = ahora.Date.AddDays(-1 * diff); _filtroFin = _filtroInicio.AddDays(7).AddTicks(-1); break;
                case "Mes": _filtroInicio = new DateTime(ahora.Year, ahora.Month, 1); _filtroFin = _filtroInicio.AddMonths(1).AddTicks(-1); break;
                case "Custom": _filtroInicio = DatePickerDesde.SelectedDate?.Date ?? DateTime.Today; _filtroFin = (DatePickerHasta.SelectedDate?.Date ?? DateTime.Today).AddDays(1).AddTicks(-1); break;
            }
        }

        private void ActualizarEstiloBotonesPeriodo()
        {
            SetPillStyle(BtnPeriodoDia, _periodoSeleccionado == "Dia");
            SetPillStyle(BtnPeriodoSemana, _periodoSeleccionado == "Semana");
            SetPillStyle(BtnPeriodoMes, _periodoSeleccionado == "Mes");
            SetPillStyle(BtnPeriodoCustom, _periodoSeleccionado == "Custom");
        }

        private static void SetPillStyle(Button? btn, bool active)
        {
            if (btn == null) return;
            if (active) { btn.SetResourceReference(Button.BackgroundProperty, "ThemeSurfaceBackground"); btn.SetResourceReference(Button.ForegroundProperty, "ThemeAccent"); }
            else { btn.Background = Brushes.Transparent; btn.SetResourceReference(Button.ForegroundProperty, "ThemeMutedForeground"); }
        }

        private bool MatchesFilter(object item)
        {
            if (item is not Venta sale) return false;
            if (sale.FechaHora < _filtroInicio || sale.FechaHora > _filtroFin) return false;
            var query = SearchBox.Text.Trim();
            if (query.Length == 0) return true;
            return sale.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) || sale.FechaHora.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-MX")).Contains(query, StringComparison.OrdinalIgnoreCase) || sale.FechaHora.ToString("d", CultureInfo.GetCultureInfo("es-MX")).Contains(query, StringComparison.OrdinalIgnoreCase) || sale.ProductosResumen.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            SalesView.Refresh();
        }

        private void RefreshMetrics()
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var periodSales = Sales.Where(s => s.FechaHora >= _filtroInicio && s.FechaHora <= _filtroFin && !string.Equals(s.Estado, "Cancelada", StringComparison.OrdinalIgnoreCase)).ToList();
            var totalVentas = periodSales.Sum(s => s.Total);
            var totalItems = periodSales.Sum(s => s.CantidadTotal);
            var totalProfit = periodSales.Sum(s => s.GananciaTotal);
            var totalGastos = _movimientos.Where(m => m.FechaHora >= _filtroInicio && m.FechaHora <= _filtroFin && (m.Tipo.Equals("Salida", StringComparison.OrdinalIgnoreCase) || m.Tipo.Equals("Gasto", StringComparison.OrdinalIgnoreCase))).Sum(m => m.Monto);
            var fondosIniciales = _movimientos.Where(m => m.FechaHora >= _filtroInicio && m.FechaHora <= _filtroFin && m.Tipo.Equals("Fondo", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Monto);
            var balanceNeto = fondosIniciales + totalVentas - totalGastos;

            TotalSalesTodayText.Text = totalVentas.ToString("C2", culture);
            TotalProfitText.Text = totalProfit.ToString("C2", culture);
            TotalProductsSoldText.Text = $"{totalItems} pzas";
            MetricGastosText.Text = totalGastos.ToString("C2", culture);
            MetricBalanceText.Text = balanceNeto.ToString("C2", culture);
        }

        private void HydrateSaleProfit(Venta sale)
        {
            if (sale.Productos.Count > 0)
            {
                foreach (var item in sale.Productos)
                {
                    if (item.Ganancia <= 0)
                    {
                        var catalogProd = _catalog.FirstOrDefault(p => p.Id == item.Id || string.Equals(p.Nombre, item.Nombre, StringComparison.OrdinalIgnoreCase));
                        if (catalogProd is not null && catalogProd.Ganancia > 0) { item.Costo = catalogProd.Costo; item.Ganancia = catalogProd.Ganancia; }
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(sale.ProductosResumen))
            {
                var parsedItems = ParseProductSummary(sale.ProductosResumen);
                foreach (var (name, quantity) in parsedItems)
                {
                    var catalogProd = _catalog.FirstOrDefault(p => string.Equals(p.Nombre, name, StringComparison.OrdinalIgnoreCase));
                    var cost = catalogProd?.Costo ?? 0m;
                    var margin = catalogProd?.Ganancia ?? 0m;
                    var price = catalogProd?.Precio ?? (sale.Total > 0 && sale.CantidadTotal > 0 ? sale.Total / sale.CantidadTotal : 0m);
                    sale.Productos.Add(new DetalleVenta { Id = catalogProd?.Id ?? 0, VentaId = sale.Id, Nombre = name, Cantidad = quantity, Costo = cost, Ganancia = margin, Precio = price, Categoria = catalogProd?.Categoria ?? "N/A" });
                }
            }
            sale.NotifyTotalesChanged();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => FocusSearch();

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RegresarAlMenu();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.F7)
            {
                ReprintTicketButton_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.F8)
            {
                ExportButton_Click(null, null);
                e.Handled = true;
            }
        }
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            FocusSearch();
        }
        private void FocusSearch()
        {
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (SearchBox != null)
                {
                    this.Focus();
                    SearchBox.Focus();
                    Keyboard.Focus(SearchBox);
                }
            }));
        }

        private void SalesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedSale = SalesGrid.SelectedItem as Venta;
            ReturnButton.IsEnabled = !_loadFailed && (_selectedSale?.Estado == "Abierta" || _selectedSale?.Estado == "Fiado");
        }

        private void SalesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedSale != null)
            {
                ViewDetailsButton_Click(sender, e);
            }
        }

        private void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSale is null) { ToastNotification.Show("Selecciona una venta de la lista", ToastType.Warning); return; }
            string details;
            if (_selectedSale.Productos.Count > 0)
            {
                details = string.Join(Environment.NewLine, _selectedSale.Productos.Select(p => $"• {p.Nombre} — {p.Cantidad} pza(s) a {p.Precio.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))} (Ganancia: {(p.Ganancia * p.Cantidad).ToString("C2", CultureInfo.GetCultureInfo("es-MX"))})"));
            }
            else
            {
                var detailLines = ParseProductSummary(_selectedSale.ProductosResumen);
                details = detailLines.Count == 0 ? "No hay productos guardados en el resumen de esta venta." : string.Join(Environment.NewLine, detailLines.Select(item => $"• {item.Name} — {item.Quantity} pieza(s)"));
            }
            var profitTotal = _selectedSale.GananciaTotal;
            var text = $"Venta #{_selectedSale.Id}\nFecha: {_selectedSale.FechaHora.ToString("dd/MM/yyyy hh:mm tt", CultureInfo.GetCultureInfo("es-MX"))}\nEstado: {_selectedSale.Estado}\n\n{details}\n\nTotal: {_selectedSale.Total.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}\nGanancia total de esta venta: {profitTotal.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}";
            CustomMessageBox.Show(Application.Current.MainWindow, text, "Datos de venta", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void ReprintTicketButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSale is null)
            {
                ToastNotification.Show("Selecciona una venta para reimprimir el comprobante", ToastType.Warning);
                return;
            }

            var config = AppConfig.Load();
            if (string.IsNullOrWhiteSpace(config.ImpresoraSeleccionada))
            {
                ToastNotification.Show("No hay una impresora configurada en Ajustes.", ToastType.Warning);
                return;
            }

            try
            {
                string textoTicket = TicketService.GenerarTextoTicket(
                    _selectedSale,
                    _selectedSale.Productos,
                    _selectedSale.PagoEfectivo > 0 ? _selectedSale.PagoEfectivo : _selectedSale.Total,
                    _selectedSale.Cambio,
                    config
                );

                byte[] buffer = System.Text.Encoding.ASCII.GetBytes(textoTicket);
                bool impreso = RawPrinterHelper.SendBytesToPrinter(config.ImpresoraSeleccionada, buffer);

                if (impreso)
                    ToastNotification.Show($"🖨️ Copia del Ticket #{_selectedSale.Id} enviada a imprimir.", ToastType.Success);
                else
                    ToastNotification.Show("No se pudo conectar con la impresora térmica.", ToastType.Error);
            }
            catch (Exception ex)
            {
                ViewDetailsButton_Click(null, null);
                ToastNotification.Show($"Impresión fallida: {ex.Message}", ToastType.Error);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (SalesView.IsEmpty)
            {
                ToastNotification.Show("No hay ventas en este periodo para exportar.", ToastType.Warning);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "Archivo PDF (*.pdf)|*.pdf",
                FileName = $"ReporteVentas_{_filtroInicio:ddMMyyyy}_al_{_filtroFin:ddMMyyyy}.pdf",
                Title = "Exportar Historial de Ventas"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var culture = CultureInfo.GetCultureInfo("es-MX");
                    var ventasFiltradas = SalesView.Cast<Venta>().ToList();

                    var totalVentas = ventasFiltradas.Where(v => v.Estado != "Cancelada").Sum(v => v.Total);
                    var totalGanancia = ventasFiltradas.Where(v => v.Estado != "Cancelada").Sum(v => v.GananciaTotal);

                    QuestPDF.Settings.License = LicenseType.Community;
                    QuestPDF.Settings.UseSystemFonts = true;

                    Document.Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Size(PageSizes.A4.Landscape());
                            page.Margin(1, Unit.Centimetre);
                            page.PageColor(QuestPDF.Helpers.Colors.White);
                            page.DefaultTextStyle(x => x.FontSize(9).FontFamily(QuestPDF.Helpers.Fonts.SegoeUI));

                            page.Header().Column(col =>
                            {
                                col.Item().Text("REPORTE DE VENTAS").SemiBold().FontSize(18).FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);
                                col.Item().Text($"Periodo: {_filtroInicio:dd/MM/yyyy} al {_filtroFin:dd/MM/yyyy}").FontSize(11).FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);
                                col.Item().PaddingVertical(5).LineHorizontal(1).LineColor(QuestPDF.Helpers.Colors.Grey.Lighten2);
                            });

                            page.Content().PaddingVertical(10).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(40);
                                    columns.ConstantColumn(85);
                                    columns.ConstantColumn(60);
                                    columns.ConstantColumn(60);
                                    columns.ConstantColumn(40);
                                    columns.ConstantColumn(60);
                                    columns.ConstantColumn(60);
                                    columns.RelativeColumn();
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Text("ID").SemiBold();
                                    header.Cell().Text("FECHA").SemiBold();
                                    header.Cell().Text("ESTADO").SemiBold();
                                    header.Cell().Text("PAGO").SemiBold();
                                    header.Cell().Text("CANT.").SemiBold().AlignRight();
                                    header.Cell().Text("TOTAL").SemiBold().AlignRight();
                                    header.Cell().Text("GANANCIA").SemiBold().AlignRight();
                                    header.Cell().Text("PRODUCTOS").SemiBold().AlignStart();
                                });

                                foreach (var v in ventasFiltradas)
                                {
                                    var textColor = v.Estado == "Cancelada" ? QuestPDF.Helpers.Colors.Red.Medium : QuestPDF.Helpers.Colors.Black;

                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).PaddingVertical(4).Text(v.Id.ToString()).FontColor(textColor);
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).PaddingVertical(4).Text(v.FechaHora.ToString("dd/MM/yyyy HH:mm")).FontColor(textColor);
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).PaddingVertical(4).Text(v.Estado).FontColor(textColor);
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).PaddingVertical(4).Text(v.MetodoPago).FontColor(textColor);
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).PaddingVertical(4).Text(v.CantidadTotal.ToString()).AlignRight().FontColor(textColor);
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).PaddingVertical(4).Text(v.Total.ToString("C2", culture)).AlignRight().FontColor(textColor);
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).PaddingVertical(4).Text(v.GananciaTotal.ToString("C2", culture)).AlignRight().FontColor(textColor);
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten3).PaddingVertical(4).PaddingLeft(10).Text(v.ProductosResumen).FontSize(8).FontColor(textColor);
                                }
                            });

                            page.Footer().Column(f =>
                            {
                                f.Item().PaddingBottom(5).Row(row =>
                                {
                                    row.RelativeItem().Text($"Ventas válidas: {totalVentas.ToString("C2", culture)}").Bold().FontSize(11).FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);
                                    row.RelativeItem().AlignRight().Text($"Ganancia válida: {totalGanancia.ToString("C2", culture)}").Bold().FontSize(11).FontColor(QuestPDF.Helpers.Colors.Green.Darken2);
                                });
                                f.Item().LineHorizontal(1).LineColor(QuestPDF.Helpers.Colors.Grey.Lighten2);
                                f.Item().AlignCenter().PaddingTop(5).Text(x =>
                                {
                                    x.Span("Página ");
                                    x.CurrentPageNumber();
                                    x.Span(" de ");
                                    x.TotalPages();
                                });
                            });
                        });
                    }).GeneratePdf(sfd.FileName);

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    ToastNotification.Show("✅ Reporte PDF generado exitosamente.", ToastType.Success);
                }
                catch (Exception ex)
                {
                    ToastNotification.Show($"Error al generar PDF: {ex.Message}", ToastType.Error);
                }
            }
        }

        private void ReturnButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || _selectedSale is null) return;

            if (_selectedSale.Estado == "Fiado")
            {
                var res = CustomMessageBox.Show(Application.Current.MainWindow, "Esta venta fue fiada. Al cancelarla se repondrá el inventario, pero recuerda ajustar la deuda manualmente en Préstamos si aplica.\n\n¿Deseas continuar?", "Devolución de Fiado", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }
            if (_selectedSale.Estado != "Abierta" && _selectedSale.Estado != "Fiado") { ToastNotification.Show("Solo se pueden devolver ventas abiertas del turno o fiadas", ToastType.Warning); return; }
            var result = CustomMessageBox.Show(Application.Current.MainWindow, "¿Deseas marcar esta venta como devuelta/cancelada?", "Confirmar devolución", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var previousState = _selectedSale.Estado;
            _selectedSale.Estado = "Cancelada";

            if (!TrySave()) { _selectedSale.Estado = previousState; return; }

            try
            {
                var catalog = ProductoDB.Load();
                if (_selectedSale.Productos.Count > 0) { foreach (var item in _selectedSale.Productos) { var prod = catalog.FirstOrDefault(p => p.Id == item.Id || string.Equals(p.Nombre, item.Nombre, StringComparison.OrdinalIgnoreCase)); if (prod != null) prod.Stock += item.Cantidad; } }
                else if (!string.IsNullOrWhiteSpace(_selectedSale.ProductosResumen)) { foreach (var (name, quantity) in ParseProductSummary(_selectedSale.ProductosResumen)) { var prod = catalog.FirstOrDefault(p => string.Equals(p.Nombre, name, StringComparison.OrdinalIgnoreCase)); if (prod != null) prod.Stock += quantity; } }
                ProductoDB.Save(catalog);
                ToastNotification.Show($"Venta #{_selectedSale.Id} cancelada. Stock reincorporado", ToastType.Success);
            }
            catch (Exception ex) { ToastNotification.Show($"Error al reponer inventario: {ex.Message}", ToastType.Error); }

            SalesView.Refresh(); RefreshMetrics();
            ReturnButton.IsEnabled = false;
        }

        private void CashCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite()) return;

            var turnoActivo = TurnoCajaManager.ObtenerTurnoActivo();
            if (turnoActivo == null) { ToastNotification.Show("No hay ningún turno abierto. Primero debe registrar un Fondo de Caja.", ToastType.Info); return; }

            decimal fondo = turnoActivo.FondoInicial;
            decimal entradas = 0m;
            decimal salidas = 0m;

            decimal cashSales = Sales
                .Where(s => s.Estado.Equals("Abierta", StringComparison.OrdinalIgnoreCase))
                .Sum(s =>
                    s.MetodoPago.Equals("Efectivo", StringComparison.OrdinalIgnoreCase) ? s.Total :
                    s.MetodoPago.Equals("Mixto", StringComparison.OrdinalIgnoreCase) ? (s.PagoEfectivo - s.Cambio) : 0m
                );

            using (var conn = DatabaseHelper.GetConnection())
            {
                using (var cmdMovs = conn.CreateCommand())
                {
                    cmdMovs.CommandText = "SELECT COALESCE(SUM(CASE WHEN Tipo = 'Entrada' THEN Monto ELSE 0 END), 0) AS Entradas, COALESCE(SUM(CASE WHEN Tipo = 'Salida' THEN Monto ELSE 0 END), 0) AS Salidas FROM MovimientosCaja WHERE CorteCajaId = @id;";
                    cmdMovs.Parameters.AddWithValue("@id", turnoActivo.Id);
                    using (var reader = cmdMovs.ExecuteReader()) { if (reader.Read()) { entradas = Convert.ToDecimal(reader.GetDouble(0)); salidas = Convert.ToDecimal(reader.GetDouble(1)); } }
                }
            }

            var totalEnCajon = fondo + cashSales + entradas - salidas;

            if (totalEnCajon == 0 && cashSales == 0 && entradas == 0 && salidas == 0 && fondo == 0) { ToastNotification.Show("El turno está en ceros. No hay ventas ni movimientos.", ToastType.Info); return; }

            var modalCiego = new CorteCiegoModal(totalEnCajon) { Owner = Application.Current.MainWindow };
            modalCiego.ShowDialog();
            if (!modalCiego.Realizado) return;

            var contadoFisico = modalCiego.TotalContadoResult;
            var diferencia = contadoFisico - totalEnCajon;
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var diferenciaTexto = diferencia == 0m ? "$0.00 (Exacto)" : (diferencia > 0m ? $"+{diferencia.ToString("C2", culture)} (Sobrante)" : $"{diferencia.ToString("C2", culture)} (Faltante)");

            var desglose = $"--- RESUMEN DE CORTE DE CAJA ---\n\nFondo Inicial:           {fondo.ToString("C2", culture)}\n(+) Ventas en Efectivo:  {cashSales.ToString("C2", culture)}\n(+) Entradas Extras:     {entradas.ToString("C2", culture)}\n(-) Gastos / Retiros:    {salidas.ToString("C2", culture)}\n---------------------------------\nEFECTIVO EN SISTEMA:     {totalEnCajon.ToString("C2", culture)}\nEFECTIVO CONTADO:        {contadoFisico.ToString("C2", culture)}\nDIFERENCIA:              {diferenciaTexto}\n\n¿Deseas confirmar el cierre de caja?";

            var confirmation = CustomMessageBox.Show(Application.Current.MainWindow, desglose, "Confirmar Cierre de Caja", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes) return;

            try
            {
                TurnoCajaManager.CerrarTurno(turnoActivo, contadoFisico, diferenciaTexto, cashSales);
                using (var conn = DatabaseHelper.GetConnection()) { using (var updateVentasCmd = conn.CreateCommand()) { updateVentasCmd.CommandText = "UPDATE Ventas SET Estado = 'Cerrada' WHERE Estado = 'Abierta';"; updateVentasCmd.ExecuteNonQuery(); } }

                RecargarDatosDesdeBD();

                ToastNotification.Show("Corte de caja finalizado exitosamente", ToastType.Success);
            }
            catch (Exception ex) { ToastNotification.Show($"Error al registrar auditoría: {ex.Message}", ToastType.Error); }
        }

        private bool CanWrite() { if (!_loadFailed) return true; ToastNotification.Show("No se encontró ventas.txt, cambios bloqueados", ToastType.Error); return false; }
        private bool TrySave() { try { VentaDB.Save(Sales); _loadFailed = false; return true; } catch (Exception exception) { ToastNotification.Show($"Error al guardar ventas: {exception.Message}", ToastType.Error); return false; } }

        private static List<(string Name, int Quantity)> ParseProductSummary(string summary)
        {
            var products = new List<(string Name, int Quantity)>();
            foreach (var part in summary.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) { var match = Regex.Match(part, @"^(?<name>.+?)\s+x(?<quantity>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); if (match.Success && int.TryParse(match.Groups["quantity"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)) products.Add((match.Groups["name"].Value.Trim(), quantity)); else products.Add((part.Trim(), 1)); }
            return products;
        }
    }
}