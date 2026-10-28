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
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        private ToastNotification(string message, ToastType type, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            SourceInitialized += (_, _) =>
            {
                var helper = new WindowInteropHelper(this);
                var exStyle = GetWindowLongPtr(helper.Handle, GWL_EXSTYLE);
                SetWindowLongPtr(helper.Handle, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_NOACTIVATE));
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
                var activeWindow = Application.Current.MainWindow;
                foreach (Window win in Application.Current.Windows)
                {
                    if (win.IsActive && win is not ToastNotification)
                    {
                        activeWindow = win;
                        break;
                    }
                }
                if (activeWindow == null || !activeWindow.IsVisible) return;
                _currentToast?.Close();
                _currentToast = new ToastNotification(message, type, activeWindow);
                _currentToast.Show();
                _currentToast.PosicionarYAnimar(activeWindow);
                activeWindow.Activate();
            });
        }
        private void PosicionarYAnimar(Window activeWindow)
        {
            this.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            this.Arrange(new Rect(new Point(0, 0), this.DesiredSize));
            double toastWidth = this.DesiredSize.Width > 0 ? this.DesiredSize.Width : this.ActualWidth;
            Left = activeWindow.Left + (activeWindow.ActualWidth - toastWidth) / 2;
            var topInicial = activeWindow.Top + 10;
            var topFinal = activeWindow.Top + 28;
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
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
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
        }
    }
}