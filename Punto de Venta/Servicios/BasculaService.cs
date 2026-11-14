using System;
using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Punto_de_Venta.Servicios
{
    public static class BasculaService
    {
        private static readonly SemaphoreSlim _serialLock = new(1, 1);

        public static string[] ObtenerPuertosDisponibles()
        {
            try
            {
                return SerialPort.GetPortNames();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static bool EsProtocoloContinuo(string? protocolo)
        {
            if (string.IsNullOrWhiteSpace(protocolo)) return false;
            return protocolo.Contains("Continuo", StringComparison.OrdinalIgnoreCase) ||
                   protocolo.Contains("Continuous", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ProbarConexion(string puerto, int baudRate, out decimal pesoObtenido, out string mensajeError, string? protocolo = null)
        {
            pesoObtenido = 0m;
            mensajeError = string.Empty;
            if (string.IsNullOrWhiteSpace(puerto))
            {
                mensajeError = "No se ha seleccionado ningun puerto COM.";
                return false;
            }

            string protoEfectivo = string.IsNullOrWhiteSpace(protocolo) ? AppConfig.Load().BasculaProtocolo : protocolo;
            bool esModoContinuo = EsProtocoloContinuo(protoEfectivo);

            _serialLock.Wait();
            try
            {
                var (exito, peso, error) = LeerPesoDesdePuerto(puerto, baudRate, esModoContinuo);
                pesoObtenido = peso;
                mensajeError = error;
                return exito;
            }
            finally
            {
                _serialLock.Release();
            }
        }

        public static async Task<(bool Exito, decimal Peso, string Error)> ObtenerPesoAsync()
        {
            var config = AppConfig.Load();

            if (!config.BasculaHabilitada)
            {
                return (false, 0m, "La bascula esta desactivada en la configuracion.");
            }
            await _serialLock.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    var puerto = config.BasculaPuerto;
                    var baudRate = config.BasculaBaudRate;
                    var esModoContinuo = EsProtocoloContinuo(config.BasculaProtocolo);

                    if (string.IsNullOrWhiteSpace(puerto))
                    {
                        return (false, 0m, "No se ha configurado un puerto COM para la bascula.");
                    }

                    return LeerPesoDesdePuerto(puerto, baudRate, esModoContinuo);
                });
            }
            finally
            {
                _serialLock.Release();
            }
        }

        private static (bool Exito, decimal Peso, string Error) LeerPesoDesdePuerto(string puerto, int baudRate, bool esModoContinuo)
        {
            try
            {
                using var port = new SerialPort(puerto, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 1200,
                    WriteTimeout = 1200
                };

                port.Open();
                port.DiscardInBuffer();

                string respuesta = string.Empty;
                if (esModoContinuo)
                {
                    try
                    {
                        _ = port.ReadLine();
                        respuesta = port.ReadLine();
                    }
                    catch
                    {
                        respuesta = port.ReadExisting();
                    }
                }
                else
                {
                    port.Write("P\r");
                    Thread.Sleep(220);
                    respuesta = port.ReadExisting();
                    if (string.IsNullOrWhiteSpace(respuesta))
                    {
                        try { respuesta = port.ReadLine(); } catch { }
                    }
                }

                if (TryParsePeso(respuesta, out var peso))
                {
                    return (true, peso, string.Empty);
                }

                return (false, 0m, string.IsNullOrWhiteSpace(respuesta)
                    ? "La bascula no respondio en el tiempo limite."
                    : $"Respuesta no valida de la bascula: '{respuesta.Trim()}'");
            }
            catch (Exception ex)
            {
                return (false, 0m, ex.Message);
            }
        }

        public static bool TryParsePeso(string respuesta, out decimal peso)
        {
            peso = 0m;
            if (string.IsNullOrWhiteSpace(respuesta)) return false;

            var match = Regex.Match(respuesta, @"(?<![\d.,])([+-]?\s*(?:\d+[.,]\d{1,3}|[.,]\d{1,3}|\d+))(?![\d.,])");
            if (!match.Success) return false;

            string token = Regex.Replace(match.Value, @"\s+", string.Empty).Replace(',', '.');
            if (token.StartsWith(".")) token = "0" + token;
            else if (token.StartsWith("-.")) token = "-0" + token[1..];
            else if (token.StartsWith("+.")) token = "0" + token[1..];

            if (!decimal.TryParse(token, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out peso))
            {
                return false;
            }
            if (peso < 0m)
            {
                peso = 0m;
                return false;
            }
            return true;
        }
    }
}