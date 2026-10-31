using Punto_de_Venta.Modelos;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Punto_de_Venta.Datos
{
    public static class LoteDB
    {
        private static string FilePath => Path.Combine(DatabaseHelper.DbFolder, "lotes.json");
        private static readonly string LegacyFilePath = "lotes.json";

        public static List<LoteRestock> Load()
        {
            var path = File.Exists(FilePath) ? FilePath : (File.Exists(LegacyFilePath) ? LegacyFilePath : null);
            if (path == null) return new List<LoteRestock>();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<LoteRestock>>(json) ?? new List<LoteRestock>();
        }
        public static void Save(IEnumerable<LoteRestock> lotes)
        {
            Directory.CreateDirectory(DatabaseHelper.DbFolder);
            var json = JsonSerializer.Serialize(lotes, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
    }
}