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
    public partial class Cobro : UserControl
    {
        #region Campos Privados y Colecciones
        private DispatcherTimer _typingTimer;
        private List<Producto> _catalog = new();
        private List<Promocion> _promocionesCargadas = new();
        private readonly DispatcherTimer _clockTimer;
        private int _saleNumber;
        private bool _purchaseFeedbackVisible;
        private ItemCarrito? _itemEnPesaje;
        private bool _modalAbierto = false;

        public ObservableCollection<ItemCarrito> Cart { get; } = new();
        #endregion

        #region Inicialización y Constructor
        public Cobro()
        {
            InitializeComponent();
            DataContext = this;
            Language = XmlLanguage.GetLanguage("es-MX");

            Cart.CollectionChanged += Cart_CollectionChanged;
            ((TextBox)FindName("SearchBox")).TextChanged += SearchBox_TextChanged;
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
        }
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _catalog = ProductoDB.Load();
                CargarPromocionesDesdeDb();
                RefreshSuggestions();
                ActualizarEstadoHardware();
            }
            catch (Exception exception)
            {
                _catalog = new();
                if (FindName("SuggestionsPopup") is Popup popup) popup.IsOpen = false;
                ToastNotification.Show($"No se pudo cargar el catálogo: {exception.Message}", ToastType.Error);
            }
            if (Cart != null)
            {
                Cart.Clear();
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
        }
        #endregion

        #region Manejo de Interfaz
        private void ActualizarVisibilidadEmptyState()
        {
            bool tieneArticulos = CartGrid.Items.Count > 0;
            EmptyCartPanel.Visibility = tieneArticulos ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility == Visibility.Visible)
            {
                try { _catalog = ProductoDB.Load(); } catch { _catalog = new(); }

                this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    this.Focus();
                    Keyboard.Focus(SearchBox);
                    SearchBox?.Focus();
                    SearchBox?.SelectAll();
                }));
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

        private void ActualizarEstadoHardware()
        {
            try
            {
                var config = AppConfig.Load();

                if (config.BasculaHabilitada)
                {
                    var puertosActivos = BasculaService.ObtenerPuertosDisponibles();
                    bool puertoDetectado = Array.Exists(puertosActivos, p => string.Equals(p, config.BasculaPuerto, StringComparison.OrdinalIgnoreCase));

                    if (puertoDetectado)
                    {
                        DotScaleStatus.Fill = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                        StatusScaleBadge.ToolTip = $"Báscula conectada en {config.BasculaPuerto}";
                    }
                    else
                    {
                        DotScaleStatus.Fill = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                        StatusScaleBadge.ToolTip = $"Puerto {config.BasculaPuerto} no detectado en el equipo";
                    }
                }
                else
                {
                    DotScaleStatus.Fill = new SolidColorBrush(Color.FromRgb(142, 142, 147));
                    StatusScaleBadge.ToolTip = "Báscula deshabilitada en ajustes";
                }

                bool impresoraValida = false;
                string nombreImpresora = config.ImpresoraSeleccionada;

                if (!string.IsNullOrWhiteSpace(nombreImpresora))
                {
                    foreach (string printer in PrinterSettings.InstalledPrinters)
                    {
                        if (string.Equals(printer, nombreImpresora, StringComparison.OrdinalIgnoreCase))
                        {
                            impresoraValida = true;
                            break;
                        }
                    }
                }
                else
                {
                    var defaultSettings = new PrinterSettings();
                    impresoraValida = defaultSettings.IsValid;
                    nombreImpresora = defaultSettings.IsValid ? defaultSettings.PrinterName : "Ninguna";
                }

                if (impresoraValida)
                {
                    DotPrinterStatus.Fill = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                    StatusPrinterBadge.ToolTip = $"Impresora lista: {nombreImpresora}";
                }
                else
                {
                    DotPrinterStatus.Fill = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                    StatusPrinterBadge.ToolTip = string.IsNullOrWhiteSpace(nombreImpresora)
                        ? "No hay impresora térmica configurada ni predeterminada"
                        : $"Impresora no encontrada: {nombreImpresora}";
                }
            }
            catch
            {
                DotScaleStatus.Fill = new SolidColorBrush(Color.FromRgb(142, 142, 147));
                DotPrinterStatus.Fill = new SolidColorBrush(Color.FromRgb(142, 142, 147));
            }
        }
        #endregion

        #region Módulo de Pesaje con Báscula (F8)

        private async void AbrirModalPesaje()
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
                ScaleStatusText.Text = "Listo para leer báscula.";
            }

            ScaleValueInput.Focus();
            ScaleValueInput.SelectAll();
            ActualizarCalculoPesaje();
        }

        private async Task ConsultarBasculaAsync()
        {
            ScaleStatusText.Text = "⏳ Conectando con la báscula...";
            var resultado = await BasculaService.ObtenerPesoAsync();

            if (resultado.Exito && resultado.Peso > 0)
            {
                ScaleValueInput.Text = resultado.Peso.ToString("0.000", CultureInfo.InvariantCulture);
                ScaleStatusText.Text = $"✅ Lectura recibida: {resultado.Peso:0.000} kg";
                ActualizarCalculoPesaje();
            }
            else
            {
                ScaleStatusText.Text = string.IsNullOrEmpty(resultado.Error)
                    ? "⚠️ Ingresa el peso manual si la báscula está apagada."
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
            TabModoPeso.IsChecked = true;
            await ConsultarBasculaAsync();
        }

        private void CancelScale_Click(object sender, RoutedEventArgs e)
        {
            ScaleOverlay.Visibility = Visibility.Collapsed;

            if (_itemEnPesaje != null && _itemEnPesaje.Cantidad == 0)
            {
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
                ToastNotification.Show("Ingresa una cantidad válida mayor a 0", ToastType.Warning);
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

        #region Búsqueda y Sugerencias de Catálogo
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

            if (matchExacto != null)
            {
                if (FindName("SuggestionsPopup") is Popup p) p.IsOpen = false;
                AddSuggestion(matchExacto);
                return;
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

            if (cartGrid.SelectedItem is not ItemCarrito selectedItem)
                return;

            selectedItem.PropertyChanged -= CartItem_PropertyChanged;
            Cart.Remove(selectedItem);

            var ultimo = Cart.LastOrDefault();
            cartGrid.SelectedItem = ultimo;

            if (ultimo != null)
                cartGrid.ScrollIntoView(ultimo);

            ToastNotification.Show(
                $"Se quitó '{selectedItem.Nombre}' del carrito",
                ToastType.Info);

            ActualizarVisibilidadEmptyState();
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
        }
        #endregion

        #region Agregar Rápido de Producto Nuevo
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
                QuickValidationMessage.Text = "Costo no válido.";
                ToastNotification.Show("Introduce un costo válido", ToastType.Warning);
                QuickCostBox.Focus();
                return;
            }

            if (!decimal.TryParse(QuickMarginBox.Text.Trim(), styles, usCulture, out var margin) && !decimal.TryParse(QuickMarginBox.Text.Trim(), styles, esCulture, out margin) || margin < 0)
            {
                QuickValidationMessage.Text = "Ganancia no válida.";
                ToastNotification.Show("Introduce una ganancia válida", ToastType.Warning);
                QuickMarginBox.Focus();
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
                SeVendePorPeso = QuickChkBascula.IsChecked == true
            };

            try
            {
                var actualizado = _catalog.Append(nuevoProd).ToList();
                ProductoDB.Save(actualizado);
                _catalog = actualizado;
            }
            catch (Exception ex)
            {
                QuickValidationMessage.Text = $"Error al guardar: {ex.Message}";
                ToastNotification.Show($"Error al guardar producto: {ex.Message}", ToastType.Error);
                return;
            }

            AddProduct(nuevoProd);
            CloseQuickAdd();
            ToastNotification.Show($"Producto '{nuevoProd.Nombre}' registrado", ToastType.Success);
        }
        #endregion

        #region Atajos de Teclado Centralizados
        private bool CerrarModalActivo()
        {
            if (PaymentOverlay != null && PaymentOverlay.Visibility == Visibility.Visible)
            {
                PaymentOverlay.Visibility = Visibility.Collapsed;
                FocusSearch();
                return true;
            }
            if (ScaleOverlay != null && ScaleOverlay.Visibility == Visibility.Visible)
            {
                CancelScale_Click(this, new RoutedEventArgs());
                return true;
            }
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
            if (CashMovementOverlay != null && CashMovementOverlay.Visibility == Visibility.Visible)
            {
                CloseCashMovement();
                return true;
            }
            if (QuickAddOverlay != null && QuickAddOverlay.Visibility == Visibility.Visible)
            {
                CloseQuickAdd();
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
            if (ReferenceEquals(e.OriginalSource, PaymentOverlay))
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

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.IsRepeat) return;

            if (ScaleOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CancelScale_Click(this, new RoutedEventArgs()); e.Handled = true; Cart.Clear(); }
                return;
            }

            if (QuickRestockOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CloseQuickRestock(); e.Handled = true; }
                return;
            }

            if (CreditOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CloseCreditOverlay(); e.Handled = true; }
                else if (e.Key == Key.Enter) { ConfirmCreditProcess(); e.Handled = true; }
                return;
            }

            if (PaymentOverlay != null && PaymentOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.F1) { RbEfectivo.IsChecked = true; e.Handled = true; return; }
                if (e.Key == Key.F2) { RbTarjeta.IsChecked = true; e.Handled = true; return; }
                if (e.Key == Key.F3) { RbTransferencia.IsChecked = true; e.Handled = true; return; }
                if (e.Key == Key.F7) { RbMixto.IsChecked = true; e.Handled = true; return; }
                if (e.Key == Key.F6) { StartCreditProcess(); e.Handled = true; return; }

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

                    FinalizePayment();
                    e.Handled = true;
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

            if (e.Key == Key.F10 || (e.Key == Key.System && e.SystemKey == Key.F10)) { OpenQuickRestockModal(); e.Handled = true; return; }
            if (e.Key == Key.F8) { AbrirModalPesaje(); e.Handled = true; return; }
            if (e.Key == Key.F5) { CajonService.AbrirCajon(); e.Handled = true; return; }
            if (e.Key == Key.F4) { OpenCashMovement(); e.Handled = true; return; }

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
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (!CerrarModalActivo())
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.RegresarAlMenu();
                    }
                }
                e.Handled = true;
            }
        }
        #endregion

        #region Movimientos de Caja y Fiados
        private void CashMovementButton_Click(object sender, RoutedEventArgs e) => OpenCashMovement();
        private void CreditButton_Click(object sender, RoutedEventArgs e) => StartCreditProcess();

        private void StartCreditProcess()
        {
            CreditValidationMessage.Text = string.Empty;
            DebtorNameBox.Clear();
            CreditOverlay.Visibility = Visibility.Visible;
            DebtorNameBox.Focus();
        }

        private void CloseCreditOverlay()
        {
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
                var existingDebtor = debtors.FirstOrDefault(d => string.Equals(d.Nombre, debtorName, StringComparison.OrdinalIgnoreCase));

                if (existingDebtor is not null)
                {
                    existingDebtor.MontoOriginal += total;
                    existingDebtor.Concepto += $" | {DateTime.Now:dd/MM}: {conceptSummary}";
                }
                else
                {
                    var nextDebtorId = debtors.Select(d => d.Id).DefaultIfEmpty(0).Max() + 1;
                    debtors.Add(new Deudor
                    {
                        Id = nextDebtorId,
                        Nombre = debtorName,
                        Concepto = $"{DateTime.Now:dd/MM}: {conceptSummary}",
                        MontoOriginal = total
                    });
                }

                var sales = VentaDB.Load();
                var saleId = checked(sales.Select(s => s.Id).DefaultIfEmpty(0).Max() + 1);
                var sale = new Venta
                {
                    Id = saleId,
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
                            VentaId = saleId,
                            Nombre = item.Nombre,
                            Cantidad = item.Cantidad,
                            Costo = item.Costo,
                            Ganancia = precioDescuento - item.Costo,
                            Precio = precioDescuento,
                            Categoria = item.Categoria
                        };
                    }).ToList()
                };

                foreach (var cartItem in Cart)
                {
                    var prod = _catalog.FirstOrDefault(p => p.Id == cartItem.ProductoId);
                    if (prod != null) prod.Stock = (decimal)Math.Max(0m, (decimal)prod.Stock - cartItem.Cantidad);
                }

                PrestamoDB.Save(debtors);
                VentaDB.Save(sales.Append(sale));
                ProductoDB.Save(_catalog);

                var config = AppConfig.Load();
                TicketService.GuardarCopiaDigital(sale, sale.Productos, 0m, 0m, config);

                foreach (var item in Cart) item.PropertyChanged -= CartItem_PropertyChanged;
                Cart.Clear();
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
                ToastNotification.Show($"Error crítico al guardar préstamo: {ex.Message}", ToastType.Error);
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
            MovementAmountBox.Focus();
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

            if (!decimal.TryParse(MovementAmountBox.Text, out decimal monto) || monto <= 0)
            {
                MovementValidationMessage.Text = "Ingresa un monto válido mayor a 0.";
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

            string tipoMovimiento = "Salida";
            if (RbEntrada.IsChecked == true) tipoMovimiento = "Entrada";
            if (RbFondo.IsChecked == true) tipoMovimiento = "Fondo";

            try
            {
                if (tipoMovimiento == "Fondo")
                {
                    var turnoActual = TurnoCajaManager.ObtenerTurnoActivo();
                    if (turnoActual != null) { MovementValidationMessage.Text = "Ya existe un turno abierto. Cierra la caja primero."; return; }
                    TurnoCajaManager.AbrirTurno(monto);
                }
                else
                {
                    var turnoActual = TurnoCajaManager.ObtenerTurnoActivo();
                    if (turnoActual == null) { MovementValidationMessage.Text = "Debes abrir caja (ingresar un Fondo) primero."; return; }
                    TurnoCajaManager.RegistrarMovimientoEfectivo(tipoMovimiento, monto, concepto);
                }

                ToastNotification.Show($"Se registró {tipoMovimiento} por {monto:C2}", ToastType.Success);
                CashMovementOverlay.Visibility = Visibility.Collapsed;
                MovementAmountBox.Clear();
                MovementConceptBox.Clear();
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
                SinglePaymentContainer.Visibility = Visibility.Visible;
                MixedPaymentContainer.Visibility = Visibility.Collapsed;

                if (RbEfectivo.IsChecked == true)
                {
                    CashReceivedBox.IsReadOnly = false;
                    CashReceivedBox.Clear();
                    CashReceivedBox.Focus();
                }
                else
                {
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
                ToastNotification.Show("El carrito está vacío", ToastType.Warning);
                FocusSearch();
                return;
            }
            CargarPromocionesDesdeDb();
            RefreshSummary();
            ((Popup)FindName("SuggestionsPopup")).IsOpen = false;
            ((Grid)FindName("PaymentOverlay")).Visibility = Visibility.Visible;

            if (RbMixto.IsChecked == true)
            {
                MixedCashBox.Text = "0";
                MixedCardBox.Text = "0";
                MixedTransferBox.Text = "0";
                MixedCashBox.Focus();
                MixedCashBox.SelectAll();
            }
            else
            {
                CashReceivedBox.Clear();
                CashReceivedBox.Focus();
            }

            ((TextBlock)FindName("PaymentMessage")).Text = string.Empty;
            ((TextBlock)FindName("ChangeText")).Text = 0m.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
            ((Button)FindName("FinalizeButton")).IsEnabled = false;
        }

        private void FinalizeButton_Click(object sender, RoutedEventArgs e) => FinalizePayment();

        private void FinalizePayment()
        {
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
                decimal.TryParse(MixedCashBox.Text, NumberStyles.Number, culture, out pEfectivo);
                decimal.TryParse(MixedCardBox.Text, NumberStyles.Number, culture, out pTarjeta);
                decimal.TryParse(MixedTransferBox.Text, NumberStyles.Number, culture, out pTransf);
                amountReceived = pEfectivo + pTarjeta + pTransf;
            }
            else
            {
                paymentMethod = RbTarjeta.IsChecked == true ? "Tarjeta" : (RbTransferencia.IsChecked == true ? "Transferencia" : "Efectivo");
                if (!decimal.TryParse(CashReceivedBox.Text, NumberStyles.Number, culture, out amountReceived))
                {
                    decimal.TryParse(CashReceivedBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("en-US"), out amountReceived);
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
                var sales = VentaDB.Load();
                int saleId = checked(sales.Select(s => s.Id).DefaultIfEmpty(0).Max() + 1);

                var sale = new Venta
                {
                    Id = saleId,
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
                            VentaId = saleId,
                            Nombre = item.Nombre,
                            Cantidad = item.Cantidad,
                            Costo = item.Costo,
                            Ganancia = precioDescuento - item.Costo,
                            Precio = precioDescuento,
                            Categoria = item.Categoria
                        };
                    }).ToList()
                };

                foreach (var cartItem in Cart)
                {
                    var prod = _catalog.FirstOrDefault(p => p.Id == cartItem.ProductoId);
                    if (prod != null) prod.Stock = (decimal)Math.Max(0m, (decimal)prod.Stock - cartItem.Cantidad);
                }

                VentaDB.Save(sales.Append(sale));
                ProductoDB.Save(_catalog);

                if (paymentMethod == "Efectivo" || (paymentMethod == "Mixto" && pEfectivo > 0))
                {
                    CajonService.AbrirCajon();
                }

                var config = AppConfig.Load();
                TicketService.GuardarCopiaDigital(sale, sale.Productos, amountReceived, change, config);

                if (config.ImprimirTicketAutomatico)
                {
                    TicketService.ImprimirFisico(sale, sale.Productos, amountReceived, change, config);
                }
                else if (config.PreguntarAntesDeImprimir)
                {
                    Window window = Window.GetWindow(this) ?? Application.Current.MainWindow;
                    var resultado = CustomMessageBox.Show(window, "¿Deseas imprimir el ticket de esta venta?", "Ticket de Venta", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (resultado == MessageBoxResult.Yes)
                    {
                        TicketService.ImprimirFisico(sale, sale.Productos, amountReceived, change, config);
                    }
                }

                Cart.Clear();
                _saleNumber++;
                RefreshSummary();
                ((Grid)FindName("PaymentOverlay")).Visibility = Visibility.Collapsed;
                ((TextBox)FindName("SearchBox")).Clear();
                ShowPurchaseFeedback(true, $"Venta liquidada ({paymentMethod}).");
                ToastNotification.Show($"Venta #{saleId} finalizada exitosamente ({paymentMethod})", ToastType.Success);
                FocusSearch();
            }
            catch (OverflowException)
            {
                ((TextBlock)FindName("PaymentMessage")).Text = "No se puede asignar un folio nuevo a la venta.";
                ToastNotification.Show("No se puede asignar folio a la venta", ToastType.Error);
            }
            catch (Exception exception)
            {
                _catalog = ProductoDB.Load();
                ((TextBlock)FindName("PaymentMessage")).Text = $"Error crítico en BD: {exception.Message}";
                ToastNotification.Show($"Error crítico al guardar venta en base de datos: {exception.Message}", ToastType.Error);
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

        #region Utilidades, Cálculos y Retroalimentación
        private void FocusSearch()
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                var searchBox = (TextBox)FindName("SearchBox");
                if (searchBox != null)
                {
                    this.Focus(); 
                    searchBox.Focus(); 
                    Keyboard.Focus(searchBox);
                }
            }));
        }
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
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

            if (!product.PedirCantidadAlCobrar)
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
                    searchMessage.Text = $"⚠️ Última pieza.";
                    searchMessage.Foreground = Brushes.DarkGoldenrod;
                    _purchaseFeedbackVisible = true;
                    ToastNotification.Show($"Última pieza de '{product.Nombre}'", ToastType.Info);
                }
                else
                {
                    searchMessage.Text = string.Empty;
                    _purchaseFeedbackVisible = false;
                }
            }

            if (existingItem != null)
            {
                existingItem.Cantidad += cantidad;
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
                Cantidad = cantidad
            };
            ActualizarVisibilidadEmptyState();
            newItem.PropertyChanged += CartItem_PropertyChanged;
            Cart.Add(newItem);
            RefreshSummary();
        }

        private decimal ObtenerTotalNetoVenta()
        {
            if (Cart == null || Cart.Count == 0) return 0m;
            var subtotalBruto = Cart.Sum(item => item.Subtotal);
            var resultadoPromo = PromocionEngine.Evaluar(Cart, _promocionesCargadas ?? new());
            return Math.Max(0m, subtotalBruto - resultadoPromo.DescuentoTotal);
        }

        private void Cart_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSummary();
        private void CartItem_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshSummary();

        private void RefreshSummary()
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var itemCount = Cart.Count;
            var subtotalBruto = Cart.Sum(item => item.Subtotal);
            var resultadoPromo = PromocionEngine.Evaluar(Cart, _promocionesCargadas ?? new());
            var totalConDescuento = Math.Max(0m, subtotalBruto - resultadoPromo.DescuentoTotal);
            ((TextBlock)FindName("ProductCountText")).Text = itemCount.ToString(culture);
            ((TextBlock)FindName("TotalText")).Text = totalConDescuento.ToString("C2", culture);
            ((TextBlock)FindName("SaleNumberText")).Text = $"#{_saleNumber}";
            if (FindName("PromotionsCountText") is TextBlock promoBlock)
            {
                promoBlock.Text = resultadoPromo.PromocionesAplicadasCount.ToString(culture);
            }
        }
        #endregion

        #region Módulo Modo ReStock Rápido
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
                QrStatusMessage.Text = "⚠️ Producto no encontrado con ese código.";
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
                QrStatusMessage.Text = "Escanea o escribe un código.";
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

            if (!decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out var addAmount) || addAmount <= 0)
            {
                QrStatusMessage.Text = "Ingresa una cantidad entera mayor a 0.";
                QrAddAmountBox.Focus();
                QrAddAmountBox.SelectAll();
                return;
            }

            prod.Stock += addAmount;

            try
            {
                ProductoDB.Save(_catalog);

                ToastNotification.Show($"+{addAmount} pzas añadidas a '{prod.Nombre}'. Nuevo Stock: {prod.Stock}", ToastType.Success);

                QrScanCodeBox.Clear();
                QrAddAmountBox.Text = "1";
                QrFoundProductLabel.Text = $"Último: {prod.Nombre} (Stock: {prod.Stock})";
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
    }
}