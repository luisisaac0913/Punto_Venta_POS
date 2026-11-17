using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Punto_de_Venta.Vistas
{
    public sealed class ItemPantallaCliente
    {
        public string CantidadTexto { get; init; } = string.Empty;
        public string Nombre { get; init; } = string.Empty;
        public string PrecioUnitarioTexto { get; init; } = string.Empty;
        public string SubtotalTexto { get; init; } = string.Empty;
    }

    public partial class PantallaClienteWindow : Window
    {
        private readonly ObservableCollection<ItemPantallaCliente> _items = new();
        private readonly DispatcherTimer _relojTimer;
        private readonly DispatcherTimer _graciasTimer;
        private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("es-MX");

        public PantallaClienteWindow()
        {
            InitializeComponent();
            ListaProductosCliente.ItemsSource = _items;

            _relojTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _relojTimer.Tick += (_, _) => TxtReloj.Text = DateTime.Now.ToString("hh:mm tt", _culture);
            _relojTimer.Start();
            TxtReloj.Text = DateTime.Now.ToString("hh:mm tt", _culture);

            _graciasTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _graciasTimer.Tick += (_, _) =>
            {
                _graciasTimer.Stop();
                PanelGraciasCompra.Visibility = Visibility.Collapsed;
                PanelResumenTotal.Visibility = Visibility.Visible;
                TxtEstadoCliente.Text = "Listo para atenderte";
            };

            Closed += (_, _) =>
            {
                _relojTimer.Stop();
                _graciasTimer.Stop();
            };
        }

        public void ActualizarCarrito(IReadOnlyList<ItemCarrito> carrito, decimal subtotal, decimal descuento, decimal totalNeto, string nombreNegocio)
        {
            if (!string.IsNullOrWhiteSpace(nombreNegocio))
            {
                TxtNombreNegocio.Text = nombreNegocio;
            }

            if (carrito.Count > 0 && PanelGraciasCompra.Visibility == Visibility.Visible)
            {
                _graciasTimer.Stop();
                PanelGraciasCompra.Visibility = Visibility.Collapsed;
                PanelResumenTotal.Visibility = Visibility.Visible;
            }

            _items.Clear();
            foreach (var item in carrito)
            {
                string cant = item.SeVendePorPeso
                    ? $"{item.Cantidad:0.###} kg"
                    : $"{item.Cantidad:0.##} ×";
                _items.Add(new ItemPantallaCliente
                {
                    CantidadTexto = cant,
                    Nombre = item.Nombre,
                    PrecioUnitarioTexto = $"{item.Precio.ToString("C2", _culture)} c/u",
                    SubtotalTexto = item.Subtotal.ToString("C2", _culture)
                });
            }

            bool vacio = _items.Count == 0;
            PanelBienvenidaCliente.Visibility = vacio ? Visibility.Visible : Visibility.Collapsed;
            TxtConteoArticulosHeader.Text = $"{_items.Count} {(_items.Count == 1 ? "articulo" : "articulos")}";
            TxtCantidadTotal.Text = carrito.Sum(i => i.SeVendePorPeso ? 1m : i.Cantidad).ToString("0.##", _culture);
            TxtSubtotal.Text = subtotal.ToString("C2", _culture);

            if (descuento > 0m)
            {
                FilaDescuento.Visibility = Visibility.Visible;
                TxtDescuento.Text = $"-{descuento.ToString("C2", _culture)}";
            }
            else
            {
                FilaDescuento.Visibility = Visibility.Collapsed;
            }

            TxtTotalPagar.Text = totalNeto.ToString("C2", _culture);
            if (PanelGraciasCompra.Visibility != Visibility.Visible)
            {
                TxtEstadoCliente.Text = vacio ? "Listo para atenderte" : "Registrando tus productos...";
            }
        }

        public void MostrarPagoCompletado(decimal total, decimal pagoCon, decimal cambio)
        {
            _graciasTimer.Stop();
            TxtGraciasTotal.Text = total.ToString("C2", _culture);
            TxtGraciasPagoCon.Text = pagoCon.ToString("C2", _culture);
            TxtGraciasCambio.Text = cambio.ToString("C2", _culture);
            TxtEstadoCliente.Text = "¡Venta completada!";

            PanelResumenTotal.Visibility = Visibility.Collapsed;
            PanelGraciasCompra.Visibility = Visibility.Visible;
            _graciasTimer.Start();
        }
    }
}

