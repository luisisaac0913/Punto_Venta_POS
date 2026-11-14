using Punto_de_Venta.Servicios;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Punto_de_Venta.Vistas
{
    public static class TextoMarkdownHelper
    {
        public static readonly DependencyProperty MarkdownProperty =
            DependencyProperty.RegisterAttached(
                "Markdown",
                typeof(string),
                typeof(TextoMarkdownHelper),
                new PropertyMetadata(string.Empty, OnMarkdownChanged));

        public static string GetMarkdown(DependencyObject obj) => (string)obj.GetValue(MarkdownProperty);
        public static void SetMarkdown(DependencyObject obj, string value) => obj.SetValue(MarkdownProperty, value);

        private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            tb.Inlines.Clear();

            string texto = (e.NewValue as string ?? string.Empty).Replace("\r\n", "\n");
            string[] lineas = texto.Split('\n');

            for (int l = 0; l < lineas.Length; l++)
            {
                if (l > 0) tb.Inlines.Add(new LineBreak());

                string linea = lineas[l];
                if (linea.StartsWith("* "))
                {
                    linea = "• " + linea[2..];
                }
                else if (linea.StartsWith("- "))
                {
                    linea = "• " + linea[2..];
                }

                int idx = 0;
                while (idx < linea.Length)
                {
                    int inicio = linea.IndexOf("**", idx, StringComparison.Ordinal);
                    if (inicio < 0)
                    {
                        tb.Inlines.Add(new Run(linea[idx..]));
                        break;
                    }

                    if (inicio > idx)
                    {
                        tb.Inlines.Add(new Run(linea[idx..inicio]));
                    }

                    int fin = linea.IndexOf("**", inicio + 2, StringComparison.Ordinal);
                    if (fin < 0)
                    {
                        tb.Inlines.Add(new Run(linea[inicio..]));
                        break;
                    }

                    string negrita = linea[(inicio + 2)..fin];
                    tb.Inlines.Add(new Run(negrita) { FontWeight = FontWeights.Bold });
                    idx = fin + 2;
                }
            }
        }
    }
    public sealed class MensajeChatIA
    {
        public string Autor { get; set; } = string.Empty;
        public string Texto { get; set; } = string.Empty;
        public HorizontalAlignment Alineacion { get; set; } = HorizontalAlignment.Left;
        public Brush Fondo { get; set; } = Brushes.Transparent;
        public Brush ColorAutor { get; set; } = Brushes.Gray;
        public Brush ColorTexto { get; set; } = Brushes.White;
    }

    public partial class AsistenteModal : Window
    {
        private readonly ObservableCollection<MensajeChatIA> _mensajes = new();
        private readonly List<object> _historialApi = new();
        private CancellationTokenSource? _consultaCts;
        private bool _ocupado = false;

        public AsistenteModal(Window? owner = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            ChatList.ItemsSource = _mensajes;

            AgregarMensajeVisual("Gemini", "Hola. Puedes consultarme o pedirme modificar inventario, prestamos/abonos y promociones, o revisar ventas del dia.", false);
            Loaded += (_, _) =>
            {
                PromptBox.Focus();
                Keyboard.Focus(PromptBox);
            };
            Closing += (_, _) =>
            {
                _consultaCts?.Cancel();
            };
            Closed += (_, _) =>
            {
                _consultaCts?.Dispose();
                _consultaCts = null;
            };
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e) => Close();

        private void PromptBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_ocupado)
            {
                _ = EnviarConsultaAsync(PromptBox.Text);
                e.Handled = true;
            }
        }

        private void BtnEnviar_Click(object sender, RoutedEventArgs e)
        {
            if (!_ocupado)
            {
                _ = EnviarConsultaAsync(PromptBox.Text);
            }
        }

        private void BtnSugerenciaStock_Click(object sender, RoutedEventArgs e) =>
            _ = EnviarConsultaAsync("¿Que productos tienen stock bajo o agotado?");

        private void BtnSugerenciaVentas_Click(object sender, RoutedEventArgs e) =>
            _ = EnviarConsultaAsync("¿Cuanto vendi hoy, cual fue mi ganancia neta y que se vendio mas?");

        private void BtnSugerenciaDeudores_Click(object sender, RoutedEventArgs e) =>
            _ = EnviarConsultaAsync("¿Quienes tienen fiados pendientes y cuanto deben en total?");

        private async Task EnviarConsultaAsync(string texto)
        {
            string limpio = (texto ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(limpio) || _ocupado) return;

            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Asistente IA (Gemini)");
                return;
            }

            if (!GeminiService.EstaConfigurada())
            {
                ToastNotification.Show("Configura tu API Key de Gemini en Configuracion -> Asistente IA.", ToastType.Warning);
                return;
            }

            _ocupado = true;
            BtnEnviar.IsEnabled = false;
            BtnEnviar.Content = "Pensando...";
            PromptBox.Clear();

            AgregarMensajeVisual("Tu", limpio, true);
            var turnoUsuario = new
            {
                role = "user",
                parts = new[] { new { text = limpio } }
            };
            var historialTemporal = new List<object>(_historialApi) { turnoUsuario };

            _consultaCts?.Cancel();
            _consultaCts?.Dispose();
            _consultaCts = new CancellationTokenSource();
            var ct = _consultaCts.Token;

            try
            {
                var herramientas = AsistenteHerramientasPOS.ObtenerDeclaraciones();
                string respuesta = await GeminiService.EjecutarChatConHerramientasAsync(
                    AsistenteHerramientasPOS.InstruccionSistema,
                    historialTemporal,
                    herramientas,
                    (nombre, args) => AsistenteHerramientasPOS.EjecutarHerramientaAsync(nombre, args, this),
                    ct: ct);

                if (ct.IsCancellationRequested) return;

                _historialApi.Add(turnoUsuario);
                _historialApi.Add(new
                {
                    role = "model",
                    parts = new[] { new { text = respuesta } }
                });
                AgregarMensajeVisual("Gemini", respuesta, false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    AgregarMensajeVisual("Sistema", ex.Message, false);
                }
            }
            finally
            {
                _ocupado = false;
                BtnEnviar.IsEnabled = true;
                BtnEnviar.Content = "Enviar";
                if (IsLoaded && IsVisible)
                {
                    PromptBox.Focus();
                }
            }
        }

        private void AgregarMensajeVisual(string autor, string texto, bool esUsuario)
        {
            var accent = TryFindResource("ThemeAccent") as Brush ?? new SolidColorBrush(Color.FromRgb(0, 122, 255));
            var surface = TryFindResource("ThemeSurfaceBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(40, 40, 40));
            var textBrush = TryFindResource("ThemeTextForeground") as Brush ?? Brushes.White;
            var muted = TryFindResource("ThemeMutedForeground") as Brush ?? Brushes.LightGray;

            _mensajes.Add(new MensajeChatIA
            {
                Autor = autor,
                Texto = texto,
                Alineacion = esUsuario ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Fondo = esUsuario ? accent : surface,
                ColorAutor = esUsuario ? Brushes.White : muted,
                ColorTexto = esUsuario ? Brushes.White : textBrush
            });

            ChatScroll.ScrollToEnd();
        }
    }
}

