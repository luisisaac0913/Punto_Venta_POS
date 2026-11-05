using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Vistas;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Punto_de_Venta
{
    public partial class Promociones : UserControl, ILimpiarSeleccion
    {
        public ObservableCollection<Promocion> ListaPromociones { get; } = new ObservableCollection<Promocion>();
        private Promocion? _promocionEnEdicion = null;
        private List<Producto> _catalogoProductos = new();

        public Promociones()
        {
            InitializeComponent();
            FechaInicioPicker.SelectedDate = DateTime.Today;
            FechaFinPicker.SelectedDate = DateTime.Today.AddDays(7);
            CargarCatalogoProductos();
            CargarPromociones();
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            FocusSearch();
        }

        private void CargarCatalogoProductos()
        {
            try
            {
                _catalogoProductos = ProductoDB.Load();
            }
            catch
            {
                _catalogoProductos = new List<Producto>();
            }
        }

        private void CargarPromociones()
        {
            ListaPromociones.Clear();
            foreach (var p in PromocionDbStore.LoadAll())
            {
                ListaPromociones.Add(p);
            }
            PromocionesGrid.ItemsSource = ListaPromociones;
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                CargarCatalogoProductos();
                CargarPromociones();
                this.Focus();
                FocusSearch();
                Keyboard.Focus(this);
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => FocusSearch();

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Visibility = Visibility.Collapsed;
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RegresarAlMenu();
                }
                e.Handled = true;
            }
        }

        #region Comportamiento del Formulario
        private void ChkTiempoLimite_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelFechas == null) return;
            bool usarFechas = ChkTiempoLimite.IsChecked == true;
            PanelFechas.Visibility = usarFechas ? Visibility.Visible : Visibility.Collapsed;
            if (usarFechas && !FechaInicioPicker.SelectedDate.HasValue && !FechaFinPicker.SelectedDate.HasValue)
            {
                FechaInicioPicker.SelectedDate = DateTime.Today;
                FechaFinPicker.SelectedDate = DateTime.Today.AddDays(7);
            }
        }

        private void TipoPromoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TipoPromoCombo.SelectedItem is not ComboBoxItem item) return;
            string tipo = item.Tag?.ToString() ?? "Volumen";

            if (PanelProducto2 == null || PanelVolumen == null || PanelPrecioCombo == null || PanelPorcentaje == null) return;

            switch (tipo)
            {
                case "Volumen":
                    LabelProd1.Text = "ID o Codigo de Barras del Producto";
                    PanelProducto2.Visibility = Visibility.Collapsed;
                    PanelVolumen.Visibility = Visibility.Visible;
                    PanelPrecioCombo.Visibility = Visibility.Collapsed;
                    PanelPorcentaje.Visibility = Visibility.Collapsed;
                    break;

                case "Combo":
                    LabelProd1.Text = "ID o Codigo Primer Producto (A)";
                    PanelProducto2.Visibility = Visibility.Visible;
                    PanelVolumen.Visibility = Visibility.Collapsed;
                    PanelPrecioCombo.Visibility = Visibility.Visible;
                    PanelPorcentaje.Visibility = Visibility.Collapsed;
                    break;

                case "PorcentajeSegundo":
                    LabelProd1.Text = "ID o Codigo de Barras del Producto";
                    PanelProducto2.Visibility = Visibility.Collapsed;
                    PanelVolumen.Visibility = Visibility.Collapsed;
                    PanelPrecioCombo.Visibility = Visibility.Collapsed;
                    PanelPorcentaje.Visibility = Visibility.Visible;
                    break;
            }
        }

        private Producto? BuscarProducto(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string limpia = input.Trim();

            // 1. Busqueda exacta por Codigo de barras
            var porCodigo = _catalogoProductos.FirstOrDefault(p => string.Equals(p.Codigo, limpia, StringComparison.OrdinalIgnoreCase));
            if (porCodigo != null) return porCodigo;

            // 2. Busqueda exacta por ID numerico
            if (int.TryParse(limpia, out var id))
            {
                var porId = _catalogoProductos.FirstOrDefault(p => p.Id == id);
                if (porId != null) return porId;
            }

            // 3. Busqueda exacta por Nombre
            var porNombreExacto = _catalogoProductos.FirstOrDefault(p => string.Equals(p.Nombre, limpia, StringComparison.OrdinalIgnoreCase));
            if (porNombreExacto != null) return porNombreExacto;

            // 4. Busqueda parcial por Nombre o Codigo
            return _catalogoProductos.FirstOrDefault(p =>
                p.Nombre.IndexOf(limpia, StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.Codigo.IndexOf(limpia, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ActualizarInfoProducto(TextBox? box, TextBlock? infoBlock)
        {
            if (box == null || infoBlock == null) return;

            string texto = box.Text.Trim();
            if (string.IsNullOrEmpty(texto))
            {
                infoBlock.Text = string.Empty;
                infoBlock.Visibility = Visibility.Collapsed;
                return;
            }

            var prod = BuscarProducto(texto);
            if (prod != null)
            {
                infoBlock.Text = $"✔ {prod.Nombre} - {prod.Precio:C2} (ID: {prod.Id})";
                infoBlock.Foreground = (Brush?)FindResource("SystemGreen") ?? Brushes.Green;
                infoBlock.Visibility = Visibility.Visible;
            }
            else
            {
                infoBlock.Text = "⚠ No se encontro producto con este ID o codigo";
                infoBlock.Foreground = (Brush?)FindResource("SystemRed") ?? Brushes.IndianRed;
                infoBlock.Visibility = Visibility.Visible;
            }
        }

        private void ProdId1Box_TextChanged(object sender, TextChangedEventArgs e)
        {
            ActualizarInfoProducto(ProdId1Box, TxtInfoProd1);
        }

        private void ProdId2Box_TextChanged(object sender, TextChangedEventArgs e)
        {
            ActualizarInfoProducto(ProdId2Box, TxtInfoProd2);
        }

        private static decimal ParseDecimalSeguro(string text)
        {
            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
            if (decimal.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out var val) ||
                decimal.TryParse(text.Trim(), styles, CultureInfo.GetCultureInfo("es-MX"), out val))
            {
                return val;
            }
            return 0m;
        }

        private void FocusSearch()
        {
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (NombrePromoBox != null)
                {
                    this.Focus();
                    NombrePromoBox.Focus();
                    Keyboard.Focus(NombrePromoBox);
                }
            }));
        }
        #endregion

        #region Acciones del DataGrid (Activar, Editar, Eliminar)
        private void ToggleActiva_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is Promocion promo)
            {
                bool nuevoEstado = cb.IsChecked == true;

                if (nuevoEstado && promo.EstaVencida)
                {
                    promo.Activa = false;
                    cb.IsChecked = false;
                    ToastNotification.Show("Esta promocion ya vencio. Edita su fecha limite para reactivarla.", ToastType.Warning);
                    return;
                }

                promo.Activa = nuevoEstado;
                if (PromocionDbStore.Update(promo, out string error))
                {
                    ToastNotification.Show(promo.Activa ? "Promocion Activada" : "Promocion Desactivada", ToastType.Info);
                }
                else
                {
                    promo.Activa = !nuevoEstado;
                    cb.IsChecked = !nuevoEstado;
                    ToastNotification.Show($"Error al cambiar estado: {error}", ToastType.Error);
                }
            }
        }

        private void EliminarPromo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Promocion promo)
            {
                Window window = Window.GetWindow(this) ?? Application.Current.MainWindow;
                if (CustomMessageBox.Show(window, $"¿Deseas eliminar la promocion '{promo.Nombre}'?", "Eliminar Promocion", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    if (PromocionDbStore.Delete(promo.Id))
                    {
                        ToastNotification.Show("Promocion eliminada", ToastType.Success);
                        CargarPromociones();
                    }
                    else
                    {
                        ToastNotification.Show("Error al eliminar la promocion de la base de datos.", ToastType.Error);
                    }
                }
            }
        }

        private void EditarPromo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Promocion promo)
            {
                _promocionEnEdicion = promo;

                TituloFormularioText.Text = "Editar Promocion";
                BtnGuardarPromo.Content = "Actualizar Cambios";
                BtnCancelarEdicion.Visibility = Visibility.Visible;
                NombrePromoBox.Text = promo.Nombre;

                var prod1 = _catalogoProductos.FirstOrDefault(p => p.Id == promo.ProductoIdPrincipal);
                ProdId1Box.Text = prod1 != null && !string.IsNullOrEmpty(prod1.Codigo)
                    ? prod1.Codigo
                    : promo.ProductoIdPrincipal.ToString();

                foreach (ComboBoxItem item in TipoPromoCombo.Items)
                {
                    if (item.Tag?.ToString() == promo.Tipo.ToString())
                    {
                        TipoPromoCombo.SelectedItem = item;
                        break;
                    }
                }

                if (promo.Tipo == TipoPromocion.Volumen)
                {
                    CantidadMinBox.Text = promo.CantidadMinima.ToString();
                    PrecioEspecialBox.Text = promo.PrecioEspecial.ToString("F2");
                }
                else if (promo.Tipo == TipoPromocion.Combo)
                {
                    var prod2 = _catalogoProductos.FirstOrDefault(p => p.Id == promo.ProductoIdSecundario);
                    ProdId2Box.Text = prod2 != null && !string.IsNullOrEmpty(prod2.Codigo)
                        ? prod2.Codigo
                        : promo.ProductoIdSecundario.ToString();
                    PrecioComboTotalBox.Text = promo.PrecioComboTotal.ToString("F2");
                }
                else if (promo.Tipo == TipoPromocion.PorcentajeSegundo)
                {
                    PorcentajeDescuentoBox.Text = (promo.PorcentajeDescuento * 100).ToString("F0");
                }

                bool tieneFechas = promo.FechaInicio.HasValue || promo.FechaFin.HasValue;
                ChkTiempoLimite.IsChecked = tieneFechas;
                PanelFechas.Visibility = tieneFechas ? Visibility.Visible : Visibility.Collapsed;
                FechaInicioPicker.SelectedDate = promo.FechaInicio;
                FechaFinPicker.SelectedDate = promo.FechaFin;
            }
        }

        private void CancelarEdicion_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormulario();
        }

        public void LimpiarSeleccion()
        {
            PromocionesGrid.SelectedItem = null;
            LimpiarFormulario();
        }

        private void LimpiarFormulario()
        {
            _promocionEnEdicion = null;
            TituloFormularioText.Text = "Nueva Promocion";
            BtnGuardarPromo.Content = "Guardar Promocion";
            BtnCancelarEdicion.Visibility = Visibility.Collapsed;

            NombrePromoBox.Clear();
            ProdId1Box.Clear();
            if (ProdId2Box != null) ProdId2Box.Clear();
            if (TxtInfoProd1 != null) { TxtInfoProd1.Text = string.Empty; TxtInfoProd1.Visibility = Visibility.Collapsed; }
            if (TxtInfoProd2 != null) { TxtInfoProd2.Text = string.Empty; TxtInfoProd2.Visibility = Visibility.Collapsed; }
            PrecioEspecialBox.Text = "0.00";
            CantidadMinBox.Text = "3";
            if (PrecioComboTotalBox != null) PrecioComboTotalBox.Text = "0.00";
            if (PorcentajeDescuentoBox != null) PorcentajeDescuentoBox.Text = "50";
            if (ChkTiempoLimite != null) ChkTiempoLimite.IsChecked = false;
            if (PanelFechas != null) PanelFechas.Visibility = Visibility.Collapsed;
            if (FechaInicioPicker != null) FechaInicioPicker.SelectedDate = DateTime.Today;
            if (FechaFinPicker != null) FechaFinPicker.SelectedDate = DateTime.Today.AddDays(7);
        }
        #endregion

        #region Guardar / Actualizar
        private void GuardarPromo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NombrePromoBox.Text))
            {
                ToastNotification.Show("Ingresa un nombre para la promocion", ToastType.Warning);
                NombrePromoBox.Focus();
                return;
            }

            var prod1 = BuscarProducto(ProdId1Box.Text.Trim());
            if (prod1 == null)
            {
                ToastNotification.Show("No se encontro un producto valido para el primer campo (usa ID o codigo)", ToastType.Warning);
                ProdId1Box.Focus();
                return;
            }

            var tipoStr = (TipoPromoCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Volumen";
            var tipo = Enum.Parse<TipoPromocion>(tipoStr);
            var promocion = _promocionEnEdicion ?? new Promocion { Activa = true };
            promocion.Nombre = NombrePromoBox.Text.Trim();
            promocion.Tipo = tipo;
            promocion.ProductoIdPrincipal = prod1.Id;

            if (tipo == TipoPromocion.Volumen)
            {
                int.TryParse(CantidadMinBox.Text.Trim(), out var cant);
                var precioEsp = ParseDecimalSeguro(PrecioEspecialBox.Text);

                if (cant < 2)
                {
                    ToastNotification.Show("La cantidad minima para volumen debe ser al menos 2", ToastType.Warning);
                    CantidadMinBox.Focus();
                    return;
                }
                if (precioEsp <= 0)
                {
                    ToastNotification.Show("El precio especial del paquete debe ser mayor a $0.00", ToastType.Warning);
                    PrecioEspecialBox.Focus();
                    return;
                }
                promocion.CantidadMinima = cant;
                promocion.PrecioEspecial = precioEsp;

                promocion.ProductoIdSecundario = 0;
                promocion.PrecioComboTotal = 0;
                promocion.PorcentajeDescuento = 0;
            }
            else if (tipo == TipoPromocion.Combo)
            {
                var prod2 = BuscarProducto(ProdId2Box.Text.Trim());
                if (prod2 == null)
                {
                    ToastNotification.Show("No se encontro un producto valido para el segundo producto del combo", ToastType.Warning);
                    ProdId2Box.Focus();
                    return;
                }
                var precioCombo = ParseDecimalSeguro(PrecioComboTotalBox.Text);
                if (precioCombo <= 0)
                {
                    ToastNotification.Show("El precio del combo debe ser mayor a $0.00", ToastType.Warning);
                    PrecioComboTotalBox.Focus();
                    return;
                }
                promocion.ProductoIdSecundario = prod2.Id;
                promocion.PrecioComboTotal = precioCombo;
                promocion.CantidadMinima = 0;
                promocion.PrecioEspecial = 0;
                promocion.PorcentajeDescuento = 0;
            }
            else if (tipo == TipoPromocion.PorcentajeSegundo)
            {
                var pct = ParseDecimalSeguro(PorcentajeDescuentoBox.Text);
                if (pct <= 0 || pct > 100)
                {
                    ToastNotification.Show("El porcentaje debe ser entre 1 y 100", ToastType.Warning);
                    PorcentajeDescuentoBox.Focus();
                    return;
                }
                decimal rawPct = pct > 1 ? pct / 100m : pct;
                promocion.PorcentajeDescuento = Math.Clamp(rawPct, 0.01m, 1.0m);
                promocion.CantidadMinima = 0;
                promocion.PrecioEspecial = 0;
                promocion.ProductoIdSecundario = 0;
                promocion.PrecioComboTotal = 0;
            }

            if (ChkTiempoLimite.IsChecked == true)
            {
                var fInicio = FechaInicioPicker.SelectedDate?.Date;
                var fFin = FechaFinPicker.SelectedDate?.Date;

                if (!fInicio.HasValue && !fFin.HasValue)
                {
                    ToastNotification.Show("Selecciona al menos una fecha de inicio o de fin", ToastType.Warning);
                    return;
                }
                if (fInicio.HasValue && fFin.HasValue && fFin.Value < fInicio.Value)
                {
                    ToastNotification.Show("La fecha de fin no puede ser menor a la fecha de inicio", ToastType.Warning);
                    return;
                }
                if (fFin.HasValue && fFin.Value < DateTime.Today)
                {
                    ToastNotification.Show("La fecha limite no puede ser una fecha pasada", ToastType.Warning);
                    return;
                }

                promocion.FechaInicio = fInicio;
                promocion.FechaFin = fFin;

                if (_promocionEnEdicion == null)
                {
                    promocion.Activa = true;
                }
            }
            else
            {
                bool estabaVencida = promocion.EstaVencida;
                promocion.FechaInicio = null;
                promocion.FechaFin = null;
                if (estabaVencida) promocion.Activa = true;
            }

            bool guardadoConExito;
            string error = string.Empty;
            if (_promocionEnEdicion != null)
            {
                guardadoConExito = PromocionDbStore.Update(promocion, out error);
            }
            else
            {
                guardadoConExito = PromocionDbStore.Save(promocion, out error);
            }

            if (guardadoConExito)
            {
                ToastNotification.Show(_promocionEnEdicion != null ? "Promocion actualizada con exito" : "Promocion guardada con exito", ToastType.Success);
                LimpiarFormulario();
                CargarPromociones();
                FocusSearch();
            }
            else
            {
                ToastNotification.Show($"Error al guardar en BD: {error}", ToastType.Error);
            }
        }
        #endregion
    }
}