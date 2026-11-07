using Newtonsoft.Json.Linq;
using Punto_de_Venta.Servicios;
using Punto_de_Venta.Vistas;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Punto_de_Venta
{
    public partial class Activacion : UserControl
    {
        private const string AzureFunctionVerificarBaseUrl = "https://chipandchapa-d5bsach3emeka6h0.mexicocentral-01.azurewebsites.net/api/verificar/";
        private const string AzureFunctionGenerarPagoUrl = "https://chipandchapa-d5bsach3emeka6h0.mexicocentral-01.azurewebsites.net/api/crear-preferencia";
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private string _hwid = string.Empty;
        private string _initPointUrl = string.Empty;
        private CancellationTokenSource? _pollingCts;
        private bool _estaPollingActivo = false;

        public Activacion()
        {
            InitializeComponent();
            _hwid = LicenciaService.ObtenerIdentificadorHardware();
            TxtHwidInactivo.Text = _hwid;
            TxtHwidActivo.Text = _hwid;

            Loaded += Activacion_Loaded;
            Unloaded += Activacion_Unloaded;
        }

        private async void Activacion_Loaded(object sender, RoutedEventArgs e)
        {
            ActualizarEstadoVisual();

            try
            {
                if (!LicenciaService.EsPremium() && !string.IsNullOrWhiteSpace(_hwid))
                {
                    bool yaPago = await ConsultarEstatusPagoAsync().ConfigureAwait(true);
                    if (yaPago)
                    {
                        CompletarActivacionExitosa();
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
                TxtEstadoHeader.Text = "Licencia Activa (Premium)";
                TxtEstadoHeader.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                BadgeEstadoHeader.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 16, 185, 129));
                BadgeEstadoHeader.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 16, 185, 129));
            }
            else
            {
                PanelLicenciaActiva.Visibility = Visibility.Collapsed;
                PanelLicenciaInactiva.Visibility = Visibility.Visible;
                TxtEstadoHeader.Text = "Version Gratuita";
                TxtEstadoHeader.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
                BadgeEstadoHeader.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 245, 158, 11));
                BadgeEstadoHeader.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 245, 158, 11));
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

                var response = await _httpClient.PostAsync(AzureFunctionGenerarPagoUrl, content).ConfigureAwait(true);

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("El servidor no pudo generar el enlace de pago.");
                }

                string responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                var data = JObject.Parse(responseString);

                _initPointUrl = data["init_point"]?.ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(_initPointUrl))
                {
                    throw new InvalidOperationException("La respuesta del servidor no contiene un enlace valido.");
                }

                TxtEstadoEspera.Text = "Esperando confirmacion del pago en la nube...";
                BtnReabrirLink.Visibility = Visibility.Visible;

                AbrirEnlaceEnNavegador(_initPointUrl);
                IniciarPollingVerificacion();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    $"Error al procesar la solicitud: {ex.Message}",
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
            catch (Exception ex)
            {
                Debug.WriteLine($"[Activacion] No se pudo abrir el navegador: {ex.Message}");
            }
        }

        private async Task<bool> ConsultarEstatusPagoAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_hwid))
            {
                return false;
            }

            try
            {
                string urlVerificacion = AzureFunctionVerificarBaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(_hwid);
                using var response = await _httpClient.GetAsync(urlVerificacion, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return InterpretarRespuestaPagado(json);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PollingActivacion] Error consultando estatus: {ex.Message}");
            }

            return false;
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
            DetenerPolling();
            PanelEspera.Visibility = Visibility.Collapsed;

            try
            {
                LicenciaService.ActivarLicencia();

                CustomMessageBox.Show(
                    "Pago confirmado con exito. El equipo ha sido registrado y la licencia ha sido activada permanentemente.",
                    "Activacion Exitosa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ActualizarEstadoVisual();

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.ActualizarEstadoLicenciaUI();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    $"El pago fue confirmado pero ocurrio un error al registrar la licencia: {ex.Message}",
                    "Error al Guardar Licencia",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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

