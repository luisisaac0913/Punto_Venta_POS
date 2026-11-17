using Punto_de_Venta.Modelos;
using Punto_de_Venta.Vistas;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace Punto_de_Venta.Servicios
{
    public static class PantallaClienteService
    {
        private static PantallaClienteWindow? _ventana;
        private static IReadOnlyList<ItemCarrito> _ultimoCarrito = Array.Empty<ItemCarrito>();
        private static decimal _ultimoSubtotal;
        private static decimal _ultimoDescuento;
        private static decimal _ultimoTotalNeto;
        private static string _ultimoNombreNegocio = "Chip&Chapa";

        public static bool EstaAbierta => _ventana != null && _ventana.IsLoaded;
        public static event Action? EstadoCambiado;

        public static bool Alternar(Window? owner)
        {
            if (EstaAbierta)
            {
                Cerrar();
                return false;
            }

            Abrir(owner);
            return true;
        }

        public static void Abrir(Window? owner)
        {
            if (EstaAbierta)
            {
                _ventana!.Activate();
                return;
            }

            var ventana = new PantallaClienteWindow();
            ventana.Closed += (_, _) =>
            {
                _ventana = null;
                EstadoCambiado?.Invoke();
            };
            _ventana = ventana;

            var monitorSecundario = ObtenerMonitorSecundario();
            if (monitorSecundario.HasValue)
            {
                var r = monitorSecundario.Value;
                ventana.WindowStartupLocation = WindowStartupLocation.Manual;
                ventana.WindowState = WindowState.Normal;

                double dpiX = 1.0;
                double dpiY = 1.0;
                var source = owner != null ? PresentationSource.FromVisual(owner) : null;
                if (source?.CompositionTarget != null)
                {
                    dpiX = source.CompositionTarget.TransformToDevice.M11;
                    dpiY = source.CompositionTarget.TransformToDevice.M22;
                }

                ventana.Left = r.Left / dpiX;
                ventana.Top = r.Top / dpiY;
                ventana.Width = (r.Right - r.Left) / dpiX;
                ventana.Height = (r.Bottom - r.Top) / dpiY;
                ventana.Show();
                ventana.WindowState = WindowState.Maximized;
            }
            else
            {
                ventana.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                ventana.Show();
            }

            ventana.ActualizarCarrito(_ultimoCarrito, _ultimoSubtotal, _ultimoDescuento, _ultimoTotalNeto, _ultimoNombreNegocio);
            EstadoCambiado?.Invoke();
        }

        public static void Cerrar()
        {
            if (_ventana != null)
            {
                var v = _ventana;
                _ventana = null;
                try { v.Close(); } catch { }
                EstadoCambiado?.Invoke();
            }
        }

        public static void ActualizarCarrito(IReadOnlyList<ItemCarrito> carrito, decimal subtotal, decimal descuento, decimal totalNeto, string nombreNegocio)
        {
            _ultimoCarrito = new List<ItemCarrito>(carrito);
            _ultimoSubtotal = subtotal;
            _ultimoDescuento = descuento;
            _ultimoTotalNeto = totalNeto;
            if (!string.IsNullOrWhiteSpace(nombreNegocio))
            {
                _ultimoNombreNegocio = nombreNegocio;
            }

            _ventana?.Dispatcher.InvokeAsync(() =>
                _ventana?.ActualizarCarrito(_ultimoCarrito, _ultimoSubtotal, _ultimoDescuento, _ultimoTotalNeto, _ultimoNombreNegocio));
        }

        public static void MostrarGraciasPorSuCompra(decimal total, decimal pagoCon, decimal cambio)
        {
            _ventana?.Dispatcher.InvokeAsync(() =>
                _ventana?.MostrarPagoCompletado(total, pagoCon, cambio));
        }

        #region Deteccion Win32 de segundo monitor
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITORINFOF_PRIMARY = 0x00000001;
        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private static RECT? ObtenerMonitorSecundario()
        {
            RECT? secundario = null;
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
                {
                    var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(hMon, ref info) && (info.dwFlags & MONITORINFOF_PRIMARY) == 0)
                    {
                        secundario = info.rcMonitor;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }
            return secundario;
        }
        #endregion
    }
}

