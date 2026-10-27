using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Vistas;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Windows.Media;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public partial class Graficas : UserControl, INotifyPropertyChanged
    {
        #region Campos Privados
        private readonly DispatcherTimer _clockTimer;
        private List<Venta> _sales;
        private List<Producto> _catalog;
        private List<CorteCaja> _cortes;
        private List<MovimientoCaja> _movimientos;
        private readonly CultureInfo _mexicanCulture = CultureInfo.GetCultureInfo("es-MX");

        private Period _currentPeriod = Period.Day;
        private ChartView _currentView = ChartView.Finanzas;

        private DateTime _customStart = DateTime.Today;
        private DateTime _customEnd = DateTime.Today;
        #endregion

        #region Propiedades (Bindings de la Vista)
        public ISeries[] SeriesPrincipal { get; private set; } = [];
        public ISeries[] SeriesPie { get; private set; } = [];
        public Axis[] EjesXPrincipal { get; private set; } = [];
        public Axis[] EjesYPrincipal { get; private set; } = [];
        public ObservableCollection<TopProductoItem> TopProductos { get; } = [];
        public ObservableCollection<CorteAuditoriaItem> CortesAuditoria { get; } = [];
        public ObservableCollection<MovimientoCaja> MovimientosDelTurnoList { get; } = new ObservableCollection<MovimientoCaja>();
        public event PropertyChangedEventHandler? PropertyChanged;
        #endregion

        #region Inicialización y Constructor
        public Graficas()
        {
            InitializeComponent();
            DataContext = this;

            try { _catalog = ProductoDB.Load() ?? []; } catch { _catalog = []; }
            try { _sales = VentaDB.Load().Where(s => s.Estado != "Cancelada").ToList(); foreach (var sale in _sales) HydrateSaleProfit(sale); }
            catch (Exception ex) { _sales = []; ToastNotification.Show($"Error al cargar ventas: {ex.Message}", ToastType.Error); }

            try { _cortes = CorteCajaDB.Load() ?? []; } catch { _cortes = []; }
            try { _movimientos = MovimientoCajaDB.Load() ?? []; } catch { _movimientos = []; }

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
            _clockTimer.Start();

            Unloaded += (_, _) => _clockTimer.Stop();

            RefreshData();
        }
        private void RecargarDatosDesdeDisco()
        {
            try { _catalog = ProductoDB.Load() ?? []; } catch { _catalog = []; }
            try { _sales = VentaDB.Load().Where(s => s.Estado != "Cancelada").ToList(); foreach (var sale in _sales) HydrateSaleProfit(sale); } catch { _sales = []; }
            try { _cortes = CorteCajaDB.Load() ?? []; } catch { _cortes = []; }
            try { _movimientos = MovimientoCajaDB.Load() ?? []; } catch { _movimientos = []; }
        }
        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility == Visibility.Visible)
            {
                this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    this.Focus();
                    Keyboard.Focus(this);
                    RecargarDatosDesdeDisco();
                    RefreshData();
                }));
            }
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
                foreach (var (name, quantity) in ParseLegacySummary(sale.ProductosResumen))
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
        #endregion

        #region Eventos de UI
        private void UserControl_Loaded(object sender, RoutedEventArgs e) => DayButton.Focus();

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.IsRepeat) return;

            if (HelpOverlay != null && HelpOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    HelpOverlay.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
                return;
            }

            if (MovimientosOverlay != null && MovimientosOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    MovimientosOverlay.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape)
            {
                this.Visibility = Visibility.Collapsed;
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RegresarAlMenu();
                }
                e.Handled = true;
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e) { HelpOverlay.Visibility = Visibility.Visible; HelpOverlay.Focus(); }
        private void CloseHelp_Click(object sender, RoutedEventArgs e) { HelpOverlay.Visibility = Visibility.Collapsed; }
        private void HelpOverlay_MouseDown(object sender, MouseButtonEventArgs e) { if (ReferenceEquals(e.OriginalSource, HelpOverlay)) HelpOverlay.Visibility = Visibility.Collapsed; }
        #endregion

        #region Navegación y Filtros de Fecha
        private void RestoreNormalPeriods()
        {
            NormalPeriodContainer.Visibility = Visibility.Visible;
            CustomPeriodContainer.Visibility = Visibility.Collapsed;
        }

        private void DayButton_Click(object sender, RoutedEventArgs e) { _currentPeriod = Period.Day; RestoreNormalPeriods(); RefreshData(); }
        private void WeekButton_Click(object sender, RoutedEventArgs e) { _currentPeriod = Period.Week; RestoreNormalPeriods(); RefreshData(); }
        private void MonthButton_Click(object sender, RoutedEventArgs e) { _currentPeriod = Period.Month; RestoreNormalPeriods(); RefreshData(); }
        private void YearButton_Click(object sender, RoutedEventArgs e) { _currentPeriod = Period.Year; RestoreNormalPeriods(); RefreshData(); }

        private void CustomButton_Click(object sender, RoutedEventArgs e)
        {
            NormalPeriodContainer.Visibility = Visibility.Collapsed;
            CustomPeriodContainer.Visibility = Visibility.Visible;
            StartDatePicker.SelectedDate = _customStart;
            EndDatePicker.SelectedDate = _customEnd;
        }

        private void ApplyCustomDate_Click(object sender, RoutedEventArgs e)
        {
            if (StartDatePicker.SelectedDate.HasValue && EndDatePicker.SelectedDate.HasValue)
            {
                _customStart = StartDatePicker.SelectedDate.Value;
                _customEnd = EndDatePicker.SelectedDate.Value;

                if (_customStart > _customEnd)
                {
                    (_customStart, _customEnd) = (_customEnd, _customStart);
                }

                _currentPeriod = Period.Custom;
                RestoreNormalPeriods();
                RefreshData();
            }
        }

        private void CancelCustomDate_Click(object sender, RoutedEventArgs e) => RestoreNormalPeriods();

        private void NavFinanzas_Click(object sender, RoutedEventArgs e) { _currentView = ChartView.Finanzas; RefreshData(); }
        private void NavMetodos_Click(object sender, RoutedEventArgs e) { _currentView = ChartView.MetodosPago; RefreshData(); }
        private void NavCategorias_Click(object sender, RoutedEventArgs e) { _currentView = ChartView.Categorias; RefreshData(); }
        private void NavHorasBtn_Click(object sender, RoutedEventArgs e) { _currentView = ChartView.HorasPico; RefreshData(); }
        private void NavRentabilidadBtn_Click(object sender, RoutedEventArgs e) { _currentView = ChartView.Rentabilidad; RefreshData(); }
        private void NavCortesBtn_Click(object sender, RoutedEventArgs e) { _currentView = ChartView.CortesCaja; RefreshData(); }
        #endregion

        #region Carga y Procesamiento Principal (RefreshData)
        private void RefreshData()
        {
            var today = DateTime.Today;
            DateTime start = today;
            DateTime end = today;

            switch (_currentPeriod)
            {
                case Period.Day: start = today; end = today; break;
                case Period.Week: start = today.AddDays(-6); end = today; break;
                case Period.Month: start = new DateTime(today.Year, today.Month, 1); end = start.AddMonths(1).AddDays(-1); break;
                case Period.Year: start = new DateTime(today.Year, 1, 1); end = today; break;
                case Period.Custom: start = _customStart.Date; end = _customEnd.Date; break;
            }

            var selectedSales = _sales.Where(s => s.FechaHora.Date >= start && s.FechaHora.Date <= end).ToList();
            var selectedCortes = _cortes.Where(c => c.FechaHora.Date >= start && c.FechaHora.Date <= end).ToList();

            var gananciaEsperada = _catalog.Sum(x => x.Ganancia * x.Stock);
            var totalVentas = selectedSales.Sum(s => s.Total);
            var totalGanancia = selectedSales.Sum(s => s.GananciaTotal);
            var totalCosto = Math.Max(0m, totalVentas - totalGanancia);
            var transacciones = selectedSales.Count;
            var ticketPromedio = transacciones > 0 ? totalVentas / transacciones : 0m;
            var margenPct = totalVentas > 0 ? (totalGanancia / totalVentas) * 100m : 0m;

            var totalGastos = _movimientos.Where(m => m.FechaHora.Date >= start && m.FechaHora.Date <= end && (m.Tipo.Equals("Salida", StringComparison.OrdinalIgnoreCase) || m.Tipo.Equals("Gasto", StringComparison.OrdinalIgnoreCase))).Sum(m => m.Monto);
            var balanceNeto = totalVentas - totalGastos;

            KpiVentaTotal.Text = totalVentas.ToString("C2", _mexicanCulture);
            KpiCostoTotal.Text = totalCosto.ToString("C2", _mexicanCulture);
            KpiGananciaNeta.Text = totalGanancia.ToString("C2", _mexicanCulture);
            KpiGastosTotal.Text = totalGastos.ToString("C2", _mexicanCulture);
            KpiBalanceNeto.Text = balanceNeto.ToString("C2", _mexicanCulture);
            InversionTotal.Text = $"{gananciaEsperada.ToString("C", _mexicanCulture)}";
            KpiTransacciones.Text = $"{transacciones} venta(s)";
            KpiTicketPromedio.Text = $"Ticket prom: {ticketPromedio.ToString("C2", _mexicanCulture)}";
            KpiMargenPorcentaje.Text = $"Margen: {margenPct:F1}%";

            ActualizarEstiloBotonPeriodo(DayButton, _currentPeriod == Period.Day);
            ActualizarEstiloBotonPeriodo(WeekButton, _currentPeriod == Period.Week);
            ActualizarEstiloBotonPeriodo(MonthButton, _currentPeriod == Period.Month);
            ActualizarEstiloBotonPeriodo(YearButton, _currentPeriod == Period.Year);
            ActualizarEstiloBotonPeriodo(CustomButton, _currentPeriod == Period.Custom);

            ActualizarEstiloBotonSidebar(NavFinanzasBtn, _currentView == ChartView.Finanzas);
            ActualizarEstiloBotonSidebar(NavMetodosBtn, _currentView == ChartView.MetodosPago);
            ActualizarEstiloBotonSidebar(NavCategoriasBtn, _currentView == ChartView.Categorias);
            ActualizarEstiloBotonSidebar(NavHorasBtn, _currentView == ChartView.HorasPico);
            ActualizarEstiloBotonSidebar(NavRentabilidadBtn, _currentView == ChartView.Rentabilidad);
            ActualizarEstiloBotonSidebar(NavCortesBtn, _currentView == ChartView.CortesCaja);

            MainChartView.Visibility = Visibility.Collapsed;
            PieChartView.Visibility = Visibility.Collapsed;
            CortesContainerView.Visibility = Visibility.Collapsed;

            if (_currentView == ChartView.CortesCaja) { CortesContainerView.Visibility = Visibility.Visible; CargarTablaCortes(selectedCortes); }
            else if (_currentView == ChartView.MetodosPago) { PieChartView.Visibility = Visibility.Visible; CargarGraficaMetodosPago(selectedSales); }
            else
            {
                MainChartView.Visibility = Visibility.Visible;
                switch (_currentView)
                {
                    case ChartView.Finanzas: CargarGraficaFinanciera(totalVentas, totalCosto, totalGanancia, totalGastos); break;
                    case ChartView.Categorias: CargarGraficaCategorias(selectedSales); break;
                    case ChartView.HorasPico: CargarGraficaHorasPico(selectedSales); break;
                    case ChartView.Rentabilidad: CargarGraficaRentabilidad(selectedSales); break;
                }
            }

            if (_currentView == ChartView.Rentabilidad) { TopListTitleText.Text = "Top Rentables (Utilidad)"; CargarTopProductosPorUtilidad(selectedSales); }
            else { TopListTitleText.Text = "Top Productos Vendidos"; CargarTopProductos(selectedSales); }
        }
        #endregion

        #region Renderizado de Gráficas
        private void CargarGraficaFinanciera(decimal venta, decimal costo, decimal ganancia, decimal gastos)
        {
            ChartTitleText.Text = "Venta Total vs Costo vs Ganancia vs Gastos";
            SeriesPrincipal = [new ColumnSeries<double> { Name = "Venta Total", Values = [(double)venta], Fill = new SolidColorPaint(new SKColor(0, 122, 255)), MaxBarWidth = 40 }, new ColumnSeries<double> { Name = "Inversion", Values = [(double)costo], Fill = new SolidColorPaint(new SKColor(142, 142, 147)), MaxBarWidth = 40 }, new ColumnSeries<double> { Name = "Ganancia Bruta", Values = [(double)ganancia], Fill = new SolidColorPaint(new SKColor(52, 199, 89)), MaxBarWidth = 40 }, new ColumnSeries<double> { Name = "Gastos de Caja", Values = [(double)gastos], Fill = new SolidColorPaint(new SKColor(239, 68, 68)), MaxBarWidth = 40 }];
            EjesXPrincipal = [new Axis { Labels = ["Total del Periodo"], LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()), TextSize = 13 }];
            EjesYPrincipal = [new Axis { MinLimit = 0, Labeler = v => v.ToString("C0", _mexicanCulture), LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()), TextSize = 12 }];
            NotifyChartProperties();
        }

        private void CargarGraficaMetodosPago(List<Venta> sales)
        {
            ChartTitleText.Text = "Distribución de Dinero Recibido por Método de Pago";
            double efectivo = 0, tarjeta = 0, transferencia = 0, fiado = 0;

            foreach (var s in sales)
            {
                if (s.Estado == "Fiado" || s.MetodoPago == "Fiado") { fiado += (double)s.Total; continue; }
                if (s.MetodoPago == "Efectivo") efectivo += (double)s.Total;
                else if (s.MetodoPago == "Tarjeta") tarjeta += (double)s.Total;
                else if (s.MetodoPago == "Transferencia") transferencia += (double)s.Total;
                else if (s.MetodoPago == "Mixto")
                {
                    efectivo += (double)(s.PagoEfectivo - s.Cambio);
                    tarjeta += (double)s.PagoTarjeta;
                    transferencia += (double)s.PagoTransferencia;
                }
            }

            SeriesPie = [
                new PieSeries<double> { Name = "Efectivo", Values = [efectivo], Fill = new SolidColorPaint(new SKColor(52, 199, 89)), InnerRadius = 60 },
                new PieSeries<double> { Name = "Tarjeta", Values = [tarjeta], Fill = new SolidColorPaint(new SKColor(0, 122, 255)), InnerRadius = 60 },
                new PieSeries<double> { Name = "Transferencia", Values = [transferencia], Fill = new SolidColorPaint(new SKColor(175, 82, 222)), InnerRadius = 60 },
                new PieSeries<double> { Name = "Fiado", Values = [fiado], Fill = new SolidColorPaint(new SKColor(255, 149, 0)), InnerRadius = 60 }
            ];
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeriesPie)));
        }

        private void CargarGraficaCategorias(List<Venta> sales)
        {
            ChartTitleText.Text = "Categorías con Mayor Volumen de Piezas";
            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase); // CORREGIDO: int -> decimal

            foreach (var s in sales)
            {
                if (s.Productos.Count > 0) { foreach (var p in s.Productos) { var cat = !string.IsNullOrWhiteSpace(p.Categoria) && p.Categoria != "N/A" ? p.Categoria.Trim() : (_catalog.FirstOrDefault(c => c.Id == p.Id)?.Categoria ?? "General"); dict[cat] = dict.GetValueOrDefault(cat) + p.Cantidad; } }
                else { foreach (var (name, qty) in ParseLegacySummary(s.ProductosResumen)) { var cat = _catalog.FirstOrDefault(c => string.Equals(c.Nombre, name, StringComparison.OrdinalIgnoreCase))?.Categoria ?? "General"; dict[cat] = dict.GetValueOrDefault(cat) + qty; } }
            }

            var sorted = dict.OrderByDescending(c => c.Value).Take(7).ToList();
            if (sorted.Count == 0) sorted.Add(new KeyValuePair<string, decimal>("Sin ventas", 0m));

            SeriesPrincipal = [new ColumnSeries<double> { Name = "Piezas Vendidas", Values = sorted.Select(c => (double)c.Value).ToArray(), Fill = new SolidColorPaint(new SKColor(0, 122, 255)), MaxBarWidth = 44 }];
            EjesXPrincipal = [new Axis { Labels = sorted.Select(c => c.Key).ToArray(), LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()), TextSize = 13 }];
            EjesYPrincipal = [new Axis { MinLimit = 0, LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()), TextSize = 12 }];
            NotifyChartProperties();
        }

        private void CargarGraficaHorasPico(List<Venta> sales)
        {
            ChartTitleText.Text = "Horas Pico de Clientes (8:00 AM - 10:00 PM)";
            var horas = Enumerable.Range(8, 15).ToArray();
            var ventasPorHora = new double[horas.Length];

            foreach (var s in sales) { int hora = s.FechaHora.Hour; int idx = Array.IndexOf(horas, hora); if (idx >= 0) ventasPorHora[idx] += (double)s.Total; }
            var etiquetas = horas.Select(h => h > 12 ? $"{h - 12}pm" : (h == 12 ? "12pm" : $"{h}am")).ToArray();

            SeriesPrincipal = [new ColumnSeries<double> { Name = "Venta en esa Hora", Values = ventasPorHora, Fill = new SolidColorPaint(new SKColor(255, 149, 0)), MaxBarWidth = 26 }];
            EjesXPrincipal = [new Axis { Labels = etiquetas, LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()), TextSize = 11 }];
            EjesYPrincipal = [new Axis { MinLimit = 0, Labeler = v => v.ToString("C0", _mexicanCulture), LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()), TextSize = 12 }];
            NotifyChartProperties();
        }

        private void CargarGraficaRentabilidad(List<Venta> sales)
        {
            ChartTitleText.Text = "Top 7 Artículos que Dejan Mayor Ganancia Real ($)";
            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sales) { foreach (var p in s.Productos) { decimal margenArticulo = p.Ganancia * p.Cantidad; dict[p.Nombre] = dict.GetValueOrDefault(p.Nombre) + margenArticulo; } }
            var sorted = dict.OrderByDescending(x => x.Value).Take(7).ToList();
            if (sorted.Count == 0) sorted.Add(new KeyValuePair<string, decimal>("Sin datos", 0m));

            SeriesPrincipal = [new ColumnSeries<double> { Name = "Ganancia Generada", Values = sorted.Select(x => (double)x.Value).ToArray(), Fill = new SolidColorPaint(new SKColor(52, 199, 89)), MaxBarWidth = 40 }];
            EjesXPrincipal = [new Axis { Labels = sorted.Select(x => x.Key).ToArray(), LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()), TextSize = 12 }];
            EjesYPrincipal = [new Axis { MinLimit = 0, Labeler = v => v.ToString("C0", _mexicanCulture), LabelsPaint = new SolidColorPaint(ObtenerColorTextoEje()), TextSize = 12 }];
            NotifyChartProperties();
        }
        #endregion

        #region Renderizado de Tablas (Top y Cortes)
        private void CargarTopProductosPorUtilidad(List<Venta> sales)
        {
            TopProductos.Clear();
            var dict = new Dictionary<string, (decimal Qty, decimal Ganancia)>(StringComparer.OrdinalIgnoreCase); // CORREGIDO: int -> decimal
            foreach (var s in sales) { foreach (var p in s.Productos) { var cur = dict.GetValueOrDefault(p.Nombre); dict[p.Nombre] = (cur.Qty + p.Cantidad, cur.Ganancia + (p.Ganancia * p.Cantidad)); } }
            var top = dict.OrderByDescending(x => x.Value.Ganancia).ThenByDescending(x => x.Value.Qty).Take(5).ToList();
            for (int i = 0; i < top.Count; i++) TopProductos.Add(new TopProductoItem(i + 1, top[i].Key, top[i].Value.Qty, top[i].Value.Ganancia));
            for (int i = top.Count; i < 5; i++) TopProductos.Add(new TopProductoItem(i + 1, "Sin datos", 0m, 0m)); // CORREGIDO: 0 -> 0m
        }
        private void ExportarReporteDia_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            DateTime start = today;
            DateTime end = today;

            switch (_currentPeriod)
            {
                case Period.Day: start = today; end = today; break;
                case Period.Week: start = today.AddDays(-6); end = today; break;
                case Period.Month: start = new DateTime(today.Year, today.Month, 1); end = start.AddMonths(1).AddDays(-1); break;
                case Period.Year: start = new DateTime(today.Year, 1, 1); end = today; break;
                case Period.Custom: start = _customStart.Date; end = _customEnd.Date; break;
            }

            var selectedSales = _sales.Where(s => s.FechaHora.Date >= start && s.FechaHora.Date <= end).ToList();
            var selectedCortes = _cortes.Where(c => c.FechaHora.Date >= start && c.FechaHora.Date <= end).ToList();
            var selectedMovimientos = _movimientos.Where(m => m.FechaHora.Date >= start && m.FechaHora.Date <= end).ToList();

            if (selectedSales.Count == 0 && selectedCortes.Count == 0)
            {
                ToastNotification.Show("No hay datos en este periodo para exportar.", ToastType.Warning);
                return;
            }

            byte[] chartImage = null;
            if (MainChartView.Visibility == Visibility.Visible) chartImage = CapturarControlWpf(MainChartView);
            else if (PieChartView.Visibility == Visibility.Visible) chartImage = CapturarControlWpf(PieChartView);

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Archivo PDF (*.pdf)|*.pdf",
                FileName = $"Reporte_Financiero_{start:ddMMyyyy}_al_{end:ddMMyyyy}.pdf",
                Title = "Exportar Reporte Global a PDF"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    QuestPDF.Settings.License = LicenseType.Community;
                    QuestPDF.Settings.UseSystemFonts = true;

                    GenerarPdfReporteGlobal(sfd.FileName, start, end, selectedSales, selectedCortes, selectedMovimientos, chartImage);

                    ToastNotification.Show("✅ Reporte exportado a PDF exitosamente.", ToastType.Success);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = sfd.FileName, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ToastNotification.Show($"Error al generar el PDF: {ex.Message}", ToastType.Error);
                }
            }
        }

        private void GenerarPdfReporteGlobal(string filePath, DateTime start, DateTime end, List<Venta> sales, List<CorteCaja> cortes, List<MovimientoCaja> movimientos, byte[] chartImage)
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var totalVentas = sales.Sum(s => s.Total);
            var totalGanancia = sales.Sum(s => s.GananciaTotal);
            var gastosList = movimientos.Where(m => m.Tipo.Equals("Salida", StringComparison.OrdinalIgnoreCase) || m.Tipo.Equals("Gasto", StringComparison.OrdinalIgnoreCase)).ToList();
            var totalGastos = gastosList.Sum(m => m.Monto);
            var balanceNeto = totalVentas - totalGastos;

            var dictTop = new Dictionary<string, (decimal Qty, decimal Ingreso)>(StringComparer.OrdinalIgnoreCase); // CORREGIDO: int -> decimal
            foreach (var s in sales)
            {
                if (s.Productos.Count > 0) { foreach (var p in s.Productos) { var cur = dictTop.GetValueOrDefault(p.Nombre); dictTop[p.Nombre] = (cur.Qty + p.Cantidad, cur.Ingreso + (p.Precio * p.Cantidad)); } }
                else { foreach (var (name, qty) in ParseLegacySummary(s.ProductosResumen)) { var cur = dictTop.GetValueOrDefault(name); dictTop[name] = (cur.Qty + qty, cur.Ingreso); } }
            }
            var topList = dictTop.OrderByDescending(x => x.Value.Ingreso).ThenByDescending(x => x.Value.Qty).Take(10).ToList();

            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(QuestPDF.Helpers.PageSizes.A4);
                    page.Margin(1, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.PageColor(QuestPDF.Helpers.Colors.White);

                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(QuestPDF.Helpers.Fonts.SegoeUI));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("REPORTE FINANCIERO GLOBAL").SemiBold().FontSize(18).FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);
                        string rangoFechas = start.Date == end.Date ? start.ToString("dd/MM/yyyy", culture) : $"{start:dd/MM/yyyy} al {end:dd/MM/yyyy}";
                        col.Item().Text($"Periodo Analizado: {rangoFechas}").FontSize(11).FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);
                        col.Item().PaddingVertical(5).LineHorizontal(1).LineColor(QuestPDF.Helpers.Colors.Grey.Lighten2);
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.Spacing(10);

                            row.RelativeItem().Background(QuestPDF.Helpers.Colors.Blue.Lighten5).Padding(10).Column(c => {
                                c.Item().Text("Venta Total").FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);
                                c.Item().Text(totalVentas.ToString("C2", culture)).FontSize(14).Bold().FontColor(QuestPDF.Helpers.Colors.Blue.Darken2);
                            });

                            row.RelativeItem().Background(QuestPDF.Helpers.Colors.Green.Lighten5).Padding(10).Column(c => {
                                c.Item().Text("Ganancia Bruta").FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);
                                c.Item().Text(totalGanancia.ToString("C2", culture)).FontSize(14).Bold().FontColor(QuestPDF.Helpers.Colors.Green.Darken2);
                            });

                            row.RelativeItem().Background(QuestPDF.Helpers.Colors.Red.Lighten5).Padding(10).Column(c => {
                                c.Item().Text("Gastos / Salidas").FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);
                                c.Item().Text(totalGastos.ToString("C2", culture)).FontSize(14).Bold().FontColor(QuestPDF.Helpers.Colors.Red.Darken2);
                            });

                            row.RelativeItem().Background(QuestPDF.Helpers.Colors.Grey.Lighten4).Padding(10).Column(c => {
                                c.Item().Text("Balance Neto").FontSize(10).FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);
                                c.Item().Text(balanceNeto.ToString("C2", culture)).FontSize(14).Bold().FontColor(QuestPDF.Helpers.Colors.Black);
                            });
                        });

                        if (chartImage != null)
                        {
                            col.Item().PaddingTop(15).Text("ANÁLISIS VISUAL").SemiBold().FontSize(12).FontColor(QuestPDF.Helpers.Colors.Grey.Darken3);
                            col.Item().PaddingTop(5).Height(240).Image(chartImage);
                        }

                        if (cortes.Any())
                        {
                            col.Item().PaddingTop(20).Text("CORTES DE CAJA (TURNOS) DEL PERIODO").SemiBold().FontSize(12).FontColor(QuestPDF.Helpers.Colors.Grey.Darken3);
                            col.Item().PaddingTop(5).Table(table =>
                            {
                                table.ColumnsDefinition(c => { c.ConstantColumn(50); c.RelativeColumn(); c.ConstantColumn(80); c.ConstantColumn(80); c.ConstantColumn(80); });
                                table.Header(h =>
                                {
                                    h.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).Text("ID").SemiBold();
                                    h.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).Text("Fecha de Cierre").SemiBold();
                                    h.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).Text("Esperado").SemiBold().AlignRight();
                                    h.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).Text("Contado").SemiBold().AlignRight();
                                    h.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).Text("Diferencia").SemiBold().AlignRight();
                                });

                                foreach (var c in cortes.OrderBy(x => x.FechaHora))
                                {
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten4).PaddingVertical(3).Text(c.Id.ToString());
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten4).PaddingVertical(3).Text(c.FechaHora.ToString("dd/MM/yyyy hh:mm tt", culture));
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten4).PaddingVertical(3).Text(c.TotalEsperado.ToString("C2", culture)).AlignRight();
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten4).PaddingVertical(3).Text(c.TotalContado.ToString("C2", culture)).AlignRight();

                                    var diffColor = c.Diferencia == 0 ? QuestPDF.Helpers.Colors.Green.Darken2 : (c.Diferencia > 0 ? QuestPDF.Helpers.Colors.Orange.Darken2 : QuestPDF.Helpers.Colors.Red.Darken2);
                                    var diffText = c.Diferencia == 0 ? "Exacto" : c.Diferencia.ToString("C2", culture);
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten4).PaddingVertical(3).Text(diffText).FontColor(diffColor).Bold().AlignRight();
                                }
                            });
                        }

                        if (topList.Any())
                        {
                            col.Item().PaddingTop(20).Text("TOP 10 PRODUCTOS CON MAYOR INGRESO").SemiBold().FontSize(12).FontColor(QuestPDF.Helpers.Colors.Grey.Darken3);
                            col.Item().PaddingTop(5).Table(table =>
                            {
                                table.ColumnsDefinition(c => { c.ConstantColumn(30); c.RelativeColumn(); c.ConstantColumn(60); c.ConstantColumn(80); });
                                table.Header(h =>
                                {
                                    h.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).Text("#").SemiBold();
                                    h.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).Text("Producto").SemiBold();
                                    h.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).Text("Cant.").SemiBold().AlignRight();
                                    h.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten2).Text("Ingreso").SemiBold().AlignRight();
                                });

                                int pos = 1;
                                foreach (var p in topList)
                                {
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten4).PaddingVertical(2).Text(pos++.ToString());
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten4).PaddingVertical(2).Text(p.Key);
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten4).PaddingVertical(2).Text(p.Value.Qty.ToString("0.###", culture)).AlignRight();
                                    table.Cell().BorderBottom(1).BorderColor(QuestPDF.Helpers.Colors.Grey.Lighten4).PaddingVertical(2).Text(p.Value.Ingreso.ToString("C2", culture)).AlignRight();
                                }
                            });
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Generado el: " + DateTime.Now.ToString("dd/MM/yyyy hh:mm tt", culture) + "  |  Página ");
                        x.CurrentPageNumber();
                        x.Span(" de ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf(filePath);
        }

        private byte[] CapturarControlWpf(FrameworkElement control)
        {
            try
            {
                if (control.ActualWidth == 0 || control.ActualHeight == 0) return null;

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)control.ActualWidth, (int)control.ActualHeight + 10, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(control);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

                using var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }
        private void CargarTablaCortes(List<CorteCaja> cortes)
        {
            ChartTitleText.Text = "Historial de Corte de Caja";
            CortesAuditoria.Clear();
            if (cortes.Count == 0) return;

            foreach (var c in cortes.OrderByDescending(x => x.FechaHora))
            {
                var dif = c.Diferencia;
                string difTexto; Brush bgBrush; Brush fgBrush;
                if (dif == 0m) { difTexto = "$0.00 Exacto"; bgBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 52, 199, 89)); fgBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 160, 70)); }
                else if (dif > 0m) { difTexto = $"+{dif.ToString("C2", _mexicanCulture)} Sobrante"; bgBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 149, 0)); fgBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(215, 120, 0)); }
                else { difTexto = $"{dif.ToString("C2", _mexicanCulture)} Faltante"; bgBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 59, 48)); fgBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(215, 40, 30)); }

                CortesAuditoria.Add(new CorteAuditoriaItem(c.Id, c.FechaHora.ToString("dd/MM/yyyy h:mm tt", CultureInfo.InvariantCulture), $"Turno #{c.Id}", c.TotalEsperado.ToString("C2", _mexicanCulture), c.TotalContado.ToString("C2", _mexicanCulture), difTexto, bgBrush, fgBrush));
            }
        }

        private void CargarTopProductos(List<Venta> sales)
        {
            TopProductos.Clear();
            var dict = new Dictionary<string, (decimal Qty, decimal Ingreso)>(StringComparer.OrdinalIgnoreCase); // CORREGIDO: int -> decimal
            foreach (var s in sales)
            {
                if (s.Productos.Count > 0) { foreach (var p in s.Productos) { var cur = dict.GetValueOrDefault(p.Nombre); dict[p.Nombre] = (cur.Qty + p.Cantidad, cur.Ingreso + (p.Precio * p.Cantidad)); } }
                else { foreach (var (name, qty) in ParseLegacySummary(s.ProductosResumen)) { var cur = dict.GetValueOrDefault(name); dict[name] = (cur.Qty + qty, cur.Ingreso); } }
            }
            var top = dict.OrderByDescending(x => x.Value.Ingreso).ThenByDescending(x => x.Value.Qty).Take(5).ToList();
            for (int i = 0; i < top.Count; i++) TopProductos.Add(new TopProductoItem(i + 1, top[i].Key, top[i].Value.Qty, top[i].Value.Ingreso));
            for (int i = top.Count; i < 5; i++) TopProductos.Add(new TopProductoItem(i + 1, "Sin datos", 0m, 0m)); // CORREGIDO: 0 -> 0m
        }
        #endregion

        #region Historial de Movimientos (Doble Clic)
        private void CorteItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is CorteAuditoriaItem item)
            {
                AbrirHistorialMovimientos(item.TurnoId, item.FolioTexto);
            }
        }

        private void AbrirHistorialMovimientos(int turnoId, string folioTexto)
        {
            MovimientosDelTurnoList.Clear();

            var movimientos = _movimientos
                .Where(m => m.CorteCajaId == turnoId)
                .OrderBy(m => m.FechaHora)
                .ToList();

            foreach (var m in movimientos)
            {
                MovimientosDelTurnoList.Add(m);
            }

            MovimientosModalTitle.Text = $"Movimientos del {folioTexto}";
            MovimientosTurnoGrid.ItemsSource = MovimientosDelTurnoList;
            MovimientosOverlay.Visibility = Visibility.Visible;
        }

        private void CloseMovimientosOverlay_Click(object sender, RoutedEventArgs e)
        {
            MovimientosOverlay.Visibility = Visibility.Collapsed;
        }
        #endregion

        #region Utilidades y Helpers
        private void ActualizarEstiloBotonPeriodo(Button btn, bool selected) { if (selected) { btn.SetResourceReference(Button.BackgroundProperty, "ThemeSurfaceBackground"); btn.SetResourceReference(Button.ForegroundProperty, "ThemeTextForeground"); } else { btn.Background = Brushes.Transparent; btn.SetResourceReference(Button.ForegroundProperty, "ThemeMutedForeground"); } }
        private void ActualizarEstiloBotonSidebar(Button btn, bool selected) { if (selected) { btn.SetResourceReference(Button.BackgroundProperty, "ThemeInputBackground"); btn.SetResourceReference(Button.ForegroundProperty, "ThemeAccent"); } else { btn.Background = Brushes.Transparent; btn.SetResourceReference(Button.ForegroundProperty, "ThemeTextForeground"); } }
        private SKColor ObtenerColorTextoEje() => Application.Current is App app && app.IsDarkMode ? new SKColor(245, 245, 247) : new SKColor(28, 28, 30);

        private void NotifyChartProperties()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeriesPrincipal)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EjesXPrincipal)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EjesYPrincipal)));
        }

        private static IEnumerable<(string Name, int Quantity)> ParseLegacySummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) yield break;
            foreach (var entry in summary.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) { var match = Regex.Match(entry, @"^(.*?)\s+x(\d+)$", RegexOptions.IgnoreCase); if (match.Success && int.TryParse(match.Groups[2].Value, out var qty)) yield return (match.Groups[1].Value.Trim(), qty); else yield return (entry.Trim(), 1); }
        }
        #endregion

        #region Enumeradores y Records
        private enum Period { Day, Week, Month, Year, Custom }
        private enum ChartView { Finanzas, MetodosPago, Categorias, HorasPico, Rentabilidad, CortesCaja }
    }

    public sealed record TopProductoItem(int Posicion, string Nombre, decimal Cantidad, decimal Ingreso); // CORREGIDO: int Cantidad -> decimal Cantidad
    public sealed record CorteAuditoriaItem(int TurnoId, string FechaHoraFormateada, string FolioTexto, string EsperadoFormateado, string ContadoFormateado, string DiferenciaFormateada, Brush BadgeBackground, Brush BadgeForeground);
    #endregion
}