using Microsoft.Win32;
using Punto_de_Venta.Servicios;
using Punto_de_Venta.Vistas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Punto_de_Venta.Vistas;

namespace Punto_de_Venta
{
    public partial class Configuracion : UserControl, IManejadorEscape
    {
        #region Campos Privados
        private AppConfig _config = new();
        private bool _isTestingScale = false;
        private bool _isCargando = true;
        private bool _actualizandoIdioma = false;
        private string _selectedColorHex = "#007AFF";
        #endregion

        public Configuracion()
        {
            InitializeComponent();
            ActualizarIndicadoresPremiumUI();
            Loaded += Configuracion_Loaded;
        }

        public void ActualizarIndicadoresPremiumUI()
        {
            bool esPremium = LicenciaService.EsPremium();

            if (NavServidorLocalBtn != null)
            {
                string navSrv = IdiomaService.Obtener("SettingsNavServer", "🌐  Servidor Local");
                NavServidorLocalBtn.Content = esPremium ? navSrv : navSrv.Replace("🌐", "🔒");
            }
            if (NavWhatsAppBtn != null)
            {
                string navWa = IdiomaService.Obtener("SettingsNavWhatsApp", "📱  WhatsApp");
                NavWhatsAppBtn.Content = esPremium ? navWa : navWa.Replace("📱", "🔒");
            }
            if (NavIABtn != null)
            {
                NavIABtn.Content = esPremium ? "✨  Asistente IA" : "🔒  Asistente IA";
            }
            if (!esPremium && PanelIA != null && PanelIA.Visibility == Visibility.Visible)
            {
                MostrarPanel(PanelImpresion, true);
            }

            if (BusinessSubtitleBox != null)
            {
                BusinessSubtitleBox.IsEnabled = esPremium;
                BusinessSubtitleBox.ToolTip = esPremium ? null : "Personalizacion de ticket exclusiva de Version Premium";
            }
            if (BusinessAddressBox != null)
            {
                BusinessAddressBox.IsEnabled = esPremium;
                BusinessAddressBox.ToolTip = esPremium ? null : "Personalizacion de ticket exclusiva de Version Premium";
            }
            if (FooterMessageBox != null)
            {
                FooterMessageBox.IsEnabled = esPremium;
                FooterMessageBox.ToolTip = esPremium ? null : "Personalizacion de ticket exclusiva de Version Premium";
            }

            if (LblColorAcentoTitulo != null)
            {
                string tituloColor = IdiomaService.Obtener("SettingsAccentTitle", "Color de Acento");
                LblColorAcentoTitulo.Text = esPremium ? tituloColor : tituloColor + " 🔒";
            }

            if (LblColorAcentoDesc != null)
            {
                string descColor = IdiomaService.Obtener("SettingsAccentDesc", "Elige el color principal para los botones, selecciones y resaltados del sistema.");
                LblColorAcentoDesc.Text = esPremium ? descColor : descColor + " (Premium)";
            }

            if (LblPinAdminTitulo != null)
            {
                string tituloPin = IdiomaService.Obtener("SettingsSecurityPinTitle", "Solicitar PIN de administrador");
                LblPinAdminTitulo.Text = esPremium ? tituloPin : tituloPin + " 🔒";
            }

            if (LblPinAdminDesc != null)
            {
                string descPin = IdiomaService.Obtener("SettingsSecurityPinDesc", "Se pedira contrasena al entrar a todos los apartados excepto Cobro.");
                LblPinAdminDesc.Text = esPremium ? descPin : descPin + " (Premium)";
            }

            if (BtnRespaldoDrive != null)
            {
                string btnDrive = IdiomaService.Obtener("SettingsDriveUploadBtn", "☁️ Subir Respaldo a Google Drive");
                BtnRespaldoDrive.Content = esPremium ? btnDrive : btnDrive + " 🔒";
            }

            if (LblRespaldoAutoTitulo != null)
            {
                string tituloAuto = IdiomaService.Obtener("SettingsDriveAuto", "Respaldo automatico en segundo plano");
                LblRespaldoAutoTitulo.Text = esPremium ? tituloAuto : tituloAuto + " 🔒";
            }
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                ActualizarIndicadoresPremiumUI();
                NavImpresion_Click(this, new RoutedEventArgs());

                if (!_isCargando)
                {
                    CargarDatosFormulario();
                }

                Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    this.Focus();
                    Keyboard.Focus(this);
                }));
            }
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                this.Focus();
                Keyboard.Focus(this);
            }));
        }

        private async void Configuracion_Loaded(object sender, RoutedEventArgs e)
        {
            _isCargando = true;
            ActualizarIndicadoresPremiumUI();
            ChkRespaldoAuto.IsChecked = Properties.Settings.Default.RespaldoAutoActivo;
            ActualizarFeedbackUltimoRespaldo();
            CargarDatosFormulario();
            await CargarDispositivosAsync();
            CmbModoBascula.SelectedIndex = _config.BasculaModoPrecio ? 1 : 0;

            _isCargando = false;
        }

        private void CargarDatosFormulario()
        {
            _config = AppConfig.Load();
            WhatsAppTemplateBox.Text = _config.MensajeWhatsAppRecordatorio ?? string.Empty;

            _selectedColorHex = _config.ColorAcentoHex ?? "#007AFF";
            CambiarColorAcentoLocally(_selectedColorHex);

            foreach (ComboBoxItem item in PaperWidthComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.AnchoTicketCaracteres.ToString())
                {
                    PaperWidthComboBox.SelectedItem = item;
                    break;
                }
            }
            if (PaperWidthComboBox.SelectedItem == null && PaperWidthComboBox.Items.Count > 0)
                PaperWidthComboBox.SelectedIndex = 0;

            BusinessNameBox.Text = _config.NombreNegocio ?? string.Empty;
            if (!LicenciaService.EsPremium())
            {
                BusinessSubtitleBox.Text = string.Empty;
                BusinessAddressBox.Text = string.Empty;
                FooterMessageBox.Text = "GRACIAS POR SU COMPRA\nVuelva pronto";
            }
            else
            {
                BusinessSubtitleBox.Text = _config.GiroNegocio ?? string.Empty;
                BusinessAddressBox.Text = _config.DireccionNegocio ?? string.Empty;
                FooterMessageBox.Text = _config.MensajePie ?? string.Empty;
            }


            DarkModeCheckBox.IsChecked = _config.ModoOscuro;
            if (CmbIdioma != null)
            {
                _actualizandoIdioma = true;
                try
                {
                    string codigo = IdiomaService.NormalizarCodigo(_config.Idioma);
                    CmbIdioma.SelectedIndex = codigo == "en" ? 1 : 0;
                }
                finally
                {
                    _actualizandoIdioma = false;
                }
            }
            if (CmbModoMetricas != null)
            {
                string modo = string.Equals(_config.ModoVisualizacionMetricas, "Tablas", StringComparison.OrdinalIgnoreCase) ? "Tablas" : "Graficas";
                foreach (ComboBoxItem item in CmbModoMetricas.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), modo, StringComparison.OrdinalIgnoreCase))
                    {
                        CmbModoMetricas.SelectedItem = item;
                        break;
                    }
                }
            }
            AskBeforePrintCheckBox.IsChecked = _config.PreguntarAntesDeImprimir;
            AutoPrintCheckBox.IsChecked = _config.ImprimirTicketAutomatico;


            if (NoPrintOrSaveCheckBox != null)
                NoPrintOrSaveCheckBox.IsChecked = !_config.PreguntarAntesDeImprimir && !_config.ImprimirTicketAutomatico;

            BackupFolderBox.Text = _config.RutaRespaldos ?? string.Empty;
            RequirePinCheckBox.IsChecked = _config.SolicitarPinAdmin;
            if (CountCoinsIndividuallyCheckBox != null)
                CountCoinsIndividuallyCheckBox.IsChecked = _config.ContarMonedasIndividualmente;
            EnableScaleCheckBox.IsChecked = _config.BasculaHabilitada;
            if (PrintersComboBox.Items.Count > 1)
            {
                if (!string.IsNullOrEmpty(_config.ImpresoraSeleccionada) && PrintersComboBox.Items.Contains(_config.ImpresoraSeleccionada))
                    PrintersComboBox.SelectedItem = _config.ImpresoraSeleccionada;
                else
                    PrintersComboBox.SelectedIndex = 0;
            }
            if (ScalePortComboBox.Items.Count > 0 && ScalePortComboBox.Items.Contains(_config.BasculaPuerto))
            {
                ScalePortComboBox.SelectedItem = _config.BasculaPuerto;
            }

            foreach (ComboBoxItem item in ScaleBaudRateComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.BasculaBaudRate.ToString())
                {
                    ScaleBaudRateComboBox.SelectedItem = item;
                    break;
                }
            }
            if (ScaleBaudRateComboBox.SelectedItem == null && ScaleBaudRateComboBox.Items.Count > 0)
                ScaleBaudRateComboBox.SelectedIndex = 0;

            bool cfgEsContinuo = BasculaService.EsProtocoloContinuo(_config.BasculaProtocolo);
            foreach (ComboBoxItem item in ScaleProtocolComboBox.Items)
            {
                var tag = item.Tag?.ToString() ?? string.Empty;
                var content = item.Content?.ToString() ?? string.Empty;
                if ((cfgEsContinuo && string.Equals(tag, "Continuo", StringComparison.OrdinalIgnoreCase)) ||
                    (!cfgEsContinuo && string.Equals(tag, "PeticionP", StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(content, _config.BasculaProtocolo, StringComparison.OrdinalIgnoreCase) ||
                    content.StartsWith(_config.BasculaProtocolo ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    ScaleProtocolComboBox.SelectedItem = item;
                    break;
                }
            }
            if (ScaleProtocolComboBox.SelectedItem == null && ScaleProtocolComboBox.Items.Count > 0)
                ScaleProtocolComboBox.SelectedIndex = 0;

            ActualizarVistaPreviaTicket();

            StandbyCheckBox.IsChecked = _config.PantallaReposoHabilitada;
            if (GeminiApiKeyBox != null)
            {
                GeminiApiKeyBox.Password = _config.ObtenerGeminiApiKey();
            }
            if (GeminiModelCombo != null)
            {
                string modeloCfg = string.IsNullOrWhiteSpace(_config.GeminiModelo) ? "gemini-3.5-flash-lite" : _config.GeminiModelo;
                foreach (ComboBoxItem item in GeminiModelCombo.Items)
                {
                    if (string.Equals(item.Tag?.ToString(), modeloCfg, StringComparison.OrdinalIgnoreCase))
                    {
                        GeminiModelCombo.SelectedItem = item;
                        break;
                    }
                }
                if (GeminiModelCombo.SelectedItem == null && GeminiModelCombo.Items.Count > 0)
                    GeminiModelCombo.SelectedIndex = 0;
            }
            foreach (ComboBoxItem item in CmbTiempoReposo.Items)
            {
                if (item.Tag?.ToString() == _config.PantallaReposoMinutos.ToString())
                {
                    CmbTiempoReposo.SelectedItem = item;
                    break;
                }
            }
            if (CmbTiempoReposo.SelectedItem == null) CmbTiempoReposo.SelectedIndex = 2;

            SeleccionarPorTag(CmbMaxRespaldosLocales, _config.MaxRespaldosLocales.ToString(), 3);
            SeleccionarPorTag(CmbMaxRespaldosDrive, _config.MaxRespaldosDrive.ToString(), 3);
            SeleccionarPorTag(CmbIntervaloRespaldo, _config.IntervaloRespaldoMinutos.ToString(), 1);

            if (EnableLocalServerCheckBox != null)
            {
                EnableLocalServerCheckBox.IsChecked = _config.ServidorLocalHabilitado;
            }
            ActualizarEstadoServidorUI();
        }

        private static void SeleccionarPorTag(ComboBox? combo, string tagBuscado, int indiceDefault = 0)
        {
            if (combo == null) return;
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag?.ToString() == tagBuscado)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            if (combo.Items.Count > indiceDefault)
            {
                combo.SelectedIndex = indiceDefault;
            }
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Color de Acento");
                return;
            }

            if (sender is Button btn && btn.Tag is string hexColor)
            {
                _selectedColorHex = hexColor;
                CambiarColorAcentoLocally(hexColor);
            }
        }

        private void CambiarColorAcentoLocally(string codigoHexadecimal)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(codigoHexadecimal);
                var nuevaBrocha = new SolidColorBrush(color);
                nuevaBrocha.Freeze();
                this.Resources["ThemeAccent"] = nuevaBrocha;
            }
            catch { }
        }

        private void RevertirColorSiNoSeGuardo()
        {
            string colorGuardado = _config.ColorAcentoHex ?? "#007AFF";
            if (_selectedColorHex != colorGuardado)
            {
                CambiarColorAcentoLocally(colorGuardado);
            }
        }

        private async Task CargarDispositivosAsync()
        {
            PrintersComboBox.Items.Clear();
            PrintersComboBox.Items.Add("Cargando impresoras...");
            PrintersComboBox.SelectedIndex = 0;
            PrintersComboBox.IsEnabled = false;

            var listaImpresoras = await Task.Run(() =>
            {
                var resultado = new List<string>();
                try { foreach (string printer in PrinterSettings.InstalledPrinters) resultado.Add(printer); } catch { }
                return resultado;
            });

            PrintersComboBox.Items.Clear();
            PrintersComboBox.Items.Add("(Ninguna / Desactivada)");
            foreach (var printer in listaImpresoras) PrintersComboBox.Items.Add(printer);
            PrintersComboBox.IsEnabled = true;

            if (!string.IsNullOrEmpty(_config.ImpresoraSeleccionada) && PrintersComboBox.Items.Contains(_config.ImpresoraSeleccionada))
                PrintersComboBox.SelectedItem = _config.ImpresoraSeleccionada;
            else
                PrintersComboBox.SelectedIndex = 0;

            CargarPuertosSerie();
        }

        private void CargarPuertosSerie()
        {
            ScalePortComboBox.Items.Clear();
            var puertos = BasculaService.ObtenerPuertosDisponibles();

            foreach (var p in puertos) ScalePortComboBox.Items.Add(p);

            if (ScalePortComboBox.Items.Contains(_config.BasculaPuerto))
                ScalePortComboBox.SelectedItem = _config.BasculaPuerto;
            else if (ScalePortComboBox.Items.Count > 0)
                ScalePortComboBox.SelectedIndex = 0;
            else
            {
                ScalePortComboBox.Items.Add("COM1");
                ScalePortComboBox.SelectedIndex = 0;
            }
        }

        private void RefreshScalePorts_Click(object sender, RoutedEventArgs e)
        {
            CargarPuertosSerie();
            ToastNotification.Show("Puertos COM actualizados", ToastType.Info);
        }

        private async void TestScale_Click(object sender, RoutedEventArgs e)
        {
            if (_isTestingScale) return;

            var puerto = ScalePortComboBox.SelectedItem?.ToString() ?? "COM1";
            var baudRate = 9600;
            if (ScaleBaudRateComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var b))
                baudRate = b;

            var protocolo = (ScaleProtocolComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                ?? (ScaleProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? "PeticionP";

            _isTestingScale = true;
            ScaleTestResultText.Text = "Conectando y leyendo bascula...";
            ScaleTestResultText.Foreground = (Brush)FindResource("ThemeMutedForeground");

            var (exito, peso, error) = await Task.Run(() =>
            {
                bool ok = BasculaService.ProbarConexion(puerto, baudRate, out decimal p, out string err, protocolo);
                return (ok, p, err);
            });

            _isTestingScale = false;

            if (exito)
            {
                ScaleTestResultText.Text = $"✅ Lectura exitosa: {peso:0.000} kg";
                ScaleTestResultText.Foreground = (Brush)FindResource("SystemGreen");
                ToastNotification.Show($"Bascula conectada: {peso:0.000} kg", ToastType.Success);
            }
            else
            {
                ScaleTestResultText.Text = $"❌ Fallo: {error}";
                ScaleTestResultText.Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                ToastNotification.Show($"Error en bascula: {error}", ToastType.Error);
            }
        }

        private void NavImpresion_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelImpresion, true);
        private void NavPersonalizacion_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelPersonalizacion, false);
        private void NavBascula_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelBascula, false);
        private void NavServidorLocal_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Servidor Local");
                return;
            }
            MostrarPanel(PanelServidorLocal, false);
            ActualizarEstadoServidorUI();
        }
        private void NavSeguridad_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelSeguridad, false);
        private void NavAtajos_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelAtajos, false);
        private void NavWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Plantilla de WhatsApp");
                return;
            }
            MostrarPanel(PanelWhatsApp, false);
        }
        private void NavIA_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Asistente IA (Gemini)");
                return;
            }
            MostrarPanel(PanelIA, false);
        }
        private void NavAyuda_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelAyuda, false);

        private void MostrarPanel(StackPanel panel, bool verPreviewTicket)
        {
            PanelImpresion.Visibility = Visibility.Collapsed;
            PanelPersonalizacion.Visibility = Visibility.Collapsed;
            PanelBascula.Visibility = Visibility.Collapsed;
            PanelServidorLocal.Visibility = Visibility.Collapsed;
            PanelSeguridad.Visibility = Visibility.Collapsed;
            PanelAtajos.Visibility = Visibility.Collapsed;
            PanelWhatsApp.Visibility = Visibility.Collapsed;
            PanelIA.Visibility = Visibility.Collapsed;
            PanelAyuda.Visibility = Visibility.Collapsed;

            panel.Visibility = Visibility.Visible;
            PreviewColumn.Width = verPreviewTicket ? new GridLength(300) : new GridLength(0);
            PreviewCardBorder.Visibility = verPreviewTicket ? Visibility.Visible : Visibility.Collapsed;

            ActualizarBotonSidebar(NavImpresionBtn, panel == PanelImpresion);
            ActualizarBotonSidebar(NavPersonalizacionBtn, panel == PanelPersonalizacion);
            ActualizarBotonSidebar(NavBasculaBtn, panel == PanelBascula);
            ActualizarBotonSidebar(NavServidorLocalBtn, panel == PanelServidorLocal);
            ActualizarBotonSidebar(NavWhatsAppBtn, panel == PanelWhatsApp);
            ActualizarBotonSidebar(NavSeguridadBtn, panel == PanelSeguridad);
            ActualizarBotonSidebar(NavAtajosBtn, panel == PanelAtajos);
            ActualizarBotonSidebar(NavIABtn, panel == PanelIA);
            ActualizarBotonSidebar(NavAyudaBtn, panel == PanelAyuda);
        }

        private void BtnCopiarTelefono_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText("526633629837");
                ToastNotification.Show("Telefono copiado al portapapeles", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo copiar: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnAbrirWhatsAppAyuda_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://wa.me/526633629837",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo abrir WhatsApp: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnCopiarCorreo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText("chipandchapaoficial@gmail.com");
                ToastNotification.Show("Correo copiado al portapapeles", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo copiar: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnEnviarCorreo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "mailto:chipandchapaoficial@gmail.com",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo abrir el cliente de correo: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnCopiarWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText("chipandchapa.com");
                ToastNotification.Show("Sitio web copiado al portapapeles", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo copiar: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnAbrirWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://chipandchapa.com",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo abrir el navegador: {ex.Message}", ToastType.Error);
            }
        }

        private void ActualizarEstadoServidorUI()
        {
            if (TxtEstadoServidor == null) return;

            bool habilitado = _config?.ServidorLocalHabilitado ?? false;
            bool activo = App.EsServidorLocalActivo();

            if (habilitado && activo)
            {
                TxtEstadoServidor.Text = "Activo en segundo plano (Puerto 5001)";
                TxtEstadoServidor.Foreground = (Brush)FindResource("SystemGreen");
            }
            else if (habilitado)
            {
                TxtEstadoServidor.Text = IdiomaService.EsIngles()
                    ? "Starting service..."
                    : "Iniciando servicio...";
                TxtEstadoServidor.SetResourceReference(TextBlock.ForegroundProperty, "ThemeAccent");
            }
            else
            {
                TxtEstadoServidor.Text = IdiomaService.EsIngles()
                    ? "Inactive (Local server disabled)"
                    : "Inactivo (Servidor local deshabilitado)";
                TxtEstadoServidor.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMutedForeground");
            }
        }

        private void BtnExtraerIp_Click(object sender, RoutedEventArgs e)
        {
            string ip = ServidorLocal.ObtenerIpLocal();
            if (TxtDireccionIp != null)
            {
                TxtDireccionIp.Text = ip;
            }
            if (TxtUrlMovil != null)
            {
                TxtUrlMovil.Text = $"https://{ip}:5001";
            }
            ActualizarEstadoServidorUI();
            ToastNotification.Show($"IP local detectada: {ip}", ToastType.Success);
        }

        private void BtnCopiarUrlMovil_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = TxtUrlMovil?.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(url))
                {
                    Clipboard.SetText(url);
                    ToastNotification.Show("Enlace copiado al portapapeles", ToastType.Success);
                }
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo copiar: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnAbrirUrlMovil_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = TxtUrlMovil?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(url)) url = "https://localhost:5001";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo abrir el navegador: {ex.Message}", ToastType.Error);
            }
        }

        private static void ActualizarBotonSidebar(Button btn, bool activo)
        {
            if (btn == null) return;
            if (activo)
            {
                btn.SetResourceReference(Button.BackgroundProperty, "ThemeInputBackground");
                btn.SetResourceReference(Button.ForegroundProperty, "ThemeAccent");
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.SetResourceReference(Button.ForegroundProperty, "ThemeTextForeground");
            }
        }

        private void TicketField_TextChanged(object sender, TextChangedEventArgs e) => ActualizarVistaPreviaTicket();
        private void PaperWidthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ActualizarVistaPreviaTicket();
        private void CmbModoBascula_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _config == null || _isCargando) return;

            _config.BasculaModoPrecio = CmbModoBascula.SelectedIndex == 1;
            _config.Save();
        }

        private void ActualizarVistaPreviaTicket()
        {
            if (PreviewTicketText == null) return;

            int cols = 32;
            if (PaperWidthComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int parsedCols))
                cols = parsedCols;

            var tempConfig = new AppConfig
            {
                NombreNegocio = BusinessNameBox?.Text ?? "Miscelanea",
                GiroNegocio = BusinessSubtitleBox?.Text ?? string.Empty,
                DireccionNegocio = BusinessAddressBox?.Text ?? string.Empty,
                MensajePie = FooterMessageBox?.Text ?? string.Empty,
                AnchoTicketCaracteres = cols
            };

            PreviewTicketText.Text = TicketService.GenerarTicketMuestra(tempConfig);
        }

        private void BrowseBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Seleccionar carpeta para respaldos automaticos",
                    Multiselect = false
                };

                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                {
                    string rutaSeleccionada = dialog.FolderName;
                    if (!Directory.Exists(rutaSeleccionada)) Directory.CreateDirectory(rutaSeleccionada);

                    string pruebaArchivo = Path.Combine(rutaSeleccionada, ".permiso_test");
                    File.WriteAllText(pruebaArchivo, "ok");
                    File.Delete(pruebaArchivo);

                    BackupFolderBox.Text = rutaSeleccionada;
                    ToastNotification.Show("Carpeta de respaldos configurada correctamente", ToastType.Success);
                }
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al seleccionar carpeta: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnRespaldarAhora_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string ruta = BackupFolderBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(ruta) || !Directory.Exists(ruta))
                {
                    ToastNotification.Show("Por favor, selecciona primero una carpeta de destino en 'Examinar...'", ToastType.Warning);
                    return;
                }

                bool generado = BackupService.GenerarRespaldo("Manual", ruta);
                if (generado)
                {
                    ToastNotification.Show("Respaldo manual generado exitosamente", ToastType.Success);
                }
                else
                {
                    ToastNotification.Show("No se pudo generar el respaldo manual. Verifica la ruta o permisos.", ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al intentar generar el respaldo: {ex.Message}", ToastType.Error);
            }
        }

        private void CmbIdioma_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var printerSel = PrintersComboBox.SelectedItem?.ToString() ?? string.Empty;
                _config.ImpresoraSeleccionada = (printerSel.StartsWith("(") || printerSel.StartsWith("Cargando")) ? string.Empty : printerSel;

                if (PaperWidthComboBox.SelectedItem is ComboBoxItem wItem && int.TryParse(wItem.Tag?.ToString(), out var width))
                    _config.AnchoTicketCaracteres = width;

                _config.NombreNegocio = BusinessNameBox.Text.Trim();
                if (!LicenciaService.EsPremium())
                {
                    _config.GiroNegocio = string.Empty;
                    _config.DireccionNegocio = string.Empty;
                    _config.MensajePie = "GRACIAS POR SU COMPRA\nVuelva pronto";
                    _config.ColorAcentoHex = "#007AFF";
                    _config.SolicitarPinAdmin = false;
                }
                else
                {
                    _config.GiroNegocio = BusinessSubtitleBox.Text.Trim();
                    _config.DireccionNegocio = BusinessAddressBox.Text.Trim();
                    _config.MensajePie = FooterMessageBox.Text.Trim();
                    _config.ColorAcentoHex = _selectedColorHex;
                    _config.SolicitarPinAdmin = RequirePinCheckBox.IsChecked == true;
                }

                var modoOscuroSeleccionado = DarkModeCheckBox.IsChecked == true;
                _config.ModoOscuro = modoOscuroSeleccionado;
                if (CmbIdioma?.SelectedItem is ComboBoxItem langItem && langItem.Tag is string langCode)
                {
                    _config.Idioma = IdiomaService.NormalizarCodigo(langCode);
                    IdiomaService.AplicarIdioma(_config.Idioma);
                    ActualizarIndicadoresPremiumUI();
                    ActualizarVistaPreviaTicket();
                }

                if (CmbModoMetricas?.SelectedItem is ComboBoxItem itemMetricas && itemMetricas.Tag != null)
                {
                    _config.ModoVisualizacionMetricas = itemMetricas.Tag.ToString() ?? "Graficas";
                }

                _config.BasculaHabilitada = EnableScaleCheckBox.IsChecked == true;
                _config.BasculaPuerto = ScalePortComboBox.SelectedItem?.ToString() ?? "COM1";
                if (ScaleBaudRateComboBox.SelectedItem is ComboBoxItem baudItem && int.TryParse(baudItem.Tag?.ToString(), out var baud))
                    _config.BasculaBaudRate = baud;
                _config.BasculaProtocolo = (ScaleProtocolComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                    ?? (ScaleProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                    ?? "PeticionP";

                if (CountCoinsIndividuallyCheckBox != null)
                    _config.ContarMonedasIndividualmente = CountCoinsIndividuallyCheckBox.IsChecked == true;


                bool noPrintSave = NoPrintOrSaveCheckBox?.IsChecked == true;
                _config.PreguntarAntesDeImprimir = !noPrintSave && (AskBeforePrintCheckBox.IsChecked == true);
                _config.ImprimirTicketAutomatico = !noPrintSave && (AutoPrintCheckBox.IsChecked == true);

                _config.RutaRespaldos = BackupFolderBox.Text.Trim();

                if (!string.IsNullOrWhiteSpace(NewPinBox.Password))
                {
                    if (!LicenciaService.EsPremium())
                    {
                        LicenciaService.MostrarMensajePremium("PIN de Seguridad");
                        NewPinBox.Clear();
                        return;
                    }

                    var nuevoPin = NewPinBox.Password.Trim();
                    if (nuevoPin.Length < 4)
                    {
                        ToastNotification.Show("El PIN debe tener al menos 4 digitos numericos", ToastType.Warning);
                        NewPinBox.Focus();
                        return;
                    }
                    _config.EstablecerNuevoPin(nuevoPin);
                    NewPinBox.Clear();
                }

                _config.PantallaReposoHabilitada = StandbyCheckBox.IsChecked == true;
                if (CmbTiempoReposo.SelectedItem is ComboBoxItem tiempoItem && int.TryParse(tiempoItem.Tag?.ToString(), out var minutos))
                {
                    _config.PantallaReposoMinutos = minutos;
                }

                if (CmbMaxRespaldosLocales?.SelectedItem is ComboBoxItem itemMaxLocales && int.TryParse(itemMaxLocales.Tag?.ToString(), out var maxLocales))
                {
                    _config.MaxRespaldosLocales = maxLocales;
                }
                if (CmbMaxRespaldosDrive?.SelectedItem is ComboBoxItem itemMaxDrive && int.TryParse(itemMaxDrive.Tag?.ToString(), out var maxDrive))
                {
                    _config.MaxRespaldosDrive = maxDrive;
                }
                if (CmbIntervaloRespaldo?.SelectedItem is ComboBoxItem itemIntervalo && int.TryParse(itemIntervalo.Tag?.ToString(), out var intervaloMin))
                {
                    _config.IntervaloRespaldoMinutos = intervaloMin;
                }

                _config.MensajeWhatsAppRecordatorio = WhatsAppTemplateBox.Text.Trim();
                bool habilitarSv = LicenciaService.EsPremium() && (EnableLocalServerCheckBox?.IsChecked == true);
                _config.ServidorLocalHabilitado = habilitarSv;
                _config.Save();

                if (habilitarSv)
                {
                    _ = Task.Run(async () =>
                    {
                        await App.IniciarServidorLocalAsync();
                        Dispatcher.Invoke(ActualizarEstadoServidorUI);
                    });
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        await App.DetenerServidorLocalAsync();
                        Dispatcher.Invoke(ActualizarEstadoServidorUI);
                    });
                }
                ActualizarEstadoServidorUI();

                if (LicenciaService.EsPremium())
                {
                    if (GeminiApiKeyBox != null)
                    {
                        _config.GuardarGeminiApiKey(GeminiApiKeyBox.Password);
                    }
                    if (GeminiModelCombo?.SelectedItem is ComboBoxItem gModel && gModel.Tag != null)
                    {
                        _config.GeminiModelo = gModel.Tag.ToString() ?? "gemini-3.5-flash-lite";
                    }
                }
                _config.Save();

                MainWindow.ReiniciarTemporizadorRespaldo();
                try
                {
                    var carpetaRespaldos = BackupService.ObtenerCarpetaRespaldos(_config.RutaRespaldos);
                    BackupService.LimpiarRespaldosAntiguos(carpetaRespaldos);
                }
                catch
                {
                }

                if (Application.Current is App app)
                {
                    app.SetDarkMode(modoOscuroSeleccionado);
                    App.CambiarColorAcento(_selectedColorHex);
                }

                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ActualizarNombreNegocioEnUI(_config);
                    mainWindow.ActualizarConfiguracionReposo(_config);
                }
                ToastNotification.Show("Configuracion guardada exitosamente", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al guardar configuracion: {ex.Message}", ToastType.Error);
            }
        }

        private void BtnObtenerApiKeyGemini_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://aistudio.google.com/apikey",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void BtnAbrirAsistenteIA_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Asistente IA (Gemini)");
                return;
            }
            if (GeminiApiKeyBox != null)
            {
                _config.GuardarGeminiApiKey(GeminiApiKeyBox.Password);
            }
            if (GeminiModelCombo?.SelectedItem is ComboBoxItem gModel && gModel.Tag != null)
            {
                _config.GeminiModelo = gModel.Tag.ToString() ?? "gemini-3.5-flash-lite";
            }
            _config.Save();

            var owner = Window.GetWindow(this);
            var modal = new AsistenteModal(owner);
            modal.ShowDialog();
        }

        public bool ManejarEscape()
        {
            RevertirColorSiNoSeGuardo();
            NewPinBox?.Clear();
            return false;
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                RevertirColorSiNoSeGuardo();
                NewPinBox.Clear();

                this.Visibility = Visibility.Collapsed;

                if (Window.GetWindow(this) is MainWindow mainWindow)
                {
                    mainWindow.RegresarAlMenu();
                    mainWindow.Focus();
                }
                e.Handled = true;
            }
        }
        private void RequirePinCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                RequirePinCheckBox.IsChecked = false;
                LicenciaService.MostrarMensajePremium("PIN de Seguridad");
            }
        }

        #region Google Drive
        private void ChkRespaldoAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium() && ChkRespaldoAuto.IsChecked == true)
            {
                ChkRespaldoAuto.IsChecked = false;
                LicenciaService.MostrarMensajePremium("Respaldo Automatico en Drive");
                return;
            }

            Properties.Settings.Default.RespaldoAutoActivo = ChkRespaldoAuto.IsChecked ?? false;
            Properties.Settings.Default.Save();
        }

        private async void BtnRespaldoDrive_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenciaService.EsPremium())
            {
                LicenciaService.MostrarMensajePremium("Respaldo en Google Drive");
                return;
            }

            BtnRespaldoDrive.IsEnabled = false;
            BtnRespaldoDrive.Content = "Subiendo respaldo...";
            PanelProgresoDrive.Visibility = Visibility.Visible;
            ProgressBarDrive.Value = 0;
            TxtProgresoDrive.Text = "Comprimiendo y conectando...";
            var progressReporter = new Progress<int>(porcentaje =>
            {
                ProgressBarDrive.Value = porcentaje;
                TxtProgresoDrive.Text = $"Subiendo a Drive: {porcentaje}%";
            });
            try
            {
                bool completado = await GoogleDriveBackupService.RespaldarEnDriveAsync(progressReporter);
                if (completado)
                {
                    TxtProgresoDrive.Text = "¡Carga completada al 100%!";
                    ActualizarFeedbackUltimoRespaldo();
                    ToastNotification.Show("Respaldo subido a Google Drive exitosamente", ToastType.Success);
                }
                else
                {
                    ToastNotification.Show("No se pudo completar la subida a Google Drive", ToastType.Error);
                }
            }
            catch (FileNotFoundException fnf)
            {
                CustomMessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                    fnf.Message,
                    "Credenciales de Google Drive requeridas",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Fallo al respaldar en Google Drive: {ex.Message}", ToastType.Error);
            }
            finally
            {
                BtnRespaldoDrive.IsEnabled = true;
                BtnRespaldoDrive.Content = IdiomaService.Obtener("SettingsDriveUploadBtn", "☁️ Subir Respaldo a Google Drive");
                await Task.Delay(2000);
                PanelProgresoDrive.Visibility = Visibility.Collapsed;
            }
        }

        private void ActualizarFeedbackUltimoRespaldo()
        {
            if (TxtUltimoRespaldoDrive == null) return;
            TxtUltimoRespaldoDrive.Text = GoogleDriveBackupService.ObtenerTextoUltimoRespaldo();
            var fecha = GoogleDriveBackupService.ObtenerUltimaFechaRespaldo();
            if (fecha.HasValue)
            {
                TxtUltimoRespaldoDrive.ToolTip = $"Fecha exacta: {fecha.Value:dd/MM/yyyy HH:mm:ss}";
            }
            else
            {
                TxtUltimoRespaldoDrive.ToolTip = "Sin respaldos registrados todavia";
            }

            if (CmbCopiasDisponibles != null && CmbCopiasDisponibles.Items.Count == 0)
            {
                var locales = BackupService.ListarRespaldosLocales(BackupFolderBox?.Text?.Trim());
                CmbCopiasDisponibles.ItemsSource = locales;
                if (locales.Count > 0) CmbCopiasDisponibles.SelectedIndex = 0;
            }
        }

        private async void BtnCargarCopias_Click(object sender, RoutedEventArgs e)
        {
            BtnCargarCopias.IsEnabled = false;
            BtnCargarCopias.Content = IdiomaService.Obtener("SettingsRestoreSearching", "Buscando...");
            try
            {
                var copias = BackupService.ListarRespaldosLocales(BackupFolderBox?.Text?.Trim());

                if (LicenciaService.EsPremium())
                {
                    try
                    {
                        var driveCopias = await GoogleDriveBackupService.ListarRespaldosDriveAsync();
                        copias.AddRange(driveCopias);
                    }
                    catch (Exception exDrive)
                    {
                        ToastNotification.Show(IdiomaService.ObtenerFormato("SettingsRestoreDriveWarn", "No se pudieron listar copias de Drive: {0}", exDrive.Message), ToastType.Warning);
                    }
                }

                var ordenadas = copias.OrderByDescending(c => c.Fecha).ToList();
                CmbCopiasDisponibles.ItemsSource = ordenadas;
                if (ordenadas.Count > 0)
                {
                    CmbCopiasDisponibles.SelectedIndex = 0;
                    ToastNotification.Show(IdiomaService.ObtenerFormato("SettingsRestoreFoundCount", "Se encontraron {0} copias de seguridad.", ordenadas.Count), ToastType.Success);
                }
                else
                {
                    ToastNotification.Show(IdiomaService.Obtener("SettingsRestoreNoneFound", "No se encontraron copias de seguridad disponibles."), ToastType.Info);
                }
            }
            catch (Exception ex)
            {
                ToastNotification.Show(IdiomaService.ObtenerFormato("SettingsRestoreSearchErr", "Error al buscar copias: {0}", ex.Message), ToastType.Error);
            }
            finally
            {
                BtnCargarCopias.IsEnabled = true;
                BtnCargarCopias.Content = IdiomaService.Obtener("SettingsRestoreSearchBtn", "Buscar Copias");
            }
        }

        private async void BtnRestaurarCopia_Click(object sender, RoutedEventArgs e)
        {
            if (CmbCopiasDisponibles.SelectedItem is not BackupService.RespaldoItem seleccion)
            {
                ToastNotification.Show(IdiomaService.Obtener("SettingsRestoreSelectFirst", "Selecciona primero una copia de seguridad de la lista."), ToastType.Warning);
                return;
            }

            BtnRestaurarCopia.IsEnabled = false;
            BtnRestaurarCopia.Content = IdiomaService.Obtener("SettingsRestoreWorking", "Restaurando...");
            string tempDescargado = string.Empty;

            try
            {
                string rutaZip = seleccion.RutaLocal;
                if (seleccion.Origen == "Google Drive")
                {
                    tempDescargado = await GoogleDriveBackupService.DescargarRespaldoDriveAsync(seleccion.DriveFileId);
                    rutaZip = tempDescargado;
                }

                var diff = await Task.Run(() => BackupService.AnalizarDiferenciasZip(rutaZip));

                string mensajeConfirm = IdiomaService.ObtenerFormato(
                    "SettingsRestoreConfirmMsg",
                    "Se restaurara la copia:\n{0}\n\nCambios en productos:\n- Se eliminaran: {1} productos actuales\n- Se agregaran: {2} productos del respaldo\n(Total actual: {3} -> Respaldo: {4})\n\nSe creara un respaldo preventivo antes de reemplazar los datos actuales. Deseas continuar?",
                    seleccion.DisplayTexto,
                    diff.ProductosAEliminar,
                    diff.ProductosAAgregar,
                    diff.ProductosActuales,
                    diff.ProductosEnRespaldo);

                var confirm = CustomMessageBox.Show(
                    Window.GetWindow(this) ?? Application.Current.MainWindow,
                    mensajeConfirm,
                    IdiomaService.Obtener("SettingsRestoreConfirmTitle", "Confirmar Restauracion"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes) return;

                await Task.Run(() => BackupService.RestaurarDesdeZip(rutaZip));

                ToastNotification.Show(IdiomaService.Obtener("SettingsRestoreSuccess", "Respaldo restaurado exitosamente. Reinicia la aplicacion para recargar todas las vistas."), ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show(IdiomaService.ObtenerFormato("SettingsRestoreError", "Error al restaurar respaldo: {0}", ex.Message), ToastType.Error);
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempDescargado) && File.Exists(tempDescargado))
                {
                    try { File.Delete(tempDescargado); } catch { }
                }
                BtnRestaurarCopia.IsEnabled = true;
                BtnRestaurarCopia.Content = IdiomaService.Obtener("SettingsRestoreConfirmBtn", "Restaurar Seleccionada");
            }
        }
        #endregion

        private void BtnGestionarLicencia_Click(object sender, RoutedEventArgs e)
        {
            (Window.GetWindow(this) as MainWindow)?.AbrirVista<Activacion>();
        }
    }
}
