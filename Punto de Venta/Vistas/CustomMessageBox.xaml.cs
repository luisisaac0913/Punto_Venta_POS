using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Punto_de_Venta.Vistas
{
    public partial class CustomMessageBox : Window
    {
        public MessageBoxResult Resultado { get; private set; } = MessageBoxResult.None;
        private int _segundosRestantes = 0;
        private DispatcherTimer? _timerBloqueo;

        private CustomMessageBox(Window owner, string mensaje, string titulo, MessageBoxButton botones, MessageBoxImage icono, int segundosBloqueo = 0)
        {
            InitializeComponent();
            _segundosRestantes = Math.Max(0, segundosBloqueo);
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = System.Windows.Media.Brushes.Transparent;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowState = WindowState.Maximized;

            if (owner != null)
            {
                this.Owner = owner;
            }
            else if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                this.Owner = Application.Current.MainWindow;
            }

            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetControlTabNavigation(this, KeyboardNavigationMode.Cycle);

            this.PreviewKeyDown += CustomMessageBox_PreviewKeyDown;

            TxtTitle.Text = Punto_de_Venta.Servicios.IdiomaService.TraducirTexto(titulo);
            TxtMessage.Text = Punto_de_Venta.Servicios.IdiomaService.TraducirTexto(mensaje);
            ConfigurarBotones(botones);
            this.Closed += (_, _) => _timerBloqueo?.Stop();
        }

        private void CustomMessageBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_segundosRestantes > 0)
                {
                    e.Handled = true;
                    return;
                }
                Resultado = (BotonesGrid.Columns == 1) ? MessageBoxResult.OK : MessageBoxResult.Cancel;
                this.Close();
                e.Handled = true;
            }
        }

        private void ConfigurarBotones(MessageBoxButton botones)
        {
            BotonesGrid.Children.Clear();

            if (botones == MessageBoxButton.OK)
            {
                BotonesGrid.Columns = 1;
                var btnOk = CrearBoton(_segundosRestantes > 0 ? $"OK ({_segundosRestantes}s)" : "OK", MessageBoxResult.OK, true);
                if (_segundosRestantes > 0)
                {
                    btnOk.IsEnabled = false;
                    _timerBloqueo = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _timerBloqueo.Tick += (_, _) =>
                    {
                        _segundosRestantes--;
                        if (_segundosRestantes > 0)
                        {
                            btnOk.Content = $"OK ({_segundosRestantes}s)";
                        }
                        else
                        {
                            _timerBloqueo?.Stop();
                            btnOk.Content = "OK";
                            btnOk.IsEnabled = true;
                            btnOk.Focus();
                            Keyboard.Focus(btnOk);
                        }
                    };
                    _timerBloqueo.Start();
                }
                BotonesGrid.Children.Add(btnOk);
            }
            else if (botones == MessageBoxButton.YesNo)
            {
                BotonesGrid.Columns = 2;
                BotonesGrid.Children.Add(CrearBoton("No", MessageBoxResult.No, false));
                BotonesGrid.Children.Add(CrearBoton(Punto_de_Venta.Servicios.IdiomaService.EsIngles() ? "Yes" : "Si", MessageBoxResult.Yes, true));
            }
            else if (botones == MessageBoxButton.OKCancel)
            {
                BotonesGrid.Columns = 2;
                BotonesGrid.Children.Add(CrearBoton(Punto_de_Venta.Servicios.IdiomaService.EsIngles() ? "Cancel" : "Cancelar", MessageBoxResult.Cancel, false));
                BotonesGrid.Children.Add(CrearBoton("OK", MessageBoxResult.OK, true));
            }
        }

        private Button CrearBoton(string texto, MessageBoxResult resultadoAlClic, bool esPrimario)
        {
            Button btn = new Button
            {
                Content = texto,
                Style = esPrimario ? (Style)FindResource("AlertPrimaryButtonStyle") : (Style)FindResource("AlertButtonStyle"),
                IsDefault = esPrimario,
                IsCancel = (resultadoAlClic == MessageBoxResult.Cancel || resultadoAlClic == MessageBoxResult.No || (BotonesGrid.Columns == 1 && esPrimario))
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

        public static MessageBoxResult ShowWithDelay(Window owner, string mensaje, string titulo, int segundos)
        {
            CustomMessageBox msg = new CustomMessageBox(owner, mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.Information, segundos);
            msg.ShowDialog();
            return msg.Resultado;
        }
    }
}