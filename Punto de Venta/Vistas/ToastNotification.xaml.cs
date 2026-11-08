using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Punto_de_Venta
{
    public enum ToastType
    {
        Success,
        Info,
        Warning,
        Error
    }

    public partial class ToastNotification : Window
    {
        private static ToastNotification? _currentToast;
        private readonly DispatcherTimer _timer;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private double _workLeft;
        private double _workWidth;

        private ToastNotification(string message, ToastType type)
        {
            InitializeComponent();

            SourceInitialized += (_, _) =>
            {
                var helper = new WindowInteropHelper(this);
                var exStyle = GetWindowLongPtr(helper.Handle, GWL_EXSTYLE);
                SetWindowLongPtr(helper.Handle, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_NOACTIVATE));
            };

            SizeChanged += (_, args) =>
            {
                if (args.WidthChanged && args.NewSize.Width > 0 && _workWidth > 0)
                {
                    Left = _workLeft + (_workWidth - args.NewSize.Width) / 2.0;
                }
            };

            switch (type)
            {
                case ToastType.Success:
                    IconText.Text = "✓";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(52, 199, 89));
                    break;
                case ToastType.Info:
                    IconText.Text = "ℹ";
                    IconText.Foreground = (Brush)FindResource("ThemeAccent");
                    break;
                case ToastType.Warning:
                    IconText.Text = "⚠";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 149, 0));
                    break;
                case ToastType.Error:
                    IconText.Text = "✕";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                    break;
            }

            MessageText.Text = message;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.3)
            };
            _timer.Tick += (_, _) => DesvanecerYCerrar();
        }

        public static void Show(string message, ToastType type = ToastType.Success)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Window? activeWindow = null;
                if (Application.Current != null)
                {
                    foreach (Window win in Application.Current.Windows)
                    {
                        if (win.IsActive && win is not ToastNotification && win.IsVisible)
                        {
                            activeWindow = win;
                            break;
                        }
                    }
                    if (activeWindow == null && Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                    {
                        activeWindow = Application.Current.MainWindow;
                    }
                }

                double workLeft = SystemParameters.WorkArea.Left;
                double workTop = SystemParameters.WorkArea.Top;
                double workWidth = SystemParameters.WorkArea.Width;
                double workHeight = SystemParameters.WorkArea.Height;

                if (activeWindow != null && activeWindow.IsLoaded)
                {
                    try
                    {
                        var helper = new WindowInteropHelper(activeWindow);
                        if (helper.Handle != IntPtr.Zero)
                        {
                            IntPtr hMonitor = MonitorFromWindow(helper.Handle, MONITOR_DEFAULTTONEAREST);
                            if (hMonitor != IntPtr.Zero)
                            {
                                var mi = new MONITORINFO();
                                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                                if (GetMonitorInfo(hMonitor, ref mi))
                                {
                                    var source = PresentationSource.FromVisual(activeWindow);
                                    if (source?.CompositionTarget != null)
                                    {
                                        Matrix matrix = source.CompositionTarget.TransformFromDevice;
                                        Point p1 = matrix.Transform(new Point(mi.rcWork.Left, mi.rcWork.Top));
                                        Point p2 = matrix.Transform(new Point(mi.rcWork.Right, mi.rcWork.Bottom));
                                        workLeft = p1.X;
                                        workTop = p1.Y;
                                        workWidth = Math.Max(100, p2.X - p1.X);
                                        workHeight = Math.Max(100, p2.Y - p1.Y);
                                    }
                                    else
                                    {
                                        workLeft = mi.rcWork.Left;
                                        workTop = mi.rcWork.Top;
                                        workWidth = Math.Max(100, mi.rcWork.Right - mi.rcWork.Left);
                                        workHeight = Math.Max(100, mi.rcWork.Bottom - mi.rcWork.Top);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        workLeft = SystemParameters.WorkArea.Left;
                        workTop = SystemParameters.WorkArea.Top;
                        workWidth = SystemParameters.WorkArea.Width;
                        workHeight = SystemParameters.WorkArea.Height;
                    }
                }

                _currentToast?.Close();
                _currentToast = new ToastNotification(message, type);
                _currentToast.PosicionarYAnimar(workLeft, workTop, workWidth, workHeight);
                _currentToast.Show();
            });
        }

        private void PosicionarYAnimar(double workLeft, double workTop, double workWidth, double workHeight)
        {
            _workLeft = workLeft;
            _workWidth = workWidth;

            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Arrange(new Rect(new Point(0, 0), DesiredSize));

            double toastWidth = DesiredSize.Width > 0 ? DesiredSize.Width : (ActualWidth > 0 ? ActualWidth : 260);
            Left = workLeft + (workWidth - toastWidth) / 2.0;

            double topInicial = workTop + 8;
            double topFinal = workTop + 24;
            Top = topInicial;
            Opacity = 0;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var slideDown = new DoubleAnimation(topInicial, topFinal, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeIn);
            BeginAnimation(TopProperty, slideDown);
            _timer.Start();
        }

        private void DesvanecerYCerrar()
        {
            _timer.Stop();
            double topActual = Top;
            double topFinal = topActual - 12;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var slideUp = new DoubleAnimation(topActual, topFinal, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (_, _) =>
            {
                Close();
                if (ReferenceEquals(_currentToast, this))
                {
                    _currentToast = null;
                }
            };

            BeginAnimation(OpacityProperty, fadeOut);
            BeginAnimation(TopProperty, slideUp);
        }
    }
}