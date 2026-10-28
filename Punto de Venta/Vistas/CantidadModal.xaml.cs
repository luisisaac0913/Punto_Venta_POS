using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Effects;

namespace Punto_de_Venta
{
    public partial class CantidadModal : Window
    {
        private BlurEffect? _blurEffect;
        public int CantidadResult { get; private set; } = 1;
        public bool Confirmado { get; private set; }
        public CantidadModal(string nombreProducto)
        {
            InitializeComponent();
            ProductTitleText.Text = nombreProducto;
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
            QuantityBox.Text = "1"; 
            QuantityBox.Focus();
            QuantityBox.SelectAll();
        }

        private void QuantityBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirmar();
                e.Handled = true;
            }
        }

        private void Accept_Click(object sender, RoutedEventArgs e) => Confirmar();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmado = false;
            Close();
        }

        private void Confirmar()
        {
            if (int.TryParse(QuantityBox.Text.Trim(), out var cant) && cant > 0)
            {
                CantidadResult = cant;
                Confirmado = true;
                Close();
            }
            else
            {
                QuantityBox.SelectAll();
                QuantityBox.Focus();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Confirmado = false;
                Close();
                e.Handled = true;
            }
        }
    }
}