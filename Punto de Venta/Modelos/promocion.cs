using System;
using System.ComponentModel;
using System.Globalization;

namespace Punto_de_Venta.Modelos
{
    public enum TipoPromocion
    {
        Volumen,  
        Combo,    
        PorcentajeSegundo 
    }

    public class Promocion : INotifyPropertyChanged
    {
        private bool _activa = true;

        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty; 
        public TipoPromocion Tipo { get; set; } = TipoPromocion.Volumen;

        public bool Activa
        {
            get => _activa;
            set
            {
                if (_activa != value)
                {
                    _activa = value;
                    OnPropertyChanged(nameof(Activa));
                    OnPropertyChanged(nameof(VigenciaTexto));
                }
            }
        }

        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public int ProductoIdPrincipal { get; set; }
        public int CantidadMinima { get; set; } = 1;     
        public decimal PrecioEspecial { get; set; }      
        public int ProductoIdSecundario { get; set; }
        public decimal PrecioComboTotal { get; set; }     
        public decimal PorcentajeDescuento { get; set; }  

        public string ProductoPrincipalNombre { get; set; } = string.Empty;
        public string ProductoSecundarioNombre { get; set; } = string.Empty;

        public string ProductoTexto
        {
            get
            {
                string p1 = !string.IsNullOrWhiteSpace(ProductoPrincipalNombre) ? ProductoPrincipalNombre : $"ID #{ProductoIdPrincipal}";
                if (Tipo == TipoPromocion.Combo)
                {
                    string p2 = !string.IsNullOrWhiteSpace(ProductoSecundarioNombre) ? ProductoSecundarioNombre : $"ID #{ProductoIdSecundario}";
                    return $"{p1} + {p2}";
                }
                return p1;
            }
        }

        public bool EstaVencida => FechaFin.HasValue && DateTime.Today > FechaFin.Value.Date;
        public bool PorIniciar => FechaInicio.HasValue && DateTime.Today < FechaInicio.Value.Date;

        public string VigenciaTexto
        {
            get
            {
                if (!Activa)
                    return "Desactivada";
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}