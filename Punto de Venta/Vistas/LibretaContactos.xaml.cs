using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Punto_de_Venta.Vistas
{
    public partial class LibretaContactos : UserControl, IManejadorEscape
    {
        private readonly ObservableCollection<NotaContacto> _notas = new();
        private ICollectionView _vistaFiltrada;
        private NotaContacto? _notaSeleccionada;
        private bool _cargandoDatos = false;
        private DispatcherTimer? _timerAutoGuardar;

        public LibretaContactos()
        {
            InitializeComponent();
            _vistaFiltrada = CollectionViewSource.GetDefaultView(_notas);
            _vistaFiltrada.Filter = FiltrarNota;
            ListaNotas.ItemsSource = _vistaFiltrada;

            _timerAutoGuardar = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _timerAutoGuardar.Tick += TimerAutoGuardar_Tick;
            IdiomaService.IdiomaCambiado += OnIdiomaCambiado;
        }

        private void OnIdiomaCambiado(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                CargarNotasDesdeBaseDatos();
                ActualizarContadorTexto();
            });
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            CargarNotasDesdeBaseDatos();
            Focus();
            Keyboard.Focus(this);
        }

        public bool ManejarEscape()
        {
            GuardarNotaActualSilencioso();
            return false;
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                GuardarNotaActualSilencioso();
                var mainWindow = Window.GetWindow(this) as MainWindow ?? Application.Current.MainWindow as MainWindow;
                mainWindow?.RegresarAlMenu();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnNuevaNota_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnGuardar_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                TxtBuscar.Focus();
                TxtBuscar.SelectAll();
                e.Handled = true;
            }
        }

        private void CargarNotasDesdeBaseDatos()
        {
            _cargandoDatos = true;
            _notas.Clear();

            try
            {
                var lista = LibretaDB.CargarTodos();
                foreach (var item in lista)
                {
                    _notas.Add(item);
                }
            }
            catch (Exception ex)
            {
                ToastNotification.Show(IdiomaService.ObtenerFormato("NotebookLoadError", "Error al cargar libreta: {0}", ex.Message), ToastType.Error);
            }
            finally
            {
                _cargandoDatos = false;
            }

            ActualizarContadorNotas();

            if (_notas.Count > 0)
            {
                ListaNotas.SelectedIndex = 0;
            }
            else
            {
                MostrarEditor(false);
            }
        }

        private bool FiltrarNota(object obj)
        {
            if (obj is not NotaContacto nota) return false;
            string filtro = TxtBuscar?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filtro)) return true;

            string tit = (nota.Titulo ?? string.Empty).ToLowerInvariant();
            string tel = (nota.Telefono ?? string.Empty).ToLowerInvariant();
            string cont = (nota.Contenido ?? string.Empty).ToLowerInvariant();

            return tit.Contains(filtro) || tel.Contains(filtro) || cont.Contains(filtro);
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            _vistaFiltrada?.Refresh();
            ActualizarContadorNotas();
        }

        private void ActualizarContadorNotas()
        {
            int total = _notas.Count;
            TxtTotalNotas.Text = total == 1
                ? IdiomaService.Obtener("NotebookRegSingular", "1 registro")
                : IdiomaService.ObtenerFormato("NotebookRegPlural", "{0} registros", total);
        }

        private void ListaNotas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cargandoDatos) return;

            GuardarNotaActualSilencioso();

            if (ListaNotas.SelectedItem is NotaContacto seleccionada)
            {
                _notaSeleccionada = seleccionada;
                CargarNotaEnEditor(seleccionada);
                MostrarEditor(true);
            }
            else
            {
                _notaSeleccionada = null;
                MostrarEditor(false);
            }
        }

        private void MostrarEditor(bool visible)
        {
            if (PanelEditor != null && PanelVacio != null)
            {
                PanelEditor.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                PanelVacio.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void CargarNotaEnEditor(NotaContacto nota)
        {
            _cargandoDatos = true;
            try
            {
                TxtTitulo.Text = nota.Titulo;
                TxtTelefono.Text = nota.Telefono;
                TxtContenido.Text = nota.Contenido;
                TxtEstadoGuardado.Text = IdiomaService.Obtener("NotebookSaved", "Guardado");
                ActualizarContadorTexto();
            }
            finally
            {
                _cargandoDatos = false;
            }
        }

        private void CamposEditados_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_cargandoDatos || _notaSeleccionada == null) return;

            TxtEstadoGuardado.Text = IdiomaService.Obtener("NotebookSaving", "Guardando...");
            ActualizarContadorTexto();

            _timerAutoGuardar?.Stop();
            _timerAutoGuardar?.Start();
        }

        private void TimerAutoGuardar_Tick(object? sender, EventArgs e)
        {
            _timerAutoGuardar?.Stop();
            GuardarNotaActualSilencioso();
        }

        private void ActualizarContadorTexto()
        {
            string texto = TxtContenido?.Text ?? string.Empty;
            int caracteres = texto.Length;
            int lineas = TxtContenido?.LineCount ?? 0;
            if (lineas <= 1 && string.IsNullOrEmpty(texto)) lineas = 0;
            TxtContador.Text = IdiomaService.ObtenerFormato("NotebookCounterFormat", "{0} caracteres | {1} lineas", caracteres, lineas);
        }

        private void GuardarNotaActualSilencioso()
        {
            if (_notaSeleccionada == null || _cargandoDatos) return;

            string tit = TxtTitulo.Text.Trim();
            string tel = TxtTelefono.Text.Trim();
            string cont = TxtContenido.Text;

            bool cambio = _notaSeleccionada.Titulo != tit ||
                          _notaSeleccionada.Telefono != tel ||
                          _notaSeleccionada.Contenido != cont;

            if (cambio)
            {
                _notaSeleccionada.Titulo = tit;
                _notaSeleccionada.Telefono = tel;
                _notaSeleccionada.Contenido = cont;

                try
                {
                    LibretaDB.Guardar(_notaSeleccionada);
                    TxtEstadoGuardado.Text = IdiomaService.Obtener("NotebookSaved", "Guardado");
                }
                catch
                {
                    TxtEstadoGuardado.Text = IdiomaService.Obtener("NotebookSaveError", "Error al guardar");
                }
            }
            else
            {
                TxtEstadoGuardado.Text = IdiomaService.Obtener("NotebookSaved", "Guardado");
            }
        }

        private void BtnNuevaNota_Click(object sender, RoutedEventArgs e)
        {
            GuardarNotaActualSilencioso();

            var nueva = new NotaContacto
            {
                Titulo = IdiomaService.Obtener("NotebookNewNoteTitle", "Nueva Nota"),
                Telefono = string.Empty,
                Contenido = string.Empty,
                FechaModificacion = DateTime.Now,
                EsFijado = false
            };

            try
            {
                LibretaDB.Guardar(nueva);
                _notas.Insert(0, nueva);
                ActualizarContadorNotas();
                ListaNotas.SelectedItem = nueva;
                MostrarEditor(true);
                TxtTitulo.Focus();
                TxtTitulo.SelectAll();
                ToastNotification.Show(IdiomaService.Obtener("NotebookCreatedToast", "Nueva nota creada"), ToastType.Info);
            }
            catch (Exception ex)
            {
                ToastNotification.Show(IdiomaService.ObtenerFormato("NotebookCreateError", "Error al crear nota: {0}", ex.Message), ToastType.Error);
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (_notaSeleccionada == null) return;
            GuardarNotaActualSilencioso();
            ToastNotification.Show(IdiomaService.Obtener("NotebookSavedToast", "Nota guardada correctamente"), ToastType.Success);
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (_notaSeleccionada == null) return;

            string nombre = _notaSeleccionada.TituloDisplay;
            var confirmacion = CustomMessageBox.Show(
                Window.GetWindow(this),
                IdiomaService.ObtenerFormato("NotebookDeleteConfirmMsg", "Deseas eliminar la nota '{0}' de tu libreta?", nombre),
                IdiomaService.Obtener("NotebookDeleteConfirmTitle", "Confirmar Eliminacion"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmacion != MessageBoxResult.Yes) return;

            try
            {
                LibretaDB.Eliminar(_notaSeleccionada.Id);
                _notas.Remove(_notaSeleccionada);
                ActualizarContadorNotas();

                if (_notas.Count > 0)
                {
                    ListaNotas.SelectedIndex = 0;
                }
                else
                {
                    _notaSeleccionada = null;
                    MostrarEditor(false);
                }

                ToastNotification.Show(IdiomaService.Obtener("NotebookDeletedToast", "Nota eliminada correctamente"), ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show(IdiomaService.ObtenerFormato("NotebookDeleteError", "Error al eliminar nota: {0}", ex.Message), ToastType.Error);
            }
        }

        private void BtnCopiarTel_Click(object sender, RoutedEventArgs e)
        {
            string tel = TxtTelefono.Text.Trim();
            if (string.IsNullOrWhiteSpace(tel))
            {
                ToastNotification.Show(IdiomaService.Obtener("NotebookNoPhoneWarn", "No hay telefono registrado en esta nota"), ToastType.Warning);
                return;
            }

            try
            {
                Clipboard.SetText(tel);
                ToastNotification.Show(IdiomaService.ObtenerFormato("NotebookPhoneCopied", "Telefono '{0}' copiado al portapapeles", tel), ToastType.Success);
            }
            catch
            {
                ToastNotification.Show(IdiomaService.Obtener("NotebookCopyError", "No se pudo copiar al portapapeles"), ToastType.Error);
            }
        }

        private void BtnWhatsApp_Click(object sender, RoutedEventArgs e)
        {
            string tel = TxtTelefono.Text.Trim();
            string digitos = Regex.Replace(tel, @"\D", "");

            if (digitos.Length < 10)
            {
                ToastNotification.Show(IdiomaService.Obtener("NotebookPhoneInvalid", "Ingresa un numero telefonico de al menos 10 digitos"), ToastType.Warning);
                TxtTelefono.Focus();
                return;
            }

            if (digitos.Length == 10)
            {
                digitos = "52" + digitos;
            }

            string url = $"https://web.whatsapp.com/send?phone={digitos}";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                ToastNotification.Show(IdiomaService.Obtener("NotebookWhatsAppOpening", "Abriendo chat de WhatsApp..."), ToastType.Success);
            }
            catch (Exception ex)
            {
                ToastNotification.Show(IdiomaService.ObtenerFormato("NotebookWhatsAppError", "Error al abrir WhatsApp: {0}", ex.Message), ToastType.Error);
            }
        }

        private void BtnRegresar_Click(object sender, RoutedEventArgs e)
        {
            GuardarNotaActualSilencioso();
            var mainWindow = Window.GetWindow(this) as MainWindow ?? Application.Current.MainWindow as MainWindow;
            mainWindow?.RegresarAlMenu();
        }
    }
}

