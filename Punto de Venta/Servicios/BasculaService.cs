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

        public static bool ProbarConexion(string puerto, int baudRate, out decimal pesoObtenido, out string mensajeError, string? protocolo = null)
        {
            pesoObtenido = 0m;
            mensajeError = string.Empty;

            var resultado = ProbarConexionAsync(puerto, baudRate, protocolo).GetAwaiter().GetResult();
            pesoObtenido = resultado.Peso;
            mensajeError = resultado.Error;
            return resultado.Exito;
        }

        public static async Task<(bool Exito, decimal Peso, string Error)> ProbarConexionAsync(
            string puerto,
            int baudRate,
            string? protocolo = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(puerto))
            {
                return (false, 0m, "No se ha seleccionado ningun puerto COM.");
            }

            var config = AppConfig.Load();
            string protoEfectivo = string.IsNullOrWhiteSpace(protocolo) ? config.BasculaProtocolo : protocolo;
            bool esModoContinuo = EsProtocoloContinuo(protoEfectivo);

            await _serialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LeerPesoDesdePuertoAsync(
                    puerto.Trim(),
                    baudRate > 0 ? baudRate : 9600,
                    config.BasculaDataBits,
                    config.BasculaParity,
                    config.BasculaStopBits,
                    config.BasculaNewLine,
                    esModoContinuo,
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
            bool esModoContinuo = EsProtocoloContinuo(config.BasculaProtocolo);

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
                    esModoContinuo,
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
            bool esModoContinuo,
            CancellationToken cancellationToken)
        {
            try
            {
                var port = ObtenerOAbrirPuertoPersistente(puerto, baudRate, dataBitsConfig, parityConfig, stopBitsConfig, newLineConfig);

                port.DiscardInBuffer();
                port.DiscardOutBuffer();

                if (!esModoContinuo)
                {
                    port.Write("P\r");
                }

                string newLineConfigurado = NormalizarNewLine(newLineConfig);
                var acumulador = new StringBuilder(128);
                var sw = Stopwatch.StartNew();

                while (sw.ElapsedMilliseconds < TimeoutLecturaMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string fragmento = port.ReadExisting();
                    if (!string.IsNullOrEmpty(fragmento))
                    {
                        acumulador.Append(fragmento);

                        if (IntentarExtraerUltimaTramaCompletaValida(
                                acumulador.ToString(),
                                esModoContinuo,
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
                                            esModoContinuo,
                                            newLineConfigurado,
                                            out decimal pesoMasReciente))
                                    {
                                        pesoDetectado = pesoMasReciente;
                                    }
                                }
                            }

                            return (true, pesoDetectado, string.Empty);
                        }
                    }

                    await Task.Delay(25, cancellationToken).ConfigureAwait(false);
                }

                string recibidoTotal = acumulador.ToString();
                if (IntentarExtraerUltimaTramaCompletaValida(recibidoTotal, esModoContinuo, newLineConfigurado, out var pesoFinal))
                {
                    return (true, pesoFinal, string.Empty);
                }

                return (false, 0m, string.IsNullOrWhiteSpace(recibidoTotal)
                    ? "La bascula no respondio en el tiempo limite."
                    : $"Trama incompleta o no valida de la bascula: '{LimpiarTextoLog(recibidoTotal)}'");
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