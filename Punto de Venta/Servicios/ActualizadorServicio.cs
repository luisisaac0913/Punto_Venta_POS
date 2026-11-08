using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Punto_de_Venta.Servicios
{
    public static class ActualizadorServicio
    {
        private const string RepoUrl = "https://github.com/luisisaac0913/POS_Miscelanea_Jaz";

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

                gestor.WaitExitThenApplyUpdates(actualizacion, silent: true, restart: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ActualizadorServicio] Error en actualizacion silenciosa: {ex.Message}");
            }
        }
    }
}

