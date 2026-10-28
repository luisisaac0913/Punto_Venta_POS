using System;

namespace Punto_de_Venta.Modelos
{
    public class CorteCaja
    {
        public int Id { get; set; }
        public DateTime FechaHora { get; set; } = DateTime.Now; 
        public DateTime? FechaHoraCierre { get; set; }
        public string Estado { get; set; } = "Cerrado"; 
        public decimal FondoInicial { get; set; }
        public decimal VentasEfectivo { get; set; }
        public decimal EntradasEfectivo { get; set; }
        public decimal SalidasEfectivo { get; set; }
        public decimal TotalEsperado { get; set; }
        public decimal TotalContado { get; set; }
        public decimal Diferencia => TotalContado - TotalEsperado; 
        public string Observaciones { get; set; } = string.Empty;
    }
}