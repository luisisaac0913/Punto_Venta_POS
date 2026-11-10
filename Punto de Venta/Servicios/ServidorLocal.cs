using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Punto_de_Venta.Datos; 
using System.Linq;
using System.Threading.Tasks;

namespace Punto_de_Venta.Servicios
{
    public class ServidorLocal
    {
        private WebApplication? _app;

        public async Task IniciarAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://0.0.0.0:5000");
            builder.Services.AddCors();
            _app = builder.Build();
            _app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
            _app.MapGet("/", () =>
            {
                string html = @"
                    <html>
                    <head>
                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                        <style> body { font-family: sans-serif; padding: 20px; background: #121212; color: #fff; } </style>
                    </head>
                    <body>
                        <h2>📱 Inventario Móvil</h2>
                        <p>Conectado al POS de la caja.</p>
                        <!-- Aquí inyectaremos el escáner HTML5 después -->
                    </body>
                    </html>";
                return Results.Content(html, "text/html");
            });
            _app.MapGet("/api/producto/{codigo}", (string codigo) =>
            {
                var catalogo = ProductoDB.Load();
                var producto = catalogo.FirstOrDefault(p => p.Codigo == codigo);

                return producto != null ? Results.Ok(producto) : Results.NotFound();
            });
            await _app.StartAsync();
        }

        public async Task DetenerAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
            }
        }
    }
}