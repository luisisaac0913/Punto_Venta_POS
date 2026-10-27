using Punto_de_Venta;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Punto_de_Venta.Vistas
{
    public partial class EnlaceRapidoModal : Window
    {
        private List<Producto> _catalogo = new();
        private Producto? _productoSeleccionado;

        public EnlaceRapidoModal()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try { _catalogo = ProductoDB.Load(); } catch { }
            TxtCodigo.Focus(); // Listo para recibir el primer escaneazo
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void TxtCodigo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (!string.IsNullOrWhiteSpace(TxtCodigo.Text))
                {
                    TxtBusqueda.Focus(); // El escáner da Enter y salta al buscador
                }
                e.Handled = true;
            }
        }

        private void TxtBusqueda_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = TxtBusqueda.Text.Trim();
            if (query.Length == 0 || (_productoSeleccionado != null && TxtBusqueda.Text == _productoSeleccionado.Nombre))
            {
                PopupSugerencias.Visibility = Visibility.Collapsed;
                return;
            }

            var matches = _catalogo.Where(p =>
                (p.Nombre != null && p.Nombre.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (p.Categoria != null && p.Categoria.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Take(10).ToList();

            if (matches.Any())
            {
                ListaSugerencias.ItemsSource = matches;
                ListaSugerencias.DisplayMemberPath = "Nombre";
                PopupSugerencias.Visibility = Visibility.Visible;
            }
            else
            {
                PopupSugerencias.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtBusqueda_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && PopupSugerencias.Visibility == Visibility.Visible)
            {
                ListaSugerencias.Focus();
                if (ListaSugerencias.Items.Count > 0) ListaSugerencias.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                BtnEnlazar_Click(null, null);
                e.Handled = true;
            }
        }

        private void ListaSugerencias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListaSugerencias.SelectedItem is Producto seleccionado)
            {
                _productoSeleccionado = seleccionado;
                TxtBusqueda.Text = seleccionado.Nombre;
                PopupSugerencias.Visibility = Visibility.Collapsed;
                TxtBusqueda.Focus();
                TxtBusqueda.CaretIndex = TxtBusqueda.Text.Length; // Mueve el cursor al final
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnEnlazar_Click(object sender, RoutedEventArgs e)
        {
            var nuevoCodigo = TxtCodigo.Text.Trim();

            if (string.IsNullOrWhiteSpace(nuevoCodigo))
            {
                ToastNotification.Show("Escanea un código primero", ToastType.Warning);
                TxtCodigo.Focus();
                return;
            }

            if (_productoSeleccionado == null)
            {
                ToastNotification.Show("Busca y selecciona un producto", ToastType.Warning);
                TxtBusqueda.Focus();
                return;
            }

            var productoEnDb = _catalogo.FirstOrDefault(p => p.Id == _productoSeleccionado.Id);
            if (productoEnDb != null)
            {
                productoEnDb.Codigo = nuevoCodigo;
                try
                {
                    ProductoDB.Save(_catalogo);
                    ToastNotification.Show($"Código enlazado a '{productoEnDb.Nombre}'", ToastType.Success);

                    // Re-cargar memoria limpia y desconectar referencias muertas (FIX)
                    _catalogo = ProductoDB.Load();
                    _productoSeleccionado = null;

                    TxtCodigo.Clear();
                    TxtBusqueda.Clear();
                    TxtCodigo.Focus();
                }
                catch (Exception ex)
                {
                    ToastNotification.Show($"Error al guardar: {ex.Message}", ToastType.Error);
                }
            }
        }
    }
}