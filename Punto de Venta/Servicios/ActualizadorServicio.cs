using Punto_de_Venta.Vistas;
using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace Punto_de_Venta.Servicios
{
    public static class ActualizadorServicio
    {
        private const string RepoUrl = "https://github.com/luisisaac0913/POS_Miscelanea_Jaz";
        private const int SegundosAvisoActualizacion = 15;

        public static async Task EjecutarActualizacionSilenciosaAsync()
        {
            try
            {
                var fuente = new GithubSource(RepoUrl, accessToken: null, prerelease: false);
                var gestor = new UpdateManager(fuente);

                if (!gestor.IsInstalled)
                {
                    return;
                }

                var actualizacion = await gestor.CheckForUpdatesAsync().ConfigureAwait(false);
                if (actualizacion == null)
                {
                    return;
                }

                await gestor.DownloadUpdatesAsync(actualizacion).ConfigureAwait(false);

                string version = actualizacion.TargetFullRelease.Version.ToString();
                bool actualizarAhora = false;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                {
                    actualizarAhora = await dispatcher.InvokeAsync(() =>
                    {
                        var modal = new ActualizacionModal(version, SegundosAvisoActualizacion);
                        if (Application.Current?.MainWindow is Window owner && owner.IsLoaded && owner.IsVisible)
                        {
                            modal.Owner = owner;
                        }
                        return modal.ShowDialog() == true && modal.ActualizarAhora;
                    });
                }

                if (actualizarAhora)
                {
                    gestor.ApplyUpdatesAndRestart(actualizacion);
                }
                else
                {
                    gestor.WaitExitThenApplyUpdates(actualizacion, silent: true, restart: false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ActualizadorServicio] Error en actualizacion silenciosa: {ex.Message}");
            }
        }
    }
}

