using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Vistas;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Punto_de_Venta
{
    public partial class Promociones : UserControl
    {
        public ObservableCollection<Promocion> ListaPromociones { get; } = new ObservableCollection<Promocion>();
        private Promocion _promocionEnEdicion = null;
        public Promociones()
        {
            InitializeComponent();
            CargarPromociones();
        }
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            FocusSearch();
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
            if (this.Visibility == Visibility.Visible)
            {
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
        private void TipoPromoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TipoPromoCombo.SelectedItem is not ComboBoxItem item) return;
            string tipo = item.Tag?.ToString() ?? "Volumen";

            if (PanelProducto2 == null || PanelVolumen == null || PanelPrecioCombo == null || PanelPorcentaje == null) return;

            switch (tipo)
            {
                case "Volumen":
                    LabelProd1.Text = "ID Producto Principal";
                    PanelProducto2.Visibility = Visibility.Collapsed;
                    PanelVolumen.Visibility = Visibility.Visible;
                    PanelPrecioCombo.Visibility = Visibility.Collapsed;
                    PanelPorcentaje.Visibility = Visibility.Collapsed;
                    break;

                case "Combo":
                    LabelProd1.Text = "ID Primer Producto (A)";
                    PanelProducto2.Visibility = Visibility.Visible;
                    PanelVolumen.Visibility = Visibility.Collapsed;
                    PanelPrecioCombo.Visibility = Visibility.Visible;
                    PanelPorcentaje.Visibility = Visibility.Collapsed;
                    break;

                case "PorcentajeSegundo":
                    LabelProd1.Text = "ID Producto";
                    PanelProducto2.Visibility = Visibility.Collapsed;
                    PanelVolumen.Visibility = Visibility.Collapsed;
                    PanelPrecioCombo.Visibility = Visibility.Collapsed;
                    PanelPorcentaje.Visibility = Visibility.Visible;
                    break;
            }
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
                if (PromocionDbStore.Update(promo, out string error))
                {
                    ToastNotification.Show(promo.Activa ? "Promoción Activada" : "Promoción Desactivada", ToastType.Info);
                }
                else
                {
                    ToastNotification.Show($"Error al cambiar estado: {error}", ToastType.Error);
                }
            }
        }
        private void EliminarPromo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Promocion promo)
            {
                Window window = Window.GetWindow(this) ?? Application.Current.MainWindow;
                if (CustomMessageBox.Show(window, $"¿Estás seguro de eliminar la promoción '{promo.Nombre}'?", "Eliminar Promoción", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    if (PromocionDbStore.Delete(promo.Id))
                    {
                        ToastNotification.Show("Promoción eliminada", ToastType.Success);
                        CargarPromociones();
                    }
                    else
                    {
                        ToastNotification.Show("Error al eliminar la promoción de la base de datos.", ToastType.Error);
                    }
                }
            }
        }

        private void EditarPromo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Promocion promo)
            {
                _promocionEnEdicion = promo;

                TituloFormularioText.Text = "Editar Promoción";
                BtnGuardarPromo.Content = "Actualizar Cambios";
                BtnCancelarEdicion.Visibility = Visibility.Visible;
                NombrePromoBox.Text = promo.Nombre;
                ProdId1Box.Text = promo.ProductoIdPrincipal.ToString();
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
                    ProdId2Box.Text = promo.ProductoIdSecundario.ToString();
                    PrecioComboTotalBox.Text = promo.PrecioComboTotal.ToString("F2");
                }
                else if (promo.Tipo == TipoPromocion.PorcentajeSegundo)
                {
                    PorcentajeDescuentoBox.Text = (promo.PorcentajeDescuento * 100).ToString("F0");
                }
            }
        }
        private void CancelarEdicion_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormulario();
        }
        private void LimpiarFormulario()
        {
            _promocionEnEdicion = null;
            TituloFormularioText.Text = "Nueva Promoción";
            BtnGuardarPromo.Content = "Guardar Promoción";
            BtnCancelarEdicion.Visibility = Visibility.Collapsed;

            NombrePromoBox.Clear();
            ProdId1Box.Clear();
            if (ProdId2Box != null) ProdId2Box.Clear();
            PrecioEspecialBox.Text = "0.00";
            CantidadMinBox.Text = "3";
            if (PrecioComboTotalBox != null) PrecioComboTotalBox.Text = "0.00";
            if (PorcentajeDescuentoBox != null) PorcentajeDescuentoBox.Text = "50";
        }
        #endregion

        #region Guardar / Actualizar
        private void GuardarPromo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NombrePromoBox.Text))
            {
                ToastNotification.Show("Ingresa un nombre para la promoción", ToastType.Warning);
                NombrePromoBox.Focus();
                return;
            }
            if (!int.TryParse(ProdId1Box.Text.Trim(), out var pId1) || pId1 <= 0)
            {
                ToastNotification.Show("Ingresa un ID válido para el primer producto", ToastType.Warning);
                ProdId1Box.Focus();
                return;
            }
            var tipoStr = (TipoPromoCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Volumen";
            var tipo = Enum.Parse<TipoPromocion>(tipoStr);
            var promocion = _promocionEnEdicion ?? new Promocion { Activa = true };
            promocion.Nombre = NombrePromoBox.Text.Trim();
            promocion.Tipo = tipo;
            promocion.ProductoIdPrincipal = pId1;

            if (tipo == TipoPromocion.Volumen)
            {
                int.TryParse(CantidadMinBox.Text.Trim(), out var cant);
                var precioEsp = ParseDecimalSeguro(PrecioEspecialBox.Text);

                if (cant < 2)
                {
                    ToastNotification.Show("La cantidad mínima para volumen debe ser al menos 2", ToastType.Warning);
                    CantidadMinBox.Focus();
                    return;
                }
                if (precioEsp <= 0)
                {
                    ToastNotification.Show("El precio especial debe ser mayor a $0.00", ToastType.Warning);
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
                if (!int.TryParse(ProdId2Box.Text.Trim(), out var pId2) || pId2 <= 0)
                {
                    ToastNotification.Show("Ingresa el ID válido para el segundo producto del combo", ToastType.Warning);
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
                promocion.ProductoIdSecundario = pId2;
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
                promocion.PorcentajeDescuento = pct > 1 ? pct / 100m : pct;
                promocion.CantidadMinima = 0;
                promocion.PrecioEspecial = 0;
                promocion.ProductoIdSecundario = 0;
                promocion.PrecioComboTotal = 0;
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
                ToastNotification.Show(_promocionEnEdicion != null ? "Promoción actualizada" : "Promoción guardada con éxito", ToastType.Success);
                LimpiarFormulario();
                CargarPromociones();
            }
            else
            {
                ToastNotification.Show($"Error al guardar: {error}", ToastType.Error);
            }
        }
        #endregion
    }
}