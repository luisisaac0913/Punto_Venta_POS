using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public partial class Prestamos : UserControl
    {
        private readonly DispatcherTimer _clockTimer;
        private Deudor? _selectedDebtor;
        private bool _loadFailed;
        private bool _successStatusVisible;
        private Dictionary<string, decimal> _preciosCatalogo = new(StringComparer.OrdinalIgnoreCase);
        private string _currentFilter = "All"; // "All", "Pending", "Settled"

        public ObservableCollection<Deudor> Debtors { get; } = [];
        public ICollectionView DebtorsView { get; }

        public Prestamos()
        {
            InitializeComponent();
            Language = XmlLanguage.GetLanguage("es-MX");
            DebtorsView = CollectionViewSource.GetDefaultView(Debtors);
            DebtorsView.Filter = MatchesSearch;
            DataContext = this;

            CargarDeudoresDesdeBaseDatos();

            try
            {
                foreach (var prod in ProductoDB.Load())
                {
                    if (!string.IsNullOrWhiteSpace(prod.Nombre))
                    {
                        _preciosCatalogo[prod.Nombre.Trim()] = prod.Precio;
                    }
                }
            }
            catch { }

            UpdateDashboardKPIs();
            SetNextId();
            UpdateSelectionButtons();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
            _clockTimer.Start();

            Unloaded += (_, _) => _clockTimer.Stop();
        }

        private void CargarDeudoresDesdeBaseDatos()
        {
            try
            {
                Debtors.Clear();
                foreach (var debtor in PrestamoDB.Load())
                {
                    Debtors.Add(debtor);
                }
                _loadFailed = false;
            }
            catch (Exception exception)
            {
                _loadFailed = true;
                ToastNotification.Show($"Error al cargar la libreta: {exception.Message}", ToastType.Error);
            }
        }

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.Visibility == Visibility.Visible)
            {
                // ACTUALIZACIÓN DINÁMICA: Refresca la BD cada vez que entras a la pantalla
                CargarDeudoresDesdeBaseDatos();
                UpdateDashboardKPIs();
                SetNextId();
                DebtorsView.Refresh();

                this.Focus();
                FocusSearch();
                Keyboard.Focus(this);
            }
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            FocusSearch();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            FocusSearch();
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (HistorialOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    HistorialOverlay.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape)
            {
                this.Visibility = Visibility.Collapsed;
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RegresarAlMenu();
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && _successStatusVisible)
            {
                StatusText.Text = string.Empty;
                _successStatusVisible = false;
                e.Handled = true;
            }
        }

        #region Salto Secuencial de Campos con Enter
        private void DebtorField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            else if (sender == DebtorPhoneBox)
            {
                DebtorNameBox.Focus();
                DebtorNameBox.SelectAll();
            }
            else if (sender == DebtorNameBox)
            {
                ConceptBox.Focus();
                ConceptBox.SelectAll();
            }
            else if (sender == ConceptBox)
            {
                AmountBox.Focus();
                AmountBox.SelectAll();
            }
            else if (sender == AmountBox)
            {
                AddButton_Click(this, new RoutedEventArgs());
            }

            e.Handled = true;
        }

        private void PaymentAmountBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyPayment();
                e.Handled = true;
            }
        }
        #endregion

        #region Búsqueda y Filtros
        private void FocusSearch()
        {
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (DebtorPhoneBox != null)
                {
                    this.Focus();
                    DebtorPhoneBox.Focus();
                    Keyboard.Focus(DebtorPhoneBox);
                }
            }));
        }

        private void FilterDebtors_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string filterTag)
            {
                _currentFilter = filterTag;
                DebtorsView.Refresh();
            }
        }

        private bool MatchesSearch(object item)
        {
            if (item is not Deudor debtor) return false;

            if (_currentFilter == "Pending" && debtor.SaldoRestante <= 0) return false;
            if (_currentFilter == "Settled" && debtor.SaldoRestante > 0) return false;

            var query = SearchBox.Text.Trim();
            return query.Length == 0 ||
                   debtor.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   debtor.Nombre.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   debtor.Telefono.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   debtor.Concepto.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            DebtorsView.Refresh();
        }
        #endregion

        private void DebtorGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedDebtor = DebtorGrid.SelectedItem as Deudor;
            UpdateSelectionButtons();
            StatusText.Text = string.Empty;
            _successStatusVisible = false;

            if (_selectedDebtor is null)
            {
                ClearForm();
                SetNextId();
                return;
            }

            DebtorIdBox.Text = _selectedDebtor.Id.ToString(CultureInfo.InvariantCulture);
            DebtorNameBox.Text = _selectedDebtor.Nombre;
            DebtorPhoneBox.Text = _selectedDebtor.Telefono;
            ConceptBox.Text = _selectedDebtor.Concepto;
            AmountBox.Text = _selectedDebtor.MontoOriginal.ToString("0.00", CultureInfo.InvariantCulture);
            PaymentAmountBox.Clear();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || !TryReadLoanForm(out var name, out var concept, out var amount))
            {
                return;
            }

            var phone = DebtorPhoneBox.Text.Trim();

            var pendingDebtor = Debtors
                .Where(debtor => string.Equals(debtor.Nombre, name, StringComparison.OrdinalIgnoreCase) && debtor.SaldoRestante > 0)
                .OrderByDescending(debtor => debtor.Id)
                .FirstOrDefault();

            if (pendingDebtor is not null)
            {
                var updatedDebtor = CopyDebtor(pendingDebtor);
                updatedDebtor.MontoOriginal += amount;
                updatedDebtor.Concepto = CombineConcepts(updatedDebtor.Concepto, concept);
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    updatedDebtor.Telefono = phone;
                }

                var updatedDebtors = Debtors.Select(debtor => debtor.Id == pendingDebtor.Id ? updatedDebtor : debtor).ToList();
                if (!TrySave(updatedDebtors))
                {
                    return;
                }

                ReplaceDebtor(pendingDebtor, updatedDebtor);
                ClearSearchAndSelect(updatedDebtor);
                UpdateDashboardKPIs();
                ShowStatus("Monto sumado a la cuenta existente del cliente.", true);
                return;
            }

            int id;
            try
            {
                id = GetNextId();
            }
            catch (OverflowException)
            {
                ShowStatus("No se puede asignar un ID nuevo: se alcanzó el límite numérico.", false);
                return;
            }

            var newDebtor = new Deudor
            {
                Id = id,
                Nombre = name,
                Telefono = phone,
                Concepto = concept,
                MontoOriginal = amount,
                TotalAbonado = 0
            };

            if (!TrySave(Debtors.Append(newDebtor)))
            {
                return;
            }
            DebtorPhoneBox.Focus();
            DebtorPhoneBox.SelectAll();
            Debtors.Add(newDebtor);
            ClearSearchAndSelect(newDebtor);
            UpdateDashboardKPIs();
            SetNextId();
            ShowStatus("Préstamo añadido.", true);
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!CanWrite() || _selectedDebtor is null)
                {
                    return;
                }

                var debtorToDelete = _selectedDebtor;

                DebtorGrid.SelectedItem = null;
                _selectedDebtor = null;

                using (var conn = DatabaseHelper.GetConnection())
                using (var tr = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmdAbonos = conn.CreateCommand())
                        {
                            cmdAbonos.Transaction = tr;
                            cmdAbonos.CommandText = "DELETE FROM Abonos WHERE DeudorId = @id;";
                            cmdAbonos.Parameters.AddWithValue("@id", debtorToDelete.Id);
                            cmdAbonos.ExecuteNonQuery();
                        }

                        using (var cmdDeudor = conn.CreateCommand())
                        {
                            cmdDeudor.Transaction = tr;
                            cmdDeudor.CommandText = "DELETE FROM Deudores WHERE Id = @id;";
                            cmdDeudor.Parameters.AddWithValue("@id", debtorToDelete.Id);
                            cmdDeudor.ExecuteNonQuery();
                        }

                        tr.Commit();
                    }
                    catch (Exception exDb)
                    {
                        tr.Rollback();
                        ToastNotification.Show($"Error al eliminar en base de datos: {exDb.Message}", ToastType.Error);
                        return;
                    }
                }

                Debtors.Remove(debtorToDelete);

                ClearForm();
                SetNextId();
                UpdateSelectionButtons();
                UpdateDashboardKPIs();

                ToastNotification.Show("Registro de deudor eliminado correctamente.", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error inesperado: {ex.Message}", ToastType.Error);
            }
        }

        private void ModifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanWrite() || _selectedDebtor is null || !TryReadLoanForm(out var name, out var concept, out var amount))
            {
                return;
            }

            if (amount < _selectedDebtor.TotalAbonado)
            {
                ShowStatus("La deuda original no puede ser menor que el total abonado.", false);
                AmountBox.Focus();
                return;
            }

            var oldDebtor = _selectedDebtor;
            var updatedDebtor = CopyDebtor(oldDebtor);
            updatedDebtor.Nombre = name;
            updatedDebtor.Telefono = DebtorPhoneBox.Text.Trim();
            updatedDebtor.Concepto = concept;
            updatedDebtor.MontoOriginal = amount;

            var updatedDebtors = Debtors.Select(debtor => debtor.Id == oldDebtor.Id ? updatedDebtor : debtor).ToList();
            if (!TrySave(updatedDebtors))
            {
                return;
            }

            ReplaceDebtor(oldDebtor, updatedDebtor);
            if (DebtorsView.Contains(updatedDebtor))
            {
                SelectDebtor(updatedDebtor);
            }
            UpdateDashboardKPIs();
            ShowStatus("Préstamo modificado.", true);
        }

        #region Operaciones de Pago
        private void PayButton_Click(object sender, RoutedEventArgs e) => ApplyPayment();

        private void LiquidateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDebtor != null && _selectedDebtor.SaldoRestante > 0)
            {
                PaymentAmountBox.Text = _selectedDebtor.SaldoRestante.ToString(CultureInfo.InvariantCulture);
                ApplyPayment();
            }
        }

        private void ApplyPayment()
        {
            if (!CanWrite() || _selectedDebtor is null)
            {
                ShowStatus("Selecciona un cliente antes de registrar un abono.", false);
                return;
            }

            if (_selectedDebtor.SaldoRestante <= 0)
            {
                ShowStatus("Este préstamo ya está completamente saldado.", false);
                return;
            }

            var turnoActual = TurnoCajaManager.ObtenerTurnoActivo();
            if (turnoActual == null)
            {
                ShowStatus("Debes abrir turno de caja (ingresar un fondo) para recibir abonos.", false);
                PaymentAmountBox.Focus();
                return;
            }

            var culture = CultureInfo.GetCultureInfo("es-MX");
            var paymentText = PaymentAmountBox.Text.Trim();

            if (!TryParseAmount(paymentText, out var payment) || payment <= 0)
            {
                ShowStatus("Introduce un monto de abono válido y mayor a $0.00.", false);
                PaymentAmountBox.Focus();
                return;
            }

            if (payment > _selectedDebtor.SaldoRestante)
            {
                ShowStatus($"El abono ({payment.ToString("C2", culture)}) no puede superar la deuda restante ({_selectedDebtor.SaldoRestante.ToString("C2", culture)}).", false);
                PaymentAmountBox.Focus();
                return;
            }

            var oldDebtor = _selectedDebtor;
            var updatedDebtor = CopyDebtor(oldDebtor);

            var todosLosAbonos = Debtors.SelectMany(d => d.Abonos).ToList();
            var nextAbonoId = todosLosAbonos.Count > 0 ? todosLosAbonos.Max(a => a.Id) + 1 : 1;

            var nuevoAbono = new Abono
            {
                Id = nextAbonoId,
                DeudorId = updatedDebtor.Id,
                Fecha = DateTime.Now,
                Monto = payment
            };

            updatedDebtor.Abonos.Insert(0, nuevoAbono);
            updatedDebtor.TotalAbonado += payment;

            var updatedDebtors = Debtors.Select(debtor => debtor.Id == oldDebtor.Id ? updatedDebtor : debtor).ToList();

            try
            {
                PrestamoDB.Save(updatedDebtors);
                TurnoCajaManager.RegistrarMovimientoEfectivo("Entrada", payment, $"Abono de cuenta: {updatedDebtor.Nombre}");

                ReplaceDebtor(oldDebtor, updatedDebtor);
                SelectDebtor(updatedDebtor);
                UpdateDashboardKPIs();
                UpdateSelectionButtons();

                PaymentAmountBox.Clear();

                var mensaje = updatedDebtor.SaldoRestante == 0
                    ? $"Deuda liquidada. Entrada de {payment.ToString("C2", culture)} guardada en caja."
                    : $"Abono de {payment.ToString("C2", culture)} registrado. Entrada guardada en caja.";

                ShowStatus(mensaje, true);
            }
            catch (Exception ex)
            {
                Debtors.Clear();
                foreach (var d in PrestamoDB.Load()) Debtors.Add(d);
                DebtorsView.Refresh();

                ToastNotification.Show($"Error crítico al procesar abono: {ex.Message}", ToastType.Error);
            }
        }
        #endregion

        #region Recordatorios WhatsApp
        private void WhatsAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDebtor == null) return;

            var telefono = DebtorPhoneBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(telefono))
            {
                ShowStatus("Ingresa el número celular del cliente (10 dígitos).", false);
                DebtorPhoneBox.Focus();
                return;
            }

            var digitos = new string(telefono.Where(char.IsDigit).ToArray());
            if (digitos.Length == 10)
            {
                digitos = "52" + digitos;
            }

            if (digitos.Length < 10)
            {
                ShowStatus("El número celular no es válido.", false);
                DebtorPhoneBox.Focus();
                return;
            }

            var config = AppConfig.Load();
            var plantilla = !string.IsNullOrWhiteSpace(config.MensajeWhatsAppRecordatorio)
                ? config.MensajeWhatsAppRecordatorio
                : "Hola {CLIENTE}, te recordamos de {NEGOCIO} que tienes un saldo pendiente de {SALDO} por concepto de: {CONCEPTO}. ¡Agradecemos tu pronto pago!";

            var mensaje = plantilla
                .Replace("{CLIENTE}", _selectedDebtor.Nombre)
                .Replace("{SALDO}", _selectedDebtor.SaldoRestante.ToString("C2", CultureInfo.GetCultureInfo("es-MX")))
                .Replace("{CONCEPTO}", _selectedDebtor.Concepto)
                .Replace("{NEGOCIO}", config.NombreNegocio);

            var url = $"https://web.whatsapp.com/send?phone={digitos}&text={Uri.EscapeDataString(mensaje)}";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                ToastNotification.Show($"Abriendo WhatsApp para {_selectedDebtor.Nombre}", ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"No se pudo abrir el navegador: {ex.Message}", ToastType.Error);
            }
        }
        #endregion

        #region Historial y Estado de Cuenta
        private void ViewHistoryButton_Click(object sender, RoutedEventArgs e) => MostrarHistorial();
        private void DebtorGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => MostrarHistorial();

        private void MostrarHistorial()
        {
            if (_selectedDebtor is null) return;

            var culture = CultureInfo.GetCultureInfo("es-MX");

            HistoryClientName.Text = $"Estado de Cuenta: {_selectedDebtor.Nombre}";
            HistorySummaryText.Text = $"Original: {_selectedDebtor.MontoOriginal.ToString("C2", culture)} | Total Abonado: {_selectedDebtor.TotalAbonado.ToString("C2", culture)} | Pendiente: {_selectedDebtor.SaldoRestante.ToString("C2", culture)}";

            HistoryMontoTotalText.Text = _selectedDebtor.SaldoRestante.ToString("C2", culture);

            AbonosListControl.ItemsSource = _selectedDebtor.Abonos;
            NoAbonosMessage.Visibility = _selectedDebtor.Abonos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            ConstruirVistaDeudas(_selectedDebtor);
            ActivarTabDeudas();

            HistorialOverlay.Visibility = Visibility.Visible;
            HistorialOverlay.Focus();
        }

        private void PrintStatementButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDebtor == null) return;

            var config = AppConfig.Load();
            if (string.IsNullOrWhiteSpace(config.ImpresoraSeleccionada))
            {
                ToastNotification.Show("No hay impresora configurada en Ajustes.", ToastType.Warning);
                return;
            }

            try
            {
                var sb = new StringBuilder();
                int ancho = config.AnchoTicketCaracteres > 0 ? config.AnchoTicketCaracteres : 32;
                var culture = CultureInfo.GetCultureInfo("es-MX");

                sb.AppendLine(TicketService.CentrarTexto(config.NombreNegocio ?? "NEGOCIO", ancho));
                sb.AppendLine(TicketService.CentrarTexto("ESTADO DE CUENTA", ancho));
                sb.AppendLine(new string('-', ancho));
                sb.AppendLine($"Cliente: {_selectedDebtor.Nombre}");
                sb.AppendLine($"Fecha: {DateTime.Now:dd/MM/yyyy hh:mm tt}");
                sb.AppendLine(new string('-', ancho));
                sb.AppendLine($"Monto Original: {_selectedDebtor.MontoOriginal.ToString("C2", culture)}");
                sb.AppendLine($"Total Abonado:  {_selectedDebtor.TotalAbonado.ToString("C2", culture)}");
                sb.AppendLine(new string('-', ancho));
                sb.AppendLine(TicketService.AlinearDerecha($"SALDO PENDIENTE: {_selectedDebtor.SaldoRestante.ToString("C2", culture)}", ancho));
                sb.AppendLine(new string('-', ancho));

                if (_selectedDebtor.Abonos.Count > 0)
                {
                    sb.AppendLine(TicketService.CentrarTexto("ULTIMOS ABONOS", ancho));
                    foreach (var abono in _selectedDebtor.Abonos.Take(3))
                    {
                        string fechaAbono = abono.Fecha.ToString("dd/MM/yy");
                        string montoAbono = abono.Monto.ToString("C2", culture);
                        int espacios = ancho - fechaAbono.Length - montoAbono.Length;

                        sb.AppendLine($"{fechaAbono}{new string(' ', Math.Max(1, espacios))}{montoAbono}");
                    }
                    sb.AppendLine(new string('-', ancho));
                }

                sb.AppendLine(TicketService.CentrarTexto("Guarde este recibo.", ancho));
                sb.AppendLine("\n\n");

                byte[] buffer = Encoding.ASCII.GetBytes(sb.ToString());
                bool impreso = RawPrinterHelper.SendBytesToPrinter(config.ImpresoraSeleccionada, buffer);

                if (impreso)
                    ToastNotification.Show("Estado de cuenta enviado a imprimir.", ToastType.Success);
                else
                    ToastNotification.Show("No se pudo conectar a la impresora.", ToastType.Error);
            }
            catch (Exception ex)
            {
                ToastNotification.Show($"Error al imprimir: {ex.Message}", ToastType.Error);
            }
        }

        private void ConstruirVistaDeudas(Deudor deudor)
        {
            DeudasContainerPanel.Children.Clear();
            var culture = CultureInfo.GetCultureInfo("es-MX");

            var bloques = (deudor.Concepto ?? string.Empty).Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries);

            if (bloques.Length == 0)
            {
                DeudasContainerPanel.Children.Add(new TextBlock
                {
                    Text = "Sin desglose de productos registrado.",
                    FontSize = 13,
                    Foreground = (Brush)FindResource("ThemeMutedForeground"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20)
                });
                return;
            }

            foreach (var bloque in bloques)
            {
                var texto = bloque.Trim();
                if (string.IsNullOrEmpty(texto)) continue;

                string fechaTexto = "Registro de Deuda";
                string itemsTexto = texto;

                int idxPuntos = texto.IndexOf(':');
                if (idxPuntos > 0 && idxPuntos <= 22)
                {
                    fechaTexto = $"Fecha: {texto.Substring(0, idxPuntos).Trim()}";
                    itemsTexto = texto.Substring(idxPuntos + 1).Trim();
                }

                var lblFecha = new TextBlock
                {
                    Text = fechaTexto,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("ThemeTextForeground"),
                    Margin = new Thickness(2, 4, 0, 6)
                };
                DeudasContainerPanel.Children.Add(lblFecha);

                var cardBorder = new Border
                {
                    Background = (Brush)FindResource("ThemeSurfaceBackground"),
                    BorderBrush = (Brush)FindResource("ThemeBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var stackCard = new StackPanel();

                var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                var hCant = new TextBlock { Text = "Cantidad", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextForeground") };
                var hProd = new TextBlock { Text = "Producto", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextForeground"), HorizontalAlignment = HorizontalAlignment.Center };
                var hPrecio = new TextBlock { Text = "Precio", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("ThemeTextForeground"), HorizontalAlignment = HorizontalAlignment.Right };

                Grid.SetColumn(hCant, 0);
                Grid.SetColumn(hProd, 1);
                Grid.SetColumn(hPrecio, 2);
                headerGrid.Children.Add(hCant);
                headerGrid.Children.Add(hProd);
                headerGrid.Children.Add(hPrecio);
                stackCard.Children.Add(headerGrid);

                var productos = itemsTexto.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var prodRaw in productos)
                {
                    var pTrim = prodRaw.Trim();
                    if (string.IsNullOrEmpty(pTrim)) continue;

                    string cantidadStr = "1";
                    string nombreProd = pTrim;

                    int lastX = pTrim.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
                    if (lastX > 0)
                    {
                        var posibleCant = pTrim.Substring(lastX + 2).Trim();
                        if (decimal.TryParse(posibleCant, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                        {
                            cantidadStr = posibleCant;
                            nombreProd = pTrim.Substring(0, lastX).Trim();
                        }
                    }

                    string precioStr = "-";
                    if (_preciosCatalogo.TryGetValue(nombreProd, out decimal precioUnitario))
                    {
                        precioStr = precioUnitario.ToString("C2", culture);
                    }
                    else if (bloques.Length == 1 && productos.Length == 1 && decimal.TryParse(cantidadStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cantUnica) && cantUnica > 0)
                    {
                        precioStr = (deudor.MontoOriginal / cantUnica).ToString("C2", culture);
                    }

                    var rowGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                    var txtCant = new TextBlock { Text = cantidadStr, FontSize = 13, Foreground = (Brush)FindResource("ThemeTextForeground"), Margin = new Thickness(8, 0, 0, 0) };
                    var txtNombre = new TextBlock { Text = nombreProd, FontSize = 13, Foreground = (Brush)FindResource("ThemeTextForeground"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
                    var txtPrecio = new TextBlock { Text = precioStr, FontSize = 13, Foreground = (Brush)FindResource("ThemeTextForeground"), HorizontalAlignment = HorizontalAlignment.Right };

                    Grid.SetColumn(txtCant, 0);
                    Grid.SetColumn(txtNombre, 1);
                    Grid.SetColumn(txtPrecio, 2);

                    rowGrid.Children.Add(txtCant);
                    rowGrid.Children.Add(txtNombre);
                    rowGrid.Children.Add(txtPrecio);
                    stackCard.Children.Add(rowGrid);
                }

                cardBorder.Child = stackCard;
                DeudasContainerPanel.Children.Add(cardBorder);
            }
        }

        private void TabDeudas_Click(object sender, RoutedEventArgs e) => ActivarTabDeudas();
        private void TabAbonos_Click(object sender, RoutedEventArgs e) => ActivarTabAbonos();

        private void ActivarTabDeudas()
        {
            PanelDeudasView.Visibility = Visibility.Visible;
            PanelAbonosView.Visibility = Visibility.Collapsed;

            TabDeudasBtn.SetResourceReference(Control.BackgroundProperty, "ThemeAccent");
            TabDeudasBtn.Foreground = Brushes.White;

            TabAbonosBtn.SetResourceReference(Control.BackgroundProperty, "ThemeSurfaceBackground");
            TabAbonosBtn.SetResourceReference(Control.ForegroundProperty, "ThemeTextForeground");

            if (_selectedDebtor != null)
            {
                HistoryMontoTotalText.Text = _selectedDebtor.SaldoRestante.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
            }
        }

        private void ActivarTabAbonos()
        {
            PanelDeudasView.Visibility = Visibility.Collapsed;
            PanelAbonosView.Visibility = Visibility.Visible;

            TabAbonosBtn.SetResourceReference(Control.BackgroundProperty, "ThemeAccent");
            TabAbonosBtn.Foreground = Brushes.White;

            TabDeudasBtn.SetResourceReference(Control.BackgroundProperty, "ThemeSurfaceBackground");
            TabDeudasBtn.SetResourceReference(Control.ForegroundProperty, "ThemeTextForeground");

            if (_selectedDebtor != null)
            {
                HistoryMontoTotalText.Text = _selectedDebtor.TotalAbonado.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
            }
        }

        private void CloseHistory_Click(object sender, RoutedEventArgs e)
        {
            HistorialOverlay.Visibility = Visibility.Collapsed;
        }

        private void HistorialOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, HistorialOverlay))
            {
                HistorialOverlay.Visibility = Visibility.Collapsed;
            }
        }
        #endregion

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            DebtorGrid.SelectedItem = null;
            _selectedDebtor = null;
            ClearForm();
            SetNextId();
            UpdateSelectionButtons();
            DebtorPhoneBox.Focus();
        }

        private bool TryReadLoanForm(out string name, out string concept, out decimal amount)
        {
            name = DebtorNameBox.Text.Trim();
            concept = ConceptBox.Text.Trim();
            amount = 0;

            if (name.Length == 0)
            {
                ShowStatus("El nombre del deudor es obligatorio.", false);
                DebtorNameBox.Focus();
                return false;
            }

            if (!TryParseAmount(AmountBox.Text, out amount) || amount <= 0)
            {
                ShowStatus("Introduce una deuda válida y mayor que cero.", false);
                AmountBox.Focus();
                return false;
            }

            return true;
        }

        private static bool TryParseAmount(string text, out decimal amount)
        {
            const NumberStyles styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint;
            return decimal.TryParse(text, styles, CultureInfo.GetCultureInfo("en-US"), out amount) ||
                   decimal.TryParse(text, styles, CultureInfo.GetCultureInfo("es-MX"), out amount) ||
                   decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out amount);
        }

        private bool CanWrite()
        {
            if (!_loadFailed) return true;

            ShowStatus("No se guardaron cambios porque los datos contienen información inválida.", false);
            return false;
        }

        private bool TrySave(IEnumerable<Deudor> debtors)
        {
            try
            {
                PrestamoDB.Save(debtors);
                return true;
            }
            catch (Exception exception)
            {
                ShowStatus($"No se pudo guardar la información: {exception.Message}", false);
                return false;
            }
        }

        private void ReplaceDebtor(Deudor oldDebtor, Deudor updatedDebtor)
        {
            var index = Debtors.IndexOf(oldDebtor);
            if (index >= 0)
            {
                Debtors[index] = updatedDebtor;
            }
        }

        private void SelectDebtor(Deudor debtor)
        {
            DebtorGrid.SelectedItem = debtor;
            DebtorGrid.ScrollIntoView(debtor);
        }

        private void ClearSearchAndSelect(Deudor debtor)
        {
            SearchBox.Clear();
            DebtorsView.Refresh();
            SelectDebtor(debtor);
        }

        private static Deudor CopyDebtor(Deudor debtor) => new()
        {
            Id = debtor.Id,
            Nombre = debtor.Nombre,
            Telefono = debtor.Telefono,
            Concepto = debtor.Concepto,
            MontoOriginal = debtor.MontoOriginal,
            TotalAbonado = debtor.TotalAbonado,
            Abonos = debtor.Abonos.ToList()
        };

        private static string CombineConcepts(string current, string added) =>
            string.Join("; ", new[] { current.Trim(), added.Trim() }.Where(value => value.Length > 0));

        private int GetNextId() => checked(Debtors.Select(debtor => debtor.Id).DefaultIfEmpty(0).Max() + 1);

        private void SetNextId()
        {
            if (_selectedDebtor is not null)
            {
                DebtorIdBox.Text = _selectedDebtor.Id.ToString(CultureInfo.InvariantCulture);
                return;
            }

            try
            {
                DebtorIdBox.Text = GetNextId().ToString(CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                DebtorIdBox.Clear();
            }
        }

        private void UpdateSelectionButtons()
        {
            var hasSelection = _selectedDebtor is not null && !_loadFailed;
            DeleteButton.IsEnabled = hasSelection;
            ModifyButton.IsEnabled = hasSelection;
            PayButton.IsEnabled = hasSelection && _selectedDebtor!.SaldoRestante > 0;
            LiquidateButton.IsEnabled = hasSelection && _selectedDebtor!.SaldoRestante > 0;
            ViewHistoryButton.IsEnabled = hasSelection;
            WhatsAppButton.IsEnabled = hasSelection && _selectedDebtor!.SaldoRestante > 0;
        }

        private void UpdateDashboardKPIs()
        {
            var culture = CultureInfo.GetCultureInfo("es-MX");
            var totalDeuda = Debtors.Where(d => d.SaldoRestante > 0).Sum(d => d.SaldoRestante);
            var totalRecuperado = Debtors.Sum(d => d.TotalAbonado);
            var clientesActivos = Debtors.Count(d => d.SaldoRestante > 0);

            KpiDeudaTotal.Text = totalDeuda.ToString("C2", culture);
            KpiTotalRecuperado.Text = totalRecuperado.ToString("C2", culture);
            KpiClientesActivos.Text = clientesActivos.ToString();
        }

        private void ClearForm()
        {
            DebtorNameBox.Clear();
            DebtorPhoneBox.Clear();
            ConceptBox.Clear();
            AmountBox.Clear();
            PaymentAmountBox.Clear();
            StatusText.Text = string.Empty;
            _successStatusVisible = false;
        }

        private void ShowStatus(string message, bool succeeded)
        {
            StatusText.Text = message;
            StatusText.Foreground = succeeded ? System.Windows.Media.Brushes.ForestGreen : System.Windows.Media.Brushes.Firebrick;
            _successStatusVisible = succeeded;

            ToastNotification.Show(message, succeeded ? ToastType.Success : ToastType.Warning);
        }
    }
}