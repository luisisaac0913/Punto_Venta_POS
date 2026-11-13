using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Punto_de_Venta.Vistas
{
    public static class ModalFocusHelper
    {
        public static bool EsDescendienteDe(DependencyObject hijo, DependencyObject ancestro)
        {
            if (hijo == null || ancestro == null) return false;
            DependencyObject actual = hijo;
            while (actual != null)
            {
                if (ReferenceEquals(actual, ancestro)) return true;
                actual = VisualTreeHelper.GetParent(actual) ?? LogicalTreeHelper.GetParent(actual);
            }
            return false;
        }

        public static void EnfocarModal(UIElement modal)
        {
            if (modal == null) return;
            modal.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                modal.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                if (!modal.IsKeyboardFocusWithin)
                {
                    modal.Focus();
                    Keyboard.Focus(modal);
                }
            }));
        }

        public static void ProtegerFoco(KeyboardFocusChangedEventArgs e, UIElement modalActivo)
        {
            if (modalActivo == null) return;
            if (e.NewFocus is DependencyObject dep && !EsDescendienteDe(dep, modalActivo))
            {
                e.Handled = true;
                EnfocarModal(modalActivo);
            }
        }
    }
}
