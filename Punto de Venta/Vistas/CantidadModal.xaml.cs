using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Effects;

namespace Punto_de_Venta
{
    public partial class CantidadModal : Window
    {
        private BlurEffect? _blurEffect;
        public decimal CantidadResult { get; private set; } = 1m;
        public bool Confirmado { get; private set; }
        public CantidadModal(string nombreProducto)
        {
            InitializeComponent();
            ProductTitleText.Text = nombreProducto;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
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
            QuantityBox.Text = "1"; 
            QuantityBox.Focus();
            QuantityBox.SelectAll();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (Owner != null)
            {
                Owner.Effect = null;
            }
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
            var raw = QuantityBox.Text.Trim().Replace(',', '.');
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var cant) && cant > 0m)
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