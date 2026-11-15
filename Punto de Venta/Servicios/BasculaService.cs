using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Punto_de_Venta.Servicios
{
    public static class BasculaService
    {
        private static readonly SemaphoreSlim _serialLock = new(1, 1);
        private static SerialPort? _puertoActivo;
        private static string _configuracionActivaKey = string.Empty;
        private const int TimeoutLecturaMs = 1400;

        public static string[] ObtenerPuertosDisponibles()
        {
            try
            {
                return SerialPort.GetPortNames();
            }
            catch (Exception ex)
            {
                PosLogger.Warn("BasculaService.ObtenerPuertosDisponibles", ex);
                return Array.Empty<string>();
            }
        }

        public static bool EsProtocoloContinuo(string? protocolo)
        {
            if (string.IsNullOrWhiteSpace(protocolo)) return false;
            return protocolo.Contains("Continuo", StringComparison.OrdinalIgnoreCase) ||
                   protocolo.Contains("Continuous", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizarProtocolo(string? protocolo)
        {
            if (string.IsNullOrWhiteSpace(protocolo)) return "PeticionP";
            string p = protocolo.Trim();
            if (p.Equals("Auto", StringComparison.OrdinalIgnoreCase) || p.Contains("Universal", StringComparison.OrdinalIgnoreCase))
                return "Auto";
            if (EsProtocoloContinuo(p))
                return "Continuo";
            if (p.Equals("PeticionW", StringComparison.OrdinalIgnoreCase) || p.Contains("CAS", StringComparison.OrdinalIgnoreCase) || p.Contains("Ohaus", StringComparison.OrdinalIgnoreCase))
                return "PeticionW";
            if (p.Equals("PeticionS", StringComparison.OrdinalIgnoreCase) || p.Contains("SICS", StringComparison.OrdinalIgnoreCase) || p.Contains("Mettler", StringComparison.OrdinalIgnoreCase))
                return "PeticionS";
            if (p.Equals("PeticionENQ", StringComparison.OrdinalIgnoreCase) || p.Contains("ENQ", StringComparison.OrdinalIgnoreCase))
                return "PeticionENQ";
            return "PeticionP";
        }

        private static string? ObtenerComandoPeticion(string protocoloNormalizado) => protocoloNormalizado switch
        {
            "Continuo" => null,
            "PeticionW" => "W\r\n",
            "PeticionS" => "SI\r\n",
            "PeticionENQ" => "\x05",
            _ => "P\r"
        };

        public static void CerrarConexionPersistente()
        {
            if (!_serialLock.Wait(500))
            {
                CerrarPuertoInterno();
                return;
            }

            try
            {
                CerrarPuertoInterno();
            }
            finally
            {
                _serialLock.Release();
            }
        }

        private static void CerrarPuertoInterno()
        {
            var port = _puertoActivo;
            _puertoActivo = null;
            _configuracionActivaKey = string.Empty;

            if (port == null) return;

            try
            {
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
            catch
            {
            }

            try
            {
                port.Dispose();
            }
            catch
            {
            }
        }

        public static async Task<(bool Exito, decimal Peso, string Error)> ProbarConexionAsync(
            string puerto,
            int baudRate,
            string? protocolo = null,
            CancellationToken cancellationToken = default,
            int? dataBitsOverride = null,
            string? parityOverride = null,
            string? stopBitsOverride = null)
        {
            if (string.IsNullOrWhiteSpace(puerto))
            {
                return (false, 0m, "No se ha seleccionado ningun puerto COM.");
            }

            var config = AppConfig.Load();
            string protoEfectivo = NormalizarProtocolo(string.IsNullOrWhiteSpace(protocolo) ? config.BasculaProtocolo : protocolo);
            int dataBits = dataBitsOverride ?? config.BasculaDataBits;
            string parity = !string.IsNullOrWhiteSpace(parityOverride) ? parityOverride : config.BasculaParity;
            string stopBits = !string.IsNullOrWhiteSpace(stopBitsOverride) ? stopBitsOverride : config.BasculaStopBits;

            await _serialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LeerPesoDesdePuertoAsync(
                    puerto.Trim(),
                    baudRate > 0 ? baudRate : 9600,
                    dataBits,
                    parity,
                    stopBits,
                    config.BasculaNewLine,
                    protoEfectivo,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _serialLock.Release();
            }
        }

        public static async Task<(bool Exito, decimal Peso, string Error)> ObtenerPesoAsync(CancellationToken cancellationToken = default)
        {
            var config = AppConfig.Load();

            if (!config.BasculaHabilitada)
            {
                return (false, 0m, "La bascula esta desactivada en la configuracion.");
            }

            var puerto = config.BasculaPuerto?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(puerto))
            {
                return (false, 0m, "No se ha configurado un puerto COM para la bascula.");
            }

            int baudRate = config.BasculaBaudRate > 0 ? config.BasculaBaudRate : 9600;
            string protoEfectivo = NormalizarProtocolo(config.BasculaProtocolo);

            await _serialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LeerPesoDesdePuertoAsync(
                    puerto,
                    baudRate,
                    config.BasculaDataBits,
                    config.BasculaParity,
                    config.BasculaStopBits,
                    config.BasculaNewLine,
                    protoEfectivo,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _serialLock.Release();
            }
        }

        private static SerialPort ObtenerOAbrirPuertoPersistente(
            string puerto,
            int baudRate,
            int dataBitsConfig,
            string? parityConfig,
            string? stopBitsConfig,
            string? newLineConfig)
        {
            int dataBits = (dataBitsConfig == 7 || dataBitsConfig == 8) ? dataBitsConfig : 8;
            Parity parity = ResolverParity(parityConfig);
            StopBits stopBits = ResolverStopBits(stopBitsConfig);
            string newLine = NormalizarNewLine(newLineConfig);

            string clave = $"{puerto.ToUpperInvariant()}|{baudRate}|{dataBits}|{parity}|{stopBits}|{newLine}";

            if (_puertoActivo != null && _puertoActivo.IsOpen && string.Equals(_configuracionActivaKey, clave, StringComparison.Ordinal))
            {
                return _puertoActivo;
            }

            CerrarPuertoInterno();

            var nuevoPuerto = new SerialPort(puerto, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = TimeoutLecturaMs,
                WriteTimeout = TimeoutLecturaMs,
                NewLine = newLine,
                Encoding = Encoding.ASCII
            };

            try
            {
                nuevoPuerto.DtrEnable = true;
                nuevoPuerto.RtsEnable = true;
            }
            catch
            {
            }

            nuevoPuerto.Open();
            _puertoActivo = nuevoPuerto;
            _configuracionActivaKey = clave;
            return nuevoPuerto;
        }

        private static async Task<(bool Exito, decimal Peso, string Error)> LeerPesoDesdePuertoAsync(
            string puerto,
            int baudRate,
            int dataBitsConfig,
            string? parityConfig,
            string? stopBitsConfig,
            string? newLineConfig,
            string protocoloNormalizado,
            CancellationToken cancellationToken)
        {
            try
            {
                var port = ObtenerOAbrirPuertoPersistente(puerto, baudRate, dataBitsConfig, parityConfig, stopBitsConfig, newLineConfig);
                string newLineConfigurado = NormalizarNewLine(newLineConfig);

                if (string.Equals(protocoloNormalizado, "Auto", StringComparison.Ordinal))
                {
                    // Modo Universal: primero escucha brevemente por si transmite en continuo y luego prueba comandos estandar
                    var intentosAuto = new (string? Comando, bool DescartarPrimera, int TimeoutMs)[]
                    {
                        (null, true, 320),
                        ("P\r", false, 450),
                        ("W\r\n", false, 450),
                        ("SI\r\n", false, 450),
                        ("\x05", false, 450)
                    };

                    string ultimoBuffer = string.Empty;
                    foreach (var intento in intentosAuto)
                    {
                        var resIntento = await EjecutarLecturaUnicaAsync(
                            port,
                            intento.Comando,
                            intento.DescartarPrimera,
                            newLineConfigurado,
                            intento.TimeoutMs,
                            cancellationToken).ConfigureAwait(false);

                        if (resIntento.Exito)
                        {
                            return (true, resIntento.Peso, string.Empty);
                        }

                        if (!string.IsNullOrWhiteSpace(resIntento.BufferRecibido))
                        {
                            ultimoBuffer = resIntento.BufferRecibido;
                        }
                    }

                    return (false, 0m, string.IsNullOrWhiteSpace(ultimoBuffer)
                        ? "La bascula no respondio en modo Universal (continuo, P, W, SI, ENQ)."
                        : $"Trama incompleta o no valida de la bascula: '{LimpiarTextoLog(ultimoBuffer)}'");
                }

                bool esModoContinuo = string.Equals(protocoloNormalizado, "Continuo", StringComparison.Ordinal);
                string? comando = ObtenerComandoPeticion(protocoloNormalizado);

                var res = await EjecutarLecturaUnicaAsync(
                    port,
                    comando,
                    esModoContinuo,
                    newLineConfigurado,
                    TimeoutLecturaMs,
                    cancellationToken).ConfigureAwait(false);

                if (res.Exito)
                {
                    return (true, res.Peso, string.Empty);
                }

                return (false, 0m, string.IsNullOrWhiteSpace(res.BufferRecibido)
                    ? "La bascula no respondio en el tiempo limite."
                    : $"Trama incompleta o no valida de la bascula: '{LimpiarTextoLog(res.BufferRecibido)}'");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is TimeoutException)
            {
                // Si se desconecto el cable USB o el adaptador CH340/PL2303 fallo, liberamos el puerto de inmediato
                CerrarPuertoInterno();
                PosLogger.Warn("BasculaService.LeerPesoDesdePuertoAsync", $"Puerto {puerto} reiniciado tras fallo serial: {ex.Message}");
                return (false, 0m, $"Error de comunicacion con la bascula ({puerto}): {ex.Message}");
            }
            catch (Exception ex)
            {
                CerrarPuertoInterno();
                PosLogger.Error("BasculaService.LeerPesoDesdePuertoAsync", ex, $"Puerto={puerto}");
                return (false, 0m, ex.Message);
            }
        }

        private static async Task<(bool Exito, decimal Peso, string BufferRecibido)> EjecutarLecturaUnicaAsync(
            SerialPort port,
            string? comandoPeticion,
            bool descartarPrimeraTramaIncompleta,
            string newLineConfigurado,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            if (!string.IsNullOrEmpty(comandoPeticion))
            {
                port.Write(comandoPeticion);
            }

            var acumulador = new StringBuilder(128);
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fragmento = port.ReadExisting();
                if (!string.IsNullOrEmpty(fragmento))
                {
                    acumulador.Append(fragmento);

                    if (IntentarExtraerUltimaTramaCompletaValida(
                            acumulador.ToString(),
                            descartarPrimeraTramaIncompleta,
                            newLineConfigurado,
                            out decimal pesoDetectado))
                    {
                        // Breve ventana de estabilizacion por si queda un '\n' o segunda trama en transito
                        if (port.BytesToRead > 0)
                        {
                            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                            string extra = port.ReadExisting();
                            if (!string.IsNullOrEmpty(extra))
                            {
                                acumulador.Append(extra);
                                if (IntentarExtraerUltimaTramaCompletaValida(
                                        acumulador.ToString(),
                                        descartarPrimeraTramaIncompleta,
                                        newLineConfigurado,
                                        out decimal pesoMasReciente))
                                {
                                    pesoDetectado = pesoMasReciente;
                                }
                            }
                        }

                        return (true, pesoDetectado, acumulador.ToString());
                    }
                }

                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }

            string recibidoTotal = acumulador.ToString();
            if (IntentarExtraerUltimaTramaCompletaValida(recibidoTotal, descartarPrimeraTramaIncompleta, newLineConfigurado, out var pesoFinal))
            {
                return (true, pesoFinal, recibidoTotal);
            }

            return (false, 0m, recibidoTotal);
        }

        public static bool IntentarExtraerUltimaTramaCompletaValida(
            string bufferCrudo,
            bool descartarPrimeraTramaIncompleta,
            string? terminadorConfigurado,
            out decimal peso)
        {
            peso = 0m;
            if (string.IsNullOrEmpty(bufferCrudo)) return false;

            // Normalizamos el terminador configurado y soportamos \r\n, \r y \n automaticamente
            string buffer = bufferCrudo;
            if (!string.IsNullOrEmpty(terminadorConfigurado) &&
                terminadorConfigurado != "\r\n" &&
                terminadorConfigurado != "\r" &&
                terminadorConfigurado != "\n")
            {
                buffer = buffer.Replace(terminadorConfigurado, "\n", StringComparison.Ordinal);
            }

            buffer = buffer.Replace("\r\n", "\n", StringComparison.Ordinal)
                           .Replace('\r', '\n');

            // Si no existe al menos un terminador de fin de linea, cualquier dato en el bufer es una trama incompleta
            int ultimoIndiceTerminador = buffer.LastIndexOf('\n');
            if (ultimoIndiceTerminador < 0)
            {
                return false;
            }

            // Descartamos todo lo que este despues del ultimo terminador (fragmento final aun en transmision)
            string soloTramasCerradas = buffer.Substring(0, ultimoIndiceTerminador);
            string[] segmentos = soloTramasCerradas.Split('\n');

            // En modo continuo, el primer segmento antes del primer '\n' puede ser la mitad final de una trama previa
            int indiceMinimo = descartarPrimeraTramaIncompleta ? 1 : 0;
            if (segmentos.Length <= indiceMinimo)
            {
                return false;
            }

            // Recorremos desde la ultima trama completa hacia atras para tomar siempre la lectura mas reciente
            for (int i = segmentos.Length - 1; i >= indiceMinimo; i--)
            {
                string trama = segmentos[i].Trim();
                if (string.IsNullOrWhiteSpace(trama)) continue;

                if (TryParsePeso(trama, out decimal pesoTrama))
                {
                    peso = pesoTrama;
                    return true;
                }
            }

            return false;
        }

        public static bool TryParsePeso(string respuesta, out decimal peso)
        {
            peso = 0m;
            if (string.IsNullOrWhiteSpace(respuesta)) return false;

            // Exige al menos un digito entero antes del punto/coma decimal para jamas aceptar fragmentos partidos como ".500"
            var matches = Regex.Matches(respuesta, @"(?<![\d.,])([+-]?\s*\d+(?:[.,]\d{1,3})?)(?![\d.,])");
            if (matches.Count == 0) return false;

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                string token = Regex.Replace(matches[i].Value, @"\s+", string.Empty).Replace(',', '.');

                if (!decimal.TryParse(token, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal candidato))
                {
                    continue;
                }

                if (candidato < 0m)
                {
                    continue;
                }

                // Conversion automatica de unidad si la trama reporta gramos (g/gr/grs) o libras (lb/lbs)
                if (Regex.IsMatch(respuesta, @"(?<![a-zA-Z])(?:gr?s?|gramos?)(?![a-zA-Z])", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(respuesta, @"(?<![a-zA-Z])kgs?(?![a-zA-Z])", RegexOptions.IgnoreCase))
                {
                    candidato = Math.Round(candidato / 1000m, 3, MidpointRounding.AwayFromZero);
                }
                else if (Regex.IsMatch(respuesta, @"(?<![a-zA-Z])lbs?(?![a-zA-Z])", RegexOptions.IgnoreCase))
                {
                    candidato = Math.Round(candidato * 0.45359237m, 3, MidpointRounding.AwayFromZero);
                }

                peso = candidato;
                return true;
            }

            return false;
        }

        private static Parity ResolverParity(string? parityConfig)
        {
            if (string.IsNullOrWhiteSpace(parityConfig)) return Parity.None;
            return parityConfig.Trim().ToLowerInvariant() switch
            {
                "even" or "par" or "e" => Parity.Even,
                "odd" or "impar" or "o" => Parity.Odd,
                "mark" or "m" => Parity.Mark,
                "space" or "s" => Parity.Space,
                _ => Parity.None
            };
        }

        private static StopBits ResolverStopBits(string? stopBitsConfig)
        {
            if (string.IsNullOrWhiteSpace(stopBitsConfig)) return StopBits.One;
            return stopBitsConfig.Trim().ToLowerInvariant() switch
            {
                "2" or "two" => StopBits.Two,
                "1.5" or "onepointfive" => StopBits.OnePointFive,
                _ => StopBits.One
            };
        }

        private static string NormalizarNewLine(string? newLineConfig)
        {
            if (string.IsNullOrEmpty(newLineConfig)) return "\r\n";
            string limpio = newLineConfig
                .Replace("\\r\\n", "\r\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal);
            return string.IsNullOrEmpty(limpio) ? "\r\n" : limpio;
        }

        private static string LimpiarTextoLog(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return string.Empty;
            string limpio = texto.Replace("\r", "\\r").Replace("\n", "\\n");
            return limpio.Length > 60 ? limpio.Substring(0, 60) + "..." : limpio;
        }
    }
}