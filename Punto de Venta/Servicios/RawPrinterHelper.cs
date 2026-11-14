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

                var di = new DOCINFOA { pDocName = docName, pDataType = "RAW" };
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
                                        success = WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out _);
                                    }
                                    finally
                                    {
                                        EndPagePrinter(hPrinter);
                                    }
                                }
                            }
                            finally
                            {
                                EndDocPrinter(hPrinter);
                            }
                        }
                    }
                    finally
                    {
                        ClosePrinter(hPrinter);
                    }
                }

                return success;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pUnmanagedBytes);
            }
        }
    }
}