using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Punto_de_Venta
{
    public partial class CorteCiegoModal : Window
    {
        private readonly decimal _totalEsperado;
        private bool _conteoConfirmado;
        private decimal _totalCalculado;
        private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("es-MX");
        private BlurEffect? _blurEffect;
        public decimal TotalContadoResult => _totalCalculado;
        public bool Realizado { get; private set; }

        public CorteCiegoModal(decimal totalEsperado)
        {
            InitializeComponent();
            _totalEsperado = totalEsperado;
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
            Box500.Focus();
        }

        private void Denomination_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CountedAmountBox == null) return;

            int.TryParse(Box500.Text.Trim(), out var b500);
            int.TryParse(Box200.Text.Trim(), out var b200);
            int.TryParse(Box100.Text.Trim(), out var b100);
            int.TryParse(Box50.Text.Trim(), out var b50);
            int.TryParse(Box20.Text.Trim(), out var b20);

            var coinsText = BoxCoins.Text.Trim();
            if (!decimal.TryParse(coinsText, NumberStyles.Number, _culture, out var coins) &&
                !decimal.TryParse(coinsText, NumberStyles.Number, CultureInfo.InvariantCulture, out coins))
            {
                coins = 0m;
            }

            _totalCalculado = (b500 * 500m) + (b200 * 200m) + (b100 * 100m) + (b50 * 50m) + (b20 * 20m) + coins;
            CountedAmountBox.Text = _totalCalculado.ToString("C2", _culture);
        }

        private void Denomination_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender == Box500) Box200.Focus();
            else if (sender == Box200) Box100.Focus();
            else if (sender == Box100) Box50.Focus();
            else if (sender == Box50) Box20.Focus();
            else if (sender == Box20) BoxCoins.Focus();
            else if (sender == BoxCoins) ProcesarPaso();

            e.Handled = true;
        }

        private void Action_Click(object sender, RoutedEventArgs e) => ProcesarPaso();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Realizado = false;
            Close();
        }

        private void ProcesarPaso()
        {
            if (!_conteoConfirmado)
            {
                _conteoConfirmado = true;
                Box500.IsEnabled = false;
                Box200.IsEnabled = false;
                Box100.IsEnabled = false;
                Box50.IsEnabled = false;
                Box20.IsEnabled = false;
                BoxCoins.IsEnabled = false;

                ExpectedText.Text = _totalEsperado.ToString("C2", _culture);
                CountedText.Text = _totalCalculado.ToString("C2", _culture);

                var dif = _totalCalculado - _totalEsperado;
                if (dif == 0)
                {
                    DifferenceText.Text = "$0.00 (Exacto)";
                    DifferenceText.Foreground = Brushes.ForestGreen;
                }
                else if (dif > 0)
                {
                    DifferenceText.Text = $"+{dif.ToString("C2", _culture)} (Sobrante)";
                    DifferenceText.Foreground = Brushes.DarkGoldenrod;
                }
                else
                {
                    DifferenceText.Text = $"{dif.ToString("C2", _culture)} (Faltante)";
                    DifferenceText.Foreground = Brushes.Firebrick;
                }

                AuditPanel.Visibility = Visibility.Visible;
                ActionBtn.Content = "Finalizar Corte";
                ActionBtn.Focus();
            }
            else
            {
                Realizado = true;
                Close();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Realizado = false;
                Close();
                e.Handled = true;
            }
        }
    }
}