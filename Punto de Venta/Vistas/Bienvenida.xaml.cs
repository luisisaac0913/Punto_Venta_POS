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
        private int _pasoActual = 0; // 0 = Video Intro
        private bool _setupCompletado = false; // Bandera para controlar el cierre seguro

        public Bienvenida()
        {
            InitializeComponent();
            CargarImpresoras();
            CargarPuertos();
            ActualizarVistaPasos();

            // Cargar icono inicial de tema oscuro/claro
            var config = AppConfig.Load();
            BtnThemeToggle.Content = config.ModoOscuro ? "☀️" : "🌙";
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

        // Se ejecuta cuando el control de video terminó de crearse en memoria
        private void VideoIntro_Loaded(object sender, RoutedEventArgs e)
        {
            VideoIntro.Play();
        }

        // Avanzar usando la tecla ENTER
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
            CmbImpresoras.Items.Add("(Ninguna / Mas tarde)");
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

            // Actualizar la barra de progreso
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

                // Foco automático
                if (_pasoActual == 1) TxtNombreNegocio.Focus();
                if (_pasoActual == 3) TxtFondoCaja.Focus();
            }
        }

        private void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            if (_pasoActual == 1 && string.IsNullOrWhiteSpace(TxtNombreNegocio.Text))
            {
                CustomMessageBox.Show("El nombre de la tienda es obligatorio para poder continuar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            // NUEVO: Validación de UX para evitar arrancar con caja cerrada si el usuario teclea mal el fondo
            string textoFondo = TxtFondoCaja.Text.Trim();
            if (!string.IsNullOrWhiteSpace(textoFondo))
            {
                if (!decimal.TryParse(textoFondo, NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                    !decimal.TryParse(textoFondo, NumberStyles.Any, CultureInfo.GetCultureInfo("es-MX"), out _))
                {
                    CustomMessageBox.Show("El monto inicial del fondo de caja contiene caracteres inválidos. Solo usa números y un punto decimal.", "Fondo Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtFondoCaja.Focus();
                    TxtFondoCaja.SelectAll();
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(TxtPinAdmin.Password) && TxtPinAdmin.Password.Length < 4)
            {
                CustomMessageBox.Show("El PIN debe tener al menos 4 dígitos numéricos o dejarse vacío por ahora.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPinAdmin.Focus();
                return;
            }

            var config = AppConfig.Load();

            config.NombreNegocio = TxtNombreNegocio.Text.Trim();
            config.DireccionNegocio = TxtDireccion.Text.Trim();
            config.MensajePie = string.IsNullOrWhiteSpace(TxtMensajePie.Text) ? "GRACIAS POR SU COMPRA\nVuelva pronto" : TxtMensajePie.Text.Trim();

            // Hardware
            if (CmbImpresoras.SelectedIndex > 0 && CmbImpresoras.SelectedItem != null)
                config.ImpresoraSeleccionada = CmbImpresoras.SelectedItem.ToString();

            config.BasculaHabilitada = ChkUsarBascula.IsChecked == true;
            if (CmbPuertosCOM.SelectedIndex > 0 && CmbPuertosCOM.SelectedItem != null)
                config.BasculaPuerto = CmbPuertosCOM.SelectedItem.ToString();

            // Seguridad
            if (!string.IsNullOrWhiteSpace(TxtPinAdmin.Password))
            {
                config.EstablecerNuevoPin(TxtPinAdmin.Password.Trim());
                config.SolicitarPinAdmin = true;
            }
            else
            {
                config.SolicitarPinAdmin = false;
            }

            config.Save();

            // Guardar fondo de caja inicial formalmente
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

            // Marcamos como completado para evitar el Shutdown accidental
            _setupCompletado = true;

            MainWindow main = new MainWindow();
            Application.Current.MainWindow = main;
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

            // Si la ventana se cierra (con Alt+F4 o la X) antes de finalizar, mata el programa
            if (!_setupCompletado)
            {
                Application.Current.Shutdown();
            }
        }
    }
}