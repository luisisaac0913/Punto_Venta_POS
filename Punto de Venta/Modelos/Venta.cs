using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Punto_de_Venta.Modelos
{
    public class Venta : INotifyPropertyChanged
    {
        private string _productosResumenGuardado = string.Empty;

        // ¡CORREGIDO! Cambiado de int a decimal para soportar los pesos de la báscula (H-01) sin romper la compilación
        private decimal _cantidadTotalGuardada;

        private string _estado = "Abierta";
        public string MetodoPago { get; set; } = "Efectivo";

        // --- NUEVAS PROPIEDADES EN MEMORIA ---
        public decimal PagoEfectivo { get; set; }
        public decimal PagoTarjeta { get; set; }
        public decimal PagoTransferencia { get; set; }
        // -------------------------------------

        public int Id { get; set; }
        public DateTime FechaHora { get; set; } = DateTime.Now;
        public decimal Total { get; set; }
        public decimal PagoCon { get; set; }
        public decimal Cambio
        {
            get
            {
                if (Estado == "Fiado" || PagoCon < Total)
                {
                    return 0m;
                }
                return PagoCon - Total;
            }
            set { } // Setter vacío para permitir deserialización o lectura sin romper
        }
        public string Categoria { get; set; } = "N/A";

        public string Estado
        {
            get => _estado;
            set
            {
                if (_estado != value)
                {
                    _estado = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<DetalleVenta> Productos { get; set; } = new List<DetalleVenta>();

        public string ProductosResumen
        {
            get => Productos.Count > 0
                // Formato "0.###" oculta decimales inútiles en enteros, pero los muestra en granel
                ? string.Join(", ", Productos.Select(product => $"{product.Nombre} x{product.Cantidad:0.###}"))
                : _productosResumenGuardado;
            set => _productosResumenGuardado = value ?? string.Empty;
        }

        public decimal CantidadTotal
        {
            get => Productos.Count > 0 ? Productos.Sum(product => product.Cantidad) : _cantidadTotalGuardada;
            set => _cantidadTotalGuardada = value;
        }

        public decimal CostoTotal =>
            Productos.Count > 0 ? Productos.Sum(product => product.Costo * product.Cantidad) : 0m;

        public decimal GananciaTotal =>
            Productos.Count > 0 ? Productos.Sum(product => product.Ganancia * product.Cantidad) : 0m;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void NotifyTotalesChanged()
        {
            OnPropertyChanged(nameof(CantidadTotal));
            OnPropertyChanged(nameof(ProductosResumen));
            OnPropertyChanged(nameof(CostoTotal));
            OnPropertyChanged(nameof(GananciaTotal));
            OnPropertyChanged(nameof(Cambio));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class DetalleVenta
    {
        public int Id { get; set; }

        // Propiedad añadida pasivamente para futura migración de normalización (H-30)
        public int ProductoId { get; set; }

        public int VentaId { get; set; }
        public string Nombre { get; set; } = string.Empty;

        // Cantidad se mantiene en decimal, clave para que la báscula funcione
        public decimal Cantidad { get; set; }

        public decimal Costo { get; set; }
        public decimal Ganancia { get; set; }
        public decimal Precio { get; set; }
        public decimal Subtotal => Precio * Cantidad;
        public string Categoria { get; set; } = "N/A";
    }
}