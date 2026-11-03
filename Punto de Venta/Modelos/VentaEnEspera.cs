using System;
using System.Collections.Generic;
using System.Globalization;

namespace Punto_de_Venta.Modelos
{
    public class VentaEnEspera
    {
        public int Id { get; set; }
        public DateTime FechaHora { get; set; } = DateTime.Now;
        public string Identificador { get; set; } = string.Empty;
        public decimal TotalNeto { get; set; }
        public decimal TotalItems { get; set; }
        public List<ItemCarrito> Items { get; set; } = new();

        public string TotalNetoFormateado => TotalNeto.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
        public string TotalItemsFormateado => $"{TotalItems} {(TotalItems == 1 ? "articulo" : "articulos")}";
        public string HoraFormateada => FechaHora.ToString("hh:mm tt", CultureInfo.GetCultureInfo("en-US"));

        public string TiempoTranscurrido
        {
            get
            {
                var diff = DateTime.Now - FechaHora;
                if (diff.TotalMinutes < 1) return "Hace un momento";
                if (diff.TotalMinutes < 60) return $"Hace {(int)diff.TotalMinutes} min";
                if (diff.TotalHours < 24) return $"Hace {(int)diff.TotalHours} h";
                return $"Hace {(int)diff.TotalDays} d";
            }
        }
    }
}

