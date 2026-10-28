using Punto_de_Venta.Datos;
using Punto_de_Venta.Servicios;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public partial class App : Application
    {
        public bool IsDarkMode { get; private set; }

        private static string ErrorLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POS_DataStorage",
            "error_log.txt");
        public static void CambiarColorAcento(string codigoHexadecimal)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(codigoHexadecimal);
                var nuevaBrocha = new SolidColorBrush(color);
                Application.Current.Resources["ThemeAccent"] = nuevaBrocha;
            }
            catch
            {
            }
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            base.OnStartup(e);
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = true;
            QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
            if (!LicenciaService.LicenciaExiste())
            {
                var activacionWnd = new ActivacionWindow();
                if (activacionWnd.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }
            else if (!LicenciaService.ValidarLicencia())
            {
                MessageBox.Show("ERROR DE LICENCIA: El hardware de este equipo no coincide con el registro original de la instalación. Contacte a soporte técnico para reactivar el sistema.",
                                "Violación de Seguridad", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
            DatabaseHelper.InicializarBaseDeDatos();
            DatabaseHelper.ActualizarEsquemaVentas();
            AppConfig config = new AppConfig();
            try
            {
                config = AppConfig.Load();
                SetDarkMode(config.ModoOscuro);
                CambiarColorAcento(config.ColorAcentoHex);
            }
            catch (Exception ex)
            {
                RegistrarErrorEnArchivo(ex, "Startup Init");
                SetDarkMode(false);
            }
            if (string.IsNullOrWhiteSpace(config.NombreNegocio))
            {
                Punto_de_Venta.Vistas.Bienvenida setup = new Punto_de_Venta.Vistas.Bienvenida();
                setup.Show();
            }
            else
            {
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }

        public void SetDarkMode(bool isDark)
        {
            IsDarkMode = isDark;
            string rutaLogo = isDark ? "pack://application:,,,/Imagenes/Blanco.png" : "pack://application:,,,/Imagenes/Oscuro.png";
            Application.Current.Resources["ThemeLogo"] = new System.Windows.Media.Imaging.BitmapImage(new Uri(rutaLogo));
            if (isDark)
            {
                Resources["ThemeWindowBackground"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
                Resources["ThemeSurfaceBackground"] = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E));
                Resources["ThemeInputBackground"] = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C));
                Resources["ThemeBorder"] = new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x4A));
                Resources["ThemeTextForeground"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF7));
                Resources["ThemeMutedForeground"] = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));
                Resources["ThemeAccent"] = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
            }
            else
            {
                Resources["ThemeWindowBackground"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF7));
                Resources["ThemeSurfaceBackground"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                Resources["ThemeInputBackground"] = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
                Resources["ThemeBorder"] = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xEA));
                Resources["ThemeTextForeground"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
                Resources["ThemeMutedForeground"] = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));
                Resources["ThemeAccent"] = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (e.Exception is OutOfMemoryException)
            {
                RegistrarErrorEnArchivo(e.Exception, "Falta Crítica de Memoria");
                return;
            }

            RegistrarErrorEnArchivo(e.Exception, "UI Dispatcher");

            MessageBox.Show(
                $"Ocurrió un problema inesperado:\n\n{e.Exception.Message}\n\nLos detalles se guardaron en 'error_log.txt'.",
                "Aviso del Sistema",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                RegistrarErrorEnArchivo(ex, "AppDomain Background");

                MessageBox.Show(
                    $"Error crítico en segundo plano:\n\n{ex.Message}\n\nSe registró el incidente en la bitácora.",
                    "Error Crítico",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            RegistrarErrorEnArchivo(e.Exception, "TaskScheduler Asíncrono");
            e.SetObserved();
        }

        private static void RegistrarErrorEnArchivo(Exception ex, string origen)
        {
            try
            {
                var dir = Path.GetDirectoryName(ErrorLogPath)!;
                Directory.CreateDirectory(dir);

                if (File.Exists(ErrorLogPath))
                {
                    var fileInfo = new FileInfo(ErrorLogPath);
                    if (fileInfo.Length > 5 * 1024 * 1024)
                    {
                        var backupPath = Path.Combine(dir, "error_log.old");
                        File.Delete(backupPath);
                        File.Move(ErrorLogPath, backupPath);
                    }
                }

                var log = new StringBuilder();
                log.AppendLine("==================================================");
                log.AppendLine($"FECHA Y HORA: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                log.AppendLine($"ORIGEN: {origen}");
                log.AppendLine($"TIPO: {ex.GetType().FullName}");
                log.AppendLine($"MENSAJE: {ex.Message}");
                log.AppendLine("DETALLES (STACK TRACE):");
                log.AppendLine(ex.ToString());
                log.AppendLine("==================================================");
                log.AppendLine();

                File.AppendAllText(ErrorLogPath, log.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}