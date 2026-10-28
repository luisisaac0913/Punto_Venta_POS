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
        /// <summary>
        /// 
        /// </summary>
        public static bool ProbarConexion(string puerto, int baudRate, out decimal pesoObtenido, out string mensajeError)
        {
            pesoObtenido = 0m;
            mensajeError = string.Empty;
            if (string.IsNullOrWhiteSpace(puerto))
            {
                mensajeError = "No se ha seleccionado ningun puerto COM.";
                return false;
            }
            SerialPort? port = null;
            try
            {
                port = new SerialPort(puerto, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 1200,
                    WriteTimeout = 1200
                };
                port.Open();
                port.DiscardInBuffer();
                port.Write("P\r");
                Thread.Sleep(220);
                var respuesta = port.ReadExisting();
                if (string.IsNullOrWhiteSpace(respuesta))
                {
                    try { respuesta = port.ReadLine(); } catch { }
                }
                port.Close();

                if (TryParsePeso(respuesta, out pesoObtenido))
                {
                    return true;
                }
                mensajeError = string.IsNullOrWhiteSpace(respuesta)
                    ? "La bascula no respondio en el tiempo limite. Ay Jaz"
                    : $"Respuesta no válida de la bascula: '{respuesta.Trim()}'";
                return false;
            }
            catch (Exception ex)
            {
                mensajeError = ex.Message;
                return false;
            }
            finally
            {
                if (port != null && port.IsOpen)
                {
                    port.Close();
                }
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public static async Task<(bool Exito, decimal Peso, string Error)> ObtenerPesoAsync()
        {
            var config = AppConfig.Load();

            if (!config.BasculaHabilitada)
            {
                return (false, 0m, "La bascula está desactivada en la configuracion.");
            }
            return await Task.Run(() =>
            {
                var puerto = config.BasculaPuerto;
                var baudRate = config.BasculaBaudRate;
                var esModoContinuo = config.BasculaProtocolo.Contains("Continuo", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(puerto))
                {
                    return (false, 0m, "No se ha configurado un puerto COM para la bascula.");
                }

                SerialPort? port = null;
                try
                {
                    port = new SerialPort(puerto, baudRate, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 1000,
                        WriteTimeout = 1000
                    };

                    port.Open();
                    port.DiscardInBuffer();

                    if (!esModoContinuo)
                    {
                        port.Write("P\r");
                    }

                    Thread.Sleep(200);

                    var respuesta = port.ReadExisting();
                    if (string.IsNullOrWhiteSpace(respuesta))
                    {
                        try { respuesta = port.ReadLine(); } catch { }
                    }

                    port.Close();

                    if (TryParsePeso(respuesta, out var peso))
                    {
                        return (true, peso, string.Empty);
                    }

                    return (false, 0m, string.IsNullOrWhiteSpace(respuesta)
                        ? "Sin respuesta de la báscula."
                        : $"Trama no reconocida: '{respuesta.Trim()}'.");
                }
                catch (Exception ex)
                {
                    return (false, 0m, ex.Message);
                }
                finally
                {
                    if (port != null && port.IsOpen)
                    {
                        port.Close();
                    }
                }
            });
        }
        public static bool TryParsePeso(string respuesta, out decimal peso)
        {
            peso = 0m;
            if (string.IsNullOrWhiteSpace(respuesta)) return false;
            var match = Regex.Match(respuesta, @"\b\d+\.\d{1,3}\b");
            if (match.Success)
            {
                return decimal.TryParse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out peso);
            }
            var matchEntero = Regex.Match(respuesta, @"\b\d+\b");
            if (matchEntero.Success)
            {
                return decimal.TryParse(matchEntero.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out peso);
            }
            return false;
        }
    }
}