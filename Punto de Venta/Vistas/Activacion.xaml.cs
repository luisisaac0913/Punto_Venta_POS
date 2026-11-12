using Newtonsoft.Json.Linq;
using Punto_de_Venta.Servicios;
using Punto_de_Venta.Vistas;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Punto_de_Venta
{
    public partial class Activacion : UserControl
    {
        private const string AzureFunctionVerificarBaseUrl = "https://chipandchapa-d5bsach3emeka6h0.mexicocentral-01.azurewebsites.net/api/verificar/";
        private const string AzureFunctionGenerarPagoUrl = "https://chipandchapa-d5bsach3emeka6h0.mexicocentral-01.azurewebsites.net/api/crear-preferencia";
        private static readonly HttpClient _httpClient = CrearHttpClientSeguro(15);

        private static HttpClient CrearHttpClientSeguro(int timeoutSegundos)
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                Proxy = null,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
                }
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSegundos) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) POS/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        }

        private static HttpClient CrearHttpClientFallback(int timeoutSegundos)
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                Proxy = null,
                ServerCertificateCustomValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSegundos) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) POS/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        }

        private string _hwid = string.Empty;
        private string _initPointUrl = string.Empty;
        private CancellationTokenSource? _pollingCts;
        private bool _estaPollingActivo = false;
        private bool _animacionAcreditadoFinalizada = false;
        private bool _activacionEnProceso = false;
        private bool _videoYaReproducido = false;

        public Activacion()
        {
            InitializeComponent();
            Focusable = true;
            _hwid = LicenciaService.ObtenerIdentificadorHardware();
            TxtHwidInactivo.Text = _hwid;
            TxtHwidActivo.Text = _hwid;

            Loaded += Activacion_Loaded;
            Unloaded += Activacion_Unloaded;
            PreviewKeyDown += Activacion_PreviewKeyDown;
        }

        private void Activacion_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DetenerPolling();
                var mainWindow = Window.GetWindow(this) as MainWindow ?? Application.Current.MainWindow as MainWindow;
                mainWindow?.RegresarAlMenu();
                e.Handled = true;
            }
        }

        private async void Activacion_Loaded(object sender, RoutedEventArgs e)
        {
            Focus();
            Keyboard.Focus(this);
            ActualizarEstadoVisual();

            try
            {
                if (!string.IsNullOrWhiteSpace(_hwid))
                {
                    bool eraPremium = LicenciaService.EsPremium();
                    bool? estadoNube = await LicenciaService.VerificarEnNubeAsync().ConfigureAwait(true);

                    if (estadoNube == true && !eraPremium)
                    {
                        CompletarActivacionExitosa();
                    }
                    else if (estadoNube == false)
                    {
                        ActualizarEstadoVisual();
                        var mainWindow = Window.GetWindow(this) as MainWindow ?? Application.Current.MainWindow as MainWindow;
                        mainWindow?.ActualizarEstadoLicenciaUI();
                    }
                }
            }
            catch
            {
            }
        }

        private void Activacion_Unloaded(object sender, RoutedEventArgs e)
        {
            DetenerPolling();
        }

        public void ActualizarEstadoVisual()
        {
            bool esPremium = LicenciaService.EsPremium();

            if (esPremium)
            {
                PanelLicenciaActiva.Visibility = Visibility.Visible;
                PanelLicenciaInactiva.Visibility = Visibility.Collapsed;
            }
            else
            {
                PanelLicenciaActiva.Visibility = Visibility.Collapsed;
                PanelLicenciaInactiva.Visibility = Visibility.Visible;
            }
        }

        private async void BtnPagarMercadoPago_Click(object sender, RoutedEventArgs e)
        {
            BtnPagarMercadoPago.IsEnabled = false;

            try
            {
                if (string.IsNullOrWhiteSpace(_hwid))
                {
                    _hwid = LicenciaService.ObtenerIdentificadorHardware();
                }

                PanelPago.Visibility = Visibility.Collapsed;
                PanelEspera.Visibility = Visibility.Visible;
                TxtEstadoEspera.Text = "Verificando estatus previo en el servidor...";
                BtnReabrirLink.Visibility = Visibility.Collapsed;

                bool yaPagado = await ConsultarEstatusPagoAsync().ConfigureAwait(true);
                if (yaPagado)
                {
                    CompletarActivacionExitosa();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_initPointUrl))
                {
                    TxtEstadoEspera.Text = "Esperando confirmacion del pago en la nube...";
                    BtnReabrirLink.Visibility = Visibility.Visible;
                    AbrirEnlaceEnNavegador(_initPointUrl);
                    IniciarPollingVerificacion();
                    return;
                }

                TxtEstadoEspera.Text = "Generando enlace de pago seguro...";
                string jsonBody = $"{{\"hwid\":\"{_hwid}\"}}";
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                string responseString = string.Empty;
                string ultimoError = string.Empty;

                try
                {
                    using var response = await _httpClient.PostAsync(AzureFunctionGenerarPagoUrl, content).ConfigureAwait(true);
                    if (response.IsSuccessStatusCode)
                    {
                        responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                    }
                    else
                    {
                        string bodyError = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                        ultimoError = $"HTTP {(int)response.StatusCode}: {bodyError}";
                    }
                }
                catch (Exception ex)
                {
                    ultimoError = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
                }

                if (string.IsNullOrWhiteSpace(responseString))
                {
                    try
                    {
                        using var fallbackClient = CrearHttpClientFallback(15);
                        using var fallbackContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                        using var responseFallback = await fallbackClient.PostAsync(AzureFunctionGenerarPagoUrl, fallbackContent).ConfigureAwait(true);
                        if (responseFallback.IsSuccessStatusCode)
                        {
                            responseString = await responseFallback.Content.ReadAsStringAsync().ConfigureAwait(true);
                        }
                        else
                        {
                            string bodyError = await responseFallback.Content.ReadAsStringAsync().ConfigureAwait(true);
                            ultimoError = $"HTTP {(int)responseFallback.StatusCode}: {bodyError}";
                        }
                    }
                    catch (Exception ex)
                    {
                        if (string.IsNullOrWhiteSpace(ultimoError))
                        {
                            ultimoError = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(responseString))
                {
                    try
                    {
                        responseString = await EjecutarPostConCurlAsync(AzureFunctionGenerarPagoUrl, jsonBody).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        if (string.IsNullOrWhiteSpace(ultimoError))
                        {
                            ultimoError = ex.Message;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(responseString))
                {
                    string detalleFinal = string.IsNullOrWhiteSpace(ultimoError)
                        ? "El servidor no devolvio respuesta."
                        : ultimoError;
                    throw new InvalidOperationException($"El servidor no pudo generar el enlace de pago. {detalleFinal}");
                }

                var data = JObject.Parse(responseString);

                _initPointUrl = data["init_point"]?.ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(_initPointUrl))
                {
                    string mensajeServidor = data["error"]?.ToString() ?? data["message"]?.ToString() ?? responseString;
                    throw new InvalidOperationException($"La respuesta del servidor no contiene un enlace valido: {mensajeServidor}");
                }

                TxtEstadoEspera.Text = "Esperando confirmacion del pago en la nube...";
                BtnReabrirLink.Visibility = Visibility.Visible;

                AbrirEnlaceEnNavegador(_initPointUrl);
                IniciarPollingVerificacion();
            }
            catch (Exception ex)
            {
                string mensajeDetalle = ex.InnerException != null
                    ? $"{ex.Message} ({ex.InnerException.Message})"
                    : ex.Message;

                CustomMessageBox.Show(
                    $"Error al procesar la solicitud: {mensajeDetalle}",
                    "Fallo de Conexion",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                PanelEspera.Visibility = Visibility.Collapsed;
                PanelPago.Visibility = Visibility.Visible;
                BtnPagarMercadoPago.IsEnabled = true;
                BtnReabrirLink.Visibility = Visibility.Visible;
            }
        }

        private void AbrirEnlaceEnNavegador(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd",
                        Arguments = $"/c start \"\" \"{url}\"",
                        CreateNoWindow = true
                    });
                }
                catch
                {
                    try { Clipboard.SetText(url); } catch { }
                    ToastNotification.Show("Enlace de pago copiado al portapapeles. Pegalo en tu navegador.", ToastType.Info);
                }
            }
        }

        private async Task<bool> ConsultarEstatusPagoAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_hwid))
            {
                return false;
            }

            string urlVerificacion = AzureFunctionVerificarBaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(_hwid);
            string json = string.Empty;

            try
            {
                using var response = await _httpClient.GetAsync(urlVerificacion, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using var fallbackClient = CrearHttpClientFallback(12);
                    using var response = await fallbackClient.GetAsync(urlVerificacion, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    json = await EjecutarGetConCurlAsync(urlVerificacion).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(json))
            {
                return InterpretarRespuestaPagado(json);
            }

            return false;
        }

        private static async Task<string> EjecutarPostConCurlAsync(string url, string jsonBody)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "curl.exe",
                    Arguments = $"-s -k -X POST -H \"Content-Type: application/json\" -H \"Accept: application/json\" -d \"{jsonBody.Replace("\"", "\\\"")}\" \"{url}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return string.Empty;
                string output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await proc.WaitForExitAsync().ConfigureAwait(false);
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task<string> EjecutarGetConCurlAsync(string url)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "curl.exe",
                    Arguments = $"-s -k -H \"Accept: application/json\" \"{url}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return string.Empty;
                string output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await proc.WaitForExitAsync().ConfigureAwait(false);
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void IniciarPollingVerificacion()
        {
            DetenerPolling();
            _pollingCts = new CancellationTokenSource();
            var cancellationToken = _pollingCts.Token;

            _estaPollingActivo = true;

            Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested && _estaPollingActivo)
                {
                    try
                    {
                        await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        bool yaPago = await ConsultarEstatusPagoAsync(cancellationToken).ConfigureAwait(false);

                        if (yaPago)
                        {
                            _estaPollingActivo = false;
                            await Dispatcher.InvokeAsync(() =>
                            {
                                CompletarActivacionExitosa();
                            });
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PollingActivacion] Error en sondeo recurrente: {ex.Message}");
                    }
                }
            }, cancellationToken);
        }

        private void DetenerPolling()
        {
            _estaPollingActivo = false;
            if (_pollingCts != null)
            {
                try
                {
                    _pollingCts.Cancel();
                    _pollingCts.Dispose();
                }
                catch { }
                _pollingCts = null;
            }
        }

        private void CompletarActivacionExitosa()
        {
            if (_activacionEnProceso || _videoYaReproducido) return;
            _activacionEnProceso = true;

            DetenerPolling();
            PanelEspera.Visibility = Visibility.Collapsed;

            try
            {
                LicenciaService.ActivarLicencia();
                _animacionAcreditadoFinalizada = false;

                var rutaVideo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Imagenes", "P_Acreditado.mp4");
                if (File.Exists(rutaVideo))
                {
                    PanelLicenciaInactiva.Visibility = Visibility.Collapsed;
                    OverlayVideoAcreditado.Visibility = Visibility.Visible;
                    VideoAcreditado.Source = new Uri(rutaVideo);
                    VideoAcreditado.Position = TimeSpan.Zero;
                    VideoAcreditado.Play();
                }
                else
                {
                    FinalizarAnimacionAcreditado();
                }
            }
            catch (Exception ex)
            {
                _activacionEnProceso = false;
                CustomMessageBox.Show(
                    $"El pago fue confirmado pero ocurrio un error al registrar la licencia: {ex.Message}",
                    "Error al Guardar Licencia",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void FinalizarAnimacionAcreditado()
        {
            if (_animacionAcreditadoFinalizada) return;
            _animacionAcreditadoFinalizada = true;
            _videoYaReproducido = true;
            _activacionEnProceso = false;

            try
            {
                VideoAcreditado.Stop();
                VideoAcreditado.Close();
                VideoAcreditado.Source = null;
            }
            catch
            {
            }

            OverlayVideoAcreditado.Visibility = Visibility.Collapsed;
            ActualizarEstadoVisual();

            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.ActualizarEstadoLicenciaUI();

            ToastNotification.Show("Pago acreditado y licencia activada exitosamente", ToastType.Success);
        }

        private void VideoAcreditado_MediaEnded(object sender, RoutedEventArgs e)
        {
            FinalizarAnimacionAcreditado();
        }

        private void VideoAcreditado_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            FinalizarAnimacionAcreditado();
        }

        private void BtnSaltarVideo_Click(object sender, RoutedEventArgs e)
        {
            FinalizarAnimacionAcreditado();
        }

        private static bool InterpretarRespuestaPagado(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;

            try
            {
                var data = JObject.Parse(json);
                if (data["pagado"] != null)
                {
                    if (data["pagado"]!.Type == JTokenType.Boolean)
                    {
                        return data["pagado"]!.Value<bool>();
                    }
                    if (data["pagado"]!.Type == JTokenType.String)
                    {
                        string val = data["pagado"]!.Value<string>()?.Trim() ?? string.Empty;
                        return bool.TryParse(val, out bool b) ? b : string.Equals(val, "pagado", StringComparison.OrdinalIgnoreCase) || string.Equals(val, "approved", StringComparison.OrdinalIgnoreCase);
                    }
                    return data["pagado"]?.Value<bool>() ?? false;
                }

                if (data["estatus"] != null || data["status"] != null)
                {
                    string? st = (data["estatus"] ?? data["status"])?.ToString();
                    return string.Equals(st, "pagado", StringComparison.OrdinalIgnoreCase) || string.Equals(st, "approved", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
            }

            string jsonLimpio = json.ToLowerInvariant();
            return jsonLimpio.Contains("\"pagado\": true") ||
                   jsonLimpio.Contains("\"pagado\":true") ||
                   jsonLimpio.Contains("\"pagado\":\"true\"") ||
                   jsonLimpio.Contains("\"pagado\": \"true\"") ||
                   jsonLimpio.Contains("\"estatus\":\"pagado\"") ||
                   jsonLimpio.Contains("\"estatus\": \"pagado\"") ||
                   jsonLimpio.Contains("\"status\":\"approved\"") ||
                   jsonLimpio.Contains("\"status\": \"approved\"");
        }

        private void BtnReabrirLink_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_initPointUrl))
            {
                AbrirEnlaceEnNavegador(_initPointUrl);
            }
        }

        private void BtnCancelarEspera_Click(object sender, RoutedEventArgs e)
        {
            DetenerPolling();
            PanelEspera.Visibility = Visibility.Collapsed;
            PanelPago.Visibility = Visibility.Visible;
            BtnPagarMercadoPago.IsEnabled = true;
        }

        private void BtnCopiarHwid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_hwid);
                ToastNotification.Show("HWID copiado al portapapeles", ToastType.Success);
            }
            catch { }
        }

        private void BtnVolver_Click(object sender, RoutedEventArgs e)
        {
            DetenerPolling();
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.RegresarAlMenu();
        }
    }
}

