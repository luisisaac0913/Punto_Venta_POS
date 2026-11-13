using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Punto_de_Venta.Modelos
{
    public class NotaContacto : INotifyPropertyChanged
    {
        private int _id;
        private string _titulo = string.Empty;
        private string _telefono = string.Empty;
        private string _contenido = string.Empty;
        private DateTime _fechaModificacion = DateTime.Now;
        private bool _esFijado;

        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Titulo
        {
            get => _titulo;
            set
            {
                if (_titulo != value)
                {
                    _titulo = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TituloDisplay));
                }
            }
        }

        public string Telefono
        {
            get => _telefono;
            set
            {
                if (_telefono != value)
                {
                    _telefono = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TieneTelefono));
                }
            }
        }

        public string Contenido
        {
            get => _contenido;
            set
            {
                if (_contenido != value)
                {
                    _contenido = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Resumen));
                }
            }
        }

        public DateTime FechaModificacion
        {
            get => _fechaModificacion;
            set
            {
                if (_fechaModificacion != value)
                {
                    _fechaModificacion = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FechaFormateada));
                }
            }
        }

        public bool EsFijado
        {
            get => _esFijado;
            set
            {
                if (_esFijado != value)
                {
                    _esFijado = value;
                    OnPropertyChanged();
                }
            }
        }

        public string TituloDisplay => string.IsNullOrWhiteSpace(Titulo) ? "Sin titulo" : Titulo;

        public bool TieneTelefono => !string.IsNullOrWhiteSpace(Telefono);

        public string Resumen
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Contenido)) return "Sin contenido";
                string[] lineas = Contenido.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lineas.Length == 0) return "Sin contenido";
                string primeraLinea = lineas[0];
                return primeraLinea.Length > 60 ? primeraLinea.Substring(0, 57) + "..." : primeraLinea;
            }
        }

        public string FechaFormateada => FechaModificacion.ToString("dd/MM/yyyy hh:mm tt");

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

