using Punto_de_Venta.Servicios;
using Punto_de_Venta.Vistas;
using System.Windows;

namespace Punto_de_Venta
{
    public partial class ActivacionWindow : Window
    {
        private const string CLAVE_MAESTRA = "JAZ2026";

        public ActivacionWindow()
        {
            InitializeComponent();
            TxtPassword.Focus();
        }

        private void BtnActivar_Click(object sender, RoutedEventArgs e)
        {
            if (TxtPassword.Password == CLAVE_MAESTRA)
            {
                LicenciaService.ActivarLicencia();
                CustomMessageBox.Show("Equipo registrado exitosamente. La licencia ha sido enlazada a este hardware.", "Activación Completada", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true; 
                this.Close();
            }
            else
            {
                CustomMessageBox.Show("Clave Maestra incorrecta.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtPassword.Clear();
                TxtPassword.Focus();
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}