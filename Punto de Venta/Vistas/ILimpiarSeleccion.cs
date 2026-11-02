namespace Punto_de_Venta
{
    /// <summary>
    /// Contrato para las vistas que permiten limpiar su seleccion actual.
    /// MainWindow invoca LimpiarSeleccion() con el atajo universal Ctrl + L.
    /// </summary>
    public interface ILimpiarSeleccion
    {
        void LimpiarSeleccion();
    }
}

