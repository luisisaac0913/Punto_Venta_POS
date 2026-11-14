namespace Punto_de_Venta.Vistas
{
    public interface IManejadorEscape
    {
        bool ManejarEscape();
        bool TieneModalActivo => false;
    }
}

