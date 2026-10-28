using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using Punto_de_Venta.Vistas;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public partial class Inventario : UserControl
    {
        #region Campos Privados y Colecciones
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _searchDebounceTimer;
        private Producto? _selectedProduct;
        private bool _loadFailed;
        private bool _isUpdatingForm;
        private string _currentStockFilter = "All";

        public ObservableCollection<LoteRestock> Lotes { get; } = new();
        public ObservableCollection<LoteItem> NuevoLoteItems { get; } = new();

        public ObservableCollection<Producto> Products { get; } = [];
        public ICollectionView ProductsView { get; }
        #endregion

        #region Constructor e Inicialización
        public Inventario()
        {
            InitializeComponent();
            Language = XmlLanguage.GetLanguage("es-MX");
            ProductsView = CollectionViewSource.GetDefaultView(Products);
            ProductsView.Filter = ProductMatchesSearch;
            DataContext = this;

            try
            {
                foreach (var product in ProductoDB.Load())
                {
                    Products.Add(product);
                }
            }
            catch (Exception exception)
            {
                _loadFailed = true;
                ToastNotification.Show($"Error al cargar inventario: {exception.Message}", ToastType.Error);
            }
            UpdateProductCount();
            SetNextId();
            UpdateSelectionActions();

            try
            {
                foreach (var lote in LoteDB.Load())
                {
                    Lotes.Add(lote);
                }
            }
            catch { }
            if (AskQuantityCheckBox != null && ScaleWeighCheckBox != null)
            {
                AskQuantityCheckBox.Checked += AskQuantityCheckBox_Checked;
                ScaleWeighCheckBox.Checked += ScaleWeighCheckBox_Checked;
            }

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
            _clockTimer.Start();

            Unloaded += (_, _) =>
            {
                _clockTimer.Stop();
                _searchDebounceTimer.Stop();
            };

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                ProductsView.Refresh();
            };
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility == Visibility.Visible)
            {
                try
                {
                    Products.Clear();
                    foreach (var product in ProductoDB.Load())
                    {
                        Products.Add(product);
                    }
                }
                catch { }

                this.Focus();
                Keyboard.Focus(this);
                SearchBox?.Focus();
                ProductsView.Refresh();
                UpdateProductCount();
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            FocusSearch();
        }
        #endregion

        #region Eventos Principales y Atajos de Teclado
        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (QuickRestockOverlay.Visibility == Visibility.Visible)
                {
                    CloseQuickRestock_Click(null, null);
                    e.Handled = true;
                    return;
                }

                if (GestorLotesOverlay.Visibility == Visibility.Visible)
                {
                    CloseGestorLotes_Click(null, null);
                    e.Handled = true;
                    return;
                }

                if (LotesOverlay.Visibility == Visibility.Visible)
                {
                    CloseLotesModal_Click(null, null);
                    e.Handled = true;
                    return;
                }

                this.Visibility = Visibility.Collapsed;
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RegresarAlMenu();
                }
                e.Handled = true;
            }

            if (e.Key == Key.F9)
            {
                ImprimirEtiquetaSeleccionada();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                ModifyButton_Click(null, null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.E && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnEnlaceRapido_Click(null, null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                FocusCodeBox();
                e.Handled = true;
                return;
            }
        }
        #endregion

        #region Módulos de Lotes (Uso y Creación)

        private void OpenLotesModal_Click(object sender, RoutedEventArgs e)
        {
            LotesListBox.SelectedItem = null;
            LoteItemsBox.ItemsSource = null;
            LotesOverlay.Visibility = Visibility.Visible;
        }

        private void CloseLotesModal_Click(object sender, RoutedEventArgs e)
        {
            LotesOverlay.Visibility = Visibility.Collapsed;
            FocusCodeBox();
        }

        private void LotesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LotesListBox.SelectedItem is LoteRestock lote)
            {
                LoteItemsBox.ItemsSource = lote.Articulos;
            }
        }

        private void ApplyLote_Click(object sender, RoutedEventArgs e)
        {
            if (LotesListBox.SelectedItem is not LoteRestock lote)
            {
                ToastNotification.Show("Selecciona un lote de la lista primero.", ToastType.Warning);
                return;
            }

            if (!CanWrite()) return;

            int productosActualizados = 0;

            foreach (var item in lote.Articulos)
            {
                var productoBD = Products.FirstOrDefault(p =>
                    string.Equals(p.Codigo, item.Codigo, StringComparison.OrdinalIgnoreCase) ||
                    p.Id.ToString(CultureInfo.InvariantCulture) == item.Codigo);

                if (productoBD != null)
                {
                    productoBD.Stock += item.Cantidad;
                    productosActualizados++;
                }
            }

            if (productosActualizados > 0)
            {
                if (TrySave(Products))
                {
                    ProductsView.Refresh();
                    ToastNotification.Show($"Lote ingresado. Se actualizó el stock de {productosActualizados} artículos.", ToastType.Success);
                    CloseLotesModal_Click(null, null);
                }
            }
            else
            {
                ToastNotification.Show("No se encontró ningún artículo de este lote en tu catálogo actual.", ToastType.Warning);
            }
        }
        private void OpenGestorLotes_Click(object sender, RoutedEventArgs e)
        {
            NuevoLoteNombreBox.Clear();
            NuevoLoteProveedorBox.Clear();
            GestorLoteCodigoBox.Clear();
            GestorLoteCantidadBox.Text = "1";
            GestorLoteProductoEncontrado.Text = "Esperando código...";
            NuevoLoteItems.Clear();

            GestorLotesOverlay.Visibility = Visibility.Visible;

            this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                NuevoLoteNombreBox.Focus();
            }));
        }

        private void CloseGestorLotes_Click(object sender, RoutedEventArgs e)
        {
            GestorLotesOverlay.Visibility = Visibility.Collapsed;
        }

        private void NuevoLoteNombreBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NuevoLoteProveedorBox.Focus();
                e.Handled = true;
            }
        }

        private void NuevoLoteProveedorBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                GestorLoteCodigoBox.Focus();
                e.Handled = true;
            }
        }

        private void GestorLoteCodigoBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var code = GestorLoteCodigoBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code)) return;

            var prod = Products.FirstOrDefault(p =>
                string.Equals(p.Codigo, code, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString(CultureInfo.InvariantCulture) == code);

            if (prod != null)
            {
                GestorLoteProductoEncontrado.Text = $"✅ {prod.Nombre}";
                GestorLoteCantidadBox.Focus();
                GestorLoteCantidadBox.SelectAll();
            }
            else
            {
                GestorLoteProductoEncontrado.Text = "⚠️ Producto no encontrado.";
                GestorLoteCodigoBox.SelectAll();
            }
            e.Handled = true;
        }

        private void GestorLoteCantidadBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ProcesarIngresoAlLote();
                e.Handled = true;
            }
        }

        private void AddProductToNewLote_Click(object sender, RoutedEventArgs e)
        {
            ProcesarIngresoAlLote();
        }

        private void ProcesarIngresoAlLote()
        {
            var code = GestorLoteCodigoBox.Text.Trim();
            var prod = Products.FirstOrDefault(p => string.Equals(p.Codigo, code, StringComparison.OrdinalIgnoreCase) || p.Id.ToString() == code);

            if (prod == null)
            {
                ToastNotification.Show("Busca un producto válido antes de añadirlo.", ToastType.Warning);
                return;
            }

            if (!decimal.TryParse(GestorLoteCantidadBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
            {
                ToastNotification.Show("Ingresa una cantidad válida mayor a 0.", ToastType.Warning);
                return;
            }

            NuevoLoteItems.Add(new LoteItem { Codigo = code, Nombre = prod.Nombre, Cantidad = qty });

            GestorLoteCodigoBox.Clear();
            GestorLoteCantidadBox.Text = "1";
            GestorLoteProductoEncontrado.Text = "Esperando código...";
            GestorLoteCodigoBox.Focus();
        }

        private void SaveNewLote_Click(object sender, RoutedEventArgs e)
        {
            var nombre = NuevoLoteNombreBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(nombre) || NuevoLoteItems.Count == 0)
            {
                ToastNotification.Show("El lote debe tener un nombre y al menos 1 producto.", ToastType.Warning);
                return;
            }

            var nuevoLote = new LoteRestock
            {
                Nombre = nombre,
                Proveedor = NuevoLoteProveedorBox.Text.Trim(),
                Articulos = NuevoLoteItems.ToList()
            };

            Lotes.Add(nuevoLote);

            try
            {
                LoteDB.Save(Lotes);
                ToastNotification.Show($"Lote '{nombre}' guardado con éxito.", ToastType.Success);
                CloseGestorLotes_Click(null, null);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al guardar: {ex.Message}", ToastType.Error);
            }
        }
        #endregion

        #region Módulo Enlace Rápido e Impresión de Etiquetas
        private void BtnEnlaceRapido_Click(object sender, RoutedEventArgs e)
        {
            var modal = new Vistas.EnlaceRapidoModal();
            modal.Owner = Window.GetWindow(this);
            modal.ShowDialog();

            try
            {
                Products.Clear();
                foreach (var product in ProductoDB.Load())
                {
                    Products.Add(product);
                }
                ProductsView.Refresh();
            }
            catch { }
        }

        private void ImprimirEtiquetaSeleccionada()
        {
            var producto = _selectedProduct ?? ProductGrid.SelectedItem as Producto;

            if (producto == null)
            {
                ToastNotification.Show("Selecciona primero un producto de la tabla para imprimir su etiqueta.", ToastType.Warning);
                return;
            }

            var config = AppConfig.Load();
            if (string.IsNullOrWhiteSpace(config.ImpresoraSeleccionada))
            {
                ToastNotification.Show("No hay una impresora configurada en Ajustes.", ToastType.Warning);
                return;
            }

            string codigoBarras = !string.IsNullOrWhiteSpace(producto.Codigo)
                ? producto.Codigo.Trim()
                : producto.Id.ToString(CultureInfo.InvariantCulture);

            try
            {
                byte[] buffer = EtiquetaPrinter.GenerarBytesEtiqueta(
                    config.NombreNegocio,
                    producto.Nombre,
                    codigoBarras,
                    producto.Precio,
                    copias: 1
                );

                bool impreso = RawPrinterHelper.SendBytesToPrinter(config.ImpresoraSeleccionada, buffer);

                if (impreso)
                {
                    ToastNotification.Show($"🏷️ Etiqueta de \"{producto.Nombre}\" enviada a imprimir.", ToastType.Success);
                }
                else
                {
                    ToastNotification.Show("No se pudo enviar la etiqueta a la impresora térmica.", ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al generar etiqueta: {ex.Message}", ToastType.Error);
            }
        }
        #endregion

        #region Búsqueda y Filtros
        private void FocusSearch()
        {
            this.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Input,
                new Action(() =>
                {
                    ProductCodeBox.Focus();
                    Keyboard.Focus(ProductCodeBox);
                }));
        }

        private void FilterStock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string filterTag })
            {
                if (_currentStockFilter == filterTag)
                    return;

                _currentStockFilter = filterTag;
                ProductsView.Refresh();
            }
        }

        private bool ProductMatchesSearch(object item)
        {
            if (item is not Producto product)
                return false;

            if (_currentStockFilter == "Low" && !product.EsStockBajo)
                return false;

            if (_currentStockFilter == "Out" && !product.EsStockAgotado)
                return false;

            var query = SearchBox.Text.Trim();

            if (query.Length == 0)
                return true;

            if (product.Id.ToString(CultureInfo.InvariantCulture)
                .Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var codigo = product.Codigo;
            if (!string.IsNullOrEmpty(codigo) &&
                codigo.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var nombre = product.Nombre;
            return !string.IsNullOrEmpty(nombre) &&
                   nombre.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility =
                string.IsNullOrEmpty(SearchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
        #endregion

        #region Interacción con Formulario y DataGrid
        private void AskQuantityCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingForm) return;
            if (ScaleWeighCheckBox != null && ScaleWeighCheckBox.IsChecked == true)
            {
                ScaleWeighCheckBox.IsChecked = false;
            }
        }

        private void ScaleWeighCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingForm) return;
            if (AskQuantityCheckBox != null && AskQuantityCheckBox.IsChecked == true)
            {
                AskQuantityCheckBox.IsChecked = false;
            }
        }

        private void FocusCodeBox()
        {
            ProductCodeBox.Focus();
            Keyboard.Focus(ProductCodeBox);
            ProductCodeBox.SelectAll();
        }

        private void ProductGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedProduct = ProductGrid.SelectedItem as Producto;
            UpdateSelectionActions();

            if (_selectedProduct is null)
            {
                ClearForm();
                return;
            }

            try
            {
                _isUpdatingForm = true;

                ProductIdBox.Text = _selectedProduct.Id.ToString(CultureInfo.InvariantCulture);
                ProductCodeBox.Text = _selectedProduct.Codigo ?? string.Empty;
                ProductNameBox.Text = _selectedProduct.Nombre ?? string.Empty;
                ProductCategoryBox.Text = _selectedProduct.Categoria ?? "N/A";
                StockBox.Text = _selectedProduct.Stock.ToString("0.###", CultureInfo.InvariantCulture);
                ProductCostBox.Text = _selectedProduct.Costo.ToString("0.00", CultureInfo.InvariantCulture);
                ProductMarginBox.Text = _selectedProduct.Ganancia.ToString("0.00", CultureInfo.InvariantCulture);

                if (AskQuantityCheckBox != null)
                {
                    AskQuantityCheckBox.IsChecked = _selectedProduct.PedirCantidadAlCobrar;
                }

                if (ScaleWeighCheckBox != null)
                {
                    ScaleWeighCheckBox.IsChecked = _selectedProduct.SeVendePorPeso;
                }
            }
            finally
            {
                _isUpdatingForm = false;
            }
        }

        private void ProductCodeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var code = ProductCodeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                ProductNameBox.Focus();
                e.Handled = true;
                return;
            }
            var existing = Products.FirstOrDefault(p =>
                string.Equals(p.Codigo, code, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                SelectProduct(existing);
                ProductCostBox.Focus();
                ProductCostBox.SelectAll();
            }
            else
            {
                ProductNameBox.Focus();
                ProductNameBox.SelectAll();
            }
            e.Handled = true;
        }

        private void ProductNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ProductCategoryBox.Focus();
                ProductCategoryBox.SelectAll();
                e.Handled = true;
            }
        }

        private void ProductCategoryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StockBox.Focus();
                StockBox.SelectAll();
                e.Handled = true;
            }
        }

        private void StockBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ProductCostBox.Focus();
                ProductCostBox.SelectAll();
                e.Handled = true;
            }
        }

        private void ProductCostBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ProductMarginBox.Focus();
                ProductMarginBox.SelectAll();
                e.Handled = true;
            }
        }

        private void ProductMarginBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (_selectedProduct is not null)
            {
                ModifyButton_Click(sender, e);
            }
            else
            {
                AddButton_Click(sender, e);
            }

            e.Handled = true;
        }

        private void CostOrMargin_TextChanged(object sender, TextChangedEventArgs e)
        {
        }
        #endregion

        #region Operaciones CRUD (Crear, Actualizar, Borrar)
        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || !TryReadForm(out var code, out var name, out var cost, out var margin, out var price, out var category, out var stock))
            {
                return;
            }

            if (HasDuplicateName(name))
            {
                ShowValidationError("Ya existe un producto con ese nombre.", "ProductNameBox");
                return;
            }

            int id;
            try
            {
                id = GetNextId();
            }
            catch (OverflowException)
            {
                ShowValidationError("Límite numérico de ID alcanzado.", "ProductNameBox");
                return;
            }

            var newProduct = new Producto
            {
                Id = id,
                Codigo = code,
                Nombre = name,
                Categoria = category,
                Costo = cost,
                Ganancia = margin,
                Precio = price,
                Stock = stock,
                PedirCantidadAlCobrar = AskQuantityCheckBox?.IsChecked == true,
                SeVendePorPeso = ScaleWeighCheckBox?.IsChecked == true
            };

            var updatedProducts = Products.Append(newProduct).ToList();
            if (!TrySave(updatedProducts))
            {
                return;
            }

            Products.Add(newProduct);
            SearchBox.Clear();
            ProductsView.Refresh();
            UpdateProductCount();
            ToastNotification.Show($"Producto '{newProduct.Nombre}' registrado", ToastType.Success);
            ClearSelectionAndForm();
            FocusCodeBox();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || _selectedProduct is null)
            {
                return;
            }

            var productToDelete = _selectedProduct;

            var window = Window.GetWindow(this) ?? Application.Current.MainWindow;
            var confirm = CustomMessageBox.Show(window,
                $"¿Seguro que deseas eliminar '{productToDelete.Nombre}' del inventario?",
                "Confirmar Eliminación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                Punto_de_Venta.Datos.ProductoDB.Delete(productToDelete.Id);

                Products.Remove(productToDelete);
                ClearSelectionAndForm();
                UpdateProductCount();
                FocusCodeBox();

                ToastNotification.Show($"Producto '{productToDelete.Nombre}' eliminado", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al eliminar de la base de datos: {ex.Message}", ToastType.Error);
            }
        }

        private void ModifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || _selectedProduct is null || !TryReadForm(out var code, out var name, out var cost, out var margin, out var price, out var category, out var stock))
            {
                return;
            }

            if (HasDuplicateName(name, _selectedProduct.Id))
            {
                ShowValidationError("Ya existe otro producto con ese nombre.", "ProductNameBox");
                return;
            }
            if (HasDuplicateCode(code, _selectedProduct.Id))
            {
                ShowValidationError("Ya existe otro producto con ese código de barras.", "ProductCodeBox");
                return;
            }

            var oldProduct = _selectedProduct;
            var updatedProduct = new Producto
            {
                Id = oldProduct.Id,
                Codigo = code,
                Nombre = name,
                Categoria = category,
                Costo = cost,
                Ganancia = margin,
                Precio = price,
                Stock = stock,
                PedirCantidadAlCobrar = AskQuantityCheckBox?.IsChecked == true,
                SeVendePorPeso = ScaleWeighCheckBox?.IsChecked == true
            };

            var updatedProducts = Products.Select(product => product.Id == oldProduct.Id ? updatedProduct : product).ToList();
            if (!TrySave(updatedProducts))
            {
                return;
            }

            var index = Products.IndexOf(oldProduct);
            if (index >= 0)
            {
                Products[index] = updatedProduct;
            }

            ProductsView.Refresh();
            UpdateProductCount();

            ToastNotification.Show($"Producto '{updatedProduct.Nombre}' actualizado", ToastType.Success);

            ClearSelectionAndForm();
            FocusCodeBox();
        }

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            ClearSelectionAndForm();
            FocusCodeBox();
        }
        #endregion

        #region Métodos Auxiliares
        private void ClearSelectionAndForm()
        {
            ProductGrid.SelectedItem = null;
            _selectedProduct = null;
            UpdateSelectionActions();
            ClearForm();
            SetNextId();
        }

        private bool TryReadForm(out string code, out string name, out decimal cost, out decimal margin, out decimal price, out string category, out decimal stock)
        {
            code = ProductCodeBox.Text.Trim();
            name = ProductNameBox.Text.Trim();
            category = ProductCategoryBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(category))
            {
                category = "N/A";
            }

            var costText = ProductCostBox.Text.Trim();
            var marginText = ProductMarginBox.Text.Trim();

            cost = 0;
            margin = 0;
            price = 0;
            stock = 0;

            if (name.Length == 0)
            {
                ShowValidationError("El nombre del producto es obligatorio.", "ProductNameBox");
                return false;
            }

            if (!decimal.TryParse(StockBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out stock) || stock < 0)
            {
                ShowValidationError("Introduce un stock válido igual o mayor que cero.", "StockBox");
                return false;
            }
            const NumberStyles styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint;
            var usCulture = CultureInfo.GetCultureInfo("en-US");
            var esCulture = CultureInfo.GetCultureInfo("es-ES");
            var parsedCost = decimal.TryParse(costText, styles, usCulture, out cost) ||
                             decimal.TryParse(costText, styles, esCulture, out cost);
            if (!parsedCost || cost < 0)
            {
                ShowValidationError("Introduce un costo decimal válido igual o mayor que cero.", "ProductCostBox");
                return false;
            }
            var parsedMargin = decimal.TryParse(marginText, styles, usCulture, out margin) ||
                               decimal.TryParse(marginText, styles, esCulture, out margin);
            if (!parsedMargin || margin < 0)
            {
                ShowValidationError("Introduce una ganancia decimal válida igual o mayor que cero.", "ProductMarginBox");
                return false;
            }
            price = cost + margin;
            return true;
        }

        private bool HasDuplicateName(string name, int? excludedId = null) =>
            Products.Any(product => product.Id != excludedId && string.Equals(product.Nombre, name, StringComparison.OrdinalIgnoreCase));

        private bool HasDuplicateCode(string code, int? excludedId = null)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            return Products.Any(product =>
                product.Id != excludedId &&
                string.Equals(product.Codigo, code, StringComparison.OrdinalIgnoreCase));
        }

        private bool CanWrite()
        {
            if (!_loadFailed)
            {
                return true;
            }

            ToastNotification.Show("El archivo de inventario no está disponible para escritura", ToastType.Error);
            return false;
        }

        private bool TrySave(IEnumerable<Producto> products)
        {
            try
            {
                ProductoDB.Save(products);
                return true;
            }
            catch (Exception exception)
            {
                ToastNotification.Show($"Error al guardar inventario: {exception.Message}", ToastType.Error);
                return false;
            }
        }

        private void ShowValidationError(string message, string controlName)
        {
            ToastNotification.Show(message, ToastType.Warning);

            if (FindName(controlName) is TextBox control)
            {
                control.Focus();
                control.SelectAll();
            }
        }

        private void UpdateSelectionActions()
        {
            var enabled = _selectedProduct is not null && !_loadFailed;
            DeleteButton.IsEnabled = enabled;
            ModifyButton.IsEnabled = enabled;
        }

        private void UpdateProductCount() =>
            CantProd.Text = $"Cantidad de prod: {Products.Count}";

        private int GetNextId() => checked(Products.Select(product => product.Id).DefaultIfEmpty(0).Max() + 1);

        private void SetNextId()
        {
            try
            {
                ProductIdBox.Text = GetNextId().ToString(CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                ProductIdBox.Text = string.Empty;
            }
        }

        private void ClearForm()
        {
            _isUpdatingForm = true;
            try
            {
                ProductCodeBox.Clear();
                ProductNameBox.Clear();
                ProductCategoryBox.Clear();
                StockBox.Text = "0";
                ProductCostBox.Clear();
                ProductMarginBox.Clear();

                if (AskQuantityCheckBox != null)
                {
                    AskQuantityCheckBox.IsChecked = false;
                }
                if (ScaleWeighCheckBox != null)
                {
                    ScaleWeighCheckBox.IsChecked = false;
                }
            }
            finally
            {
                _isUpdatingForm = false;
            }
        }

        private void SelectProduct(Producto product)
        {
            ProductGrid.SelectedItem = product;
            ProductGrid.ScrollIntoView(product);
        }
        #endregion

        #region Módulo Modo ReStock Rápido (Modal Instantáneo)
        private void OpenQuickRestockModal_Click(object sender, RoutedEventArgs e)
        {
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
            FocusCodeBox();
        }

        private void QuickRestock_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProduct is null || sender is not Button btn || btn.Tag is not string amountStr)
            {
                ToastNotification.Show("Selecciona un producto para reabastecer", ToastType.Warning);
                return;
            }

            if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var addAmount)) return;

            decimal.TryParse(StockBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var currentStock);
            var newStock = currentStock + addAmount;

            StockBox.Text = newStock.ToString("0.###", CultureInfo.InvariantCulture);
            _selectedProduct.Stock = newStock;

            try
            {
                ProductoDB.Save(Products);
                ProductsView.Refresh();
                ToastNotification.Show($"+{addAmount} pzas añadidas ({_selectedProduct.Nombre}). Stock: {newStock}", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al guardar reabastecimiento: {ex.Message}", ToastType.Error);
            }
        }

        private void QrScanCodeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var code = QrScanCodeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code)) return;

            var prod = Products.FirstOrDefault(p =>
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

            var prod = Products.FirstOrDefault(p =>
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
                QrStatusMessage.Text = "Ingresa una cantidad válida mayor a 0.";
                QrAddAmountBox.Focus();
                QrAddAmountBox.SelectAll();
                return;
            }

            prod.Stock += addAmount;

            if (TrySave(Products))
            {
                ProductsView.Refresh();
                ToastNotification.Show($"+{addAmount} pzas añadidas a '{prod.Nombre}'. Nuevo Stock: {prod.Stock}", ToastType.Success);

                QrScanCodeBox.Clear();
                QrAddAmountBox.Text = "1";
                QrFoundProductLabel.Text = $"Último: {prod.Nombre} (Stock: {prod.Stock})";
                QrStatusMessage.Text = string.Empty;
                QrScanCodeBox.Focus();
            }
            else
            {
                prod.Stock -= addAmount;
                QrStatusMessage.Text = "Error al guardar en disco.";
            }
        }
        #endregion
    }
}