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
        private DateTime _ultimoDiaVerificadoTurno = DateTime.Today;
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
            Loaded += MainWindow_Loaded;
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
                    try { _clockTimer?.Stop(); } catch { }
                    try { _inactivityTimer?.Stop(); } catch { }
                    try { _standbyClockTimer?.Stop(); } catch { }
                    try { _ctsAutoBackup.Cancel(); _ctsAutoBackup.Dispose(); } catch { }
                    try
                    {
                        if (_vistasCache.TryGetValue("Activacion", out var vistaAct) && vistaAct is Activacion actView)
                        {
                            actView.DetenerActivacionPendiente();
                        }
                    }
                    catch { }

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
            this.PreviewGotKeyboardFocus += MainWindow_PreviewGotKeyboardFocus;
            Punto_de_Venta.Servicios.IdiomaService.IdiomaCambiado += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ActualizarNombreNegocioEnUI();
                    ActualizarEstadoLicenciaUI();
                    UpdateClock();
                });
            };

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    bool? estadoNube = await LicenciaService.VerificarEnNubeAsync().ConfigureAwait(false);
                    if (estadoNube.HasValue && !_isShuttingDown)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_isShuttingDown) return;
                            ActualizarEstadoLicenciaUI();
                            if (_vistasCache.TryGetValue("Activacion", out var actView) && actView is Activacion act)
                            {
                                act.ActualizarEstadoVisual();
                            }
                        });
                    }
                }
                catch
                {
                }
            });
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            VerificarTurnoPendienteDeDiasAnteriores();
            MostrarGuiaPrimeraVez();
        }

        private void MostrarGuiaPrimeraVez()
        {
            try
            {
                var cfg = AppConfig.Load();
                if (cfg.GuiaInicioMostrada) return;

                cfg.GuiaInicioMostrada = true;
                cfg.Save();

                bool esIngles = string.Equals(cfg.Idioma, "en", StringComparison.OrdinalIgnoreCase);
                string titulo = esIngles ? "Quick Navigation Tip" : "Consejo rapido de uso";
                string mensaje = esIngles
                    ? "Welcome!\n\n• Double-click on any record (sales, products, debtors, or cuts) to view its details.\n• Press the ESC key at any time to go back."
                    : "¡Bienvenido!\n\n• Haz doble clic sobre cualquier registro (ventas, productos, deudores o cortes) para ver sus detalles.\n• Presiona la tecla ESC en cualquier momento para volver atras.";

                CustomMessageBox.ShowWithDelay(this, mensaje, titulo, 5);
            }
            catch
            {
            }
        }

        public void VerificarTurnoPendienteDeDiasAnteriores()
        {
            try
            {
                var turno = TurnoCajaManager.ObtenerTurnoPendienteDeDiasAnteriores();
                if (turno != null)
                {
                    string fechaStr = turno.FechaHora.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                    string horaStr = turno.FechaHora.ToString("hh:mm tt", CultureInfo.InvariantCulture);
                    string mensaje = $"Tiene un turno pendiente por cerrar del dia {fechaStr} ({horaStr}). Por favor realiza el corte de caja en Ventas.";
                    ToastNotification.Show(mensaje, ToastType.Warning);
                    CustomMessageBox.Show(this, mensaje, "Turno Pendiente de Cierre", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch
            {
            }
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
                string vistaTraducida = Punto_de_Venta.Servicios.IdiomaService.TraducirTexto(_vistaActualTitulo);
                string textoCompleto = string.IsNullOrEmpty(vistaTraducida)
                    ? _nombreNegocioActual
                    : $"{_nombreNegocioActual} - {vistaTraducida}";

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

                Title = $"{nombre} - {Punto_de_Venta.Servicios.IdiomaService.Obtener("MainSubtitleDefault", "Punto de Venta")}";
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

            if (now.Date != _ultimoDiaVerificadoTurno)
            {
                _ultimoDiaVerificadoTurno = now.Date;
                VerificarTurnoPendienteDeDiasAnteriores();
            }

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
                "LibretaContactos" => "Libreta",
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
                TxtTituloPromociones.Text = Punto_de_Venta.Servicios.IdiomaService.Obtener("MainNavPromos", "Promociones");

            if (TxtTituloPrestamos != null)
                TxtTituloPrestamos.Text = Punto_de_Venta.Servicios.IdiomaService.Obtener("MainNavCredits", "Prestamos");

            string titEtiq = Punto_de_Venta.Servicios.IdiomaService.Obtener("MainNavBarcodes", "Etiquetas");
            if (TxtTituloEtiquetas != null)
                TxtTituloEtiquetas.Text = esPremium ? titEtiq : $"{titEtiq} 🔒";

            string titExp = Punto_de_Venta.Servicios.IdiomaService.Obtener("MainNavExport", "Exportar");
            if (TxtTituloExportar != null)
                TxtTituloExportar.Text = esPremium ? titExp : $"{titExp} 🔒";

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
                AbrirVista<Promociones>();
            }
        }

        private void Libreta_Click(object sender, RoutedEventArgs e)
        {
            AbrirVista<LibretaContactos>();
        }

        private void MainWindow_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UIElement modalActivo = null;
            if (ImportarPreviewOverlay != null && ImportarPreviewOverlay.Visibility == Visibility.Visible) modalActivo = ImportarPreviewOverlay;
            else if (ExportarOverlay != null && ExportarOverlay.Visibility == Visibility.Visible) modalActivo = ExportarOverlay;
            else if (ImportarOverlay != null && ImportarOverlay.Visibility == Visibility.Visible) modalActivo = ImportarOverlay;

            if (modalActivo != null)
            {
                ModalFocusHelper.ProtegerFoco(e, modalActivo);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (StandbyOverlay != null && StandbyOverlay.Visibility == Visibility.Visible)
            {
                OcultarPantallaReposo();
                e.Handled = true;
                return;
            }

            if (ImportarPreviewOverlay != null && ImportarPreviewOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    CerrarImportarPreview();
                    e.Handled = true;
                }
                return;
            }

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
                if (e.Key == Key.Escape)
                {
                    if (ContenedorVistas.Content is IManejadorEscape manejador)
                    {
                        if (manejador.ManejarEscape())
                        {
                            e.Handled = true;
                            return;
                        }
                    }

                    RegresarAlMenu();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                    && ContenedorVistas.Content is ILimpiarSeleccion vistaConSeleccion)
                {
                    vistaConSeleccion.LimpiarSeleccion();
                    e.Handled = true;
                    return;
                }

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
                case Key.D9:
                    Libreta_Click(this, new RoutedEventArgs());
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

        private void DescargarPlantillaImportacion_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar plantilla de productos",
                Filter = "Archivo Excel (*.xlsx)|*.xlsx|Archivo CSV (*.csv)|*.csv",
                DefaultExt = ".xlsx",
                AddExtension = true,
                FileName = "Plantilla_Productos.xlsx"
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                if (string.Equals(Path.GetExtension(dialog.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    ImportarExportarService.GenerarPlantillaProductosXlsx(dialog.FileName);
                }
                else
                {
                    ImportarExportarService.GenerarPlantillaProductosCsv(dialog.FileName);
                }
                ToastNotification.Show("Plantilla guardada exitosamente.", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo guardar la plantilla: {ex.Message}", ToastType.Error);
            }
        }

        private string _archivoImportacionActual = string.Empty;
        private ResultadoPreviewImportacion _previewImportacionActual = new();
        private bool _previewImportacionInicializado = false;

        private void EjecutarImportacion_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Archivos Excel y CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Archivo Excel (*.xlsx)|*.xlsx|Archivos CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*",
                Title = "Seleccionar lista de productos para importar a SQLite"
            };

            if (openFileDialog.ShowDialog() != true) return;

            try
            {
                _archivoImportacionActual = openFileDialog.FileName;
                TxtPreviewArchivoNombre.Text = Path.GetFileName(_archivoImportacionActual);
                _previewImportacionInicializado = false;
                CmbModoImportacion.SelectedIndex = 0;
                _previewImportacionInicializado = true;

                RecalcularPreviewImportacion();

                if (FindName("ImportarPreviewOverlay") is Grid overlay)
                {
                    overlay.Visibility = Visibility.Visible;
                    overlay.Focus();
                }
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al leer el archivo: {ex.Message}", ToastType.Error);
            }
        }

        private ModoImportacion ObtenerModoImportacionSeleccionado()
        {
            if (CmbModoImportacion.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                return tag switch
                {
                    "SoloNuevos" => ModoImportacion.SoloNuevos,
                    "SumarStockYCrear" => ModoImportacion.SumarStockYCrear,
                    _ => ModoImportacion.CrearYActualizar
                };
            }
            return ModoImportacion.CrearYActualizar;
        }

        private void RecalcularPreviewImportacion()
        {
            if (!_previewImportacionInicializado || string.IsNullOrWhiteSpace(_archivoImportacionActual)) return;

            try
            {
                _previewImportacionActual = ImportarExportarService.PrevisualizarImportacion(_archivoImportacionActual, LicenciaService.EsPremium(), ObtenerModoImportacionSeleccionado());
                GridImportPreview.ItemsSource = _previewImportacionActual.Filas;

                TxtResumenNuevos.Text = $"Nuevos: {_previewImportacionActual.Creados}";
                TxtResumenActualizar.Text = $"Actualizar: {_previewImportacionActual.Actualizados}";
                TxtResumenOmitidos.Text = $"Omitidos: {_previewImportacionActual.Omitidos}";
                TxtResumenRechazados.Text = $"Rechazados: {_previewImportacionActual.Rechazados}";

                BtnExportarErrores.Visibility = _previewImportacionActual.Errores.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                BtnConfirmarImportacion.IsEnabled = _previewImportacionActual.ProductosParaGuardar.Count > 0;
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al analizar archivo: {ex.Message}", ToastType.Error);
                BtnConfirmarImportacion.IsEnabled = false;
            }
        }

        private void CmbModoImportacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RecalcularPreviewImportacion();
        }

        private void BtnExportarErrores_Click(object sender, RoutedEventArgs e)
        {
            if (_previewImportacionActual.Errores.Count == 0) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar reporte de errores de importacion",
                Filter = "Archivo de texto (*.txt)|*.txt",
                DefaultExt = ".txt",
                AddExtension = true,
                FileName = $"Errores_Importacion_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                File.WriteAllLines(dialog.FileName, _previewImportacionActual.Errores, new UTF8Encoding(true));
                ToastNotification.Show("Reporte de errores guardado.", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo guardar el reporte: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnConfirmarImportacion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var res = ImportarExportarService.ConfirmarImportacion(_previewImportacionActual);
                CerrarImportarPreview();
                CerrarImportar();
                ToastNotification.Show($"Importacion lista: {res.Creados} nuevos, {res.Actualizados} actualizados, {res.Rechazados} rechazados.", res.Rechazados > 0 ? ToastType.Info : ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al guardar en base de datos: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnCancelarPreview_Click(object sender, RoutedEventArgs e)
        {
            CerrarImportarPreview();
        }

        private void ImportarPreviewOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FindName("ImportarPreviewOverlay") is Grid overlay && ReferenceEquals(e.OriginalSource, overlay))
            {
                CerrarImportarPreview();
            }
        }

        private void ImportarPreviewOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CerrarImportarPreview();
                e.Handled = true;
            }
        }

        private void CerrarImportarPreview()
        {
            if (FindName("ImportarPreviewOverlay") is Grid overlay) overlay.Visibility = Visibility.Collapsed;
            GridImportPreview.ItemsSource = null;
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

        private bool UsarFormatoExcel()
        {
            return CmbFormatoTabla?.SelectedItem is ComboBoxItem item &&
                   string.Equals(item.Tag?.ToString(), "xlsx", StringComparison.OrdinalIgnoreCase);
        }

        private void ExportarTabla(string baseName, Action<string> generateXlsxAction, Action<string> generateCsvAction)
        {
            bool esExcel = UsarFormatoExcel();
            string ext = esExcel ? ".xlsx" : ".csv";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = esExcel ? "Guardar archivo Excel (.xlsx)" : "Guardar archivo CSV",
                Filter = esExcel ? "Archivo Excel (*.xlsx)|*.xlsx" : "Archivo CSV (*.csv)|*.csv",
                DefaultExt = ext,
                AddExtension = true,
                FileName = $"{baseName}{ext}"
            };

            try
            {
                if (dialog.ShowDialog(this) == true)
                {
                    if (esExcel) generateXlsxAction(dialog.FileName);
                    else generateCsvAction(dialog.FileName);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                    ToastNotification.Show(esExcel ? "Archivo Excel exportado exitosamente" : "Archivo CSV exportado exitosamente", ToastType.Success);
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

        private void ExportarProductosCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var prods = ProductoDB.Load();
                ExportarTabla(
                    $"Catalogo_Productos_{DateTime.Now:yyyy-MM-dd}",
                    path => ImportarExportarService.ExportarProductosXlsx(prods, path),
                    path => ImportarExportarService.ExportarProductosCsv(prods, path));
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
        }

        private void ExportarVentasCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sales = VentaDB.Load();
                ExportarTabla(
                    $"Ventas_{DateTime.Now:yyyy-MM-dd}",
                    path => ImportarExportarService.ExportarVentasXlsx(sales, path),
                    path => ImportarExportarService.ExportarVentasCsv(sales, path));
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
        }

        private void ExportarCortesCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportarTabla(
                    $"Cortes_Caja_{DateTime.Now:yyyy-MM-dd}",
                    ImportarExportarService.ExportarCortesXlsx,
                    ImportarExportarService.ExportarCortesCsv);
            }
            catch (Exception exception)
            {
                MostrarErrorExportacion(exception);
            }
        }

        private void ExportarPrestamosCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var debtors = PrestamoDB.Load();
                ExportarTabla(
                    $"Prestamos_{DateTime.Now:yyyy-MM-dd}",
                    path => ImportarExportarService.ExportarPrestamosXlsx(debtors, path),
                    path => ImportarExportarService.ExportarPrestamosCsv(debtors, path));
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