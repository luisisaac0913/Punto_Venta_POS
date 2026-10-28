using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Punto_de_Venta.Vistas
{
    public partial class CustomMessageBox : Window
    {
        public MessageBoxResult Resultado { get; private set; } = MessageBoxResult.None;

        private CustomMessageBox(Window owner, string mensaje, string titulo, MessageBoxButton botones, MessageBoxImage icono)
        {
            InitializeComponent();
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = System.Windows.Media.Brushes.Transparent;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowState = WindowState.Maximized;

            if (owner != null)
            {
                this.Owner = owner;
            }

            TxtTitle.Text = titulo;
            TxtMessage.Text = mensaje;
            ConfigurarBotones(botones);
        }

        private void ConfigurarBotones(MessageBoxButton botones)
        {
            BotonesGrid.Children.Clear();

            if (botones == MessageBoxButton.OK)
            {
                BotonesGrid.Columns = 1;
                BotonesGrid.Children.Add(CrearBoton("OK", MessageBoxResult.OK, true));
            }
            else if (botones == MessageBoxButton.YesNo)
            {
                BotonesGrid.Columns = 2;
                BotonesGrid.Children.Add(CrearBoton("No", MessageBoxResult.No, false));
                BotonesGrid.Children.Add(CrearBoton("Sí", MessageBoxResult.Yes, true));
            }
            else if (botones == MessageBoxButton.OKCancel)
            {
                BotonesGrid.Columns = 2;
                BotonesGrid.Children.Add(CrearBoton("Cancelar", MessageBoxResult.Cancel, false));
                BotonesGrid.Children.Add(CrearBoton("OK", MessageBoxResult.OK, true));
            }
        }

        private Button CrearBoton(string texto, MessageBoxResult resultadoAlClic, bool esPrimario)
        {
            Button btn = new Button
            {
                Content = texto,
                Style = esPrimario ? (Style)FindResource("AlertPrimaryButtonStyle") : (Style)FindResource("AlertButtonStyle")
            };
            if (esPrimario)
            {
                btn.Loaded += (s, e) =>
                {
                    btn.Focus();
                    Keyboard.Focus(btn);
                };
            }

            btn.Click += (s, e) =>
            {
                Resultado = resultadoAlClic;
                this.Close();
            };

            return btn;
        }

        private void Fondo_MouseDown(object sender, MouseButtonEventArgs e)
        {
        }


        public static MessageBoxResult Show(string mensaje)
        {
            return Show(null, mensaje, "Mensaje", MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string mensaje, string titulo)
        {
            return Show(null, mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string mensaje, string titulo, MessageBoxButton botones)
        {
            return Show(null, mensaje, titulo, botones, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string mensaje, string titulo, MessageBoxButton botones, MessageBoxImage icono)
        {
            return Show(null, mensaje, titulo, botones, icono);
        }

        public static MessageBoxResult Show(Window owner, string mensaje)
        {
            return Show(owner, mensaje, "Mensaje", MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(Window owner, string mensaje, string titulo)
        {
            return Show(owner, mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(Window owner, string mensaje, string titulo, MessageBoxButton botones)
        {
            return Show(owner, mensaje, titulo, botones, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(Window owner, string mensaje, string titulo, MessageBoxButton botones, MessageBoxImage icono)
        {
            CustomMessageBox msg = new CustomMessageBox(owner, mensaje, titulo, botones, icono);
            msg.ShowDialog();
            return msg.Resultado;
        }
    }
}