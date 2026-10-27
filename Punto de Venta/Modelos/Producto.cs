using System;
using System.Collections.Generic;
using System.Text;

namespace Punto_de_Venta.Modelos
{
    public class Producto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Categoria { get; set; } = "N/A";
        public decimal Costo { get; set; }
        public decimal Ganancia { get; set; }
        public decimal Precio { get; set; }
        public decimal Stock { get; set; } = 0;
        public bool EsStockAgotado => Stock <= 0;
        public bool EsStockBajo => Stock > 0 && Stock <= 3;
        public bool PedirCantidadAlCobrar { get; set; } = false;
        public bool SeVendePorPeso { get; set; }
    }
}
