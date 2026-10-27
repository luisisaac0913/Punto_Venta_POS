using System.Collections.Generic;

namespace Punto_de_Venta.Modelos
{
    public class LoteRestock
    {
        public string Nombre { get; set; } = string.Empty;
        public string Proveedor { get; set; } = string.Empty;
        public List<LoteItem> Articulos { get; set; } = new();
    }

    public class LoteItem
    {
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
    }
}