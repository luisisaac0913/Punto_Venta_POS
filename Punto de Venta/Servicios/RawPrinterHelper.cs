using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Punto_de_Venta.Servicios
{
    public static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public static bool EsImpresoraFisicaValida(string? printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return false;
            string p = printerName.Trim();
            if (p.StartsWith("(", StringComparison.Ordinal) ||
                p.Contains("Ninguna", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("None", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (p.Contains("Print to PDF", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("XPS Document Writer", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("OneNote", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Fax", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        public static bool SendBytesToPrinter(string szPrinterName, byte[] bytes, string docName = "Documento POS")
        {
            if (!EsImpresoraFisicaValida(szPrinterName) || bytes == null || bytes.Length == 0)
                return false;

            var pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

                bool success = TrySendWithDataType(szPrinterName, pUnmanagedBytes, bytes.Length, docName, "RAW", out int lastError);
                if (!success)
                {
                    // Drivers Windows v4/XPS suelen rechazar pDataType="RAW" (ej. Win32Error 1804 ERROR_INVALID_DATATYPE)
                    PosLogger.Warn("RawPrinterHelper.SendBytesToPrinter", $"Reintentando con XPS_PASS en '{szPrinterName}' tras fallo en modo RAW (Win32Error={lastError}).");
                    success = TrySendWithDataType(szPrinterName, pUnmanagedBytes, bytes.Length, docName, "XPS_PASS", out _);
                }

                return success;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pUnmanagedBytes);
            }
        }

        private static bool TrySendWithDataType(string szPrinterName, IntPtr pUnmanagedBytes, int byteCount, string docName, string dataType, out int win32Error)
        {
            win32Error = 0;
            var di = new DOCINFOA { pDocName = docName, pDataType = dataType };
            bool success = false;

            if (OpenPrinter(szPrinterName.Normalize(), out var hPrinter, IntPtr.Zero))
            {
                try
                {
                    if (StartDocPrinter(hPrinter, 1, di))
                    {
                        try
                        {
                            if (StartPagePrinter(hPrinter))
                            {
                                try
                                {
                                    success = WritePrinter(hPrinter, pUnmanagedBytes, byteCount, out _);
                                    if (!success)
                                    {
                                        win32Error = Marshal.GetLastWin32Error();
                                        PosLogger.Warn("RawPrinterHelper.WritePrinter", $"Fallo al enviar {dataType} a '{szPrinterName}'. Win32Error={win32Error}");
                                    }
                                }
                                finally
                                {
                                    EndPagePrinter(hPrinter);
                                }
                            }
                            else
                            {
                                win32Error = Marshal.GetLastWin32Error();
                                PosLogger.Warn("RawPrinterHelper.StartPagePrinter", $"No se pudo iniciar pagina {dataType} en '{szPrinterName}'. Win32Error={win32Error}");
                            }
                        }
                        finally
                        {
                            EndDocPrinter(hPrinter);
                        }
                    }
                    else
                    {
                        win32Error = Marshal.GetLastWin32Error();
                        PosLogger.Warn("RawPrinterHelper.StartDocPrinter", $"No se pudo iniciar documento {dataType} '{docName}' en '{szPrinterName}'. Win32Error={win32Error}");
                    }
                }
                finally
                {
                    ClosePrinter(hPrinter);
                }
            }
            else
            {
                win32Error = Marshal.GetLastWin32Error();
                PosLogger.Warn("RawPrinterHelper.OpenPrinter", $"No se pudo abrir la impresora '{szPrinterName}'. Win32Error={win32Error}");
            }

            return success;
        }
    }
}