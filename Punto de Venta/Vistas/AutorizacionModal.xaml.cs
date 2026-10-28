using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Punto_de_Venta
{
    public partial class AutorizacionModal : Window
    {
        public bool Autorizado { get; private set; }
        public BlurEffect? _blurEffect;
        public AutorizacionModal()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (Owner != null)
                {
                    _blurEffect = new BlurEffect
                    {
                        Radius = 12,
                        RenderingBias = RenderingBias.Performance
                    };
                    Owner.Effect = _blurEffect;
                }
            };
            Closed += (_, _) =>
            {
                if (Owner != null)
                {
                    Owner.Effect = null;
                }
            };
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PinBox.Focus();
        }

        private void PinBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ValidarAcceso();
                e.Handled = true;
            }
        }
        private void ShakePinBox()
        {
            var shakeTransform = new TranslateTransform();
            PinBox.RenderTransform = shakeTransform;

            var anim = new DoubleAnimation
            {
                From = 0,
                To = 8,
                Duration = TimeSpan.FromMilliseconds(45),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };

            shakeTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        private void Accept_Click(object sender, RoutedEventArgs e) => ValidarAcceso();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Autorizado = false;
            Close();
        }

        private void ValidarAcceso()
        {
            var config = AppConfig.Load();
            if (config.ValidarPin(PinBox.Password))
            {
                Autorizado = true;
                Close();
            }
            else
            {
                ErrorMessage.Text = "PIN incorrecto.";
                ShakePinBox();
                PinBox.Clear();
                PinBox.Focus();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Autorizado = false;
                Close();
                e.Handled = true;
            }
        }
    }
}