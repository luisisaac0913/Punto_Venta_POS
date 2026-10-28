using Punto_de_Venta.Modelos;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Punto_de_Venta.Datos
{
    public static class LoteDB
    {
        private static readonly string filePath = "lotes.json";

        public static List<LoteRestock> Load()
        {
            if (!File.Exists(filePath)) return new List<LoteRestock>();
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<LoteRestock>>(json) ?? new List<LoteRestock>();
        }
        public static void Save(IEnumerable<LoteRestock> lotes)
        {
            var json = JsonSerializer.Serialize(lotes, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
    }
}