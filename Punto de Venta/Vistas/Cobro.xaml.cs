using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Punto_de_Venta.Vistas
{
    public partial class Cobro : UserControl, ILimpiarSeleccion, IManejadorEscape
    {
        #region Campos Privados y Colecciones
        private DispatcherTimer _typingTimer;
        private bool _procesandoPago = false;
        private List<Producto> _catalog = new();
        private List<Promocion> _promocionesCargadas = new();
        private Dictionary<int, List<Promocion>> _promosPorProducto = new();
        private List<Promocion> _combosCargados = new();
        private readonly DispatcherTimer _clockTimer;
        private int _saleNumber;
        private bool _purchaseFeedbackVisible;
        private ItemCarrito? _itemEnPesaje;
        private bool _modalAbierto = false;

        public ObservableCollection<ItemCarrito> Cart { get; } = new();
        private readonly ObservableCollection<VentaEnEspera> _ventasEnEspera = new();
        #endregion

        #region Inicializacion y Constructor
        public Cobro()
        {
            InitializeComponent();
            DataContext = this;
            Language = XmlLanguage.GetLanguage("es-MX");
            Cart.CollectionChanged += Cart_CollectionChanged;
            if (VentasEnEsperaListBox != null)
            {
                VentasEnEsperaListBox.ItemsSource = _ventasEnEspera;
            }
            RefreshSummary();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => ((TextBlock)FindName("ClockText")).Text = DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
            _clockTimer.Start();
            ActualizarVisibilidadEmptyState();
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _typingTimer.Tick += (s, args) =>
            {
                _typingTimer.Stop();
                RefreshSuggestions();
            };
            var searchBox = (TextBox)FindName("SearchBox");
            if (searchBox != null)
            {
                searchBox.LostKeyboardFocus += (s, e) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!SuggestionsPopup.IsKeyboardFocusWithin)
                        {
                            SuggestionsPopup.IsOpen = false;
                        }
                    }), DispatcherPriority.Input);
                };
            }
            this.PreviewGotKeyboardFocus += Cobro_PreviewGotKeyboardFocus;
            if (QuickChkCantidad != null && QuickChkBascula != null && QuickChkContar1Pz != null)
            {
                QuickChkCantidad.Checked += (s, e) =>
                {
                    QuickChkBascula.IsChecked = false;
                    QuickChkContar1Pz.IsChecked = false;
                };
                QuickChkBascula.Checked += (s, e) =>
                {
                    QuickChkCantidad.IsChecked = false;
                    QuickChkContar1Pz.IsChecked = false;
                };
                QuickChkContar1Pz.Checked += (s, e) =>
                {
                    QuickChkCantidad.IsChecked = false;
                    QuickChkBascula.IsChecked = false;
                };
            }
            Unloaded += UserControl_Unloaded;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ActualizarIndicadoresPremiumUI();
            VerificarTurnoPendienteDeDiasAnteriores();
            try
            {
                _catalog = ProductoDB.Load();
                CargarPromocionesDesdeDb();
                CargarVentasEnEsperaDesdeDb();
                CargarProductosRapidosDesdeConfig();
                RefreshSuggestions();
                _ = ActualizarEstadoHardwareAsync();
            }
            catch (Exception exception)
            {
                PosLogger.Error("Cobro.UserControl_Loaded", exception);
                _catalog = new();
                if (FindName("SuggestionsPopup") is Popup popup) popup.IsOpen = false;
                ToastNotification.Show($"No se pudo cargar el catalogo: {exception.Message}", ToastType.Error);
            }
            if (Cart != null)
            {
                LimpiarEventosCarrito();
            }
            RefreshSummary();
            var searchBox = (TextBox)FindName("SearchBox");
            if (searchBox != null)
            {
                searchBox.Text = string.Empty;
                searchBox.Focus();
                Keyboard.Focus(searchBox);
            }
            if (FindName("CartGrid") is DataGrid cartGrid)
            {
                cartGrid.SelectedIndex = -1;
            }
            if (_clockTimer != null && !_clockTimer.IsEnabled)
            {
                _clockTimer.Start();
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _clockTimer?.Stop();
            _typingTimer?.Stop();
        }
        #endregion

        #region Manejo de Interfaz
        private void ActualizarVisibilidadEmptyState()
        {
            bool tieneArticulos = CartGrid.Items.Count > 0;
            EmptyCartPanel.Visibility = tieneArticulos ? Visibility.Collapsed : Visibility.Visible;
        }

        public void RecargarCatalogoDesdeBD()
        {
            try { _catalog = ProductoDB.Load(); } catch { _catalog = new(); }
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                ActualizarIndicadoresPremiumUI();
                VerificarTurnoPendienteDeDiasAnteriores();
                try { _catalog = ProductoDB.Load(); } catch { _catalog = new(); }
                if (Cart.Count > 0 && _catalog.Count > 0)
                {
                    var porId = _catalog.ToDictionary(p => p.Id);
                    foreach (var item in Cart)
                    {
                        if (porId.TryGetValue(item.ProductoId, out var prodActualizado))
                        {
                            item.Nombre = prodActualizado.Nombre;
                            item.Costo = prodActualizado.Costo;
                            if (!item.ContarComoUnaPieza)
                            {
                                item.Precio = prodActualizado.Precio;
                                item.Ganancia = prodActualizado.Ganancia;
                            }
                            item.Categoria = prodActualizado.Categoria;
                            item.SeVendePorPeso = prodActualizado.SeVendePorPeso;
                            item.ContarComoUnaPieza = prodActualizado.ContarComoUnaPieza;
                        }
                    }
                    RefreshSummary();
                }
                CargarPromocionesDesdeDb();
                CargarVentasEnEsperaDesdeDb();
                CargarProductosRapidosDesdeConfig();
                _ = ActualizarEstadoHardwareAsync();

                this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    this.Focus();
                    Keyboard.Focus(SearchBox);
                    SearchBox?.Focus();
                    SearchBox?.SelectAll();
                }));
            }
        }

        private void VerificarTurnoPendienteDeDiasAnteriores()
        {
            try
            {
                var turno = TurnoCajaManager.ObtenerTurnoPendienteDeDiasAnteriores();
                if (turno != null)
                {
                    string fechaStr = turno.FechaHora.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                    ToastNotification.Show($"Atencion: Tienes un turno pendiente por cerrar del dia {fechaStr}.", ToastType.Warning);
                }
            }
            catch
            {
            }
        }

        private void AnimarEntradaProducto()
        {
            var animX = new DoubleAnimation
            {
                From = 1.0,
                To = 1.06,
                Duration = TimeSpan.FromMilliseconds(90),
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var animY = new DoubleAnimation
            {
                From = 1.0,
                To = 1.06,
                Duration = TimeSpan.FromMilliseconds(90),
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            TotalScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
            TotalScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
        }

        private async Task ActualizarEstadoHardwareAsync()
        {
            try
            {
                var config = AppConfig.Load();

                if (config.BasculaHabilitada && !string.IsNullOrWhiteSpace(config.BasculaPuerto))
                {
                    var puertosActivos = BasculaService.ObtenerPuertosDisponibles();
                    bool puertoDetectado = Array.Exists(puertosActivos, p => string.Equals(p, config.BasculaPuerto, StringComparison.OrdinalIgnoreCase));

                    if (puertoDetectado)
                    {
                        DotScaleStatus.Fill = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                        StatusScaleBadge.Opacity = 1.0;
                        StatusScaleBadge.ToolTip = $"Bascula conectada en {config.BasculaPuerto}";
                    }
                    else
                    {
                        DotScaleStatus.Fill = new SolidColorBrush(Color.FromRgb(142, 142, 147));
                        StatusScaleBadge.Opacity = 0.5;
                        StatusScaleBadge.ToolTip = $"Puerto {config.BasculaPuerto} no detectado en el equipo";
                    }
                }
                else
                {
                    DotScaleStatus.Fill = new SolidColorBrush(Color.FromRgb(142, 142, 147));
                    StatusScaleBadge.Opacity = 0.5;
                    StatusScaleBadge.ToolTip = "Bascula deshabilitada en ajustes";
                }

                string nombreImpresora = config.ImpresoraSeleccionada?.Trim() ?? string.Empty;
                bool impresionHabilitada = TicketService.EsImpresoraConfiguradaValida(nombreImpresora)
                    && (config.PreguntarAntesDeImprimir || config.ImprimirTicketAutomatico);
                bool impresoraValida = false;
                bool impresoraConAdvertencia = false;
                string mensajeEstadoImpresora = string.Empty;

                if (impresionHabilitada)
                {
                    var estadoCola = await Task.Run(() => TicketService.VerificarEstadoImpresora(nombreImpresora));
                    impresoraValida = estadoCola.Instalada;
                    impresoraConAdvertencia = estadoCola.TieneAdvertenciaCola;
                    mensajeEstadoImpresora = estadoCola.MensajeEstado;
                }

                if (impresoraValida && !impresoraConAdvertencia)
                {
                    DotPrinterStatus.Fill = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                    StatusPrinterBadge.Opacity = 1.0;
                    StatusPrinterBadge.ToolTip = $"Impresora lista: {nombreImpresora}";
                }
                else if (impresoraValida && impresoraConAdvertencia)
                {
                    DotPrinterStatus.Fill = new SolidColorBrush(Color.FromRgb(243, 156, 18));
                    StatusPrinterBadge.Opacity = 1.0;
                    StatusPrinterBadge.ToolTip = mensajeEstadoImpresora;
                }
                else
                {
                    DotPrinterStatus.Fill = new SolidColorBrush(Color.FromRgb(142, 142, 147));
                    StatusPrinterBadge.Opacity = 0.5;
                    StatusPrinterBadge.ToolTip = !impresionHabilitada
                        ? "Impresora desactivada en ajustes"
                        : (string.IsNullOrWhiteSpace(mensajeEstadoImpresora) ? $"Impresora no encontrada: {nombreImpresora}" : mensajeEstadoImpresora);
                }
            }
            catch (Exception ex)
            {
                PosLogger.Warn("Cobro.ActualizarEstadoHardwareAsync", ex.Message);
                try
                {
                    DotScaleStatus.Fill = new SolidColorBrush(Color.FromRgb(142, 142, 147));
                    StatusScaleBadge.Opacity = 0.5;
                    DotPrinterStatus.Fill = new SolidColorBrush(Color.FromRgb(142, 142, 147));
                    StatusPrinterBadge.Opacity = 0.5;
                }
                catch { }
            }
        }
        public void ActualizarIndicadoresPremiumUI()
        {
            bool esPremium = LicenciaService.EsPremium();
            if (BtnPausarVenta != null)
            {
                BtnPausarVenta.Content = esPremium ? "Pausar / Espera (F2)" : "🔒 Espera (F2)";
            }
            if (RbMixto != null)
            {
                RbMixto.Content = "Mixto (F7)";
            }
            if (CreditButton != null)
            {
                CreditButton.Content = "FIAR (F6)";
            }
        }
        #endregion

        #region Modulo de Pesaje con Bascula (F8)

        private void AbrirModalPesaje()
        {
            _ = AbrirModalPesajeAsync();
        }

        private async Task AbrirModalPesajeAsync()
        {
            try
            {
                var cartGrid = (DataGrid)FindName("CartGrid");
                _itemEnPesaje = cartGrid.SelectedItem as ItemCarrito ?? Cart.LastOrDefault();

                if (_itemEnPesaje == null)
                {
                    var searchBox = (TextBox)FindName("SearchBox");
                    var query = searchBox.Text.Trim();
                    if (!string.IsNullOrEmpty(query))
                    {
                        var match = _catalog.FirstOrDefault(p =>
                            string.Equals(p.Codigo, query, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.Nombre, query, StringComparison.OrdinalIgnoreCase));

                        if (match != null)
                        {
                            AddProduct(match);
                            _itemEnPesaje = Cart.LastOrDefault();
                        }
                    }
                }

                if (_itemEnPesaje == null)
                {
                    ToastNotification.Show("Añade o selecciona un producto para pesar", ToastType.Warning);
                    FocusSearch();
                    return;
                }

                ScaleProductNameText.Text = $"{_itemEnPesaje.Nombre}";
                ScalePricePerKgText.Text = $"{_itemEnPesaje.Precio.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))} / kg";

                var config = AppConfig.Load();
                bool abrirEnModoPrecio = config.BasculaModoPrecio;

                if (abrirEnModoPrecio)
                {
                    TabModoPrecio.IsChecked = true;
                }
                else
                {
                    TabModoPeso.IsChecked = true;
                    ScaleValueInput.Text = _itemEnPesaje.Cantidad > 0 ? _itemEnPesaje.Cantidad.ToString("0.000", CultureInfo.InvariantCulture) : "0.000";
                }

                ActualizarCalculoPesaje();

                ScaleOverlay.Visibility = Visibility.Visible;
                ScaleValueInput.Focus();
                ScaleValueInput.SelectAll();

                if (!abrirEnModoPrecio)
                {
                    await ConsultarBasculaAsync();
                }
                else
                {
                    ScaleStatusText.Text = "Modo inverso activo: Escribe el monto en pesos.";
                }
            }
            catch (Exception ex)
            {
                PosLogger.Error("Cobro.AbrirModalPesajeAsync", ex);
                ToastNotification.Show($"Error al abrir modal de pesaje: {ex.Message}", ToastType.Error);
            }
        }

        private void ScaleMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _itemEnPesaje == null) return;

            bool isPriceMode = TabModoPrecio.IsChecked == true;

            if (isPriceMode)
            {
                ScaleInputLabel.Text = "MONTO A GASTAR ($)";
                ScaleResultLabel.Text = "Peso equivalente:";
                ScaleValueInput.Text = "0.00";
                ScaleStatusText.Text = "Modo inverso activo: Escribe el monto en pesos.";
            }
            else
            {
                ScaleInputLabel.Text = "PESO DETECTADO (KG)";
                ScaleResultLabel.Text = "Importe total:";
                ScaleValueInput.Text = _itemEnPesaje.Cantidad > 0 ? _itemEnPesaje.Cantidad.ToString("0.000", CultureInfo.InvariantCulture) : "0.000";
                ScaleStatusText.Text = "Listo para leer bascula.";
            }

            ScaleValueInput.Focus();
            ScaleValueInput.SelectAll();
            ActualizarCalculoPesaje();
        }

        private async Task ConsultarBasculaAsync()
        {
            ScaleStatusText.Text = "⏳ Conectando con la bascula...";
            var resultado = await BasculaService.ObtenerPesoAsync();

            if (resultado.Exito)
            {
                ScaleValueInput.Text = resultado.Peso.ToString("0.000", CultureInfo.InvariantCulture);
                ScaleStatusText.Text = resultado.Peso > 0
                    ? $"✅ Lectura recibida: {resultado.Peso:0.000} kg"
                    : "✅ Báscula conectada (plato vacío: 0.000 kg)";
                ActualizarCalculoPesaje();
            }
            else
            {
                ScaleStatusText.Text = string.IsNullOrEmpty(resultado.Error)
                    ? "⚠️ Ingresa el peso manual si la bascula esta apagada."
                    : $"⚠️ {resultado.Error}";
            }
        }

        private void ActualizarCalculoPesaje()
        {
            if (_itemEnPesaje == null) return;

            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            var usCulture = CultureInfo.GetCultureInfo("en-US");
            var mxCulture = CultureInfo.GetCultureInfo("es-MX");

            bool isPriceMode = TabModoPrecio.IsChecked == true;

            if (decimal.TryParse(ScaleValueInput.Text.Trim(), styles, usCulture, out var valor) ||
                decimal.TryParse(ScaleValueInput.Text.Trim(), styles, mxCulture, out valor))
            {
                if (isPriceMode)
                {
                    decimal pesoCalculado = _itemEnPesaje.Precio > 0 ? valor / _itemEnPesaje.Precio : 0;
                    ScaleResultText.Text = $"{pesoCalculado:0.000} kg";
                    ScaleResultText.Foreground = (Brush)FindResource("ThemeTextForeground");
                }
                else
                {
                    decimal importe = valor * _itemEnPesaje.Precio;
                    ScaleResultText.Text = importe.ToString("C2", mxCulture);
                    ScaleResultText.Foreground = (Brush)FindResource("SystemGreen");
                }
            }
            else
            {
                ScaleResultText.Text = isPriceMode ? "0.000 kg" : "$0.00";
            }
        }

        private void ScaleValueInput_TextChanged(object sender, TextChangedEventArgs e) => ActualizarCalculoPesaje();

        private void ScaleValueInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ConfirmarPesaje();
                e.Handled = true;
            }
        }

        private async void RereadScale_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TabModoPeso.IsChecked = true;
                await ConsultarBasculaAsync();
            }
            catch (Exception ex)
            {
                PosLogger.Error("Cobro.RereadScale_Click", ex);
            }
        }

        private void CancelScale_Click(object sender, RoutedEventArgs e)
        {
            ScaleOverlay.Visibility = Visibility.Collapsed;

            if (_itemEnPesaje != null && _itemEnPesaje.Cantidad == 0)
            {
                _itemEnPesaje.PropertyChanged -= CartItem_PropertyChanged;
                Cart.Remove(_itemEnPesaje);
                ActualizarVisibilidadEmptyState();
            }

            _itemEnPesaje = null;

            var cartGrid = (DataGrid)FindName("CartGrid");
            if (cartGrid != null)
            {
                var ultimo = Cart.LastOrDefault();
                cartGrid.SelectedItem = ultimo;
                if (ultimo != null)
                {
                    cartGrid.ScrollIntoView(ultimo);
                }
            }

            FocusSearch();
        }

        private void ConfirmScale_Click(object sender, RoutedEventArgs e) => ConfirmarPesaje();

        private void ConfirmarPesaje()
        {
            if (_itemEnPesaje == null)
            {
                ScaleOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            var usCulture = CultureInfo.GetCultureInfo("en-US");
            var mxCulture = CultureInfo.GetCultureInfo("es-MX");

            if (!decimal.TryParse(ScaleValueInput.Text.Trim(), styles, usCulture, out var valorIngresado) &&
                !decimal.TryParse(ScaleValueInput.Text.Trim(), styles, mxCulture, out valorIngresado) || valorIngresado <= 0)
            {
                ToastNotification.Show("Ingresa una cantidad valida mayor a 0", ToastType.Warning);
                ScaleValueInput.Focus();
                ScaleValueInput.SelectAll();
                return;
            }

            bool isPriceMode = TabModoPrecio.IsChecked == true;
            decimal pesoFinal = valorIngresado;

            if (isPriceMode)
            {
                pesoFinal = _itemEnPesaje.Precio > 0 ? valorIngresado / _itemEnPesaje.Precio : 0;
            }

            _itemEnPesaje.Cantidad = Math.Round(pesoFinal, 3);
            RefreshSummary();

            ScaleOverlay.Visibility = Visibility.Collapsed;
            ToastNotification.Show($"Peso registrado: {_itemEnPesaje.Cantidad:0.000} kg en '{_itemEnPesaje.Nombre}'", ToastType.Success);
            FocusSearch();
        }
        #endregion

        #region Busqueda y Sugerencias de Catalogo
        private void CargarPromocionesDesdeDb()
        {
            try
            {
                _promocionesCargadas = PromocionDbStore.LoadAll();
            }
            catch
            {
                _promocionesCargadas = new();
            }

            _promosPorProducto = new();
            _combosCargados = new();

            foreach (var promo in _promocionesCargadas)
            {
                if (!promo.EstaVigente()) continue;

                if (promo.Tipo == TipoPromocion.Combo)
                {
                    _combosCargados.Add(promo);
                }
                else
                {
                    if (!_promosPorProducto.TryGetValue(promo.ProductoIdPrincipal, out var lista))
                    {
                        lista = new List<Promocion>();
                        _promosPorProducto[promo.ProductoIdPrincipal] = lista;
                    }
                    lista.Add(promo);
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!this.IsVisible)
            {
                if (FindName("SuggestionsPopup") is Popup popup)
                    popup.IsOpen = false;

                return;
            }

            var searchBox = (TextBox)FindName("SearchBox");

            ((TextBlock)FindName("SearchPlaceholder")).Visibility =
                string.IsNullOrEmpty(searchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (_purchaseFeedbackVisible)
            {
                ClearPurchaseFeedback();
            }

            _typingTimer.Stop();
            _typingTimer.Start();
        }

        private void RefreshSuggestions()
        {
            var searchBox = (TextBox)FindName("SearchBox");
            var suggestionsList = (ListBox)FindName("SuggestionsList");
            var suggestionsPopup = (Popup)FindName("SuggestionsPopup");

            var query = searchBox.Text.Trim();

            if (query.Length == 0)
            {
                suggestionsList.ItemsSource = null;
                suggestionsPopup.IsOpen = false;
                return;
            }

            var idQueryMatches =
                int.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idQuery) &&
                string.Equals(idQuery.ToString(CultureInfo.InvariantCulture), query, StringComparison.Ordinal);

            List<Producto>? exactCodeNameStartsMatches = null;
            List<Producto>? exactCodeOtherMatches = null;
            List<Producto>? codeStartsNameStartsMatches = null;
            List<Producto>? codeStartsOtherMatches = null;
            List<Producto>? codeContainsNameStartsMatches = null;
            List<Producto>? codeContainsOtherMatches = null;
            List<Producto>? nameStartsMatches = null;
            List<Producto>? otherMatches = null;

            foreach (var p in _catalog)
            {
                var codigo = p.Codigo;
                var nombre = p.Nombre;

                var nombreEmpieza = !string.IsNullOrEmpty(nombre) && nombre.StartsWith(query, StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(codigo))
                {
                    if (string.Equals(codigo, query, StringComparison.OrdinalIgnoreCase))
                    {
                        if (nombreEmpieza) (exactCodeNameStartsMatches ??= new List<Producto>()).Add(p);
                        else (exactCodeOtherMatches ??= new List<Producto>()).Add(p);
                        continue;
                    }

                    if (codigo.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    {
                        if (nombreEmpieza) (codeStartsNameStartsMatches ??= new List<Producto>()).Add(p);
                        else (codeStartsOtherMatches ??= new List<Producto>()).Add(p);
                        continue;
                    }

                    if (codigo.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        if (nombreEmpieza) (codeContainsNameStartsMatches ??= new List<Producto>()).Add(p);
                        else (codeContainsOtherMatches ??= new List<Producto>()).Add(p);
                        continue;
                    }
                }

                if (nombreEmpieza)
                {
                    (nameStartsMatches ??= new List<Producto>()).Add(p);
                    continue;
                }

                if (!string.IsNullOrEmpty(nombre) && nombre.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    (otherMatches ??= new List<Producto>()).Add(p);
                    continue;
                }

                if (idQueryMatches && p.Id == idQuery)
                {
                    (otherMatches ??= new List<Producto>()).Add(p);
                }
            }

            var totalMatches =
                (exactCodeNameStartsMatches?.Count ?? 0) +
                (exactCodeOtherMatches?.Count ?? 0) +
                (codeStartsNameStartsMatches?.Count ?? 0) +
                (codeStartsOtherMatches?.Count ?? 0) +
                (codeContainsNameStartsMatches?.Count ?? 0) +
                (codeContainsOtherMatches?.Count ?? 0) +
                (nameStartsMatches?.Count ?? 0) +
                (otherMatches?.Count ?? 0);

            var matches = new List<Producto>(totalMatches);

            if (exactCodeNameStartsMatches != null) matches.AddRange(exactCodeNameStartsMatches);
            if (exactCodeOtherMatches != null) matches.AddRange(exactCodeOtherMatches);
            if (codeStartsNameStartsMatches != null) matches.AddRange(codeStartsNameStartsMatches);
            if (codeStartsOtherMatches != null) matches.AddRange(codeStartsOtherMatches);
            if (codeContainsNameStartsMatches != null) matches.AddRange(codeContainsNameStartsMatches);
            if (codeContainsOtherMatches != null) matches.AddRange(codeContainsOtherMatches);
            if (nameStartsMatches != null) matches.AddRange(nameStartsMatches);
            if (otherMatches != null) matches.AddRange(otherMatches);

            suggestionsList.ItemsSource = matches;
            suggestionsList.SelectedIndex = matches.Count > 0 ? 0 : -1;

            suggestionsPopup.IsOpen = matches.Count > 0 && this.IsVisible;
        }

        private void MoveSuggestionSelection(int offset)
        {
            var suggestionsList = (ListBox)FindName("SuggestionsList");

            if (suggestionsList.Items.Count == 0)
                return;

            var nextIndex = suggestionsList.SelectedIndex < 0
                ? 0
                : Math.Clamp(suggestionsList.SelectedIndex + offset, 0, suggestionsList.Items.Count - 1);

            suggestionsList.SelectedIndex = nextIndex;
            suggestionsList.ScrollIntoView(suggestionsList.SelectedItem);
        }

        private void SuggestionItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem { DataContext: Producto product })
            {
                AddSuggestion(product);
                e.Handled = true;
            }
        }

        private void AddSuggestion(Producto product)
        {
            _typingTimer?.Stop();
            if (FindName("SuggestionsPopup") is Popup popup)
                popup.IsOpen = false;

            if (ObtenerModalActivo() != null)
                return;

            decimal cantidad = 1m;

            if (product.SeVendePorPeso)
            {
                AddProduct(product, 0m);

                var cartGridScale = (DataGrid)FindName("CartGrid");
                var cartItemScale = Cart.FirstOrDefault(item => item.ProductoId == product.Id);

                if (cartItemScale != null && cartGridScale != null)
                {
                    cartGridScale.SelectedItem = cartItemScale;
                    cartGridScale.ScrollIntoView(cartItemScale);
                }

                var sBoxScale = (TextBox)FindName("SearchBox");
                sBoxScale?.Clear();

                ActualizarVisibilidadEmptyState();
                AnimarEntradaProducto();

                AbrirModalPesaje();
                return;
            }
            else if (product.ContarComoUnaPieza)
            {
                var modal = new CantidadModal(product.Nombre);

                if (Window.GetWindow(this) is Window w)
                {
                    modal.Owner = w;
                }

                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                modal.ShowDialog();

                if (!modal.Confirmado)
                {
                    var sBox = (TextBox)FindName("SearchBox");
                    sBox?.Clear();
                    FocusSearch();
                    return;
                }

                decimal factor = modal.CantidadResult;
                AddProductContarComoUnaPieza(product, factor);

                var sBox1pz = (TextBox)FindName("SearchBox");
                sBox1pz?.Clear();

                FocusSearch();
                ActualizarVisibilidadEmptyState();
                AnimarEntradaProducto();
                return;
            }
            else if (product.PedirCantidadAlCobrar)
            {
                var modal = new CantidadModal(product.Nombre);

                if (Window.GetWindow(this) is Window w)
                {
                    modal.Owner = w;
                }

                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                modal.ShowDialog();

                if (!modal.Confirmado)
                {
                    var sBox = (TextBox)FindName("SearchBox");
                    sBox?.Clear();
                    FocusSearch();
                    return;
                }

                cantidad = modal.CantidadResult;
            }

            AddProduct(product, cantidad);

            var cartGrid = (DataGrid)FindName("CartGrid");
            var cartItem = Cart.FirstOrDefault(item => item.ProductoId == product.Id);

            if (cartItem != null && cartGrid != null)
            {
                cartGrid.SelectedItem = cartItem;
                cartGrid.ScrollIntoView(cartItem);
            }

            var searchBox = (TextBox)FindName("SearchBox");
            searchBox?.Clear();

            FocusSearch();
            ActualizarVisibilidadEmptyState();
            AnimarEntradaProducto();
        }

        private void AddProductContarComoUnaPieza(Producto product, decimal factor)
        {
            decimal precioBase = product.Precio > 0m ? product.Precio : 1m;
            decimal costoBase = product.Costo;
            decimal precioFinal = Math.Round(precioBase * factor, 2);
            decimal costoFinal = Math.Round(costoBase * factor, 2);
            decimal gananciaFinal = precioFinal - costoFinal;

            var newItem = new ItemCarrito
            {
                ProductoId = product.Id,
                Codigo = product.Codigo,
                Nombre = product.Nombre,
                Costo = costoFinal,
                Ganancia = gananciaFinal,
                Precio = precioFinal,
                Categoria = string.IsNullOrWhiteSpace(product.Categoria) ? "N/A" : product.Categoria,
                Cantidad = 1m,
                SeVendePorPeso = false,
                ContarComoUnaPieza = true
            };
            ActualizarVisibilidadEmptyState();
            newItem.PropertyChanged += CartItem_PropertyChanged;
            Cart.Add(newItem);
            if (CartGrid != null)
            {
                CartGrid.SelectedItem = newItem;
                CartGrid.ScrollIntoView(newItem);
            }
            RefreshSummary();
        }

        private void AddSearchedProduct()
        {
            _typingTimer?.Stop();

            var searchBox = (TextBox)FindName("SearchBox");
            var query = searchBox.Text.Trim();

            if (query.Length == 0)
            {
                if (FindName("SuggestionsPopup") is Popup p) p.IsOpen = false;
                FocusSearch();
                return;
            }

            var idValido = int.TryParse(query, out var idBuscado);
            var querySpan = query.AsSpan();

            Producto? matchExacto = null;
            foreach (var p in _catalog)
            {
                if (!string.IsNullOrEmpty(p.Codigo) && p.Codigo.AsSpan().Trim().Equals(querySpan, StringComparison.OrdinalIgnoreCase))
                {
                    matchExacto = p;
                    break;
                }
                if (idValido && p.Id == idBuscado)
                {
                    matchExacto = p;
                }
            }

            if (FindName("SuggestionsPopup") is Popup popup && popup.IsOpen)
            {
                var suggestionsList = (ListBox)FindName("SuggestionsList");
                if (suggestionsList?.SelectedItem is Producto selectedProduct)
                {
                    popup.IsOpen = false;
                    AddSuggestion(selectedProduct);
                    return;
                }
            }

            if (matchExacto != null)
            {
                if (FindName("SuggestionsPopup") is Popup p) p.IsOpen = false;
                AddSuggestion(matchExacto);
                return;
            }

            if (FindName("SuggestionsPopup") is Popup popupCierre) popupCierre.IsOpen = false;

            Producto? productoCodigoPrefijo = null;
            Producto? productoNombrePrefijo = null;
            Producto? productoNombreContiene = null;

            foreach (var p in _catalog)
            {
                var codigo = p.Codigo;
                if (productoCodigoPrefijo == null && !string.IsNullOrWhiteSpace(codigo) && codigo.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                {
                    productoCodigoPrefijo = p;
                }

                var nombre = p.Nombre;
                if (!string.IsNullOrWhiteSpace(nombre))
                {
                    if (productoNombrePrefijo == null && nombre.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    {
                        productoNombrePrefijo = p;
                    }
                    else if (productoNombreContiene == null && nombre.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        productoNombreContiene = p;
                    }
                }
            }

            var product = productoCodigoPrefijo ?? productoNombrePrefijo ?? productoNombreContiene;

            if (product is null)
            {
                OpenQuickAdd(query);
                return;
            }

            ActualizarVisibilidadEmptyState();
            AddSuggestion(product);
        }

        private void RemoveSelectedProduct()
        {
            var cartGrid = (DataGrid)FindName("CartGrid");

            var selectedItem = cartGrid?.SelectedItem as ItemCarrito ?? Cart.LastOrDefault();
            if (selectedItem == null)
            {
                FocusSearch();
                return;
            }

            selectedItem.PropertyChanged -= CartItem_PropertyChanged;
            Cart.Remove(selectedItem);

            var ultimo = Cart.LastOrDefault();
            if (cartGrid != null)
            {
                cartGrid.SelectedItem = ultimo;
                if (ultimo != null)
                    cartGrid.ScrollIntoView(ultimo);
            }

            ToastNotification.Show(
                $"Se quito '{selectedItem.Nombre}' del carrito",
                ToastType.Info);

            ActualizarVisibilidadEmptyState();
            FocusSearch();
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
        }
        #endregion

        #region Modulo Modo ReStock Rapido
        private void OpenQuickRestockModal()
        {
            if (FindName("SuggestionsPopup") is System.Windows.Controls.Primitives.Popup popup)
                popup.IsOpen = false;

            QrScanCodeBox.Clear();
            QrAddAmountBox.Text = "1";
            QrFoundProductLabel.Text = string.Empty;
            QrStatusMessage.Text = string.Empty;

            QuickRestockOverlay.Visibility = Visibility.Visible;
            QrScanCodeBox.Focus();
            Keyboard.Focus(QrScanCodeBox);
        }

        private void CloseQuickRestock_Click(object sender, RoutedEventArgs e) => CloseQuickRestock();

        private void CloseQuickRestock()
        {
            QuickRestockOverlay.Visibility = Visibility.Collapsed;
            FocusSearch();
        }

        private void QrScanCodeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var code = QrScanCodeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code)) return;

            var prod = _catalog.FirstOrDefault(p =>
                string.Equals(p.Codigo, code, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString(CultureInfo.InvariantCulture) == code);

            if (prod != null)
            {
                QrFoundProductLabel.Text = $"✅ {prod.Nombre} (Stock actual: {prod.Stock})";
                QrStatusMessage.Text = string.Empty;
                QrAddAmountBox.Focus();
                QrAddAmountBox.SelectAll();
            }
            else
            {
                QrFoundProductLabel.Text = string.Empty;
                QrStatusMessage.Text = "⚠️ Producto no encontrado con ese codigo.";
                QrScanCodeBox.SelectAll();
            }

            e.Handled = true;
        }

        private void QrAddAmountBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ConfirmQuickRestockProcess();
                e.Handled = true;
            }
        }

        private void ConfirmQuickRestock_Click(object sender, RoutedEventArgs e) => ConfirmQuickRestockProcess();

        private void ConfirmQuickRestockProcess()
        {
            var code = QrScanCodeBox.Text.Trim();
            var amountText = QrAddAmountBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(code))
            {
                QrStatusMessage.Text = "Escanea o escribe un codigo.";
                QrScanCodeBox.Focus();
                return;
            }

            var prod = _catalog.FirstOrDefault(p =>
                string.Equals(p.Codigo, code, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString(CultureInfo.InvariantCulture) == code);

            if (prod == null)
            {
                QrStatusMessage.Text = "Producto no encontrado.";
                QrScanCodeBox.Focus();
                return;
            }

            string limpioAmount = (amountText ?? string.Empty).Trim().Replace(',', '.');
            if (!decimal.TryParse(limpioAmount, NumberStyles.AllowDecimalPoint | NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var addAmount) || addAmount <= 0)
            {
                QrStatusMessage.Text = "Ingresa una cantidad entera mayor a 0.";
                QrAddAmountBox.Focus();
                QrAddAmountBox.SelectAll();
                return;
            }

            prod.Stock += addAmount;

            try
            {
                ProductoDB.Save(new[] { prod });

                ToastNotification.Show($"+{addAmount} pzas añadidas a '{prod.Nombre}'. Nuevo Stock: {prod.Stock}", ToastType.Success);

                QrScanCodeBox.Clear();
                QrAddAmountBox.Text = "1";
                QrFoundProductLabel.Text = $"Ultimo: {prod.Nombre} (Stock: {prod.Stock})";
                QrStatusMessage.Text = string.Empty;
                QrScanCodeBox.Focus();
            }
            catch (Exception ex)
            {
                prod.Stock -= addAmount;
                QrStatusMessage.Text = "Error al guardar en disco.";
                ToastNotification.Show($"Error al guardar: {ex.Message}", ToastType.Error);
            }
        }
        #endregion

        #region Agregar Rapido de Producto Nuevo
        private void OpenQuickAdd(string code)
        {
            ((Popup)FindName("SuggestionsPopup")).IsOpen = false;
            QuickCodeBox.Text = code;
            QuickNameBox.Clear();
            QuickCategoryBox.Text = "Abarrotes";
            QuickCostBox.Clear();
            QuickMarginBox.Clear();
            QuickPriceBox.Clear();
            QuickValidationMessage.Text = string.Empty;
            QuickChkCantidad.IsChecked = false;
            QuickChkBascula.IsChecked = false;
            QuickChkContar1Pz.IsChecked = false;

            QuickAddOverlay.Visibility = Visibility.Visible;
            QuickNameBox.Focus();
        }

        private void CloseQuickAdd()
        {
            QuickAddOverlay.Visibility = Visibility.Collapsed;
            ((TextBox)FindName("SearchBox")).Clear();
            FocusSearch();
        }

        private void CancelQuickAdd_Click(object sender, RoutedEventArgs e) => CloseQuickAdd();

        private void QuickCostOrMargin_TextChanged(object sender, TextChangedEventArgs e)
        {
            var costText = QuickCostBox.Text.Trim();
            var marginText = QuickMarginBox.Text.Trim();

            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            var usCulture = CultureInfo.GetCultureInfo("en-US");
            var esCulture = CultureInfo.GetCultureInfo("es-ES");

            var parsedCost = decimal.TryParse(costText, styles, usCulture, out var cost) || decimal.TryParse(costText, styles, esCulture, out cost);
            var parsedMargin = decimal.TryParse(marginText, styles, usCulture, out var margin) || decimal.TryParse(marginText, styles, esCulture, out margin);

            if (parsedCost && parsedMargin && cost >= 0 && margin >= 0) QuickPriceBox.Text = (cost + margin).ToString("0.00", usCulture);
            else QuickPriceBox.Text = string.Empty;
        }

        private void QuickField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender == QuickNameBox) { QuickCategoryBox.Focus(); QuickCategoryBox.SelectAll(); }
            else if (sender == QuickCategoryBox) { QuickCostBox.Focus(); QuickCostBox.SelectAll(); }
            else if (sender == QuickCostBox) { QuickMarginBox.Focus(); QuickMarginBox.SelectAll(); }
            e.Handled = true;
        }

        private void QuickMarginBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { SaveAndAddProduct(); e.Handled = true; }
        }

        private void SaveQuickAdd_Click(object sender, RoutedEventArgs e) => SaveAndAddProduct();

        private void SaveAndAddProduct()
        {
            var codigo = QuickCodeBox.Text.Trim();
            var nombre = QuickNameBox.Text.Trim();
            var category = string.IsNullOrWhiteSpace(QuickCategoryBox.Text) ? "Abarrotes" : QuickCategoryBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                QuickValidationMessage.Text = "El nombre es obligatorio.";
                ToastNotification.Show("El nombre del producto es obligatorio", ToastType.Warning);
                QuickNameBox.Focus();
                return;
            }

            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            var usCulture = CultureInfo.GetCultureInfo("en-US");
            var esCulture = CultureInfo.GetCultureInfo("es-ES");

            if (!decimal.TryParse(QuickCostBox.Text.Trim(), styles, usCulture, out var cost) && !decimal.TryParse(QuickCostBox.Text.Trim(), styles, esCulture, out cost) || cost < 0)
            {
                QuickValidationMessage.Text = "Costo no valido.";
                ToastNotification.Show("Introduce un costo valido", ToastType.Warning);
                QuickCostBox.Focus();
                return;
            }

            if (!decimal.TryParse(QuickMarginBox.Text.Trim(), styles, usCulture, out var margin) && !decimal.TryParse(QuickMarginBox.Text.Trim(), styles, esCulture, out margin) || margin < 0)
            {
                QuickValidationMessage.Text = "Ganancia no valida.";
                ToastNotification.Show("Introduce una ganancia valida", ToastType.Warning);
                QuickMarginBox.Focus();
                return;
            }

            if (!LicenciaService.EsPremium() && _catalog.Count >= 400)
            {
                QuickValidationMessage.Text = "Limite de 400 productos alcanzado.";
                LicenciaService.MostrarMensajePremium("Limite de 400 productos en inventario");
                return;
            }

            var siguiente = _catalog.Select(p => p.Id).DefaultIfEmpty(0).Max() + 1;
            var nuevoProd = new Producto
            {
                Id = siguiente,
                Codigo = codigo,
                Nombre = nombre,
                Categoria = category,
                Costo = cost,
                Ganancia = margin,
                Precio = cost + margin,
                PedirCantidadAlCobrar = QuickChkCantidad.IsChecked == true,
                SeVendePorPeso = QuickChkBascula.IsChecked == true,
                ContarComoUnaPieza = QuickChkContar1Pz.IsChecked == true
            };

            try
            {
                var actualizado = _catalog.Append(nuevoProd).ToList();
                ProductoDB.Save(new[] { nuevoProd });
                _catalog = actualizado;
            }
            catch (Exception ex)
            {
                QuickValidationMessage.Text = $"Error al guardar: {ex.Message}";
                ToastNotification.Show($"Error al guardar producto: {ex.Message}", ToastType.Error);
                return;
            }

            CloseQuickAdd();
            ToastNotification.Show($"Producto '{nuevoProd.Nombre}' registrado", ToastType.Success);
            AddSuggestion(nuevoProd);
        }
        #endregion

        #region Atajos de Teclado Centralizados
        public bool ManejarEscape()
        {
            if (CerrarModalActivo())
            {
                return true;
            }
            LimpiarEventosCarrito();
            return false;
        }

        public bool TieneModalActivo => ObtenerModalActivo() != null;

        private UIElement? ObtenerModalActivo()
        {
            if (QuickRestockOverlay != null && QuickRestockOverlay.Visibility == Visibility.Visible) return QuickRestockOverlay;
            if (CreditOverlay != null && CreditOverlay.Visibility == Visibility.Visible) return CreditOverlay;
            if (ScaleOverlay != null && ScaleOverlay.Visibility == Visibility.Visible) return ScaleOverlay;
            if (QuickAddOverlay != null && QuickAddOverlay.Visibility == Visibility.Visible) return QuickAddOverlay;
            if (CashMovementOverlay != null && CashMovementOverlay.Visibility == Visibility.Visible) return CashMovementOverlay;
            if (VentasEnEsperaOverlay != null && VentasEnEsperaOverlay.Visibility == Visibility.Visible) return VentasEnEsperaOverlay;
            if (ProductosRapidosOverlay != null && ProductosRapidosOverlay.Visibility == Visibility.Visible) return ProductosRapidosOverlay;
            if (PaymentOverlay != null && PaymentOverlay.Visibility == Visibility.Visible) return PaymentOverlay;
            return null;
        }

        private void Cobro_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UIElement? modal = ObtenerModalActivo();
            if (modal != null)
            {
                ModalFocusHelper.ProtegerFoco(e, modal);
            }
        }

        private bool CerrarModalActivo()
        {
            if (QuickRestockOverlay != null && QuickRestockOverlay.Visibility == Visibility.Visible)
            {
                CloseQuickRestock();
                return true;
            }
            if (CreditOverlay != null && CreditOverlay.Visibility == Visibility.Visible)
            {
                CloseCreditOverlay();
                return true;
            }
            if (ScaleOverlay != null && ScaleOverlay.Visibility == Visibility.Visible)
            {
                CancelScale_Click(this, new RoutedEventArgs());
                return true;
            }
            if (QuickAddOverlay != null && QuickAddOverlay.Visibility == Visibility.Visible)
            {
                CloseQuickAdd();
                return true;
            }
            if (CashMovementOverlay != null && CashMovementOverlay.Visibility == Visibility.Visible)
            {
                CloseCashMovement();
                return true;
            }
            if (VentasEnEsperaOverlay != null && VentasEnEsperaOverlay.Visibility == Visibility.Visible)
            {
                CloseVentasEnEspera();
                return true;
            }
            if (ProductosRapidosOverlay != null && ProductosRapidosOverlay.Visibility == Visibility.Visible)
            {
                CloseProductosRapidos();
                return true;
            }
            if (PaymentOverlay != null && PaymentOverlay.Visibility == Visibility.Visible)
            {
                PaymentOverlay.Visibility = Visibility.Collapsed;
                FocusSearch();
                return true;
            }

            var ssp = (Popup)FindName("SuggestionsPopup");
            if (ssp != null && ssp.IsOpen)
            {
                ssp.IsOpen = false;
                return true;
            }

            return false;
        }

        private void PaymentOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_procesandoPago && ReferenceEquals(e.OriginalSource, PaymentOverlay))
            {
                PaymentOverlay.Visibility = Visibility.Collapsed;
                FocusSearch();
            }
        }

        private void QuickRestockOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, QuickRestockOverlay))
            {
                CloseQuickRestock();
            }
        }

        public void LimpiarSeleccion()
        {
            CartGrid.SelectedIndex = -1;
            FocusSearch();
        }

        private async void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                await ProcesarPreviewKeyDownAsync(e);
            }
            catch (Exception ex)
            {
                PosLogger.Error("Cobro.UserControl_PreviewKeyDown", ex);
            }
        }

        private async Task ProcesarPreviewKeyDownAsync(KeyEventArgs e)
        {
            if (e.IsRepeat) return;
            if (_procesandoPago) { e.Handled = true; return; }

            if (ScaleOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CancelScale_Click(this, new RoutedEventArgs()); e.Handled = true; }
                return;
            }

            if (QuickRestockOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CloseQuickRestock(); e.Handled = true; }
                return;
            }

            if (CreditOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    if (DebtorSuggestionsBox != null && DebtorSuggestionsBox.Visibility == Visibility.Visible)
                    {
                        DebtorSuggestionsBox.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        CloseCreditOverlay();
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && DebtorSuggestionsList?.IsKeyboardFocusWithin != true)
                {
                    ConfirmCreditProcess();
                    e.Handled = true;
                }
                return;
            }

            if (VentasEnEsperaOverlay != null && VentasEnEsperaOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CloseVentasEnEspera(); e.Handled = true; return; }
                if (e.Key == Key.Enter) { RecuperarVentaSeleccionada(); e.Handled = true; return; }
                if (e.Key == Key.Delete) { DescartarVentaSeleccionada(); e.Handled = true; return; }
                return;
            }

            if (PaymentOverlay != null && PaymentOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.F1) { RbEfectivo.IsChecked = true; e.Handled = true; return; }
                if (e.Key == Key.F2) { RbTarjeta.IsChecked = true; e.Handled = true; return; }
                if (e.Key == Key.F3) { RbTransferencia.IsChecked = true; e.Handled = true; return; }
                if (e.Key == Key.F7)
                {
                    RbMixto.IsChecked = true;
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.F6)
                {
                    StartCreditProcess();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Escape)
                {
                    PaymentOverlay.Visibility = Visibility.Collapsed;
                    FocusSearch();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Enter)
                {
                    var paymentMessage = (TextBlock)FindName("PaymentMessage");
                    if (!string.IsNullOrWhiteSpace(paymentMessage?.Text))
                    {
                        paymentMessage.Text = string.Empty;
                        if (RbMixto.IsChecked == true) { MixedCashBox.Focus(); MixedCashBox.SelectAll(); }
                        else { var cashBox = (TextBox)FindName("CashReceivedBox"); cashBox?.Focus(); cashBox?.SelectAll(); }
                        e.Handled = true;
                        return;
                    }

                    e.Handled = true;
                    await FinalizePaymentAsync();
                    return;
                }
                return;
            }

            if (CashMovementOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CloseCashMovement(); e.Handled = true; }
                return;
            }

            if (QuickAddOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CloseQuickAdd(); e.Handled = true; }
                return;
            }

            var suggestionsPopup = (Popup)FindName("SuggestionsPopup");
            if (suggestionsPopup != null && suggestionsPopup.IsOpen)
            {
                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    MoveSuggestionSelection(e.Key == Key.Down ? 1 : -1);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape)
                {
                    suggestionsPopup.IsOpen = false;
                    e.Handled = true;
                    return;
                }
            }

            if (ObtenerModalActivo() != null)
            {
                if (e.Key == Key.Escape)
                {
                    CerrarModalActivo();
                    e.Handled = true;
                    return;
                }
                return;
            }

            if (e.Key == Key.F10 || (e.Key == Key.System && e.SystemKey == Key.F10)) { OpenQuickRestockModal(); e.Handled = true; return; }
            if (e.Key == Key.F8)
            {
                AbrirModalPesaje();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F5)
            {
                _ = Task.Run(() =>
                {
                    bool abierto = CajonService.AbrirCajon();
                    if (!abierto)
                    {
                        Dispatcher.Invoke(() => ToastNotification.Show("No se pudo abrir el cajon. Verifica la impresora termica en Ajustes.", ToastType.Warning));
                    }
                });
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F2)
            {
                e.Handled = true;
                if (!LicenciaService.EsPremium())
                {
                    LicenciaService.MostrarMensajePremium("Ventas en Espera");
                    return;
                }
                if (Cart.Count > 0)
                {
                    PausarVentaActual();
                }
                else
                {
                    AbrirModalVentasEnEspera();
                }
                return;
            }
            if (e.Key == Key.F4) { OpenCashMovement(); e.Handled = true; return; }

            if (_productosRapidos.Count > 0)
            {
                var teclaPresionada = FormatearAtajoTecla(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
                if (!string.IsNullOrEmpty(teclaPresionada))
                {
                    var rapido = _productosRapidos.FirstOrDefault(r => string.Equals(r.Tecla, teclaPresionada, StringComparison.OrdinalIgnoreCase));
                    if (rapido != null)
                    {
                        EjecutarProductoRapido(rapido);
                        e.Handled = true;
                        return;
                    }
                }
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Left || e.Key == Key.Tab)
            {
                FocusSearch();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                if (_modalAbierto) return;
                if (suggestionsPopup != null) suggestionsPopup.IsOpen = false;
                try { _modalAbierto = true; StartPayment(); }
                finally { _modalAbierto = false; }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                if (_modalAbierto) return;
                try
                {
                    _modalAbierto = true;
                    AddSearchedProduct();
                }
                finally { _modalAbierto = false; }

                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                var searchBox = (TextBox)FindName("SearchBox");
                if (searchBox != null && (!searchBox.IsKeyboardFocusWithin || string.IsNullOrEmpty(searchBox.Text)))
                {
                    RemoveSelectedProduct();
                    FocusSearch();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (!CerrarModalActivo())
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        LimpiarEventosCarrito();
                        mainWindow.RegresarAlMenu();
                    }
                }
                e.Handled = true;
            }
        }
        #endregion

        #region Movimientos de Caja y Fiados
        private List<string> _sugerenciasDeudores = [];
        private bool _actualizandoNombreDeudor = false;

        private void CashMovementButton_Click(object sender, RoutedEventArgs e) => OpenCashMovement();
        private void CreditButton_Click(object sender, RoutedEventArgs e) => StartCreditProcess();

        private void StartCreditProcess()
        {
            CreditValidationMessage.Text = string.Empty;
            _actualizandoNombreDeudor = true;
            DebtorNameBox.Clear();
            _actualizandoNombreDeudor = false;
            if (DebtorSuggestionsBox != null) DebtorSuggestionsBox.Visibility = Visibility.Collapsed;
            if (DebtorSuggestionsList != null) DebtorSuggestionsList.ItemsSource = null;

            try
            {
                _sugerenciasDeudores = PrestamoDB.Load()
                    .Select(d => d.Nombre?.Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                _sugerenciasDeudores = [];
            }

            CreditOverlay.Visibility = Visibility.Visible;
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                DebtorNameBox.Focus();
                Keyboard.Focus(DebtorNameBox);
            }));
        }

        private void CloseCreditOverlay()
        {
            if (DebtorSuggestionsBox != null) DebtorSuggestionsBox.Visibility = Visibility.Collapsed;
            CreditOverlay.Visibility = Visibility.Collapsed;
            if (RbMixto.IsChecked == true)
            {
                MixedCashBox.Focus();
                MixedCashBox.SelectAll();
            }
            else
            {
                var cashBox = (TextBox)FindName("CashReceivedBox");
                cashBox.Focus();
                cashBox.SelectAll();
            }
        }

        private void CancelCredit_Click(object sender, RoutedEventArgs e) => CloseCreditOverlay();

        private void DebtorNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_actualizandoNombreDeudor || DebtorSuggestionsBox == null || DebtorSuggestionsList == null) return;

            string query = DebtorNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query) || _sugerenciasDeudores.Count == 0)
            {
                DebtorSuggestionsBox.Visibility = Visibility.Collapsed;
                DebtorSuggestionsList.ItemsSource = null;
                return;
            }

            var coincidencias = _sugerenciasDeudores
                .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(6)
                .ToList();

            if (coincidencias.Count == 0 || (coincidencias.Count == 1 && string.Equals(coincidencias[0], query, StringComparison.OrdinalIgnoreCase)))
            {
                DebtorSuggestionsBox.Visibility = Visibility.Collapsed;
                DebtorSuggestionsList.ItemsSource = null;
                return;
            }

            DebtorSuggestionsList.ItemsSource = coincidencias;
            DebtorSuggestionsBox.Visibility = Visibility.Visible;
        }

        private void DebtorNameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && DebtorSuggestionsBox?.Visibility == Visibility.Visible && DebtorSuggestionsList?.Items.Count > 0)
            {
                DebtorSuggestionsList.SelectedIndex = 0;
                DebtorSuggestionsList.Focus();
                e.Handled = true;
            }
        }

        private void DebtorSuggestionsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            SeleccionarSugerenciaDeudor();
        }

        private void DebtorSuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SeleccionarSugerenciaDeudor();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (DebtorSuggestionsBox != null) DebtorSuggestionsBox.Visibility = Visibility.Collapsed;
                DebtorNameBox.Focus();
                e.Handled = true;
            }
        }

        private void SeleccionarSugerenciaDeudor()
        {
            if (DebtorSuggestionsList?.SelectedItem is not string seleccionado) return;
            _actualizandoNombreDeudor = true;
            DebtorNameBox.Text = seleccionado;
            DebtorNameBox.CaretIndex = seleccionado.Length;
            _actualizandoNombreDeudor = false;
            if (DebtorSuggestionsBox != null) DebtorSuggestionsBox.Visibility = Visibility.Collapsed;
            DebtorNameBox.Focus();
        }

        private void DebtorNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ConfirmCreditProcess();
                e.Handled = true;
            }
        }

        private void ConfirmCredit_Click(object sender, RoutedEventArgs e) => ConfirmCreditProcess();

        private void ConfirmCreditProcess()
        {
            var debtorName = DebtorNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(debtorName))
            {
                CreditValidationMessage.Text = "Introduce el nombre del cliente.";
                ToastNotification.Show("Introduce el nombre del cliente", ToastType.Warning);
                DebtorNameBox.Focus();
                return;
            }

            var total = ObtenerTotalNetoVenta();
            var totalBruto = Cart.Sum(item => item.Subtotal);
            var factor = totalBruto > 0 ? total / totalBruto : 1m;

            try
            {
                var debtors = PrestamoDB.Load();
                var conceptSummary = string.Join(", ", Cart.Select(i => $"{i.Nombre} x{i.Cantidad:0.###}"));
                var existingDebtor = debtors.Where(d => string.Equals(d.Nombre, debtorName, StringComparison.OrdinalIgnoreCase) && d.SaldoRestante > 0).OrderByDescending(d => d.Id).FirstOrDefault();

                Deudor targetDebtor;
                var culture = CultureInfo.GetCultureInfo("es-MX");
                if (existingDebtor is not null)
                {
                    if (existingDebtor.ExcederiaLimite(total))
                    {
                        string msgLimite = $"'{debtorName}' excede su limite de credito ({existingDebtor.LimiteCredito.ToString("C2", culture)}). Saldo actual: {existingDebtor.SaldoRestante.ToString("C2", culture)}, disponible: {existingDebtor.CreditoDisponible.ToString("C2", culture)}.";
                        CreditValidationMessage.Text = msgLimite;
                        ToastNotification.Show(msgLimite, ToastType.Warning);
                        DebtorNameBox.Focus();
                        return;
                    }
                    existingDebtor.MontoOriginal += total;
                    existingDebtor.Concepto += $" | {DateTime.Now:dd/MM}: {conceptSummary}";
                    targetDebtor = existingDebtor;
                }
                else
                {
                    var historialDeudor = debtors
                        .Where(d => string.Equals(d.Nombre, debtorName, StringComparison.OrdinalIgnoreCase) && d.TieneLimiteCredito)
                        .OrderByDescending(d => d.Id)
                        .FirstOrDefault();

                    if (historialDeudor is not null && total > historialDeudor.LimiteCredito)
                    {
                        string msgLimite = $"'{debtorName}' excede su limite de credito ({historialDeudor.LimiteCredito.ToString("C2", culture)}). Disponible: {historialDeudor.LimiteCredito.ToString("C2", culture)}.";
                        CreditValidationMessage.Text = msgLimite;
                        ToastNotification.Show(msgLimite, ToastType.Warning);
                        DebtorNameBox.Focus();
                        return;
                    }

                    if (!LicenciaService.EsPremium() && debtors.Count >= 5)
                    {
                        CreditValidationMessage.Text = "Limite de 5 prestamos alcanzado.";
                        LicenciaService.MostrarMensajePremium("Limite de 5 prestamos");
                        return;
                    }

                    targetDebtor = new Deudor
                    {
                        Id = 0,
                        Nombre = debtorName,
                        Telefono = historialDeudor?.Telefono ?? string.Empty,
                        LimiteCredito = historialDeudor?.LimiteCredito ?? 0m,
                        Concepto = $"{DateTime.Now:dd/MM}: {conceptSummary}",
                        MontoOriginal = total
                    };
                }

                var sale = new Venta
                {
                    Id = 0,
                    FechaHora = DateTime.Now,
                    Total = total,
                    MetodoPago = "N/A",
                    PagoCon = 0m,
                    Cambio = 0m,
                    Estado = "Fiado",
                    Productos = Cart.Select(item => {
                        decimal precioDescuento = Math.Round(item.Precio * factor, 2);
                        return new DetalleVenta
                        {
                            Id = item.ProductoId,
                            ProductoId = item.ProductoId,
                            VentaId = 0,
                            Nombre = item.Nombre,
                            Cantidad = Math.Round(item.Cantidad, 3),
                            Costo = item.Costo,
                            Ganancia = precioDescuento - item.Costo,
                            Precio = precioDescuento,
                            Categoria = item.Categoria,
                            SeVendePorPeso = item.SeVendePorPeso,
                            ContarComoUnaPieza = item.ContarComoUnaPieza
                        };
                    }).ToList()
                };

                VentaDB.RegistrarFiadoConStock(sale, Array.Empty<Producto>(), new[] { targetDebtor });

                foreach (var cartItem in Cart)
                {
                    var prod = _catalog.FirstOrDefault(p => p.Id == cartItem.ProductoId);
                    if (prod != null)
                    {
                        prod.Stock = (decimal)Math.Max(0m, (decimal)prod.Stock - cartItem.Cantidad);
                    }
                }

                var config = AppConfig.Load();
                TicketService.GuardarCopiaDigital(sale, sale.Productos, 0m, 0m, config);

                PantallaClienteService.MostrarGraciasPorSuCompra(sale.Total, 0m, 0m);
                LimpiarEventosCarrito();
                _saleNumber++;
                RefreshSummary();

                CreditOverlay.Visibility = Visibility.Collapsed;
                ((Grid)FindName("PaymentOverlay")).Visibility = Visibility.Collapsed;
                ((TextBox)FindName("SearchBox")).Clear();

                ShowPurchaseFeedback(true, $"Fiado registrado a '{debtorName}' por {total.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}.");
                ToastNotification.Show($"Fiado a {debtorName} registrado ({total.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))})", ToastType.Success);
                FocusSearch();
            }
            catch (Exception ex)
            {
                _catalog = ProductoDB.Load();
                ToastNotification.Show($"Error critico al guardar prestamo: {ex.Message}", ToastType.Error);
            }
        }

        private void OpenCashMovement()
        {
            ((Popup)FindName("SuggestionsPopup")).IsOpen = false;
            MovementAmountBox.Clear();
            MovementConceptBox.Clear();
            MovementValidationMessage.Text = string.Empty;
            RbSalida.IsChecked = true;

            CashMovementOverlay.Visibility = Visibility.Visible;
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                MovementAmountBox.Focus();
                Keyboard.Focus(MovementAmountBox);
            }));
        }

        private void CloseCashMovement()
        {
            CashMovementOverlay.Visibility = Visibility.Collapsed;
            FocusSearch();
        }

        private void CancelCashMovement_Click(object sender, RoutedEventArgs e) => CloseCashMovement();

        private void MovementAmountBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                MovementConceptBox.Focus();
                MovementConceptBox.SelectAll();
                e.Handled = true;
            }
        }

        private void MovementConceptBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveCashMovement_Click(sender, e);
                e.Handled = true;
            }
        }

        private void SaveCashMovement_Click(object sender, RoutedEventArgs e)
        {
            MovementValidationMessage.Text = string.Empty;

            string tipoMovimiento = "Salida";
            if (RbEntrada.IsChecked == true) tipoMovimiento = "Entrada";
            if (RbFondo.IsChecked == true) tipoMovimiento = "Fondo";

            if (!decimal.TryParse(MovementAmountBox.Text, out decimal monto) || (tipoMovimiento == "Fondo" ? monto < 0 : monto <= 0))
            {
                MovementValidationMessage.Text = tipoMovimiento == "Fondo" ? "Ingresa un monto valido mayor o igual a 0." : "Ingresa un monto valido mayor a 0.";
                MovementAmountBox.Focus();
                return;
            }

            string concepto = MovementConceptBox.Text.Trim();
            if (string.IsNullOrEmpty(concepto))
            {
                MovementValidationMessage.Text = "Debes ingresar un concepto o motivo.";
                MovementConceptBox.Focus();
                return;
            }

            try
            {
                if (tipoMovimiento == "Fondo")
                {
                    var turnoActual = TurnoCajaManager.ObtenerTurnoActivo();
                    if (turnoActual != null) { MovementValidationMessage.Text = "Ya existe un turno abierto. Cierra la caja primero."; return; }
                    TurnoCajaManager.AbrirTurno(monto, concepto);
                }
                else
                {
                    var turnoActual = TurnoCajaManager.ObtenerTurnoActivo();
                    if (turnoActual == null) { MovementValidationMessage.Text = "Debes abrir caja (ingresar un Fondo) primero."; return; }
                    TurnoCajaManager.RegistrarMovimientoEfectivo(tipoMovimiento, monto, concepto);
                }

                ToastNotification.Show($"Se registro {tipoMovimiento} por {monto:C2}", ToastType.Success);
                CashMovementOverlay.Visibility = Visibility.Collapsed;
                MovementAmountBox.Clear();
                MovementConceptBox.Clear();
                FocusSearch();
            }
            catch (Exception ex)
            {
                MovementValidationMessage.Text = $"Error: {ex.Message}";
            }
        }
        #endregion

        #region Proceso de Cobro de Venta
        private void PaymentMethod_Changed(object sender, RoutedEventArgs e)
        {
            if (RbMixto == null || RbEfectivo == null) return;
            var total = Math.Round(ObtenerTotalNetoVenta(), 2);

            if (RbMixto.IsChecked == true)
            {
                SinglePaymentLabel.Visibility = Visibility.Collapsed;
                SinglePaymentContainer.Visibility = Visibility.Collapsed;
                MixedPaymentContainer.Visibility = Visibility.Visible;

                MixedCashBox.Text = "0";
                MixedCardBox.Text = "0";
                MixedTransferBox.Text = "0";

                MixedCashBox.Focus();
                MixedCashBox.SelectAll();
            }
            else
            {
                SinglePaymentLabel.Visibility = Visibility.Visible;
                MixedPaymentContainer.Visibility = Visibility.Collapsed;

                if (RbEfectivo.IsChecked == true)
                {
                    SinglePaymentContainer.Visibility = Visibility.Visible;
                    SinglePaymentLabel.Text = "Dinero Recibido";

                    CashReceivedBox.IsReadOnly = false;
                    CashReceivedBox.Clear();
                    CashReceivedBox.Focus();
                }
                else if (RbTarjeta.IsChecked == true)
                {
                    SinglePaymentContainer.Visibility = Visibility.Visible;
                    SinglePaymentLabel.Text = "Monto en Terminal";

                    CashReceivedBox.Text = total.ToString("0.00", CultureInfo.InvariantCulture);
                    CashReceivedBox.IsReadOnly = true;
                    CalcularCambioVisual();
                }
                else if (RbTransferencia.IsChecked == true)
                {
                    SinglePaymentContainer.Visibility = Visibility.Visible;
                    SinglePaymentLabel.Text = "Monto por Transferencia";

                    CashReceivedBox.Text = total.ToString("0.00", CultureInfo.InvariantCulture);
                    CashReceivedBox.IsReadOnly = true;
                    CalcularCambioVisual();
                }
            }
        }

        private void StartPayment_Click(object sender, RoutedEventArgs e) => StartPayment();

        private void StartPayment()
        {
            if (Cart.Count == 0)
            {
                ShowPurchaseFeedback(false, "No hay productos para cobrar.");
                ToastNotification.Show("El carrito esta vacio", ToastType.Warning);
                FocusSearch();
                return;
            }
            CargarPromocionesDesdeDb();
                CargarVentasEnEsperaDesdeDb();
            RefreshSummary();
            ((Popup)FindName("SuggestionsPopup")).IsOpen = false;
            ((Grid)FindName("PaymentOverlay")).Visibility = Visibility.Visible;

            ((TextBlock)FindName("PaymentMessage")).Text = string.Empty;
            ((TextBlock)FindName("ChangeText")).Text = 0m.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
            ((Button)FindName("FinalizeButton")).IsEnabled = false;

            RbEfectivo.IsChecked = true;
            PaymentMethod_Changed(this, new RoutedEventArgs());
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (((Grid)FindName("PaymentOverlay")).Visibility == Visibility.Visible && RbEfectivo.IsChecked == true)
                {
                    CashReceivedBox.Focus();
                }
            }), DispatcherPriority.Input);
        }

        private async void FinalizeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await FinalizePaymentAsync();
            }
            catch (Exception ex)
            {
                PosLogger.Error("Cobro.FinalizeButton_Click", ex);
                ToastNotification.Show($"Error inesperado al finalizar cobro: {ex.Message}", ToastType.Error);
            }
        }

        private async Task FinalizePaymentAsync()
        {
            if (_procesandoPago) return;

            var culture = CultureInfo.GetCultureInfo("es-MX");
            var total = Math.Round(ObtenerTotalNetoVenta(), 2);
            var totalBruto = Cart.Sum(item => item.Subtotal);
            var factor = totalBruto > 0 ? total / totalBruto : 1m;

            decimal amountReceived = 0m;
            decimal pEfectivo = 0m, pTarjeta = 0m, pTransf = 0m;
            string paymentMethod = "Efectivo";

            if (RbMixto.IsChecked == true)
            {
                paymentMethod = "Mixto";
                decimal.TryParse((MixedCashBox.Text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out pEfectivo);
                decimal.TryParse((MixedCardBox.Text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out pTarjeta);
                decimal.TryParse((MixedTransferBox.Text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out pTransf);
                if (pEfectivo < 0m || pTarjeta < 0m || pTransf < 0m)
                {
                    ((TextBlock)FindName("PaymentMessage")).Text = "Los montos de pago no pueden ser negativos.";
                    ToastNotification.Show("No se permiten montos negativos en pago mixto", ToastType.Warning);
                    return;
                }
                amountReceived = pEfectivo + pTarjeta + pTransf;
            }
            else
            {
                paymentMethod = RbTarjeta.IsChecked == true ? "Tarjeta" : (RbTransferencia.IsChecked == true ? "Transferencia" : "Efectivo");
                string textoRecibido = (CashReceivedBox.Text ?? string.Empty).Trim().Replace(',', '.');
                if (!decimal.TryParse(textoRecibido, NumberStyles.Number, CultureInfo.InvariantCulture, out amountReceived))
                {
                    decimal.TryParse(CashReceivedBox.Text, NumberStyles.Number, culture, out amountReceived);
                }

                if (paymentMethod == "Efectivo") pEfectivo = amountReceived;
                else if (paymentMethod == "Tarjeta") pTarjeta = amountReceived;
                else if (paymentMethod == "Transferencia") pTransf = amountReceived;
            }

            if (amountReceived < total || Cart.Count == 0)
            {
                ((TextBlock)FindName("PaymentMessage")).Text = "El dinero recibido no cubre el total.";
                ToastNotification.Show("El dinero recibido no cubre el total", ToastType.Warning);
                return;
            }

            var change = Math.Max(0m, amountReceived - total);

            if (change > pEfectivo)
            {
                ((TextBlock)FindName("PaymentMessage")).Text = "No puedes dar cambio de pagos con tarjeta o transferencia.";
                ToastNotification.Show("El cambio supera el efectivo recibido", ToastType.Warning);
                return;
            }

            try
            {
                _procesandoPago = true;

                var sale = new Venta
                {
                    Id = 0,
                    FechaHora = DateTime.Now,
                    Total = total,
                    PagoCon = amountReceived,
                    Cambio = change,
                    Estado = "Abierta",
                    MetodoPago = paymentMethod,
                    PagoEfectivo = pEfectivo,
                    PagoTarjeta = pTarjeta,
                    PagoTransferencia = pTransf,
                    Productos = Cart.Select(item => {
                        decimal precioDescuento = Math.Round(item.Precio * factor, 2);
                        return new DetalleVenta
                        {
                            Id = item.ProductoId,
                            ProductoId = item.ProductoId,
                            VentaId = 0,
                            Nombre = item.Nombre,
                            Cantidad = Math.Round(item.Cantidad, 3),
                            Costo = item.Costo,
                            Ganancia = precioDescuento - item.Costo,
                            Precio = precioDescuento,
                            Categoria = item.Categoria,
                            SeVendePorPeso = item.SeVendePorPeso,
                            ContarComoUnaPieza = item.ContarComoUnaPieza
                        };
                    }).ToList()
                };

                await Task.Run(() =>
                {
                    VentaDB.RegistrarVentaConStock(sale, Array.Empty<Producto>());
                });

                int saleId = sale.Id;
                foreach (var cartItem in Cart)
                {
                    var prod = _catalog.FirstOrDefault(p => p.Id == cartItem.ProductoId);
                    if (prod != null)
                    {
                        prod.Stock = (decimal)Math.Max(0m, (decimal)prod.Stock - cartItem.Cantidad);
                    }
                }

                PantallaClienteService.MostrarGraciasPorSuCompra(sale.Total, amountReceived, change);
                LimpiarEventosCarrito();
                _saleNumber++;
                RefreshSummary();
                ((Grid)FindName("PaymentOverlay")).Visibility = Visibility.Collapsed;
                ((TextBox)FindName("SearchBox")).Clear();
                ShowPurchaseFeedback(true, $"Venta liquidada ({paymentMethod}).");
                ToastNotification.Show($"Venta #{saleId} finalizada exitosamente ({paymentMethod})", ToastType.Success);
                FocusSearch();

                var config = AppConfig.Load();
                bool imprimir = config.ImprimirTicketAutomatico;

                if (!imprimir && config.PreguntarAntesDeImprimir)
                {
                    Window window = Window.GetWindow(this) ?? Application.Current.MainWindow;
                    var resultado = CustomMessageBox.Show(window, "¿Deseas imprimir el ticket de esta venta?", "Ticket de Venta", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    imprimir = (resultado == MessageBoxResult.Yes);
                }

                await Task.Run(() =>
                {
                    if (paymentMethod == "Efectivo" || (paymentMethod == "Mixto" && pEfectivo > 0))
                    {
                        try
                        {
                            bool cajonAbierto = CajonService.AbrirCajon(out string errorCajon, config.ImpresoraSeleccionada);
                            if (!cajonAbierto && RawPrinterHelper.EsImpresoraFisicaValida(config.ImpresoraSeleccionada))
                            {
                                string msgCajon = string.IsNullOrWhiteSpace(errorCajon)
                                    ? "No se pudo abrir el cajon de dinero."
                                    : $"Cajon: {errorCajon}";
                                ToastNotification.Show(msgCajon, ToastType.Warning);
                            }
                        }
                        catch (Exception exCajon)
                        {
                            PosLogger.Error("Cobro.FinalizePaymentAsync.AbrirCajon", exCajon);
                            ToastNotification.Show("No se pudo enviar el pulso al cajon de dinero.", ToastType.Warning);
                        }
                    }

                    try
                    {
                        TicketService.GuardarCopiaDigital(sale, sale.Productos, amountReceived, change, config);
                    }
                    catch (Exception exCopia)
                    {
                        PosLogger.Warn("Cobro.FinalizePaymentAsync.GuardarCopiaDigital", exCopia);
                    }

                    if (imprimir)
                    {
                        try
                        {
                            bool impreso = TicketService.ImprimirFisico(sale, sale.Productos, amountReceived, change, config, out string detalleImpresion);
                            if (!impreso && TicketService.EsImpresoraConfiguradaValida(config.ImpresoraSeleccionada))
                            {
                                string msgTicket = string.IsNullOrWhiteSpace(detalleImpresion)
                                    ? "No se pudo imprimir el ticket en la impresora configurada."
                                    : $"No se pudo imprimir el ticket: {detalleImpresion}";
                                ToastNotification.Show(msgTicket, ToastType.Warning);
                            }
                            else if (impreso && !string.IsNullOrWhiteSpace(detalleImpresion))
                            {
                                ToastNotification.Show($"⚠️ {detalleImpresion}", ToastType.Warning);
                            }
                        }
                        catch (Exception exPrint)
                        {
                            PosLogger.Error("Cobro.FinalizePaymentAsync.ImprimirFisico", exPrint);
                            ToastNotification.Show($"Error al enviar ticket a la impresora: {exPrint.Message}", ToastType.Warning);
                        }
                    }
                });
            }
            catch (OverflowException exOv)
            {
                PosLogger.Error("Cobro.FinalizePaymentAsync.Overflow", exOv);
                ((TextBlock)FindName("PaymentMessage")).Text = "No se puede asignar un folio nuevo a la venta.";
                ToastNotification.Show("No se puede asignar folio a la venta", ToastType.Error);
            }
            catch (Exception exception)
            {
                PosLogger.Error("Cobro.FinalizePaymentAsync", exception);
                try { _catalog = ProductoDB.Load(); } catch { }
                ((TextBlock)FindName("PaymentMessage")).Text = $"Error critico en BD: {exception.Message}";
                ToastNotification.Show($"Error critico al guardar venta en base de datos: {exception.Message}", ToastType.Error);
            }
            finally
            {
                _procesandoPago = false;
            }
        }

        private void CashReceivedBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var cashBox = sender as TextBox;
            if (cashBox == null) return;
            var separatorPresent = cashBox.Text.Contains('.') || cashBox.Text.Contains(',');
            e.Handled = !e.Text.All(char.IsDigit) && !(e.Text.Length == 1 && (e.Text[0] == '.' || e.Text[0] == ',') && !separatorPresent);
        }

        private void CashReceivedBox_TextChanged(object sender, TextChangedEventArgs e) => CalcularCambioVisual();
        private void MixedPaymentBox_TextChanged(object sender, TextChangedEventArgs e) => CalcularCambioVisual();

        private void CalcularCambioVisual()
        {
            var changeText = (TextBlock)FindName("ChangeText");
            var changeLabel = (TextBlock)FindName("ChangeLabel");
            var changeContainer = (Border)FindName("ChangeContainerBorder");
            var changeScale = (ScaleTransform)FindName("ChangeScaleTransform");
            var paymentMessage = (TextBlock)FindName("PaymentMessage");
            var finalizeButton = (Button)FindName("FinalizeButton");
            var culture = CultureInfo.GetCultureInfo("es-MX");

            if (changeText == null) return;

            decimal amountReceived = 0m;

            if (RbMixto?.IsChecked == true)
            {
                decimal.TryParse(MixedCashBox?.Text, NumberStyles.Number, culture, out var mCash);
                decimal.TryParse(MixedCardBox?.Text, NumberStyles.Number, culture, out var mCard);
                decimal.TryParse(MixedTransferBox?.Text, NumberStyles.Number, culture, out var mTrans);
                amountReceived = mCash + mCard + mTrans;
            }
            else
            {
                if (!decimal.TryParse(CashReceivedBox?.Text, NumberStyles.Number, culture, out amountReceived))
                {
                    decimal.TryParse(CashReceivedBox?.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("en-US"), out amountReceived);
                }
            }

            var total = Math.Round(ObtenerTotalNetoVenta(), 2);

            if (amountReceived >= total)
            {
                var cambio = amountReceived - total;
                changeText.Text = cambio.ToString("C2", culture);
                changeText.Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113));

                if (changeLabel != null)
                {
                    changeLabel.Text = "CAMBIO A ENTREGAR";
                    changeLabel.Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                }

                if (changeContainer != null)
                {
                    changeContainer.Background = new SolidColorBrush(Color.FromArgb(24, 46, 204, 113));
                    changeContainer.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 46, 204, 113));
                }

                if (paymentMessage != null) paymentMessage.Text = string.Empty;
                if (finalizeButton != null) finalizeButton.IsEnabled = true;
            }
            else
            {
                var falta = total - amountReceived;
                changeText.Text = $"-{falta.ToString("C2", culture)}";
                changeText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));

                if (changeLabel != null)
                {
                    changeLabel.Text = "FALTA POR CUBRIR";
                    changeLabel.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                }

                if (changeContainer != null)
                {
                    changeContainer.Background = new SolidColorBrush(Color.FromArgb(20, 231, 76, 60));
                    changeContainer.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 231, 76, 60));
                }

                if (paymentMessage != null) paymentMessage.Text = "El dinero recibido no cubre el total.";
                if (finalizeButton != null) finalizeButton.IsEnabled = false;
            }

            if (changeScale != null)
            {
                var anim = new DoubleAnimation { From = 1.0, To = 1.04, Duration = TimeSpan.FromMilliseconds(60), AutoReverse = true };
                changeScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                changeScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
        }
        #endregion

        #region Utilidades, Calculos y Retroalimentacion
        private void FocusSearch()
        {
            void DoFocus()
            {
                if (CashMovementOverlay.IsVisible ||
                    CreditOverlay.IsVisible ||
                    PaymentOverlay.IsVisible ||
                    QuickAddOverlay.IsVisible ||
                    QuickRestockOverlay.IsVisible ||
                    VentasEnEsperaOverlay.IsVisible ||
                    ScaleOverlay.IsVisible)
                {
                    return;
                }

                var searchBox = (TextBox)FindName("SearchBox");
                if (searchBox != null)
                {
                    searchBox.Focus();
                    Keyboard.Focus(searchBox);
                    searchBox.CaretIndex = searchBox.Text.Length;
                }
            }

            DoFocus();
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(DoFocus));
        }

        private void LimpiarEventosCarrito()
        {
            foreach (var item in Cart)
            {
                item.PropertyChanged -= CartItem_PropertyChanged;
            }
            Cart.Clear();
            ActualizarVisibilidadEmptyState();
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CashMovementOverlay.IsVisible ||
                CreditOverlay.IsVisible ||
                PaymentOverlay.IsVisible ||
                QuickAddOverlay.IsVisible ||
                QuickRestockOverlay.IsVisible ||
                VentasEnEsperaOverlay.IsVisible ||
                ScaleOverlay.IsVisible)
            {
                return;
            }
            FocusSearch();
        }

        private void ShowPurchaseFeedback(bool succeeded, string message)
        {
            var feedback = (TextBlock)FindName("SearchMessage");
            feedback.Text = message;
            feedback.Foreground = succeeded ? Brushes.ForestGreen : Brushes.Firebrick;
            _purchaseFeedbackVisible = true;
        }

        private void ClearPurchaseFeedback()
        {
            var feedback = (TextBlock)FindName("SearchMessage");
            feedback.Text = string.Empty;
            feedback.Foreground = Brushes.Firebrick;
            _purchaseFeedbackVisible = false;
        }

        private void AddProduct(Producto product, decimal cantidad = 1m)
        {
            var existingItem = Cart.FirstOrDefault(item => item.ProductoId == product.Id);
            var currentCountInCart = existingItem?.Cantidad ?? 0m;
            var nextCount = currentCountInCart + cantidad;

            var searchMessage = (TextBlock)FindName("SearchMessage");

            if (!product.PedirCantidadAlCobrar && !product.ContarComoUnaPieza)
            {
                if (product.Stock <= 0)
                {
                    searchMessage.Text = $"⚠️ '{product.Nombre}' no tiene existencias.";
                    searchMessage.Foreground = Brushes.DarkOrange;
                    _purchaseFeedbackVisible = true;
                    ToastNotification.Show($"'{product.Nombre}' no tiene existencias", ToastType.Warning);
                }
                else if (nextCount > product.Stock)
                {
                    searchMessage.Text = $"⚠️ Supera las existencias en inventario.";
                    searchMessage.Foreground = Brushes.Crimson;
                    _purchaseFeedbackVisible = true;
                    ToastNotification.Show($"Se superan las existencias de '{product.Nombre}'", ToastType.Warning);
                }
                else if (nextCount == product.Stock)
                {
                    searchMessage.Text = $"⚠️ Ultima pieza.";
                    searchMessage.Foreground = Brushes.DarkGoldenrod;
                    _purchaseFeedbackVisible = true;
                    ToastNotification.Show($"Ultima pieza de '{product.Nombre}'", ToastType.Info);
                }
                else
                {
                    searchMessage.Text = string.Empty;
                    _purchaseFeedbackVisible = false;
                }
            }

            if (existingItem != null)
            {
                existingItem.Cantidad = Math.Round(existingItem.Cantidad + cantidad, 3);
                if (CartGrid != null)
                {
                    CartGrid.SelectedItem = existingItem;
                    CartGrid.ScrollIntoView(existingItem);
                }
                RefreshSummary();
                return;
            }

            var newItem = new ItemCarrito
            {
                ProductoId = product.Id,
                Codigo = product.Codigo,
                Nombre = product.Nombre,
                Costo = product.Costo,
                Ganancia = product.Ganancia,
                Precio = product.Precio,
                Categoria = string.IsNullOrWhiteSpace(product.Categoria) ? "N/A" : product.Categoria,
                Cantidad = Math.Round(cantidad, 3),
                SeVendePorPeso = product.SeVendePorPeso,
                ContarComoUnaPieza = product.ContarComoUnaPieza
            };
            ActualizarVisibilidadEmptyState();
            newItem.PropertyChanged += CartItem_PropertyChanged;
            Cart.Add(newItem);
            if (CartGrid != null)
            {
                CartGrid.SelectedItem = newItem;
                CartGrid.ScrollIntoView(newItem);
            }
            RefreshSummary();
        }



        internal void ActualizarStockEnCatalogo(string codigo, decimal nuevoStock)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return;
            string limpio = codigo.Trim();
            string sinCeros = limpio.TrimStart('0');

            var prod = _catalog.FirstOrDefault(p =>
                string.Equals(p.Codigo?.Trim(), limpio, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(sinCeros) && string.Equals(p.Codigo?.Trim().TrimStart('0'), sinCeros, StringComparison.OrdinalIgnoreCase)) ||
                p.Id.ToString() == limpio);

            if (prod != null)
            {
                prod.Stock = nuevoStock;
            }
        }

        private decimal ObtenerTotalNetoVenta()
        {
            if (Cart == null || Cart.Count == 0) return 0m;
            var subtotalBruto = Cart.Sum(item => item.Subtotal);
            var resultadoPromo = PromocionEngine.EvaluarIndexado(Cart, _promosPorProducto, _combosCargados);
            return CalcularTotalNeto(subtotalBruto, resultadoPromo);
        }

        private static decimal CalcularTotalNeto(decimal subtotalBruto, ResultadoPromocion resultadoPromo)
        {
            var totalConDescuento = Math.Max(0m, subtotalBruto - resultadoPromo.DescuentoTotal);
            return Math.Round(totalConDescuento, 0, MidpointRounding.AwayFromZero);
        }

        private void Cart_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSummary();
        private void CartItem_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();

        private void RefreshSummary()
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var itemCount = Cart.Count;
            var subtotalBruto = Cart.Sum(item => item.Subtotal);
            var resultadoPromo = PromocionEngine.EvaluarIndexado(Cart, _promosPorProducto, _combosCargados);
            var totalNetoRedondeado = CalcularTotalNeto(subtotalBruto, resultadoPromo);
            ((TextBlock)FindName("ProductCountText")).Text = itemCount.ToString(culture);
            ((TextBlock)FindName("TotalText")).Text = totalNetoRedondeado.ToString("C2", culture);
            ((TextBlock)FindName("SaleNumberText")).Text = $"#{_saleNumber}";
            if (FindName("PromotionsCountText") is TextBlock promoBlock)
            {
                promoBlock.Text = resultadoPromo.PromocionesAplicadasCount.ToString(culture);
            }

            string nombreNegocio = AppConfig.Load().NombreNegocio;
            PantallaClienteService.ActualizarCarrito(Cart, subtotalBruto, resultadoPromo.DescuentoTotal, totalNetoRedondeado, nombreNegocio);
        }
        #endregion

        #region Ventas en Espera (Carrito en Pausa)
        private void CargarVentasEnEsperaDesdeDb()
        {
            try
            {
                var lista = VentaEnEsperaDB.Load();
                _ventasEnEspera.Clear();
                foreach (var v in lista)
                {
                    _ventasEnEspera.Add(v);
                }
                ActualizarBadgeVentasEnEspera();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al cargar ventas en espera: {ex.Message}");
            }
        }

        private void ActualizarBadgeVentasEnEspera()
        {
            int conteo = _ventasEnEspera.Count;
            if (TxtVentasEsperaBadge != null)
            {
                TxtVentasEsperaBadge.Text = $"En espera ({conteo})";
            }
            if (TxtVentasEsperaVacio != null)
            {
                TxtVentasEsperaVacio.Visibility = conteo == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private string GenerarIdentificadorInteligente(IReadOnlyCollection<ItemCarrito> items)
        {
            var hora = DateTime.Now.ToString("HH:mm");
            if (items == null || items.Count == 0)
                return $"Venta ({hora})";

            var topItem = items.OrderByDescending(i => i.Subtotal).FirstOrDefault();
            string nombreBase = topItem != null ? topItem.Nombre : "Producto";
            if (nombreBase.Length > 24)
                nombreBase = nombreBase.Substring(0, 24).Trim() + "...";

            int otros = items.Count - 1;
            if (otros <= 0)
            {
                return $"{nombreBase} ({hora})";
            }
            else if (otros == 1)
            {
                return $"{nombreBase} y 1 articulo mas ({hora})";
            }
            else
            {
                return $"{nombreBase} y {otros} articulos mas ({hora})";
            }
        }

        private void PausarVentaActual(bool mostrarToast = true)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Ventas en Espera");
                return;
            }

            if (Cart == null || Cart.Count == 0)
            {
                if (mostrarToast)
                {
                    ToastNotification.Show("No hay productos en el carrito para pausar.", ToastType.Warning);
                }
                return;
            }

            try
            {
                var totalNeto = ObtenerTotalNetoVenta();
                var identificador = GenerarIdentificadorInteligente(Cart);

                var nueva = new VentaEnEspera
                {
                    FechaHora = DateTime.Now,
                    Identificador = identificador,
                    TotalNeto = totalNeto,
                    TotalItems = Cart.Count,
                    Items = Cart.Select(i => new ItemCarrito
                    {
                        ProductoId = i.ProductoId,
                        Codigo = i.Codigo,
                        Nombre = i.Nombre,
                        Costo = i.Costo,
                        Ganancia = i.Ganancia,
                        Precio = i.Precio,
                        Categoria = i.Categoria,
                        Cantidad = i.Cantidad,
                        SeVendePorPeso = i.SeVendePorPeso,
                        ContarComoUnaPieza = i.ContarComoUnaPieza
                    }).ToList()
                };

                VentaEnEsperaDB.Save(nueva);

                foreach (var item in Cart)
                {
                    item.PropertyChanged -= CartItem_PropertyChanged;
                }
                Cart.Clear();
                ActualizarVisibilidadEmptyState();
                RefreshSummary();

                CargarVentasEnEsperaDesdeDb();

                if (mostrarToast)
                {
                    ToastNotification.Show($"Venta en pausa: {identificador}", ToastType.Success);
                }

                FocusSearch();
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al pausar la venta: {ex.Message}", ToastType.Error);
            }
        }

        private void AbrirModalVentasEnEspera()
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Ventas en Espera");
                return;
            }

            CargarVentasEnEsperaDesdeDb();
            if (VentasEnEsperaOverlay != null)
            {
                VentasEnEsperaOverlay.Visibility = Visibility.Visible;
                if (VentasEnEsperaListBox != null && _ventasEnEspera.Count > 0)
                {
                    VentasEnEsperaListBox.SelectedIndex = 0;
                    VentasEnEsperaListBox.Focus();
                }
            }
        }

        private void CloseVentasEnEspera()
        {
            if (VentasEnEsperaOverlay != null)
            {
                VentasEnEsperaOverlay.Visibility = Visibility.Collapsed;
            }
            FocusSearch();
        }

        private void BtnVentasEnEspera_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Ventas en Espera");
                return;
            }

            if (sender != BtnVentasEnEsperaBadge && Cart.Count > 0)
            {
                PausarVentaActual();
                return;
            }

            AbrirModalVentasEnEspera();
        }

        private void CloseVentasEnEspera_Click(object sender, RoutedEventArgs e)
        {
            CloseVentasEnEspera();
        }

        private void VentasEnEsperaOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == VentasEnEsperaOverlay)
            {
                CloseVentasEnEspera();
            }
        }

        private void VentasEnEsperaListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                RecuperarVentaSeleccionada();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DescartarVentaSeleccionada();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseVentasEnEspera();
                e.Handled = true;
            }
        }

        private void VentasEnEsperaListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            RecuperarVentaSeleccionada();
        }

        private void RecuperarVentaEnEspera_Click(object sender, RoutedEventArgs e)
        {
            RecuperarVentaSeleccionada();
        }

        private void DescartarVentaEnEspera_Click(object sender, RoutedEventArgs e)
        {
            DescartarVentaSeleccionada();
        }

        private void RecuperarVentaSeleccionada()
        {
            if (VentasEnEsperaListBox?.SelectedItem is not VentaEnEspera venta)
            {
                ToastNotification.Show("Selecciona una venta de la lista para recuperarla.", ToastType.Warning);
                return;
            }

            if (Cart != null && Cart.Count > 0)
            {
                Window? parent = Window.GetWindow(this);
                var resp = parent != null
                    ? CustomMessageBox.Show(
                        parent,
                        "Hay articulos en la venta actual.\n\nDeseas pausar la venta actual y recuperar la venta seleccionada?",
                        "Venta en curso",
                        MessageBoxButton.YesNo)
                    : CustomMessageBox.Show(
                        "Hay articulos en la venta actual.\n\nDeseas pausar la venta actual y recuperar la venta seleccionada?",
                        "Venta en curso",
                        MessageBoxButton.YesNo);

                if (resp != MessageBoxResult.Yes)
                {
                    return;
                }

                PausarVentaActual(mostrarToast: false);
            }

            try
            {
                foreach (var it in Cart)
                {
                    it.PropertyChanged -= CartItem_PropertyChanged;
                }
                Cart.Clear();

                if (venta.Items != null)
                {
                    foreach (var item in venta.Items)
                    {
                        item.PropertyChanged += CartItem_PropertyChanged;
                        Cart.Add(item);
                    }
                }

                var ultimoItemRecuperado = Cart.LastOrDefault();
                if (CartGrid != null && ultimoItemRecuperado != null)
                {
                    CartGrid.SelectedItem = ultimoItemRecuperado;
                    CartGrid.ScrollIntoView(ultimoItemRecuperado);
                }

                VentaEnEsperaDB.Delete(venta.Id);
                _ventasEnEspera.Remove(venta);
                ActualizarBadgeVentasEnEspera();

                ActualizarVisibilidadEmptyState();
                RefreshSummary();
                CloseVentasEnEspera();

                ToastNotification.Show($"Venta recuperada: {venta.Identificador}", ToastType.Success);
                FocusSearch();
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al recuperar la venta: {ex.Message}", ToastType.Error);
            }
        }

        private void DescartarVentaSeleccionada()
        {
            if (VentasEnEsperaListBox?.SelectedItem is not VentaEnEspera venta)
            {
                ToastNotification.Show("Selecciona una venta de la lista para descartarla.", ToastType.Warning);
                return;
            }

            Window? parent = Window.GetWindow(this);
            var resp = parent != null
                ? CustomMessageBox.Show(
                    parent,
                    $"Deseas eliminar permanentemente esta venta en espera?\n\n{venta.Identificador} - {venta.TotalNetoFormateado}",
                    "Descartar venta en espera",
                    MessageBoxButton.YesNo)
                : CustomMessageBox.Show(
                    $"Deseas eliminar permanentemente esta venta en espera?\n\n{venta.Identificador} - {venta.TotalNetoFormateado}",
                    "Descartar venta en espera",
                    MessageBoxButton.YesNo);

            if (resp != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                VentaEnEsperaDB.Delete(venta.Id);
                _ventasEnEspera.Remove(venta);
                ActualizarBadgeVentasEnEspera();
                ToastNotification.Show("Venta en espera eliminada.", ToastType.Info);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al descartar la venta: {ex.Message}", ToastType.Error);
            }
        }
        #endregion

        #region Productos Rapidos
        private readonly ObservableCollection<ProductoRapidoConfig> _productosRapidos = [];
        private string _teclaCapturadaTemporal = string.Empty;

        private void CargarProductosRapidosDesdeConfig()
        {
            _productosRapidos.Clear();
            var cfg = AppConfig.Load();
            if (cfg.ProductosRapidos != null)
            {
                var porId = _catalog.ToDictionary(p => p.Id);
                foreach (var item in cfg.ProductosRapidos)
                {
                    if (porId.TryGetValue(item.ProductoId, out var prod))
                    {
                        item.NombreProducto = prod.Nombre;
                    }
                    _productosRapidos.Add(item);
                }
            }
            if (QuickProdsListBox != null)
            {
                QuickProdsListBox.ItemsSource = _productosRapidos;
            }
            ActualizarBadgeProductosRapidos();
        }

        private void ActualizarBadgeProductosRapidos()
        {
            if (TxtProductosRapidosBadge != null)
            {
                TxtProductosRapidosBadge.Text = $"⚡ Rapidos ({_productosRapidos.Count})";
            }
            if (TxtQuickProdsEmpty != null)
            {
                TxtQuickProdsEmpty.Visibility = _productosRapidos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void GuardarProductosRapidosEnConfig()
        {
            var cfg = AppConfig.Load();
            cfg.ProductosRapidos = _productosRapidos.ToList();
            cfg.Save();
            ActualizarBadgeProductosRapidos();
        }

        private void BtnProductosRapidos_Click(object sender, RoutedEventArgs e)
        {
            AbrirModalProductosRapidos();
        }

        private void AbrirModalProductosRapidos()
        {
            if (ProductosRapidosOverlay == null) return;
            CargarProductosRapidosDesdeConfig();
            if (QuickProdSearchBox != null) QuickProdSearchBox.Clear();
            RefrescarComboProductosRapidos(string.Empty);
            _teclaCapturadaTemporal = string.Empty;
            if (QuickKeyCaptureBox != null) QuickKeyCaptureBox.Text = "Presiona tecla...";
            if (QuickProdQtyBox != null) QuickProdQtyBox.Text = "1";
            if (QuickProdValidationText != null)
            {
                QuickProdValidationText.Text = string.Empty;
                QuickProdValidationText.Visibility = Visibility.Collapsed;
            }
            ProductosRapidosOverlay.Visibility = Visibility.Visible;
            QuickProdSearchBox?.Focus();
        }

        private void CloseProductosRapidos()
        {
            if (ProductosRapidosOverlay != null)
            {
                ProductosRapidosOverlay.Visibility = Visibility.Collapsed;
            }
            FocusSearch();
        }

        private void CloseProductosRapidos_Click(object sender, RoutedEventArgs e) => CloseProductosRapidos();

        private void ProductosRapidosOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, ProductosRapidosOverlay))
            {
                CloseProductosRapidos();
            }
        }

        private void QuickProdSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefrescarComboProductosRapidos(QuickProdSearchBox?.Text ?? string.Empty);
        }

        private void RefrescarComboProductosRapidos(string filtro)
        {
            if (QuickProdCombo == null) return;
            var q = filtro.Trim();
            var lista = string.IsNullOrWhiteSpace(q)
                ? _catalog.Take(60).ToList()
                : _catalog.Where(p =>
                    (!string.IsNullOrEmpty(p.Nombre) && p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(p.Codigo) && p.Codigo.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    .Take(60)
                    .ToList();
            QuickProdCombo.ItemsSource = lista;
            if (lista.Count > 0) QuickProdCombo.SelectedIndex = 0;
        }

        private void QuickKeyCaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Tab) return;
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var atajo = FormatearAtajoTecla(key, Keyboard.Modifiers);
            if (string.IsNullOrEmpty(atajo)) return;

            if (!EsAtajoValidoParaProductoRapido(atajo, out var error))
            {
                _teclaCapturadaTemporal = string.Empty;
                if (QuickKeyCaptureBox != null) QuickKeyCaptureBox.Text = "Presiona tecla...";
                MostrarValidacionProductoRapido(error);
                return;
            }

            _teclaCapturadaTemporal = atajo;
            if (QuickKeyCaptureBox != null) QuickKeyCaptureBox.Text = atajo;
            OcultarValidacionProductoRapido();
        }

        private void BtnAgregarProductoRapido_Click(object sender, RoutedEventArgs e)
        {
            if (QuickProdCombo?.SelectedItem is not Producto prod)
            {
                MostrarValidacionProductoRapido("Selecciona un producto del catalogo.");
                return;
            }

            if (!EsAtajoValidoParaProductoRapido(_teclaCapturadaTemporal, out var error))
            {
                MostrarValidacionProductoRapido(error);
                QuickKeyCaptureBox?.Focus();
                return;
            }

            if (!decimal.TryParse(QuickProdQtyBox?.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cant) &&
                !decimal.TryParse(QuickProdQtyBox?.Text.Trim(), NumberStyles.Any, CultureInfo.GetCultureInfo("es-MX"), out cant))
            {
                cant = 0m;
            }
            if (cant <= 0m)
            {
                MostrarValidacionProductoRapido("La cantidad debe ser mayor que cero.");
                QuickProdQtyBox?.Focus();
                return;
            }

            var existenteMismaTecla = _productosRapidos.FirstOrDefault(r => string.Equals(r.Tecla, _teclaCapturadaTemporal, StringComparison.OrdinalIgnoreCase));
            if (existenteMismaTecla != null)
            {
                _productosRapidos.Remove(existenteMismaTecla);
            }

            _productosRapidos.Add(new ProductoRapidoConfig
            {
                ProductoId = prod.Id,
                NombreProducto = prod.Nombre,
                Tecla = _teclaCapturadaTemporal,
                Cantidad = Math.Round(cant, 3)
            });

            GuardarProductosRapidosEnConfig();
            _teclaCapturadaTemporal = string.Empty;
            if (QuickKeyCaptureBox != null) QuickKeyCaptureBox.Text = "Presiona tecla...";
            if (QuickProdQtyBox != null) QuickProdQtyBox.Text = "1";
            OcultarValidacionProductoRapido();
            ToastNotification.Show($"Atajo '{_productosRapidos.Last().Tecla}' asignado a {prod.Nombre}.", ToastType.Success);
        }

        private void BtnUsarProductoRapido_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProductoRapidoConfig item)
            {
                CloseProductosRapidos();
                EjecutarProductoRapido(item);
            }
        }

        private void BtnEliminarProductoRapido_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProductoRapidoConfig item)
            {
                _productosRapidos.Remove(item);
                GuardarProductosRapidosEnConfig();
                ToastNotification.Show($"Atajo '{item.Tecla}' eliminado.", ToastType.Info);
            }
        }

        private void EjecutarProductoRapido(ProductoRapidoConfig item)
        {
            var prod = _catalog.FirstOrDefault(p => p.Id == item.ProductoId);
            if (prod == null)
            {
                ToastNotification.Show($"No se encontro el producto '{item.NombreProducto}' en el catalogo.", ToastType.Warning);
                return;
            }

            decimal cantidad = item.Cantidad > 0 ? item.Cantidad : 1m;
            if (prod.SeVendePorPeso || prod.ContarComoUnaPieza || cantidad == 1m)
            {
                AddSuggestion(prod);
            }
            else
            {
                AddProduct(prod, cantidad);
                ActualizarVisibilidadEmptyState();
                AnimarEntradaProducto();
                FocusSearch();
            }

            ToastNotification.Show($"+{cantidad:0.###} {prod.Nombre} ({item.Tecla})", ToastType.Success);
        }

        private void MostrarValidacionProductoRapido(string msg)
        {
            if (QuickProdValidationText != null)
            {
                QuickProdValidationText.Text = msg;
                QuickProdValidationText.Visibility = Visibility.Visible;
            }
        }

        private void OcultarValidacionProductoRapido()
        {
            if (QuickProdValidationText != null)
            {
                QuickProdValidationText.Text = string.Empty;
                QuickProdValidationText.Visibility = Visibility.Collapsed;
            }
        }

        public static string FormatearAtajoTecla(Key key, ModifierKeys modifiers)
        {
            if (key == Key.None || key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return string.Empty;
            }

            string keyName = key switch
            {
                >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
                >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
                _ => key.ToString()
            };

            var parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
            parts.Add(keyName);
            return string.Join("+", parts);
        }

        public static bool EsAtajoValidoParaProductoRapido(string atajo, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(atajo))
            {
                error = "Presiona una tecla o combinacion valida (ej. F3, F9, Ctrl+1, Alt+P).";
                return false;
            }

            var reservadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Return", "Enter", "Escape", "Tab", "Delete", "Back", "Space",
                "F2", "F4", "F5", "F8", "F10", "F12",
                "Shift+Return", "Shift+Enter", "Ctrl+Left", "Up", "Down", "Left", "Right"
            };

            if (reservadas.Contains(atajo))
            {
                error = $"La tecla '{atajo}' esta reservada por el sistema de cobro.";
                return false;
            }

            bool tieneModificador = atajo.Contains("Ctrl+", StringComparison.OrdinalIgnoreCase) ||
                                    atajo.Contains("Alt+", StringComparison.OrdinalIgnoreCase);
            bool esTeclaFuncion = System.Text.RegularExpressions.Regex.IsMatch(atajo, @"^(Shift\+)?F([1-9]|1[0-2])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!tieneModificador && !esTeclaFuncion)
            {
                error = "Usa una tecla de funcion (ej. F3, F9, F11) o una combinacion con Ctrl/Alt (ej. Ctrl+1, Alt+P) para no interferir al escribir o escanear.";
                return false;
            }

            return true;
        }
        #endregion
    }
}
