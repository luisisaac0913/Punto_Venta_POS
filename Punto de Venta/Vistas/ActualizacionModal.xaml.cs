using Punto_de_Venta.Servicios;
using System;
using System.Windows;
using System.Windows.Threading;

namespace Punto_de_Venta.Vistas
{
    public partial class ActualizacionModal : Window
    {
        private readonly DispatcherTimer _timer;
        private int _segundosRestantes;

        public bool ActualizarAhora { get; private set; }

        public ActualizacionModal(string version, int segundos = 15)
        {
            InitializeComponent();
            _segundosRestantes = Math.Max(3, segundos);
            BarraTiempo.Maximum = _segundosRestantes;
            BarraTiempo.Value = _segundosRestantes;

            TxtSubtitulo.Text = IdiomaService.ObtenerFormato("UpdateSubtitle", "Se descargo la nueva version {0}.", version);
            ActualizarTextoContador();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            Loaded += (_, _) => _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _segundosRestantes--;
            ActualizarTextoContador();

            if (_segundosRestantes <= 0)
            {
                _timer.Stop();
                ActualizarAhora = true;
                DialogResult = true;
                Close();
            }
        }

        private void ActualizarTextoContador()
        {
            int mostrar = Math.Max(0, _segundosRestantes);
            TxtSegundosBadge.Text = $"{mostrar}s";
            BarraTiempo.Value = mostrar;
            TxtContador.Text = IdiomaService.ObtenerFormato("UpdateCountdownMsg", "La aplicacion se actualizara y reiniciara en {0} segundos...", mostrar);
        }

        private void BtnActualizarAhora_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            ActualizarAhora = true;
            DialogResult = true;
            Close();
        }

        private void BtnAlCerrar_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            ActualizarAhora = false;
            DialogResult = false;
            Close();
        }
    }
}

