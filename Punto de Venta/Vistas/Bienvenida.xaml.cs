using System;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Punto_de_Venta.Servicios;

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
            }
            else if (string.IsNullOrWhiteSpace(TxtMensajePie.Text) ||
                     TxtMensajePie.Text == "Gracias por su preferencia!" ||
                     TxtMensajePie.Text == "Thank you for your business!")
            {
                TxtMensajePie.Text = IdiomaService.Obtener("WelcomeTicketMsgDefault", "Gracias por su preferencia!");
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
            BarraProgreso.Value = _pasoActual;

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
                config.ImpresoraSeleccionada = CmbImpresoras.SelectedItem.ToString();
            config.BasculaHabilitada = ChkUsarBascula.IsChecked == true;
            if (CmbPuertosCOM.SelectedIndex > 0 && CmbPuertosCOM.SelectedItem != null)
                config.BasculaPuerto = CmbPuertosCOM.SelectedItem.ToString();
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