using System;
using System.Collections.Generic;
using System.Globalization;

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

        public bool EstaVencida => FechaFin.HasValue && DateTime.Today > FechaFin.Value.Date;
        public bool PorIniciar => FechaInicio.HasValue && DateTime.Today < FechaInicio.Value.Date;

        public string VigenciaTexto
        {
            get
            {
                if (EstaVencida)
                    return $"Vencida ({FechaFin!.Value:dd/MM/yyyy})";
                if (PorIniciar)
                    return $"Inicia {FechaInicio!.Value:dd/MM/yyyy}";
                if (FechaInicio.HasValue && FechaFin.HasValue)
                    return $"{FechaInicio.Value:dd/MM/yyyy} - {FechaFin.Value:dd/MM/yyyy}";
                if (FechaFin.HasValue)
                    return $"Hasta {FechaFin.Value:dd/MM/yyyy}";
                if (FechaInicio.HasValue)
                    return $"Desde {FechaInicio.Value:dd/MM/yyyy}";
                return "Indefinida";
            }
        }

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