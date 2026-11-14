using Punto_de_Venta.Servicios;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Punto_de_Venta.Vistas
{
    public static class TextoMarkdownHelper
    {
        private static readonly Regex RegexListaNumerada = new(@"^(\s*)(\d+)[.)]\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex RegexListaVineta = new(@"^(\s*)[-*•]\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex RegexEncabezado = new(@"^#{1,3}\s+(.+)$", RegexOptions.Compiled);

        private sealed class ItemListaMarkdown
        {
            public string Texto { get; set; } = string.Empty;
            public bool EsNumerada { get; set; }
            public string Numero { get; set; } = string.Empty;
            public bool Subnivel { get; set; }
        }

        public static readonly DependencyProperty MensajeProperty =
            DependencyProperty.RegisterAttached(
                "Mensaje",
                typeof(MensajeChatIA),
                typeof(TextoMarkdownHelper),
                new PropertyMetadata(null, OnMensajeChanged));

        public static MensajeChatIA? GetMensaje(DependencyObject obj) => (MensajeChatIA?)obj.GetValue(MensajeProperty);
        public static void SetMensaje(DependencyObject obj, MensajeChatIA? value) => obj.SetValue(MensajeProperty, value);

        private static void OnMensajeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not StackPanel host) return;
            host.Children.Clear();

            if (e.NewValue is not MensajeChatIA msg) return;

            Brush accent = Application.Current?.TryFindResource("ThemeAccent") as Brush ?? new SolidColorBrush(Color.FromRgb(0, 122, 255));
            Brush inputBg = Application.Current?.TryFindResource("ThemeInputBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 30, 30));
            Brush borderBrush = Application.Current?.TryFindResource("ThemeBorder") as Brush ?? new SolidColorBrush(Color.FromRgb(70, 70, 75));
            Brush muted = Application.Current?.TryFindResource("ThemeMutedForeground") as Brush ?? Brushes.Gray;
            Brush listBg = msg.EsUsuario ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : inputBg;
            Brush listBorder = msg.EsUsuario ? new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)) : borderBrush;

            string texto = (msg.Texto ?? string.Empty).Replace("\r\n", "\n");
            string[] lineas = texto.Split('\n');

            var bufferParrafo = new List<string>();
            var bufferLista = new List<ItemListaMarkdown>();

            foreach (string lineaOriginal in lineas)
            {
                string lineaRecortada = lineaOriginal.TrimEnd();
                if (string.IsNullOrWhiteSpace(lineaRecortada))
                {
                    if (bufferLista.Count > 0)
                    {
                        AgregarBloqueLista(host, bufferLista, msg.ColorTexto, accent, muted, listBg, listBorder, msg.EsUsuario);
                        bufferLista.Clear();
                    }
                    if (bufferParrafo.Count > 0)
                    {
                        AgregarBloqueParrafo(host, bufferParrafo, msg.ColorTexto, accent, msg.EsUsuario);
                        bufferParrafo.Clear();
                    }
                    continue;
                }

                var mNum = RegexListaNumerada.Match(lineaRecortada);
                if (mNum.Success)
                {
                    if (bufferParrafo.Count > 0)
                    {
                        AgregarBloqueParrafo(host, bufferParrafo, msg.ColorTexto, accent, msg.EsUsuario);
                        bufferParrafo.Clear();
                    }
                    bufferLista.Add(new ItemListaMarkdown
                    {
                        EsNumerada = true,
                        Numero = mNum.Groups[2].Value,
                        Texto = mNum.Groups[3].Value.Trim(),
                        Subnivel = mNum.Groups[1].Value.Length >= 2
                    });
                    continue;
                }

                var mVin = RegexListaVineta.Match(lineaRecortada);
                if (mVin.Success)
                {
                    if (bufferParrafo.Count > 0)
                    {
                        AgregarBloqueParrafo(host, bufferParrafo, msg.ColorTexto, accent, msg.EsUsuario);
                        bufferParrafo.Clear();
                    }
                    bufferLista.Add(new ItemListaMarkdown
                    {
                        EsNumerada = false,
                        Texto = mVin.Groups[2].Value.Trim(),
                        Subnivel = mVin.Groups[1].Value.Length >= 2
                    });
                    continue;
                }

                if (bufferLista.Count > 0)
                {
                    AgregarBloqueLista(host, bufferLista, msg.ColorTexto, accent, muted, listBg, listBorder, msg.EsUsuario);
                    bufferLista.Clear();
                }

                var mEnc = RegexEncabezado.Match(lineaRecortada.TrimStart());
                if (mEnc.Success)
                {
                    if (bufferParrafo.Count > 0)
                    {
                        AgregarBloqueParrafo(host, bufferParrafo, msg.ColorTexto, accent, msg.EsUsuario);
                        bufferParrafo.Clear();
                    }
                    var tbEnc = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = msg.EsUsuario ? msg.ColorTexto : accent,
                        Margin = new Thickness(0, 6, 0, 3)
                    };
                    AgregarInlines(tbEnc.Inlines, mEnc.Groups[1].Value.Trim(), accent, msg.EsUsuario);
                    host.Children.Add(tbEnc);
                    continue;
                }

                if (lineaRecortada.Trim() == "---" || lineaRecortada.Trim() == "***")
                {
                    if (bufferParrafo.Count > 0)
                    {
                        AgregarBloqueParrafo(host, bufferParrafo, msg.ColorTexto, accent, msg.EsUsuario);
                        bufferParrafo.Clear();
                    }
                    host.Children.Add(new Border
                    {
                        Height = 1,
                        Background = listBorder,
                        Opacity = 0.65,
                        Margin = new Thickness(0, 6, 0, 6)
                    });
                    continue;
                }

                bufferParrafo.Add(lineaRecortada);
            }

            if (bufferLista.Count > 0)
            {
                AgregarBloqueLista(host, bufferLista, msg.ColorTexto, accent, muted, listBg, listBorder, msg.EsUsuario);
            }
            if (bufferParrafo.Count > 0)
            {
                AgregarBloqueParrafo(host, bufferParrafo, msg.ColorTexto, accent, msg.EsUsuario);
            }
        }

        private static void AgregarBloqueParrafo(StackPanel host, List<string> lineas, Brush colorTexto, Brush accent, bool esUsuario)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                LineHeight = 19,
                Foreground = colorTexto,
                Margin = new Thickness(0, 1, 0, 4)
            };

            for (int i = 0; i < lineas.Count; i++)
            {
                if (i > 0) tb.Inlines.Add(new LineBreak());
                AgregarInlines(tb.Inlines, lineas[i], accent, esUsuario);
            }

            host.Children.Add(tb);
        }

        private static void AgregarBloqueLista(
            StackPanel host,
            List<ItemListaMarkdown> items,
            Brush colorTexto,
            Brush accent,
            Brush muted,
            Brush listBg,
            Brush listBorder,
            bool esUsuario)
        {
            var panelItems = new StackPanel();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (i > 0 && !item.Subnivel)
                {
                    panelItems.Children.Add(new Border
                    {
                        Height = 1,
                        Background = listBorder,
                        Opacity = 0.55,
                        Margin = new Thickness(0, 6, 0, 6)
                    });
                }

                var fila = new Grid
                {
                    Margin = new Thickness(item.Subnivel ? 22 : 0, item.Subnivel ? 4 : 1, 0, 1)
                };
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                if (item.EsNumerada)
                {
                    var badge = new Border
                    {
                        MinWidth = 20,
                        Height = 20,
                        Padding = new Thickness(6, 0, 6, 0),
                        CornerRadius = new CornerRadius(10),
                        Background = esUsuario ? new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)) : accent,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 0, 10, 0),
                        SnapsToDevicePixels = true,
                        Child = new TextBlock
                        {
                            Text = item.Numero,
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    Grid.SetColumn(badge, 0);
                    fila.Children.Add(badge);
                }
                else
                {
                    var punto = new Ellipse
                    {
                        Width = item.Subnivel ? 5 : 6,
                        Height = item.Subnivel ? 5 : 6,
                        Fill = item.Subnivel ? muted : (esUsuario ? Brushes.White : accent),
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(3, 7, 10, 0)
                    };
                    Grid.SetColumn(punto, 0);
                    fila.Children.Add(punto);
                }

                var tbItem = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    LineHeight = 19,
                    Foreground = colorTexto,
                    VerticalAlignment = VerticalAlignment.Center
                };
                AgregarInlines(tbItem.Inlines, item.Texto, accent, esUsuario);
                Grid.SetColumn(tbItem, 1);
                fila.Children.Add(tbItem);

                panelItems.Children.Add(fila);
            }

            host.Children.Add(new Border
            {
                Background = listBg,
                BorderBrush = listBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 4, 0, 6),
                SnapsToDevicePixels = true,
                Child = panelItems
            });
        }

        private static void AgregarInlines(InlineCollection inlines, string linea, Brush accent, bool esUsuario)
        {
            int idx = 0;
            while (idx < linea.Length)
            {
                int posNegrita = linea.IndexOf("**", idx, StringComparison.Ordinal);
                int posCodigo = linea.IndexOf('`', idx);
                int posCursiva = BuscarInicioCursiva(linea, idx);

                int siguiente = int.MaxValue;
                int tipo = 0;

                if (posNegrita >= 0 && posNegrita < siguiente)
                {
                    siguiente = posNegrita;
                    tipo = 1;
                }
                if (posCodigo >= 0 && posCodigo < siguiente)
                {
                    siguiente = posCodigo;
                    tipo = 2;
                }
                if (posCursiva >= 0 && posCursiva < siguiente)
                {
                    siguiente = posCursiva;
                    tipo = 3;
                }

                if (tipo == 0)
                {
                    inlines.Add(new Run(linea[idx..]));
                    break;
                }

                if (siguiente > idx)
                {
                    inlines.Add(new Run(linea[idx..siguiente]));
                }

                if (tipo == 1)
                {
                    int fin = linea.IndexOf("**", siguiente + 2, StringComparison.Ordinal);
                    if (fin < 0)
                    {
                        inlines.Add(new Run(linea[siguiente..]));
                        break;
                    }
                    inlines.Add(new Run(linea[(siguiente + 2)..fin]) { FontWeight = FontWeights.Bold });
                    idx = fin + 2;
                }
                else if (tipo == 2)
                {
                    int fin = linea.IndexOf('`', siguiente + 1);
                    if (fin < 0)
                    {
                        inlines.Add(new Run(linea[siguiente..]));
                        break;
                    }
                    inlines.Add(new Run(linea[(siguiente + 1)..fin])
                    {
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        FontWeight = FontWeights.SemiBold,
                        Foreground = esUsuario ? Brushes.White : accent
                    });
                    idx = fin + 1;
                }
                else
                {
                    int fin = BuscarFinCursiva(linea, siguiente + 1);
                    if (fin < 0)
                    {
                        inlines.Add(new Run(linea[siguiente].ToString()));
                        idx = siguiente + 1;
                        continue;
                    }
                    inlines.Add(new Run(linea[(siguiente + 1)..fin]) { FontStyle = FontStyles.Italic });
                    idx = fin + 1;
                }
            }
        }

        private static int BuscarInicioCursiva(string linea, int desde)
        {
            for (int i = desde; i < linea.Length - 1; i++)
            {
                if (linea[i] != '*') continue;
                if (linea[i + 1] == '*')
                {
                    i++;
                    continue;
                }
                if (!char.IsWhiteSpace(linea[i + 1]) && BuscarFinCursiva(linea, i + 1) > i)
                {
                    return i;
                }
            }
            return -1;
        }

        private static int BuscarFinCursiva(string linea, int desde)
        {
            for (int i = desde; i < linea.Length; i++)
            {
                if (linea[i] != '*') continue;
                if (i + 1 < linea.Length && linea[i + 1] == '*')
                {
                    i++;
                    continue;
                }
                if (i > desde && !char.IsWhiteSpace(linea[i - 1]))
                {
                    return i;
                }
            }
            return -1;
        }
    }
    public sealed class MensajeChatIA
    {
        public string Autor { get; set; } = string.Empty;
        public string Texto { get; set; } = string.Empty;
        public bool EsUsuario { get; set; }
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

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

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
                EsUsuario = esUsuario,
                Alineacion = esUsuario ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Fondo = esUsuario ? accent : surface,
                ColorAutor = esUsuario ? Brushes.White : muted,
                ColorTexto = esUsuario ? Brushes.White : textBrush
            });

            Dispatcher.BeginInvoke(
                new Action(() => ChatScroll.ScrollToEnd()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}

