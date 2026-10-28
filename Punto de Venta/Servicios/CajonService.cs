using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Punto_de_Venta.Servicios
{
    public static class CajonService
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string? pDocName;
            [MarshalAs(UnmanagedType.LPStr)]
            public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)]
            public string? pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        private static bool SendBytesToPrinter(string printerName, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(printerName) || bytes == null || bytes.Length == 0)
                return false;
            var di = new DOCINFOA
            {
                pDocName = "Apertura Cajon POS",
                pDataType = "RAW"
            };
            if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
                return false;
            var success = false;
            if (StartDocPrinter(hPrinter, 1, di))
            {
                if (StartPagePrinter(hPrinter))
                {
                    var pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);
                        success = WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out _);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pUnmanagedBytes);
                        EndPagePrinter(hPrinter);
                    }
                }
                EndDocPrinter(hPrinter);
            }
            ClosePrinter(hPrinter);
            return success;
        }

        /// <summary>
        /// 
        /// </summary>
        public static bool AbrirCajon(string? nombreImpresora = null)
        {
            try
            {
                var printer = nombreImpresora;
                if (string.IsNullOrWhiteSpace(printer))
                {
                    var config = AppConfig.Load();
                    printer = config.ImpresoraSeleccionada;
                }
                if (string.IsNullOrWhiteSpace(printer) || printer.StartsWith("(", StringComparison.Ordinal))
                {
                    return false;
                }
                byte[] pulsoCajon = [0x1B, 0x70, 0x00, 0x19, 0xFA, 0x1B, 0x70, 0x01, 0x19, 0xFA];
                return SendBytesToPrinter(printer, pulsoCajon);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CajonService] No se pudo abrir el cajón: {ex.Message}");
                return false;
            }
        }
    }
}