using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
        private const int MaxReintentosPorLote = 3;

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
            var filasOmitidasPorIA = new List<(int NumFila, List<string> Columnas)>();
            int totalLotes = (int)Math.Ceiling(filasConIndice.Count / (double)TamanoLote);

            for (int loteIdx = 0; loteIdx < totalLotes; loteIdx++)
            {
                ct.ThrowIfCancellationRequested();
                progreso?.Report($"Normalizando lote {loteIdx + 1} de {totalLotes} con Gemini...");

                var lote = filasConIndice.Skip(loteIdx * TamanoLote).Take(TamanoLote).ToList();
                string promptLote = ConstruirPromptLote(encabezado, lote);

                var respuestaLote = await InvocarLoteConBackoffAsync(
                    instruccionSistema,
                    promptLote,
                    loteIdx + 1,
                    totalLotes,
                    progreso,
                    ct).ConfigureAwait(false);

                // Si el modelo devolvio exactamente la misma cantidad de elementos pero olvido asignar NumeroFila en alguno,
                // reconciliamos por posicion dentro del lote.
                if (respuestaLote.Count == lote.Count)
                {
                    for (int k = 0; k < respuestaLote.Count; k++)
                    {
                        if (respuestaLote[k].NumeroFila <= 0)
                        {
                            respuestaLote[k].NumeroFila = lote[k].NumFila;
                        }
                    }
                }

                var filasDevueltasEnLote = new HashSet<int>();
                foreach (var item in respuestaLote)
                {
                    if (item.NumeroFila > 0 && filasDevueltasEnLote.Add(item.NumeroFila))
                    {
                        normalizadosTotales.Add(item);
                    }
                }

                // Detectamos explicitamente si Gemini omitio alguna fila enviada en este lote
                foreach (var filaOriginal in lote)
                {
                    if (!filasDevueltasEnLote.Contains(filaOriginal.NumFila))
                    {
                        filasOmitidasPorIA.Add(filaOriginal);
                    }
                }
            }

            var resultadoBase = new ResultadoPreviewImportacion();
            var itemsValidos = new List<(int NumFila, Producto Prod, string? Observacion)>();

            // Registramos explicitamente cualquier fila omitida por Gemini para que nunca desaparezca en silencio
            foreach (var (numFilaOmitida, colsOmitidas) in filasOmitidasPorIA)
            {
                string resumenFila = string.Join(" | ", colsOmitidas.Where(c => !string.IsNullOrWhiteSpace(c)).Take(4));
                if (resumenFila.Length > 70)
                {
                    resumenFila = resumenFila.Substring(0, 67) + "...";
                }

                string msgError = $"Fila {numFilaOmitida}: omitida por la IA en la respuesta JSON (datos: '{resumenFila}').";
                resultadoBase.Rechazados++;
                resultadoBase.Errores.Add(msgError);
                resultadoBase.Filas.Add(new FilaImportacionPreview
                {
                    NumeroFila = numFilaOmitida,
                    Estado = "No procesado (IA)",
                    Nombre = string.IsNullOrWhiteSpace(resumenFila) ? $"Fila #{numFilaOmitida}" : resumenFila,
                    Detalle = "⚠️ La IA omitió esta fila en su respuesta. Revísala o impórtala manualmente."
                });
            }

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

                decimal precioRaw = item.Precio ?? 0m;
                decimal costoRaw = item.Costo ?? 0m;
                decimal stockRaw = item.Stock ?? 0m;

                if (precioRaw <= 0m && costoRaw <= 0m)
                {
                    resultadoBase.Rechazados++;
                    string err = $"Fila {item.NumeroFila} ({nombre}): precio de venta en $0.00 o ausente tras normalizar.";
                    resultadoBase.Errores.Add(err);
                    resultadoBase.Filas.Add(new FilaImportacionPreview
                    {
                        NumeroFila = item.NumeroFila,
                        Estado = "Rechazado",
                        Codigo = (item.Codigo ?? string.Empty).Trim(),
                        Nombre = nombre,
                        Categoria = string.IsNullOrWhiteSpace(item.Categoria) ? "General" : item.Categoria.Trim(),
                        Detalle = err
                    });
                    continue;
                }

                var advertenciasItem = new List<string>();
                if (!string.IsNullOrWhiteSpace(item.Observacion))
                {
                    advertenciasItem.Add(item.Observacion.Trim());
                }

                if (costoRaw < 0m || stockRaw < 0m)
                {
                    advertenciasItem.Add("⚠️ Valores negativos ajustados a 0");
                }

                decimal costo = Math.Max(0m, Math.Round(costoRaw, 2));
                decimal precio = Math.Max(0m, Math.Round(precioRaw, 2));
                if (precio <= 0m && costo > 0m)
                {
                    precio = costo;
                    advertenciasItem.Add("⚠️ Sin precio de venta: se igualó al costo");
                }

                if (costo > precio && precio > 0m)
                {
                    string alertaMargen = $"⚠️ Costo (${costo:0.00}) supera Precio (${precio:0.00})";
                    advertenciasItem.Add(alertaMargen);
                    resultadoBase.Errores.Add($"[Aviso IA] Fila {item.NumeroFila} ({nombre}): {alertaMargen}");
                }

                decimal gananciaRaw = item.Ganancia ?? 0m;
                decimal ganancia = (gananciaRaw != 0m && precio >= costo)
                    ? Math.Round(gananciaRaw, 2)
                    : Math.Round(precio - costo, 2);
                decimal stock = Math.Max(0m, Math.Round(stockRaw, 3));
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

                string? observacionConsolidada = advertenciasItem.Count > 0
                    ? string.Join(" | ", advertenciasItem.Distinct(StringComparer.OrdinalIgnoreCase))
                    : null;

                itemsValidos.Add((item.NumeroFila, prod, observacionConsolidada));
            }

            var previewFinal = ImportarExportarService.EvaluarProductosParaPreview(itemsValidos, esPremium, modo, resultadoBase);
            return (previewFinal, itemsValidos);
        }

        private static async Task<List<ProductoNormalizadoIA>> InvocarLoteConBackoffAsync(
            string instruccionSistema,
            string promptLote,
            int numeroLote,
            int totalLotes,
            IProgress<string>? progreso,
            CancellationToken ct)
        {
            Exception? ultimoError = null;
            int[] demorasMs = [1500, 3500, 7500];

            for (int intento = 0; intento <= MaxReintentosPorLote; intento++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    string jsonLote = await GeminiService.GenerarJsonTextoAsync(
                        instruccionSistema,
                        promptLote,
                        ct).ConfigureAwait(false);

                    var parsed = DeserializarListaFlexible(jsonLote);
                    if (parsed != null)
                    {
                        return parsed;
                    }

                    throw new InvalidOperationException("Gemini devolvió un JSON sin arreglo de productos.");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (intento < MaxReintentosPorLote && EsErrorTransitorioReintentable(ex))
                {
                    ultimoError = ex;
                    int esperaMs = demorasMs[Math.Min(intento, demorasMs.Length - 1)] + Random.Shared.Next(100, 450);
                    PosLogger.Warn(
                        "NormalizadorImportacionIA.InvocarLoteConBackoffAsync",
                        $"Lote {numeroLote}/{totalLotes} intento {intento + 1} falló ({ex.Message}). Reintentando en {esperaMs}ms...");
                    progreso?.Report($"Lote {numeroLote}/{totalLotes}: reintentando ({intento + 1}/{MaxReintentosPorLote})...");
                    await Task.Delay(esperaMs, ct).ConfigureAwait(false);
                }
            }

            throw ultimoError ?? new InvalidOperationException($"No se pudo procesar el lote {numeroLote} con Gemini.");
        }

        private static bool EsErrorTransitorioReintentable(Exception ex)
        {
            if (ex is HttpRequestException || ex is JsonException)
            {
                return true;
            }

            string msg = ex.Message ?? string.Empty;
            if (msg.Contains("invalida", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("sin permisos", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Premium", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Configura tu API Key", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return msg.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("Limite de peticiones", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("500", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("502", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("503", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("504", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("tardo demasiado", StringComparison.OrdinalIgnoreCase) ||
                   msg.Contains("respuesta vacia", StringComparison.OrdinalIgnoreCase);
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
            sb.AppendLine("1. NO omitas ninguna fila recibida; devuelve exactamente un objeto por cada fila de entrada conservando su numeroFila exacto.");
            sb.AppendLine("2. Limpia simbolos de moneda y texto en numeros (ej. '$18.50 MXN' -> 18.50).");
            sb.AppendLine("3. Estandariza nombres a formato limpio (ej. 'COCA COLA 600ML' -> 'Coca Cola 600ml') sin inventar datos.");
            sb.AppendLine($"4. Reutiliza estas categorias existentes cuando encajen: [{cats}]. Si ninguna encaja, asigna una categoria corta y clara.");
            sb.AppendLine("5. Si falta ganancia, calcula precio - costo. Si falta costo, pon 0 y ganancia = precio. Si no hay columna de stock minimo, usa 5.");
            sb.AppendLine("6. Marca seVendePorPeso=true si el producto indica venta a granel o por kilo/gramo (kg, gr, granel).");
            sb.AppendLine("7. Si notas algo sospechoso (precio <= 0 o precio < costo), anotalo brevemente en observacion.");
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
