using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Punto_de_Venta.Servicios
{
    public sealed class ProductoNormalizadoIA
    {
        public int NumeroFila { get; set; }
        public string? Codigo { get; set; }
        public string? Nombre { get; set; }
        public string? Categoria { get; set; }
        public decimal? Costo { get; set; }
        public decimal? Precio { get; set; }
        public decimal? Ganancia { get; set; }
        public decimal? Stock { get; set; }
        public decimal? StockMinimo { get; set; }
        public bool? SeVendePorPeso { get; set; }
        public bool? PedirCantidadAlCobrar { get; set; }
        public string? Observacion { get; set; }
    }

    public static class NormalizadorImportacionIA
    {
        private const int TamanoLote = 50;

        public static async Task<(ResultadoPreviewImportacion Preview, List<(int NumFila, Producto Prod, string? Observacion)> Items)> NormalizarArchivoAsync(
            string filePath,
            bool esPremium,
            ModoImportacion modo,
            IProgress<string>? progreso = null,
            CancellationToken ct = default)
        {
            var filasCrudas = ImportarExportarService.LeerFilasCrudas(filePath);
            if (filasCrudas.Count == 0)
            {
                var vacio = new ResultadoPreviewImportacion();
                vacio.Errores.Add("El archivo no contiene filas validas para normalizar.");
                return (vacio, new List<(int, Producto, string?)>());
            }

            var categoriasExistentes = ProductoDB.Load()
                .Select(p => p.Categoria?.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var encabezado = filasCrudas[0];
            var filasConIndice = new List<(int NumFila, List<string> Columnas)>();
            for (int i = 0; i < filasCrudas.Count; i++)
            {
                var fila = filasCrudas[i];
                if (fila.All(string.IsNullOrWhiteSpace)) continue;
                if (i == 0 && PareceEncabezado(fila)) continue;
                filasConIndice.Add((i + 1, fila));
            }

            if (filasConIndice.Count == 0)
            {
                var vacio = new ResultadoPreviewImportacion();
                vacio.Errores.Add("No se encontraron filas de productos para normalizar.");
                return (vacio, new List<(int, Producto, string?)>());
            }

            string instruccionSistema = ConstruirPromptSistema(categoriasExistentes!);
            var normalizadosTotales = new List<ProductoNormalizadoIA>();
            int totalLotes = (int)Math.Ceiling(filasConIndice.Count / (double)TamanoLote);

            for (int loteIdx = 0; loteIdx < totalLotes; loteIdx++)
            {
                ct.ThrowIfCancellationRequested();
                progreso?.Report($"Normalizando lote {loteIdx + 1} de {totalLotes} con Gemini...");

                var lote = filasConIndice.Skip(loteIdx * TamanoLote).Take(TamanoLote).ToList();
                string promptLote = ConstruirPromptLote(encabezado, lote);

                string jsonLote = await GeminiService.GenerarJsonTextoAsync(
                    instruccionSistema,
                    promptLote,
                    ct).ConfigureAwait(false);

                var respuestaLote = DeserializarListaFlexible(jsonLote);
                if (respuestaLote != null)
                {
                    normalizadosTotales.AddRange(respuestaLote);
                }
            }

            var resultadoBase = new ResultadoPreviewImportacion();
            var itemsValidos = new List<(int NumFila, Producto Prod, string? Observacion)>();

            foreach (var item in normalizadosTotales)
            {
                string nombre = (item.Nombre ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(nombre))
                {
                    resultadoBase.Rechazados++;
                    string err = $"Fila {item.NumeroFila}: nombre vacio tras normalizar.";
                    resultadoBase.Errores.Add(err);
                    resultadoBase.Filas.Add(new FilaImportacionPreview
                    {
                        NumeroFila = item.NumeroFila,
                        Estado = "Rechazado",
                        Detalle = err
                    });
                    continue;
                }

                decimal precio = Math.Max(0m, Math.Round(item.Precio ?? 0m, 2));
                decimal costo = Math.Max(0m, Math.Round(item.Costo ?? 0m, 2));
                decimal gananciaRaw = item.Ganancia ?? 0m;
                decimal ganancia = gananciaRaw != 0m ? Math.Round(gananciaRaw, 2) : Math.Round(precio - costo, 2);
                decimal stock = Math.Max(0m, Math.Round(item.Stock ?? 0m, 3));
                decimal stockMinimo = Math.Max(0m, Math.Round(item.StockMinimo ?? 5m, 3));
                string categoria = string.IsNullOrWhiteSpace(item.Categoria) ? "General" : item.Categoria.Trim();
                bool porPeso = item.SeVendePorPeso ?? false;
                bool pedirCant = item.PedirCantidadAlCobrar ?? false;

                var prod = new Producto
                {
                    Codigo = (item.Codigo ?? string.Empty).Trim(),
                    Nombre = nombre,
                    Categoria = categoria,
                    Costo = costo,
                    Precio = precio,
                    Ganancia = ganancia,
                    Stock = stock,
                    StockMinimo = stockMinimo,
                    SeVendePorPeso = porPeso,
                    PedirCantidadAlCobrar = pedirCant && !porPeso
                };

                itemsValidos.Add((item.NumeroFila, prod, item.Observacion));
            }

            var previewFinal = ImportarExportarService.EvaluarProductosParaPreview(itemsValidos, esPremium, modo, resultadoBase);
            return (previewFinal, itemsValidos);
        }

        private static List<ProductoNormalizadoIA>? DeserializarListaFlexible(string json)
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<ProductoNormalizadoIA>>(json, opts);
            }
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        return JsonSerializer.Deserialize<List<ProductoNormalizadoIA>>(prop.Value.GetRawText(), opts);
                    }
                }
            }
            return new List<ProductoNormalizadoIA>();
        }

        private static bool PareceEncabezado(List<string> fila)
        {
            string unido = string.Join(" ", fila).ToLowerInvariant();
            return unido.Contains("nombre") || unido.Contains("producto") || unido.Contains("descripcion") ||
                   unido.Contains("precio") || unido.Contains("codigo") || unido.Contains("stock") ||
                   unido.Contains("costo") || unido.Contains("existencia");
        }

        private static string ConstruirPromptSistema(List<string> categoriasExistentes)
        {
            string cats = categoriasExistentes.Count > 0
                ? string.Join(", ", categoriasExistentes)
                : "General, Abarrotes, Bebidas, Dulces, Limpieza, Lacteos";

            var sb = new StringBuilder();
            sb.AppendLine("Eres un experto normalizador de catalogos para un Punto de Venta en Mexico.");
            sb.AppendLine("Recibiras filas crudas de un archivo Excel/CSV con posibles columnas desordenadas, simbolos de moneda ($/MXN), textos en mayusculas o categorias inconsistentes.");
            sb.AppendLine("Devuelve EXCLUSIVAMENTE un arreglo JSON de objetos con estas propiedades exactas:");
            sb.AppendLine("numeroFila (int), codigo (string), nombre (string), categoria (string), costo (decimal), precio (decimal), ganancia (decimal), stock (decimal), stockMinimo (decimal), seVendePorPeso (bool), pedirCantidadAlCobrar (bool), observacion (string).");
            sb.AppendLine("Reglas:");
            sb.AppendLine("1. Limpia simbolos de moneda y texto en numeros (ej. '$18.50 MXN' -> 18.50).");
            sb.AppendLine("2. Estandariza nombres a formato limpio (ej. 'COCA COLA 600ML' -> 'Coca Cola 600ml') sin inventar datos.");
            sb.AppendLine($"3. Reutiliza estas categorias existentes cuando encajen: [{cats}]. Si ninguna encaja, asigna una categoria corta y clara.");
            sb.AppendLine("4. Si falta ganancia, calcula precio - costo. Si falta costo, pon 0 y ganancia = precio. Si no hay columna de stock minimo, usa 5.");
            sb.AppendLine("5. Marca seVendePorPeso=true si el producto indica venta a granel o por kilo/gramo (kg, gr, granel).");
            sb.AppendLine("6. Si notas algo sospechoso (precio <= 0 o precio < costo), anotalo brevemente en observacion.");
            return sb.ToString();
        }

        private static string ConstruirPromptLote(List<string> encabezado, List<(int NumFila, List<string> Columnas)> lote)
        {
            var payload = new
            {
                posibleEncabezado = encabezado,
                filas = lote.Select(x => new { numeroFila = x.NumFila, columnas = x.Columnas })
            };
            return JsonSerializer.Serialize(payload);
        }
    }
}

