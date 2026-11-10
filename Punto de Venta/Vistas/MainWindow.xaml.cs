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
        #region Campos Privados y Cache
        public static MainWindow? InstanciaActual { get; private set; }
        private readonly DispatcherTimer _clockTimer;
        private bool _isShuttingDown;
        private Point _lastMousePosition;
        private bool _reposoHabilitado;
        private System.Windows.Threading.DispatcherTimer _inactivityTimer;
        private System.Windows.Threading.DispatcherTimer _standbyClockTimer;
        private readonly Dictionary<string, UserControl> _vistasCache = new Dictionary<string, UserControl>();
        private readonly CancellationTokenSource _ctsAutoBackup = new CancellationTokenSource();
        private string _nombreNegocioActual = "Punto de Venta";
        private string _vistaActualTitulo = string.Empty;
        #endregion

        #region Inicializacion
        public MainWindow()
        {
            InstanciaActual = this;
            InitializeComponent();
            try
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Imagenes/CHC_icono.ico"));
            }
            catch { }
            IniciarRespaldoAutomaticoSilencioso();
            _standbyClockTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _standbyClockTimer.Tick += (s, args) =>
            {
                ActualizarRelojReposo();
            };

            _inactivityTimer = new System.Windows.Threading.DispatcherTimer();
            _inactivityTimer.Tick += (s, args) =>
            {
                MostrarPantallaReposo();
            };

            var config = AppConfig.Load();

            ActualizarConfiguracionReposo(config);

            System.Windows.Input.InputManager.Current.PreProcessInput += Global_PreProcessInput;

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = true;

            ActualizarNombreNegocioEnUI(config);

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
                    try { _ctsAutoBackup.Cancel(); _ctsAutoBackup.Dispose(); } catch { }

                    try
                    {
                        var cierreConfig = AppConfig.Load();
                        BackupService.GenerarRespaldo("Cierre", cierreConfig.RutaRespaldos);
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
            ActualizarEstadoLicenciaUI();

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                bool? estadoNube = await LicenciaService.VerificarEnNubeAsync().ConfigureAwait(false);
                if (estadoNube.HasValue)
                {
                    Dispatcher.Invoke(() =>
                    {
                        ActualizarEstadoLicenciaUI();
                        if (_vistasCache.TryGetValue("Activacion", out var actView) && actView is Activacion act)
                        {
                            act.ActualizarEstadoVisual();
                        }
                    });
                }
            });
        }
        #endregion

        #region Gestion de Ventana y Reloj

        public void ActualizarNombreNegocioEnUI(AppConfig? config = null)
        {
            config ??= AppConfig.Load();
            string nombre = !string.IsNullOrWhiteSpace(config?.NombreNegocio)
                ? config.NombreNegocio
                : "Punto de Venta";
            _nombreNegocioActual = nombre;

            try
            {
                string textoCompleto = string.IsNullOrEmpty(_vistaActualTitulo)
                    ? _nombreNegocioActual
                    : $"{_nombreNegocioActual} - {_vistaActualTitulo}";

                if (TxtMiniLogo != null)
                {
                    TxtMiniLogo.Text = textoCompleto;
                }
                else if (FindName("TxtMiniLogo") is TextBlock miniLogo)
                {
                    miniLogo.Text = textoCompleto;
                }
                else if (FindName("TxtTituloDashboard") is TextBlock tituloDash)
                {
                    tituloDash.Text = textoCompleto;
                }

                Title = $"{nombre} - Punto de Venta";
            }
            catch { }
        }

        public void ActualizarConfiguracionReposo(AppConfig config)
        {
            _inactivityTimer.Stop();

            _reposoHabilitado = config != null && config.PantallaReposoHabilitada && config.PantallaReposoMinutos > 0;

            if (_reposoHabilitado)
            {
                _inactivityTimer.Interval = TimeSpan.FromMinutes(config.PantallaReposoMinutos);
                _inactivityTimer.Start();

            }
        }

        private void Global_PreProcessInput(object sender, System.Windows.Input.PreProcessInputEventArgs e)
        {
            bool userInputDetected = false;

            if (e.StagingItem.Input is System.Windows.Input.KeyboardEventArgs keyboardArgs)
            {
                if (keyboardArgs.RoutedEvent == System.Windows.Input.Keyboard.PreviewKeyDownEvent ||
                    keyboardArgs.RoutedEvent == System.Windows.Input.Keyboard.KeyDownEvent)
                {
                    userInputDetected = true;
                }
            }
            else if (e.StagingItem.Input is System.Windows.Input.MouseEventArgs mouseArgs)
            {
                if (mouseArgs.RoutedEvent == System.Windows.Input.Mouse.PreviewMouseDownEvent ||
                    mouseArgs.RoutedEvent == System.Windows.Input.Mouse.MouseDownEvent ||
                    mouseArgs.RoutedEvent == System.Windows.Input.Mouse.MouseWheelEvent)
                {
                    userInputDetected = true;
                }
                else if (mouseArgs.RoutedEvent == System.Windows.Input.Mouse.MouseMoveEvent)
                {
                    Point currentPosition = mouseArgs.GetPosition(this);
                    if (Math.Abs(currentPosition.X - _lastMousePosition.X) > 5 ||
                        Math.Abs(currentPosition.Y - _lastMousePosition.Y) > 5)
                    {
                        userInputDetected = true;
                        _lastMousePosition = currentPosition;
                    }
                }
            }

            if (userInputDetected)
            {
                if (StandbyOverlay.Visibility == Visibility.Visible)
                {
                    OcultarPantallaReposo();
                }

                if (_reposoHabilitado) { _inactivityTimer.Stop(); _inactivityTimer.Start(); }

            }
        }

        private void MostrarPantallaReposo()
        {
            _inactivityTimer.Stop();

            try
            {
                var modalesAbiertos = Application.Current.Windows.OfType<Window>()
                    .Where(w => w != this && w is not ToastNotification && w.IsVisible)
                    .ToList();

                foreach (var modal in modalesAbiertos)
                {
                    try
                    {
                        modal.DialogResult = false;
                    }
                    catch
                    {
                        try { modal.Close(); } catch { }
                    }
                }
            }
            catch { }

            try
            {
                if (ContenedorVistas?.Content is Cobro cobroView)
                {
                    var popup = cobroView.FindName("SuggestionsPopup") as System.Windows.Controls.Primitives.Popup;
                    if (popup != null) popup.IsOpen = false;
                }
            }
            catch { }

            ActualizarRelojReposo();
            _standbyClockTimer.Start();
            try { _lastMousePosition = Mouse.GetPosition(this); } catch { }
            StandbyOverlay.Visibility = Visibility.Visible;
            MainGrid.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 15 };
        }

        private void OcultarPantallaReposo()
        {
            StandbyOverlay.Visibility = Visibility.Collapsed;
            _standbyClockTimer.Stop();
            MainGrid.Effect = null;
            if (_reposoHabilitado)
            {
                _inactivityTimer.Stop();
                _inactivityTimer.Start();
            }
        }

        private void ActualizarRelojReposo()
        {
            StandbyClockText.Text = DateTime.Now.ToString("h:mm tt", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
            if (FindName("StandbyDateText") is TextBlock standbyDate)
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo("es-MX");
                standbyDate.Text = culture.TextInfo.ToTitleCase(DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy", culture));
            }
        }

        private void StandbyOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OcultarPantallaReposo();
        }

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

        #region Navegacion y Rutas (Vistas Modulares)
        public void AbrirVista<T>() where T : UserControl, new()
        {
            string nombreVista = typeof(T).Name;
            _vistaActualTitulo = nombreVista switch
            {
                "CodigosBarrasWindow" => "Etiquetas",
                _ => nombreVista
            };

            if (TxtMiniLogo != null)
            {
                TxtMiniLogo.Text = $"{_nombreNegocioActual} - {_vistaActualTitulo}";
            }

            if (!_vistasCache.ContainsKey(nombreVista))
            {
                _vistasCache[nombreVista] = new T();
            }

            _vistasCache[nombreVista].Visibility = Visibility.Visible;

            GridDashboard.Visibility = Visibility.Collapsed;
            ContenedorVistas.Content = _vistasCache[nombreVista];
            ContenedorVistas.Visibility = Visibility.Visible;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _vistasCache[nombreVista].Focus();
            }), DispatcherPriority.Loaded);
        }

        public void RegresarAlMenu()
        {
            _vistaActualTitulo = string.Empty;
            if (TxtMiniLogo != null)
            {
                TxtMiniLogo.Text = _nombreNegocioActual;
            }

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

        public void ActualizarEstadoLicenciaUI()
        {
            bool esPremium = LicenciaService.EsPremium();

            if (TxtTituloPromociones != null)
                TxtTituloPromociones.Text = esPremium ? "Promociones" : "Promociones 🔒";

            if (TxtTituloPrestamos != null)
                TxtTituloPrestamos.Text = esPremium ? "Prestamos" : "Prestamos 🔒";

            if (TxtTituloEtiquetas != null)
                TxtTituloEtiquetas.Text = esPremium ? "Etiquetas" : "Etiquetas 🔒";

            if (TxtTituloExportar != null)
                TxtTituloExportar.Text = esPremium ? "Exportar" : "Exportar 🔒";

            if (_vistasCache.TryGetValue("Configuracion", out var configView) && configView is Configuracion cfg)
            {
                cfg.ActualizarIndicadoresPremiumUI();
            }
            if (_vistasCache.TryGetValue("Cobro", out var cobroView) && cobroView is Cobro cbr)
            {
                cbr.ActualizarIndicadoresPremiumUI();
            }
            if (_vistasCache.TryGetValue("Inventario", out var invView) && invView is Inventario inv)
            {
                inv.ActualizarIndicadoresPremiumUI();
            }
            if (_vistasCache.TryGetValue("Ventas", out var vtaView) && vtaView is Ventas vta)
            {
                vta.ActualizarIndicadoresPremiumUI();
            }
            if (_vistasCache.TryGetValue("Graficas", out var grfView) && grfView is Graficas grf)
            {
                grf.RefreshData();
            }
        }

        public void ActualizarStockDesdeMovil(string codigo, decimal nuevoStock, string nombreProducto, decimal cantidadModificada, bool esSuma)
        {
            string limpio = codigo?.Trim() ?? string.Empty;
            string sinCeros = limpio.TrimStart('0');

            if (_vistasCache.TryGetValue("Inventario", out var invView) && invView is Inventario inv)
            {
                var prodEnLista = inv.Products.FirstOrDefault(p =>
                    string.Equals(p.Codigo?.Trim(), limpio, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(sinCeros) && string.Equals(p.Codigo?.Trim().TrimStart('0'), sinCeros, StringComparison.OrdinalIgnoreCase)) ||
                    p.Id.ToString() == limpio);
                if (prodEnLista != null)
                {
                    prodEnLista.Stock = nuevoStock;
                    inv.ProductsView?.Refresh();
                    inv.UpdateProductCount();
                }
            }

            if (_vistasCache.TryGetValue("Cobro", out var cobroView) && cobroView is Cobro cbr)
            {
                cbr.ActualizarStockEnCatalogo(codigo, nuevoStock);
            }

            string mensaje = esSuma
                ? $"[Movil] +{cantidadModificada} pzas a '{nombreProducto}'. Stock: {nuevoStock}"
                : $"[Movil] '{nombreProducto}': stock fijado en {nuevoStock}";
            ToastNotification.Show(mensaje, ToastType.Success);
        }

        public void AbrirInventarioYSeleccionar(string codigo)
        {
            AbrirVista<Inventario>();
            if (ContenedorVistas?.Content is Inventario inv)
            {
                inv.SeleccionarProductoDesdeMovil(codigo);
            }
        }

        public void AbrirInventarioYNuevoCodigo(string codigo)
        {
            AbrirVista<Inventario>();
            if (ContenedorVistas?.Content is Inventario inv)
            {
                inv.CargarNuevoCodigoDesdeMovil(codigo);
            }
        }

        public bool CrearProductoEnInventarioDesdeMovil(string codigo, string nombre, decimal precio, decimal costo, decimal stock, string categoria)
        {
            AbrirVista<Inventario>();
            if (ContenedorVistas?.Content is Inventario inv)
            {
                return inv.CrearProductoDesdeMovil(codigo, nombre, precio, costo, stock, categoria);
            }
            return false;
        }

        private void BtnEstadoLicencia_Click(object sender, RoutedEventArgs e)
        {
            AbrirVista<Activacion>();
        }

        public static void NavegarAActivacion()
        {
            InstanciaActual?.Dispatcher.Invoke(() =>
            {
                InstanciaActual.AbrirVista<Activacion>();
            });
        }

        private void Cobro_Click(object sender, RoutedEventArgs e) => AbrirVista<Cobro>();

        private void Prestamos_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Cuentas por Cobrar (Fiado)");
                return;
            }
            AbrirVista<Prestamos>();
        }

        private void CodigosDeBarras_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Generacion e Impresion de Etiquetas");
                return;
            }
            AbrirVista<CodigosBarrasWindow>();
        }

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
            if (SolicitarAutorizacion())
            {
                if (!LicenciaService.EsPremium())
                {
                    LicenciaService.MostrarMensajePremium("Promociones y Ofertas");
                    return;
                }
                AbrirVista<Promociones>();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (StandbyOverlay != null && StandbyOverlay.Visibility == Visibility.Visible) OcultarPantallaReposo();

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

            if (ContenedorVistas.Visibility == Visibility.Visible
                && e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && ContenedorVistas.Content is ILimpiarSeleccion vistaConSeleccion)
            {
                vistaConSeleccion.LimpiarSeleccion();
                e.Handled = true;
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
            if (!config.SolicitarPinAdmin || !LicenciaService.EsPremium()) return true;

            var modal = new AutorizacionModal { Owner = this };
            modal.ShowDialog();
            return modal.Autorizado;
        }
        #endregion

        #region Importacion de Base de Datos (Archivos CSV)
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
                    ToastNotification.Show("El archivo seleccionado esta completamente vacio.", ToastType.Warning);
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
                    ToastNotification.Show("No se detecto ningun producto valido. Verifica que el archivo sea un CSV con el orden de columnas correcto.", ToastType.Warning);
                }
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error critico al leer el archivo: {ex.Message}", ToastType.Error);
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

        #region Exportacion (Archivos PDF)
        private void Exportar_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Exportacion de Datos");
                return;
            }

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
                    var fechaLocal = sale.FechaHora.Date;
                    return sale.Estado != "Cancelada" && sale.Estado != "Abono" && fechaLocal >= inicioSemana && fechaLocal <= hoy;
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
                        c.Item().Text("CATALOGO DE PRODUCTOS").SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);
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
                            header.Cell().Text("CODIGO").SemiBold();
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
                        x.Span("Pagina ");
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
                        c.Item().Text("REPORTE DE PRESTAMOS Y DEUDAS").SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);
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
                        x.Span("Pagina ");
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
                            var dailySales = sales.Where(s => s.FechaHora.Date == day).ToList();
                            var dailyTotal = dailySales.Sum(s => s.Total);
                            var dailyProfit = dailySales.Sum(s => s.GananciaTotal);

                            totalWeek += dailyTotal;
                            totalProfitWeek += dailyProfit;

                            col.Item().PaddingTop(10).Text($"Dia: {day.ToString("dd/MM/yyyy", culture)}").Bold().FontSize(12);

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

                            col.Item().PaddingBottom(10).AlignRight().Text($"Total del dia: {dailyTotal.ToString("C2", culture)} | Ganancia: {dailyProfit.ToString("C2", culture)}").SemiBold().FontColor(Colors.Green.Darken2);
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
                        x.Span("Pagina ");
                        x.CurrentPageNumber();
                        x.Span(" de ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf(filePath);
        }
        #endregion

        #region Google Drive
        private static DateTime _proximoRespaldoAuto = DateTime.MinValue;

        public static void ReiniciarTemporizadorRespaldo()
        {
            try
            {
                var config = AppConfig.Load();
                int minutos = config.IntervaloRespaldoMinutos > 0 ? config.IntervaloRespaldoMinutos : 60;
                _proximoRespaldoAuto = DateTime.Now.AddMinutes(minutos);
            }
            catch
            {
                _proximoRespaldoAuto = DateTime.Now.AddMinutes(60);
            }
        }

        private void IniciarRespaldoAutomaticoSilencioso()
        {
            ReiniciarTemporizadorRespaldo();

            Task.Run(async () =>
            {
                while (!_ctsAutoBackup.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), _ctsAutoBackup.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (DateTime.Now >= _proximoRespaldoAuto)
                    {
                        ReiniciarTemporizadorRespaldo();

                        try
                        {
                            var config = AppConfig.Load();
                            BackupService.GenerarRespaldo("Auto", config.RutaRespaldos);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoBackup Local] Error: {ex.Message}");
                        }

                        if (!Properties.Settings.Default.RespaldoAutoActivo || !GoogleDriveBackupService.UsuarioEstaAutenticado())
                        {
                            continue;
                        }

                        try
                        {
                            bool completado = await GoogleDriveBackupService.RespaldarEnDriveAsync(null, modoSilencioso: true).ConfigureAwait(false);

                            if (completado)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AutoBackup Drive] Respaldo exitoso a las {DateTime.Now}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoBackup Drive] Error: {ex.Message}");
                        }
                    }
                }
            }, _ctsAutoBackup.Token);
        }
        #endregion
    }
}