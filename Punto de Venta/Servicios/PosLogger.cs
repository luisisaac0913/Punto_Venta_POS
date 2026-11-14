using Punto_de_Venta.Datos;
using System;
using System.IO;
using System.Text;

namespace Punto_de_Venta.Servicios
{
    public static class PosLogger
    {
        private static readonly object _logLock = new();
        private const long MaxLogSizeBytes = 2 * 1024 * 1024; // 2 MB

        private static string LogFilePath =>
            Path.Combine(DatabaseHelper.DbFolder, "logs", "pos_errores.log");

        public static void Error(string origen, Exception ex, string? detalle = null)
        {
            if (ex == null) return;
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] [ERROR] [").Append(origen).Append("] ");
            if (!string.IsNullOrWhiteSpace(detalle))
            {
                sb.Append(detalle).Append(" | ");
            }
            sb.Append(ex.GetType().Name).Append(": ").AppendLine(ex.Message);
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                sb.AppendLine(ex.StackTrace);
            }
            if (ex.InnerException != null)
            {
                sb.Append("  --> Inner ").Append(ex.InnerException.GetType().Name).Append(": ").AppendLine(ex.InnerException.Message);
            }
            EscribirLineaSegura(sb.ToString());
        }

        public static void Warn(string origen, string mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje)) return;
            string linea = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [WARN] [{origen}] {mensaje}{Environment.NewLine}";
            EscribirLineaSegura(linea);
        }

        public static void Warn(string origen, Exception ex)
        {
            if (ex == null) return;
            Warn(origen, $"{ex.GetType().Name}: {ex.Message}");
        }

        public static void Info(string origen, string mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje)) return;
            string linea = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] [{origen}] {mensaje}{Environment.NewLine}";
            EscribirLineaSegura(linea);
        }

        private static void EscribirLineaSegura(string texto)
        {
            try
            {
                lock (_logLock)
                {
                    string path = LogFilePath;
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (File.Exists(path))
                    {
                        var info = new FileInfo(path);
                        if (info.Length >= MaxLogSizeBytes)
                        {
                            string oldPath = Path.Combine(dir!, "pos_errores.old.log");
                            if (File.Exists(oldPath))
                            {
                                File.Delete(oldPath);
                            }
                            File.Move(path, oldPath);
                        }
                    }

                    File.AppendAllText(path, texto, Encoding.UTF8);
                }
            }
            catch
            {
                // El logger jamas debe interrumpir el flujo del POS
            }
        }
    }
}
