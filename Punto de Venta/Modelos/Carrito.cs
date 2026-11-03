using System;
using System.ComponentModel;

namespace Punto_de_Venta.Modelos
{
    public class ItemCarrito : INotifyPropertyChanged
    {
        private decimal _cantidad = 1m;

        public int ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal Costo { get; set; }
        public decimal Ganancia { get; set; }
        public decimal Precio { get; set; }
        public string Categoria { get; set; } = "N/A";
        public bool SeVendePorPeso { get; set; }

        public decimal Cantidad
        {
            get => _cantidad;
            set
            {
                var valorRedondeado = Math.Round(value, 3);
                if (_cantidad != valorRedondeado)
                {
                    _cantidad = valorRedondeado;
                    OnPropertyChanged(nameof(Cantidad));
                    OnPropertyChanged(nameof(Subtotal));
                }
            }
        }

        public decimal Subtotal => Precio * Cantidad;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}