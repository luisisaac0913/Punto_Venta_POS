using Microsoft.Win32;
using Punto_de_Venta.Servicios;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Punto_de_Venta
{
    public partial class Configuracion : UserControl
    {
        #region Campos Privados
        private AppConfig _config = new();
        private bool _isTestingScale = false;
        private bool _isCargando = true;
        private string _selectedColorHex = "#007AFF";
        #endregion

        public Configuracion()
        {
            InitializeComponent();
            Loaded += Configuracion_Loaded;
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility == Visibility.Visible)
            {
                NavImpresion_Click(this, new RoutedEventArgs());

                if (!_isCargando)
                {
                    CargarDatosFormulario();
                }

                // Rescate de foco inicial al abrir la ventana usando el patrón agresivo del Dispatcher
                Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    this.Focus();
                    Keyboard.Focus(this);
                }));
            }
        }

        // Rescate de foco al hacer clic en el fondo vacío de Configuración
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
            BusinessSubtitleBox.Text = _config.GiroNegocio ?? string.Empty;
            BusinessAddressBox.Text = _config.DireccionNegocio ?? string.Empty;
            FooterMessageBox.Text = _config.MensajePie ?? string.Empty;

            DarkModeCheckBox.IsChecked = _config.ModoOscuro;
            AskBeforePrintCheckBox.IsChecked = _config.PreguntarAntesDeImprimir;
            AutoPrintCheckBox.IsChecked = _config.ImprimirTicketAutomatico;

            if (NoPrintOrSaveCheckBox != null)
                NoPrintOrSaveCheckBox.IsChecked = !_config.PreguntarAntesDeImprimir && !_config.ImprimirTicketAutomatico;

            BackupFolderBox.Text = _config.RutaRespaldos ?? string.Empty;
            RequirePinCheckBox.IsChecked = _config.SolicitarPinAdmin;
            EnableScaleCheckBox.IsChecked = _config.BasculaHabilitada;

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

            foreach (ComboBoxItem item in ScaleProtocolComboBox.Items)
            {
                var content = item.Content?.ToString() ?? string.Empty;
                if (string.Equals(content, _config.BasculaProtocolo, StringComparison.OrdinalIgnoreCase) ||
                    content.StartsWith(_config.BasculaProtocolo ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    ScaleProtocolComboBox.SelectedItem = item;
                    break;
                }
            }
            if (ScaleProtocolComboBox.SelectedItem == null && ScaleProtocolComboBox.Items.Count > 0)
                ScaleProtocolComboBox.SelectedIndex = 0;

            ActualizarVistaPreviaTicket();
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
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

            _isTestingScale = true;
            ScaleTestResultText.Text = "Conectando y leyendo báscula...";
            ScaleTestResultText.Foreground = (Brush)FindResource("ThemeMutedForeground");

            var (exito, peso, error) = await Task.Run(() =>
            {
                bool ok = BasculaService.ProbarConexion(puerto, baudRate, out decimal p, out string err);
                return (ok, p, err);
            });

            _isTestingScale = false;

            if (exito)
            {
                ScaleTestResultText.Text = $"✅ Lectura exitosa: {peso:0.000} kg";
                ScaleTestResultText.Foreground = (Brush)FindResource("SystemGreen");
                ToastNotification.Show($"Báscula conectada: {peso:0.000} kg", ToastType.Success);
            }
            else
            {
                ScaleTestResultText.Text = $"❌ Fallo: {error}";
                ScaleTestResultText.Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                ToastNotification.Show($"Error en báscula: {error}", ToastType.Error);
            }
        }

        private void NavImpresion_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelImpresion, true);
        private void NavPersonalizacion_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelPersonalizacion, false);
        private void NavBascula_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelBascula, false);
        private void NavSeguridad_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelSeguridad, false);
        private void NavAtajos_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelAtajos, false);
        private void NavWhatsApp_Click(object sender, RoutedEventArgs e) => MostrarPanel(PanelWhatsApp, false);

        private void MostrarPanel(StackPanel panel, bool verPreviewTicket)
        {
            PanelImpresion.Visibility = Visibility.Collapsed;
            PanelPersonalizacion.Visibility = Visibility.Collapsed;
            PanelBascula.Visibility = Visibility.Collapsed;
            PanelSeguridad.Visibility = Visibility.Collapsed;
            PanelAtajos.Visibility = Visibility.Collapsed;
            PanelWhatsApp.Visibility = Visibility.Collapsed;

            panel.Visibility = Visibility.Visible;
            PreviewColumn.Width = verPreviewTicket ? new GridLength(300) : new GridLength(0);
            PreviewCardBorder.Visibility = verPreviewTicket ? Visibility.Visible : Visibility.Collapsed;

            ActualizarBotonSidebar(NavImpresionBtn, panel == PanelImpresion);
            ActualizarBotonSidebar(NavPersonalizacionBtn, panel == PanelPersonalizacion);
            ActualizarBotonSidebar(NavBasculaBtn, panel == PanelBascula);
            ActualizarBotonSidebar(NavWhatsAppBtn, panel == PanelWhatsApp);
            ActualizarBotonSidebar(NavSeguridadBtn, panel == PanelSeguridad);
            ActualizarBotonSidebar(NavAtajosBtn, panel == PanelAtajos);
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
                    Title = "Seleccionar carpeta para respaldos automáticos",
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

        // NUEVA FUNCIÓN AÑADIDA: Respaldar Ahora (Manual)
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

                BackupService.GenerarRespaldo("Manual", ruta);
                ToastNotification.Show("Respaldo manual generado exitosamente", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al intentar generar el respaldo: {ex.Message}", ToastType.Error);
            }
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
                _config.GiroNegocio = BusinessSubtitleBox.Text.Trim();
                _config.DireccionNegocio = BusinessAddressBox.Text.Trim();
                _config.MensajePie = FooterMessageBox.Text.Trim();

                var modoOscuroSeleccionado = DarkModeCheckBox.IsChecked == true;
                _config.ModoOscuro = modoOscuroSeleccionado;
                _config.ColorAcentoHex = _selectedColorHex;
                _config.SolicitarPinAdmin = RequirePinCheckBox.IsChecked == true;

                _config.BasculaHabilitada = EnableScaleCheckBox.IsChecked == true;
                _config.BasculaPuerto = ScalePortComboBox.SelectedItem?.ToString() ?? "COM1";
                if (ScaleBaudRateComboBox.SelectedItem is ComboBoxItem baudItem && int.TryParse(baudItem.Tag?.ToString(), out var baud))
                    _config.BasculaBaudRate = baud;
                _config.BasculaProtocolo = (ScaleProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Torrey / Rhino (Petición P)";

                bool noPrintSave = NoPrintOrSaveCheckBox?.IsChecked == true;
                _config.PreguntarAntesDeImprimir = !noPrintSave && (AskBeforePrintCheckBox.IsChecked == true);
                _config.ImprimirTicketAutomatico = !noPrintSave && (AutoPrintCheckBox.IsChecked == true);

                _config.RutaRespaldos = BackupFolderBox.Text.Trim();

                if (!string.IsNullOrWhiteSpace(NewPinBox.Password))
                {
                    var nuevoPin = NewPinBox.Password.Trim();
                    if (nuevoPin.Length < 4)
                    {
                        ToastNotification.Show("El PIN debe tener al menos 4 dígitos numéricos", ToastType.Warning);
                        NewPinBox.Focus();
                        return;
                    }
                    _config.EstablecerNuevoPin(nuevoPin);
                    NewPinBox.Clear();
                }

                _config.MensajeWhatsAppRecordatorio = WhatsAppTemplateBox.Text.Trim();
                _config.Save();

                if (Application.Current is App app)
                {
                    app.SetDarkMode(modoOscuroSeleccionado);
                    App.CambiarColorAcento(_selectedColorHex);
                }

                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    if (mainWindow.FindName("TxtMiniLogo") is TextBlock miniLogo)
                        miniLogo.Text = _config.NombreNegocio;

                    if (mainWindow.FindName("TxtTituloDashboard") is TextBlock tituloDash)
                        tituloDash.Text = _config.NombreNegocio;
                }

                ToastNotification.Show("Configuración guardada exitosamente", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al guardar configuración: {ex.Message}", ToastType.Error);
            }
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
    }
}