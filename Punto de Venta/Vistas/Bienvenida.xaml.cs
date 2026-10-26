using System.Drawing.Printing;
using System.Windows;
using System.Windows.Input;
using Punto_de_Venta.Servicios;

namespace Punto_de_Venta.Vistas
{
    public partial class Bienvenida : Window
    {
        private int _pasoActual = 0; // 0 = Video Intro

        public Bienvenida()
        {
            InitializeComponent();
            CargarImpresoras();
            CargarPuertos();
            ActualizarVistaPasos();
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
                if (_pasoActual == 3) TxtPinAdmin.Focus();
            }
        }

        private void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            // VALIDACIÓN OBLIGATORIA DEL NOMBRE DEL NEGOCIO
            if (_pasoActual == 1 && string.IsNullOrWhiteSpace(TxtNombreNegocio.Text))
            {
                CustomMessageBox.Show("El nombre de la tienda es obligatorio para poder continuar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtNombreNegocio.Focus();
                return; // Impide avanzar
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

        private void BtnFinalizar_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtPinAdmin.Password) && TxtPinAdmin.Password.Length < 4)
            {
                CustomMessageBox.Show("El PIN debe tener 4 dígitos o dejarse vacio por ahora.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPinAdmin.Focus();
                return;
            }

            var config = AppConfig.Load();

            // Guardamos el nombre tal como está, porque ya aseguramos que no esté vacío
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

            config.Save();

            MainWindow main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            this.Close();
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}