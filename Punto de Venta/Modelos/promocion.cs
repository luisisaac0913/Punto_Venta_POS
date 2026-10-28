using System;
using System.Collections.Generic;

namespace Punto_de_Venta.Modelos
{
    public enum TipoPromocion
    {
        Volumen,  
        Combo,    
        PorcentajeSegundo 
    }

    public class Promocion
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty; 
        public TipoPromocion Tipo { get; set; } = TipoPromocion.Volumen;
        public bool Activa { get; set; } = true;
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public int ProductoIdPrincipal { get; set; }
        public int CantidadMinima { get; set; } = 1;     
        public decimal PrecioEspecial { get; set; }      
        public int ProductoIdSecundario { get; set; }
        public decimal PrecioComboTotal { get; set; }     
        public decimal PorcentajeDescuento { get; set; }  
        public bool EstaVigente()
        {
            if (!Activa) return false;
            var hoy = DateTime.Today;
            if (FechaInicio.HasValue && hoy < FechaInicio.Value.Date) return false;
            if (FechaFin.HasValue && hoy > FechaFin.Value.Date) return false;
            return true;
        }
    }
}