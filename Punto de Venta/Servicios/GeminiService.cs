using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Punto_de_Venta.Servicios
{
    public sealed class EstadoCuotaGemini
    {
        public string Modelo { get; init; } = string.Empty;
        public int PeticionesHoy { get; init; }
        public int LimiteDiarioEstimado { get; init; }
        public int RestantesEstimados { get; init; }
        public double PorcentajeRestante { get; init; }
        public int TokensHoy { get; init; }
        public bool LimiteAgotado { get; init; }
    }

    public static class GeminiService
    {
        private sealed class RegistroUsoDiario
        {
            public string Fecha { get; set; } = string.Empty;
            public int Peticiones { get; set; }
            public int Tokens { get; set; }
            public bool LimiteAgotado { get; set; }
        }

        private static readonly object UsoLock = new();
        private static readonly string UsoPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POS_DataStorage",
            "gemini_uso.json");
        private static readonly HttpClient Http = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static bool EstaConfigurada()
        {
            var cfg = AppConfig.Load();
            return !string.IsNullOrWhiteSpace(cfg.ObtenerGeminiApiKey()) ||
                   !string.IsNullOrWhiteSpace(cfg.GeminiProxyUrl);
        }

        public static EstadoCuotaGemini ObtenerEstadoCuota()
        {
            var cfg = AppConfig.Load();
            string modelo = ResolverModeloActivo(cfg);
            int limite = ObtenerLimiteDiarioEstimado(modelo);

            lock (UsoLock)
            {
                var uso = CargarUsoDiarioSinLock();
                int restantes = uso.LimiteAgotado ? 0 : Math.Max(0, limite - uso.Peticiones);
                double porcentaje = limite > 0
                    ? Math.Round((restantes * 100.0) / limite, 1)
                    : 0.0;

                return new EstadoCuotaGemini
                {
                    Modelo = modelo,
                    PeticionesHoy = uso.Peticiones,
                    LimiteDiarioEstimado = limite,
                    RestantesEstimados = restantes,
                    PorcentajeRestante = Math.Clamp(porcentaje, 0.0, 100.0),
                    TokensHoy = uso.Tokens,
                    LimiteAgotado = uso.LimiteAgotado
                };
            }
        }

        private static string ResolverModeloActivo(AppConfig cfg)
        {
            string modelo = string.IsNullOrWhiteSpace(cfg.GeminiModelo) ? "gemini-3.5-flash-lite" : cfg.GeminiModelo.Trim();
            if (modelo.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase))
            {
                modelo = "gemini-3.5-flash-lite";
            }
            return modelo;
        }

        private static int ObtenerLimiteDiarioEstimado(string modelo)
        {
            if (modelo.Contains("pro", StringComparison.OrdinalIgnoreCase)) return 250;
            if (modelo.Contains("flash-lite", StringComparison.OrdinalIgnoreCase)) return 1500;
            if (modelo.Contains("flash", StringComparison.OrdinalIgnoreCase)) return 1000;
            return 1500;
        }

        private static RegistroUsoDiario CargarUsoDiarioSinLock()
        {
            string hoy = DateTime.Today.ToString("yyyy-MM-dd");
            try
            {
                if (File.Exists(UsoPath))
                {
                    var leido = JsonSerializer.Deserialize<RegistroUsoDiario>(File.ReadAllText(UsoPath));
                    if (leido != null && string.Equals(leido.Fecha, hoy, StringComparison.Ordinal))
                    {
                        return leido;
                    }
                }
            }
            catch
            {
            }
            return new RegistroUsoDiario { Fecha = hoy };
        }

        private static void GuardarUsoDiarioSinLock(RegistroUsoDiario uso)
        {
            try
            {
                string? dir = Path.GetDirectoryName(UsoPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(UsoPath, JsonSerializer.Serialize(uso));
            }
            catch
            {
            }
        }

        private static void RegistrarPeticionExitosa(int tokens)
        {
            lock (UsoLock)
            {
                var uso = CargarUsoDiarioSinLock();
                uso.Peticiones++;
                if (tokens > 0) uso.Tokens += tokens;
                uso.LimiteAgotado = false;
                GuardarUsoDiarioSinLock(uso);
            }
        }

        private static void RegistrarLimiteAlcanzado()
        {
            lock (UsoLock)
            {
                var uso = CargarUsoDiarioSinLock();
                uso.LimiteAgotado = true;
                GuardarUsoDiarioSinLock(uso);
            }
        }

        public static async Task<string> GenerarJsonTextoAsync(string instruccionSistema, string promptUsuario, CancellationToken ct = default)
        {
            var payload = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = instruccionSistema } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = promptUsuario } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    responseMimeType = "application/json"
                }
            };

            using var doc = await EnviarPeticionAsync(payload, ct, timeoutSegundos: 60).ConfigureAwait(false);
            string texto = ExtraerTextoRespuesta(doc);
            if (string.IsNullOrWhiteSpace(texto))
            {
                throw new InvalidOperationException("Gemini devolvio una respuesta vacia.");
            }

            return LimpiarBloqueJson(texto);
        }

        public static async Task<T> GenerarJsonAsync<T>(string instruccionSistema, string promptUsuario, CancellationToken ct = default)
        {
            string jsonLimpio = await GenerarJsonTextoAsync(instruccionSistema, promptUsuario, ct).ConfigureAwait(false);
            var resultado = JsonSerializer.Deserialize<T>(jsonLimpio, JsonOpts);
            if (resultado == null)
            {
                throw new InvalidOperationException("No se pudo interpretar el JSON devuelto por Gemini.");
            }
            return resultado;
        }

        public static async Task<string> BuscarInformacionInternetAsync(string consulta, CancellationToken ct = default)
        {
            string prompt =
                "Busca en internet informacion comercial actualizada en Mexico (pesos mexicanos MXN) sobre:\n" +
                consulta +
                "\n\nDevuelve una lista clara y estructurada con: nombre comercial completo (incluyendo marca y presentacion/gramaje), categoria sugerida de tienda/abarrotes, precio de venta al publico sugerido en MXN, costo estimado de compra (aprox. 70-75% del precio si no hay dato exacto) y codigo de barras EAN/UPC solo si es verificado.";

            var payloadConBusqueda = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = "Eres un investigador de catalogo comercial para tiendas de abarrotes y miscelaneas en Mexico. Usa busqueda web para obtener productos reales, marcas y precios vigentes en MXN." } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                },
                tools = new[] { new { google_search = new { } } },
                generationConfig = new { temperature = 0.2 }
            };

            try
            {
                using var doc = await EnviarPeticionAsync(payloadConBusqueda, ct, timeoutSegundos: 35).ConfigureAwait(false);
                string texto = ExtraerTextoRespuesta(doc);
                if (!string.IsNullOrWhiteSpace(texto)) return texto;
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Error de Gemini (400)", StringComparison.OrdinalIgnoreCase))
            {
                // Si el modelo o proxy no soporta google_search grounding, reintentar sin herramienta web.
            }

            var payloadRespaldo = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = "Eres un especialista en catalogo comercial de tiendas de abarrotes en Mexico. Proporciona productos reales, marcas y precios sugeridos realistas en MXN." } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                },
                generationConfig = new { temperature = 0.2 }
            };

            using var docRespaldo = await EnviarPeticionAsync(payloadRespaldo, ct, timeoutSegundos: 30).ConfigureAwait(false);
            string textoRespaldo = ExtraerTextoRespuesta(docRespaldo);
            return string.IsNullOrWhiteSpace(textoRespaldo)
                ? "No se encontraron resultados en internet para esa consulta."
                : textoRespaldo;
        }

        public static async Task<string> EjecutarChatConHerramientasAsync(
            string instruccionSistema,
            List<object> historialContents,
            List<object> declaracionesHerramientas,
            Func<string, JsonElement, Task<object>> ejecutorHerramienta,
            int maxPasos = 6,
            CancellationToken ct = default)
        {
            var contentsTrabajo = new List<object>(historialContents);

            for (int paso = 0; paso < maxPasos; paso++)
            {
                object payload = declaracionesHerramientas.Count > 0
                    ? new
                    {
                        system_instruction = new { parts = new[] { new { text = instruccionSistema } } },
                        contents = contentsTrabajo,
                        tools = new[] { new { functionDeclarations = declaracionesHerramientas } },
                        generationConfig = new { temperature = 0.3 }
                    }
                    : new
                    {
                        system_instruction = new { parts = new[] { new { text = instruccionSistema } } },
                        contents = contentsTrabajo,
                        generationConfig = new { temperature = 0.3 }
                    };

                using var doc = await EnviarPeticionAsync(payload, ct).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                {
                    return "No obtuve respuesta del modelo.";
                }

                var candidato = candidates[0];
                if (!candidato.TryGetProperty("content", out var content) ||
                    !content.TryGetProperty("parts", out var parts))
                {
                    string motivo = candidato.TryGetProperty("finishReason", out var fr)
                        ? (fr.GetString() ?? "desconocido")
                        : "sin contenido";
                    return $"Gemini no pudo completar la respuesta (motivo: {motivo}).";
                }

                var llamadas = new List<(string Name, JsonElement Args)>();
                var textoFinal = new StringBuilder();

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("functionCall", out var fc) &&
                        fc.TryGetProperty("name", out var nameProp))
                    {
                        string nombre = nameProp.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(nombre))
                        {
                            JsonElement args = fc.TryGetProperty("args", out var a) ? a.Clone() : default;
                            llamadas.Add((nombre, args));
                        }
                    }
                    else if (part.TryGetProperty("text", out var txt))
                    {
                        textoFinal.Append(txt.GetString());
                    }
                }

                if (llamadas.Count == 0)
                {
                    return textoFinal.Length > 0 ? textoFinal.ToString().Trim() : "Listo.";
                }

                contentsTrabajo.Add(JsonSerializer.Deserialize<JsonElement>(content.GetRawText()));

                var responseParts = new List<object>();
                foreach (var (nombre, args) in llamadas)
                {
                    object resultadoTool;
                    try
                    {
                        resultadoTool = await ejecutorHerramienta(nombre, args).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        resultadoTool = new { error = ex.Message };
                    }

                    responseParts.Add(new
                    {
                        functionResponse = new
                        {
                            name = nombre,
                            response = new { result = resultadoTool }
                        }
                    });
                }

                contentsTrabajo.Add(new
                {
                    role = "user",
                    parts = responseParts
                });
            }

            return "Se alcanzo el limite de pasos para completar la tarea.";
        }

        private static async Task<JsonDocument> EnviarPeticionAsync(object payload, CancellationToken ct, int timeoutSegundos = 25)
        {
            if (!LicenciaService.EsPremium())
            {
                throw new InvalidOperationException("Las funciones de IA (Gemini) son exclusivas de la version Premium.");
            }

            var cfg = AppConfig.Load();
            string apiKey = cfg.ObtenerGeminiApiKey();
            string proxyUrl = cfg.GeminiProxyUrl?.Trim() ?? string.Empty;
            string modelo = string.IsNullOrWhiteSpace(cfg.GeminiModelo) ? "gemini-3.5-flash-lite" : cfg.GeminiModelo.Trim();
            if (modelo.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase))
            {
                modelo = "gemini-3.5-flash-lite";
            }

            if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(proxyUrl))
            {
                throw new InvalidOperationException("Configura tu API Key de Gemini en Configuracion antes de usar la IA.");
            }

            string url = !string.IsNullOrWhiteSpace(proxyUrl)
                ? proxyUrl
                : $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelo)}:generateContent";

            string jsonBody = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Add("x-goog-api-key", apiKey);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSegundos));

            HttpResponseMessage resp;
            try
            {
                resp = await Http.SendAsync(req, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException($"Gemini tardo demasiado en responder ({timeoutSegundos}s). Prueba con el modelo gemini-3.5-flash-lite o verifica tu conexion.");
            }
            using (resp)
            {
                string body;
                try
                {
                    body = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new InvalidOperationException($"Gemini tardo demasiado en responder ({timeoutSegundos}s). Prueba con el modelo gemini-3.5-flash-lite o verifica tu conexion.");
                }

                if (!resp.IsSuccessStatusCode)
                {
                    if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                    {
                        throw new InvalidOperationException("API Key de Gemini invalida o sin permisos.");
                    }
                    if ((int)resp.StatusCode == 429)
                    {
                        RegistrarLimiteAlcanzado();
                        throw new InvalidOperationException("Limite de peticiones de Gemini alcanzado. Intenta de nuevo en unos segundos.");
                    }
                    throw new InvalidOperationException($"Error de Gemini ({(int)resp.StatusCode}): {ExtraerErrorApi(body)}");
                }

                var jsonDoc = JsonDocument.Parse(body);
                int tokens = 0;
                if (jsonDoc.RootElement.TryGetProperty("usageMetadata", out var usage) &&
                    usage.TryGetProperty("totalTokenCount", out var totalProp) &&
                    totalProp.TryGetInt32(out int totalTokens))
                {
                    tokens = totalTokens;
                }
                RegistrarPeticionExitosa(tokens);
                return jsonDoc;
            }
        }

        private static string ExtraerTextoRespuesta(JsonDocument doc)
        {
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return string.Empty;
            }
            var candidato = candidates[0];
            if (!candidato.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts))
            {
                return string.Empty;
            }
            var sb = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var txt))
                {
                    sb.Append(txt.GetString());
                }
            }
            return sb.ToString().Trim();
        }

        private static string LimpiarBloqueJson(string texto)
        {
            string s = texto.Trim();
            if (s.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                s = s[7..];
            }
            else if (s.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                s = s[3..];
            }
            if (s.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                s = s[..^3];
            }
            return s.Trim();
        }

        private static string ExtraerErrorApi(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "Sin detalle de error.";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                {
                    return msg.GetString() ?? body;
                }
            }
            catch (JsonException)
            {
            }
            return body.Length > 200 ? body[..200] : body;
        }
    }
}

