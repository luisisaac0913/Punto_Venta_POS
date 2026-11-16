using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Punto_de_Venta.Vistas
{
    public partial class Bienvenida : Window
    {
        private int _pasoActual = 0; 
        private bool _setupCompletado = false;
        private bool _actualizandoIdioma = false;

        public Bienvenida()
        {
            InitializeComponent();
            try
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Imagenes/CHC_icono.ico"));
            }
            catch { }
            var config = AppConfig.Load();
            IdiomaService.AplicarIdioma(config.Idioma);
            CargarImpresoras();
            CargarPuertos();
            ActualizarVistaPasos();
            BtnThemeToggle.Content = config.ModoOscuro ? "☀️" : "🌙";
            if (!string.IsNullOrWhiteSpace(config.NombreNegocio))
            {
                TxtNombreNegocio.Text = config.NombreNegocio;
            }

            SincronizarControlesIdioma();
            AplicarTextosDinamicos();
            if (CmbAnchoTicket != null)
            {
                CmbAnchoTicket.SelectedIndex = config.AnchoTicketCaracteres <= 34 ? 0 : 1;
            }
            ActualizarVistaPreviaTicket();
            ActualizarEstadoBasculaUI();

            if (LicenciaService.EsPremium())
            {
                if (!string.IsNullOrWhiteSpace(config.DireccionNegocio))
                {
                    TxtDireccion.Text = config.DireccionNegocio;
                }
                if (!string.IsNullOrWhiteSpace(config.MensajePie))
                {
                    TxtMensajePie.Text = config.MensajePie;
                }
            }
        }

        private void SincronizarControlesIdioma()
        {
            _actualizandoIdioma = true;
            try
            {
                bool esEn = IdiomaService.EsIngles();
                if (CmbIdiomaHeader != null)
                {
                    CmbIdiomaHeader.SelectedIndex = esEn ? 1 : 0;
                }
            }
            finally
            {
                _actualizandoIdioma = false;
            }
        }

        private void CambiarIdioma(string codigo)
        {
            if (_actualizandoIdioma) return;
            IdiomaService.AplicarIdioma(codigo);
            var config = AppConfig.Load();
            config.Idioma = IdiomaService.IdiomaActual;
            if (AppConfig.ExisteArchivoConfiguracion())
            {
                config.Save();
            }
            SincronizarControlesIdioma();
            AplicarTextosDinamicos();
            if (CmbAnchoTicket != null)
            {
                CmbAnchoTicket.SelectedIndex = config.AnchoTicketCaracteres <= 34 ? 0 : 1;
            }
            ActualizarVistaPreviaTicket();
            ActualizarEstadoBasculaUI();
        }

        private void AplicarTextosDinamicos()
        {
            if (CmbImpresoras != null && CmbImpresoras.Items.Count > 0)
            {
                int sel = CmbImpresoras.SelectedIndex;
                CmbImpresoras.Items[0] = IdiomaService.Obtener("WelcomePrinterNone", "(Ninguna / Mas tarde)");
                CmbImpresoras.SelectedIndex = sel >= 0 ? sel : 0;
            }

            bool esPremium = LicenciaService.EsPremium();
            if (!esPremium)
            {
                LblDireccion.Text = IdiomaService.Obtener("WelcomeAddressLocked", "Direccion (Opcional) 🔒");
                TxtDireccion.IsEnabled = false;
                TxtDireccion.Text = string.Empty;
                TxtDireccion.ToolTip = IdiomaService.Obtener("WelcomePremiumTicketTooltip", "Personalizacion de ticket exclusiva de Version Premium");

                LblMensajeTicket.Text = IdiomaService.Obtener("WelcomeTicketMsgLocked", "Mensaje del Ticket 🔒");
                TxtMensajePie.IsEnabled = false;
                TxtMensajePie.Text = IdiomaService.Obtener("WelcomeTicketFreeDefault", "GRACIAS POR SU COMPRA\nVuelva pronto");
                TxtMensajePie.ToolTip = IdiomaService.Obtener("WelcomePremiumTicketTooltip", "Personalizacion de ticket exclusiva de Version Premium");

                LblPinAdminTitulo.Text = IdiomaService.Obtener("WelcomePinLocked", "PIN de Administrador (4 digitos) 🔒");
                TxtPinAdmin.IsEnabled = false;
                LblPinAdminDesc.Text = IdiomaService.Obtener("WelcomePinLockedDesc", "El PIN de seguridad y autorizacion de cancelaciones requiere Version Premium.");
                if (BannerPromoGratis != null) BannerPromoGratis.Visibility = Visibility.Visible;
            }
            else
            {
                if (BannerPromoGratis != null) BannerPromoGratis.Visibility = Visibility.Collapsed;
                if (string.IsNullOrWhiteSpace(TxtMensajePie.Text) ||
                    TxtMensajePie.Text == "Gracias por su preferencia!" ||
                    TxtMensajePie.Text == "Thank you for your business!")
                {
                    TxtMensajePie.Text = IdiomaService.Obtener("WelcomeTicketMsgDefault", "Gracias por su preferencia!");
                }
            }
        }

        private void CmbIdiomaHeader_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_actualizandoIdioma) return;
            if (CmbIdiomaHeader?.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string codigo)
            {
                CambiarIdioma(codigo);
            }
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app)
            {
                bool isDark = !app.IsDarkMode;
                app.SetDarkMode(isDark);
                BtnThemeToggle.Content = isDark ? "☀️" : "🌙";

                var config = AppConfig.Load();
                config.ModoOscuro = isDark;
                config.Save();
            }
        }
        private void VideoIntro_Loaded(object sender, RoutedEventArgs e)
        {
            VideoIntro.Play();
        }
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_pasoActual == 0)
                    BtnIniciarIntro_Click(this, new RoutedEventArgs());
                else if (_pasoActual < 3)
                    BtnSiguiente_Click(this, new RoutedEventArgs());
                else if (_pasoActual == 3)
                    BtnFinalizar_Click(this, new RoutedEventArgs());
            }
        }

        private void CargarImpresoras()
        {
            CmbImpresoras.Items.Add(IdiomaService.Obtener("WelcomePrinterNone", "(Ninguna / Mas tarde)"));
            try
            {
                foreach (string printer in PrinterSettings.InstalledPrinters)
                    CmbImpresoras.Items.Add(printer);
            }
            catch { }
            CmbImpresoras.SelectedIndex = 0;
        }

        private void CargarPuertos()
        {
            CmbPuertosCOM.Items.Add("(Auto)");
            try
            {
                var puertos = BasculaService.ObtenerPuertosDisponibles();
                foreach (var p in puertos) CmbPuertosCOM.Items.Add(p);
            }
            catch { }
            CmbPuertosCOM.SelectedIndex = 0;
        }

        private void BtnIniciarIntro_Click(object sender, RoutedEventArgs e)
        {
            _pasoActual = 1;
            VideoIntro.Stop();
            ActualizarVistaPasos();
        }

        private void ActualizarVistaPasos()
        {
            PanelPaso0.Visibility = _pasoActual == 0 ? Visibility.Visible : Visibility.Collapsed;
            PanelPaso1.Visibility = _pasoActual == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelPaso2.Visibility = _pasoActual == 2 ? Visibility.Visible : Visibility.Collapsed;
            PanelPaso3.Visibility = _pasoActual == 3 ? Visibility.Visible : Visibility.Collapsed;
            GridNavegacion.Visibility = _pasoActual == 0 ? Visibility.Collapsed : Visibility.Visible;
            BarraProgreso.Visibility = _pasoActual == 0 ? Visibility.Collapsed : Visibility.Visible;
            PanelPasosIndicador.Visibility = _pasoActual == 0 ? Visibility.Collapsed : Visibility.Visible;
            BarraProgreso.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new DoubleAnimation(_pasoActual, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            var accentBrush = TryFindResource("ThemeAccent") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DodgerBlue;
            var mutedBrush = TryFindResource("ThemeMutedForeground") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
            System.Windows.Media.Brush accentFill = accentBrush is System.Windows.Media.SolidColorBrush scb
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, scb.Color.R, scb.Color.G, scb.Color.B))
                : System.Windows.Media.Brushes.Transparent;

            BadgePaso1.BorderBrush = _pasoActual == 1 ? accentBrush : System.Windows.Media.Brushes.Transparent;
            BadgePaso1.Background = _pasoActual == 1 ? accentFill : System.Windows.Media.Brushes.Transparent;
            TxtBadgePaso1.Foreground = _pasoActual == 1 ? accentBrush : mutedBrush;

            BadgePaso2.BorderBrush = _pasoActual == 2 ? accentBrush : System.Windows.Media.Brushes.Transparent;
            BadgePaso2.Background = _pasoActual == 2 ? accentFill : System.Windows.Media.Brushes.Transparent;
            TxtBadgePaso2.Foreground = _pasoActual == 2 ? accentBrush : mutedBrush;

            BadgePaso3.BorderBrush = _pasoActual == 3 ? accentBrush : System.Windows.Media.Brushes.Transparent;
            BadgePaso3.Background = _pasoActual == 3 ? accentFill : System.Windows.Media.Brushes.Transparent;
            TxtBadgePaso3.Foreground = _pasoActual == 3 ? accentBrush : mutedBrush;

            if (_pasoActual == 1) AnimarPaso(PanelPaso1);
            else if (_pasoActual == 2) AnimarPaso(PanelPaso2);
            else if (_pasoActual == 3) AnimarPaso(PanelPaso3);
            else AnimarPaso(PanelPaso0);

            if (_pasoActual > 0)
            {
                BtnAnterior.Visibility = _pasoActual > 1 ? Visibility.Visible : Visibility.Hidden;

                if (_pasoActual == 3)
                {
                    BtnSiguiente.Visibility = Visibility.Hidden;
                    BtnFinalizar.Visibility = Visibility.Visible;
                }
                else
                {
                    BtnSiguiente.Visibility = Visibility.Visible;
                    BtnFinalizar.Visibility = Visibility.Hidden;
                }
                if (_pasoActual == 1) TxtNombreNegocio.Focus();
                if (_pasoActual == 3) TxtFondoCaja.Focus();
            }
        }

        private void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            if (_pasoActual == 1 && string.IsNullOrWhiteSpace(TxtNombreNegocio.Text))
            {
                CustomMessageBox.Show(
                    IdiomaService.Obtener("WelcomeStoreRequired", "El nombre de la tienda es obligatorio para poder continuar."),
                    IdiomaService.Obtener("CommonNotice", "Aviso"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TxtNombreNegocio.Focus();
                return;
            }
            if (_pasoActual < 3)
            {
                _pasoActual++;
                ActualizarVistaPasos();
            }
        }

        private void BtnAnterior_Click(object sender, RoutedEventArgs e)
        {
            if (_pasoActual > 1)
            {
                _pasoActual--;
                ActualizarVistaPasos();
            }
        }

        private void VideoIntro_MediaEnded(object sender, RoutedEventArgs e)
        {
            VideoIntro.Position = TimeSpan.Zero;
        }

        private void BtnFinalizar_Click(object sender, RoutedEventArgs e)
        {
            string textoFondo = TxtFondoCaja.Text.Trim();
            if (!string.IsNullOrWhiteSpace(textoFondo))
            {
                if (!decimal.TryParse(textoFondo, NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                    !decimal.TryParse(textoFondo, NumberStyles.Any, CultureInfo.GetCultureInfo("es-MX"), out _))
                {
                    CustomMessageBox.Show(
                        IdiomaService.Obtener("WelcomeCashInvalid", "El monto inicial del fondo de caja contiene caracteres invalidos. Solo usa numeros y un punto decimal."),
                        IdiomaService.Obtener("CommonNotice", "Aviso"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    TxtFondoCaja.Focus();
                    TxtFondoCaja.SelectAll();
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(TxtPinAdmin.Password) && TxtPinAdmin.Password.Length < 4)
            {
                CustomMessageBox.Show(
                    IdiomaService.Obtener("WelcomePinInvalid", "El PIN debe tener al menos 4 digitos numericos o dejarse vacio por ahora."),
                    IdiomaService.Obtener("CommonNotice", "Aviso"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TxtPinAdmin.Focus();
                return;
            }

            var config = AppConfig.Load();
            config.Idioma = IdiomaService.IdiomaActual;

            config.NombreNegocio = TxtNombreNegocio.Text.Trim();
            if (!LicenciaService.EsPremium())
            {
                config.DireccionNegocio = string.Empty;
                config.MensajePie = IdiomaService.Obtener("WelcomeTicketFreeDefault", "GRACIAS POR SU COMPRA\nVuelva pronto");
                config.SolicitarPinAdmin = false;
            }
            else
            {
                config.DireccionNegocio = TxtDireccion.Text.Trim();
                config.MensajePie = string.IsNullOrWhiteSpace(TxtMensajePie.Text)
                    ? IdiomaService.Obtener("WelcomeTicketFreeDefault", "GRACIAS POR SU COMPRA\nVuelva pronto")
                    : TxtMensajePie.Text.Trim();
                if (!string.IsNullOrWhiteSpace(TxtPinAdmin.Password))
                {
                    config.EstablecerNuevoPin(TxtPinAdmin.Password.Trim());
                    config.SolicitarPinAdmin = true;
                }
                else
                {
                    config.SolicitarPinAdmin = false;
                }
            }

            if (CmbImpresoras.SelectedIndex > 0 && CmbImpresoras.SelectedItem != null)
                config.ImpresoraSeleccionada = CmbImpresoras.SelectedItem.ToString() ?? string.Empty;
            if (CmbAnchoTicket?.SelectedItem is ComboBoxItem anchoItem && int.TryParse(anchoItem.Tag?.ToString(), out int anchoCols))
                config.AnchoTicketCaracteres = anchoCols;
            config.BasculaHabilitada = ChkUsarBascula.IsChecked == true;
            if (CmbPuertosCOM.SelectedIndex > 0 && CmbPuertosCOM.SelectedItem != null)
                config.BasculaPuerto = CmbPuertosCOM.SelectedItem.ToString() ?? "Auto";
            config.Save();
            if (decimal.TryParse(textoFondo, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal montoFondo) ||
                decimal.TryParse(textoFondo, NumberStyles.Any, CultureInfo.GetCultureInfo("es-MX"), out montoFondo))
            {
                if (montoFondo > 0)
                {
                    try
                    {
                        TurnoCajaManager.AbrirTurno(montoFondo);
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.Show($"No se pudo registrar el fondo de caja inicial: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            _setupCompletado = true;
            MainWindow main = new MainWindow();
            Application.Current.MainWindow = main;
            main.ActualizarNombreNegocioEnUI(config);
            main.Show();
            this.Close();
        }

        private void BadgePaso1_Click(object sender, MouseButtonEventArgs e)
        {
            if (_pasoActual > 1) { _pasoActual = 1; ActualizarVistaPasos(); }
        }

        private void BadgePaso2_Click(object sender, MouseButtonEventArgs e)
        {
            if (_pasoActual > 2) { _pasoActual = 2; ActualizarVistaPasos(); }
        }

        private void BadgePaso3_Click(object sender, MouseButtonEventArgs e)
        {
            if (_pasoActual > 3) { _pasoActual = 3; ActualizarVistaPasos(); }
        }

        private void BtnOmitirIntro_Click(object sender, RoutedEventArgs e)
        {
            VideoIntro.Stop();
            var config = AppConfig.Load();
            config.Idioma = IdiomaService.IdiomaActual;
            if (string.IsNullOrWhiteSpace(config.NombreNegocio))
            {
                config.NombreNegocio = "Mi Tienda";
            }
            config.Save();

            _setupCompletado = true;
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.ActualizarNombreNegocioEnUI(config);
            main.Show();
            this.Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private static void AnimarPaso(UIElement? paso)
        {
            if (paso == null) return;
            if (paso.RenderTransform is not TranslateTransform translate)
            {
                translate = new TranslateTransform();
                paso.RenderTransform = translate;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            paso.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        }

        private void TicketPreviewInputs_Changed(object sender, TextChangedEventArgs e) => ActualizarVistaPreviaTicket();
        private void CmbAnchoTicket_SelectionChanged(object sender, SelectionChangedEventArgs e) => ActualizarVistaPreviaTicket();

        private string GenerarTextoTicketEjemplo()
        {
            int anchoCols = (CmbAnchoTicket?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int cols))
                ? cols
                : 42;

            var cfg = new AppConfig
            {
                NombreNegocio = string.IsNullOrWhiteSpace(TxtNombreNegocio?.Text) ? "MI TIENDA" : TxtNombreNegocio.Text.Trim(),
                DireccionNegocio = LicenciaService.EsPremium() ? (TxtDireccion?.Text.Trim() ?? string.Empty) : string.Empty,
                MensajePie = string.IsNullOrWhiteSpace(TxtMensajePie?.Text) ? "Gracias por su preferencia!" : TxtMensajePie.Text.Trim(),
                AnchoTicketCaracteres = anchoCols
            };

            var ventaPrueba = new Venta
            {
                Id = 1,
                FechaHora = DateTime.Now,
                Total = 25m,
                PagoCon = 50m,
                Estado = "Abierta",
                MetodoPago = "Efectivo",
                PagoEfectivo = 25m,
                Productos =
                [
                    new DetalleVenta { ProductoId = 1, Nombre = "Producto de prueba", Cantidad = 1m, Precio = 25m }
                ]
            };

            return TicketService.GenerarTextoTicket(ventaPrueba, ventaPrueba.Productos, 50m, 25m, cfg);
        }

        private void ActualizarVistaPreviaTicket()
        {
            if (TxtPreviewTicket == null) return;
            TxtPreviewTicket.Text = GenerarTextoTicketEjemplo();
        }

        private void ChkUsarBascula_Changed(object sender, RoutedEventArgs e) => ActualizarEstadoBasculaUI();

        private void ActualizarEstadoBasculaUI()
        {
            bool activa = ChkUsarBascula?.IsChecked == true;
            if (CmbPuertosCOM != null) CmbPuertosCOM.IsEnabled = activa;
            if (BtnProbarBascula != null) BtnProbarBascula.IsEnabled = activa;
        }

        private void BtnProbarImpresora_Click(object sender, RoutedEventArgs e)
        {
            if (CmbImpresoras.SelectedIndex <= 0 || CmbImpresoras.SelectedItem == null)
            {
                TxtPruebaHardware.Foreground = System.Windows.Media.Brushes.Goldenrod;
                TxtPruebaHardware.Text = "Selecciona una impresora primero para enviar un ticket de prueba.";
                return;
            }

            string impresora = CmbImpresoras.SelectedItem.ToString() ?? string.Empty;
            try
            {
                using var doc = new PrintDocument();
                doc.PrinterSettings.PrinterName = impresora;
                string texto = GenerarTextoTicketEjemplo();
                doc.PrintPage += (_, args) =>
                {
                    if (args.Graphics != null)
                    {
                        using var font = new Font("Consolas", 8.5f);
                        args.Graphics.DrawString(texto, font, System.Drawing.Brushes.Black, new PointF(8, 8));
                    }
                };
                doc.Print();
                TxtPruebaHardware.Foreground = System.Windows.Media.Brushes.ForestGreen;
                TxtPruebaHardware.Text = $"Ticket de prueba enviado correctamente a '{impresora}'.";
            }
            catch (Exception ex)
            {
                TxtPruebaHardware.Foreground = System.Windows.Media.Brushes.Firebrick;
                TxtPruebaHardware.Text = $"No se pudo imprimir en '{impresora}': {ex.Message}";
            }
        }

        private async void BtnProbarBascula_Click(object sender, RoutedEventArgs e)
        {
            string puerto = CmbPuertosCOM.SelectedItem?.ToString() ?? string.Empty;
            if (CmbPuertosCOM.SelectedIndex <= 0)
            {
                var puertos = BasculaService.ObtenerPuertosDisponibles();
                puerto = puertos.Length > 0 ? puertos[0] : string.Empty;
            }

            BtnProbarBascula.IsEnabled = false;
            TxtPruebaHardware.Foreground = System.Windows.Media.Brushes.Gray;
            TxtPruebaHardware.Text = "Probando conexion con bascula...";
            try
            {
                var res = await BasculaService.ProbarConexionAsync(puerto, 9600);
                if (res.Exito)
                {
                    TxtPruebaHardware.Foreground = System.Windows.Media.Brushes.ForestGreen;
                    TxtPruebaHardware.Text = $"Bascula conectada ({puerto}). Lectura actual: {res.Peso:0.000} kg";
                }
                else
                {
                    TxtPruebaHardware.Foreground = System.Windows.Media.Brushes.Firebrick;
                    TxtPruebaHardware.Text = $"Sin lectura de bascula: {res.Error}";
                }
            }
            finally
            {
                ActualizarEstadoBasculaUI();
            }
        }

        private void BtnRestaurarRespaldo_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Seleccionar respaldo o catalogo de productos",
                Filter = "Respaldo o Catalogo (*.db;*.xlsx;*.csv;*.tsv;*.txt)|*.db;*.xlsx;*.csv;*.tsv;*.txt|Base de datos SQLite (*.db)|*.db|Excel (*.xlsx)|*.xlsx|CSV / Texto (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt"
            };
            if (ofd.ShowDialog(this) != true) return;

            try
            {
                string ext = Path.GetExtension(ofd.FileName);
                if (string.Equals(ext, ".db", StringComparison.OrdinalIgnoreCase))
                {
                    SqliteConnection.ClearAllPools();
                    string destino = DatabaseHelper.DbPath;
                    string? dir = Path.GetDirectoryName(destino);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(ofd.FileName, destino, overwrite: true);

                    DatabaseHelper.InicializarBaseDeDatos();
                    DatabaseHelper.ActualizarEsquemaVentas();

                    var config = AppConfig.Load();
                    if (string.IsNullOrWhiteSpace(config.NombreNegocio))
                    {
                        config.NombreNegocio = "Mi Tienda";
                    }
                    config.Save();

                    _setupCompletado = true;
                    var main = new MainWindow();
                    Application.Current.MainWindow = main;
                    main.ActualizarNombreNegocioEnUI(config);
                    main.Show();
                    this.Close();
                    return;
                }

                var preview = ImportarExportarService.PrevisualizarImportacion(
                    ofd.FileName,
                    LicenciaService.EsPremium(),
                    ModoImportacion.CrearYActualizar);

                if (preview.Errores.Count > 0 && preview.Creados == 0 && preview.Actualizados == 0)
                {
                    CustomMessageBox.Show(this, string.Join("\n", preview.Errores.Take(3)), "Aviso de importacion", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var resultado = ImportarExportarService.ConfirmarImportacion(preview);
                string resumen = $"Catalogo importado correctamente.\n\n• Creados: {resultado.Creados}\n• Actualizados: {resultado.Actualizados}\n• Rechazados: {resultado.Rechazados}";
                if (resultado.LimiteFreeAlcanzado)
                {
                    resumen += "\n\n(Se alcanzo el limite gratuito de 400 productos.)";
                }
                CustomMessageBox.Show(this, resumen, "Importacion completada", MessageBoxButton.OK, MessageBoxImage.Information);

                _pasoActual = 1;
                VideoIntro.Stop();
                ActualizarVistaPasos();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"No se pudo importar el archivo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (!_setupCompletado)
            {
                Application.Current.Shutdown();
            }
        }
    }
}