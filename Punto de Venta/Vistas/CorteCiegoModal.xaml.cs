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
        private readonly bool _contarMonedasIndividualmente;
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

            var config = AppConfig.Load();
            _contarMonedasIndividualmente = config.ContarMonedasIndividualmente;

            if (_contarMonedasIndividualmente)
            {
                Height = 690;
                SubtituloCorte.Text = "Ingresa el desglose de billetes y monedas";
                PanelBox10.Visibility = Visibility.Visible;
                PanelBoxCoins.Visibility = Visibility.Collapsed;
                PanelMonedasIndividuales.Visibility = Visibility.Visible;
            }
            else
            {
                Height = 580;
                SubtituloCorte.Text = "Ingresa los billetes de distinto valor";
                PanelBox10.Visibility = Visibility.Collapsed;
                PanelBoxCoins.Visibility = Visibility.Visible;
                PanelMonedasIndividuales.Visibility = Visibility.Collapsed;
            }
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
            Box500.Focus();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (Owner != null)
            {
                Owner.Effect = null;
            }
        }

        private void Denomination_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CountedAmountBox == null) return;

            int.TryParse(Box500.Text.Trim(), out var b500);
            int.TryParse(Box200.Text.Trim(), out var b200);
            int.TryParse(Box100.Text.Trim(), out var b100);
            int.TryParse(Box50.Text.Trim(), out var b50);
            int.TryParse(Box20.Text.Trim(), out var b20);

            decimal totalMonedas = 0m;
            if (_contarMonedasIndividualmente)
            {
                int.TryParse(Box10?.Text.Trim(), out var c10);
                int.TryParse(Box5?.Text.Trim(), out var c5);
                int.TryParse(Box2?.Text.Trim(), out var c2);
                int.TryParse(Box1?.Text.Trim(), out var c1);

                var centavosText = BoxCentavos?.Text.Trim() ?? string.Empty;
                decimal centavos = 0m;
                if (!string.IsNullOrEmpty(centavosText))
                {
                    if (!decimal.TryParse(centavosText, NumberStyles.Number, _culture, out centavos) &&
                        !decimal.TryParse(centavosText, NumberStyles.Number, CultureInfo.InvariantCulture, out centavos))
                    {
                        centavos = 0m;
                    }
                }

                totalMonedas = (c10 * 10m) + (c5 * 5m) + (c2 * 2m) + (c1 * 1m) + centavos;
            }
            else
            {
                var coinsText = BoxCoins?.Text.Trim() ?? string.Empty;
                decimal coins = 0m;
                if (!string.IsNullOrEmpty(coinsText))
                {
                    if (!decimal.TryParse(coinsText, NumberStyles.Number, _culture, out coins) &&
                        !decimal.TryParse(coinsText, NumberStyles.Number, CultureInfo.InvariantCulture, out coins))
                    {
                        coins = 0m;
                    }
                }
                totalMonedas = coins;
            }

            _totalCalculado = (b500 * 500m) + (b200 * 200m) + (b100 * 100m) + (b50 * 50m) + (b20 * 20m) + totalMonedas;
            CountedAmountBox.Text = _totalCalculado.ToString("C2", _culture);
        }

        private void Denomination_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender == Box500) Box200.Focus();
            else if (sender == Box200) Box100.Focus();
            else if (sender == Box100) Box50.Focus();
            else if (sender == Box50) Box20.Focus();
            else if (sender == Box20)
            {
                if (_contarMonedasIndividualmente && Box10 != null) Box10.Focus();
                else BoxCoins.Focus();
            }
            else if (sender == Box10) Box5?.Focus();
            else if (sender == Box5) Box2?.Focus();
            else if (sender == Box2) Box1?.Focus();
            else if (sender == Box1)
            {
                if (BoxCentavos != null) BoxCentavos.Focus();
                else ProcesarPaso();
            }
            else if (sender == BoxCentavos || sender == BoxCoins)
            {
                ProcesarPaso();
            }

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

                if (_contarMonedasIndividualmente)
                {
                    if (Box10 != null) Box10.IsEnabled = false;
                    if (Box5 != null) Box5.IsEnabled = false;
                    if (Box2 != null) Box2.IsEnabled = false;
                    if (Box1 != null) Box1.IsEnabled = false;
                    if (BoxCentavos != null) BoxCentavos.IsEnabled = false;
                }
                else
                {
                    if (BoxCoins != null) BoxCoins.IsEnabled = false;
                }

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