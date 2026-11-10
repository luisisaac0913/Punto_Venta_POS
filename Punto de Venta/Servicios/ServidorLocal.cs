using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Punto_de_Venta.Datos;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;

namespace Punto_de_Venta.Servicios
{
    public record ActualizarProductoRequest(string? Codigo, string? Modo, decimal Cantidad);
    public record SeleccionarInventarioRequest(string? Codigo);
    public record CrearProductoRequest(string? Codigo, string? Nombre, decimal Precio, decimal Costo, decimal Stock, string? Categoria);

    public class ServidorLocal
    {
        private WebApplication? _app;

        private static X509Certificate2 ObtenerOCrearCertificado()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var certDir = Path.Combine(appData, "PuntoDeVenta");
            Directory.CreateDirectory(certDir);
            var certPath = Path.Combine(certDir, "servidor_ssl.pfx");
            const string certPassword = "ChipAndChapaSecureCert2026";

            if (File.Exists(certPath))
            {
                try
                {
                    var certCargado = new X509Certificate2(certPath, certPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                    if (certCargado.NotAfter > DateTime.Now.AddDays(30))
                    {
                        return certCargado;
                    }
                }
                catch
                {
                }
            }

            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=ChipAndChapaLocalServer",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);

            try
            {
                var hostName = Dns.GetHostName();
                sanBuilder.AddDnsName(hostName);
                var hostAddresses = Dns.GetHostAddresses(hostName);
                foreach (var ip in hostAddresses)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        sanBuilder.AddIpAddress(ip);
                    }
                }
            }
            catch
            {
            }

            request.CertificateExtensions.Add(sanBuilder.Build());
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

            using var certAutoFirmado = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(10));

            var pfxBytes = certAutoFirmado.Export(X509ContentType.Pfx, certPassword);
            File.WriteAllBytes(certPath, pfxBytes);

            return new X509Certificate2(pfxBytes, certPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        private static Punto_de_Venta.Modelos.Producto? BuscarProducto(string? codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return null;

            string limpio = codigo.Trim();
            string sinCeros = limpio.TrimStart('0');
            string conCero = "0" + limpio;

            try
            {
                var catalogo = ProductoDB.Load();

                var prod = catalogo.FirstOrDefault(p =>
                {
                    if (string.IsNullOrWhiteSpace(p.Codigo)) return false;
                    var c = p.Codigo.Trim();
                    return string.Equals(c, limpio, StringComparison.OrdinalIgnoreCase) ||
                           (!string.IsNullOrEmpty(sinCeros) && string.Equals(c.TrimStart('0'), sinCeros, StringComparison.OrdinalIgnoreCase)) ||
                           string.Equals(c, conCero, StringComparison.OrdinalIgnoreCase) ||
                           p.Id.ToString() == limpio;
                });

                if (prod != null) return prod;

                prod = ProductoDB.BuscarPorCodigoOId(limpio);
                if (prod != null) return prod;

                if (!string.IsNullOrEmpty(sinCeros))
                {
                    prod = ProductoDB.BuscarPorCodigoOId(sinCeros);
                }
                if (prod != null) return prod;

                prod = catalogo.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(p.Nombre) &&
                    string.Equals(p.Nombre.Trim(), limpio, StringComparison.OrdinalIgnoreCase));
                if (prod != null) return prod;

                prod = catalogo.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(p.Nombre) &&
                    p.Nombre.Contains(limpio, StringComparison.OrdinalIgnoreCase));

                return prod;
            }
            catch
            {
                return null;
            }
        }

        private static System.Collections.Generic.List<Punto_de_Venta.Modelos.Producto> BuscarProductosLista(string? query)
        {
            try
            {
                var catalogo = ProductoDB.Load();
                if (string.IsNullOrWhiteSpace(query))
                {
                    return catalogo.Take(150).ToList();
                }

                string q = query.Trim();
                string sinCeros = q.TrimStart('0');

                return catalogo.Where(p =>
                {
                    if (!string.IsNullOrWhiteSpace(p.Codigo) && (
                        p.Codigo.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(sinCeros) && p.Codigo.Contains(sinCeros, StringComparison.OrdinalIgnoreCase))))
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(p.Nombre) && p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (p.Id.ToString() == q)
                    {
                        return true;
                    }

                    return false;
                }).Take(150).ToList();
            }
            catch
            {
                return new System.Collections.Generic.List<Punto_de_Venta.Modelos.Producto>();
            }
        }

        public async Task IniciarAsync()
        {
            try
            {
                var builder = WebApplication.CreateBuilder();
                var cert = ObtenerOCrearCertificado();

                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(5000);
                    options.ListenAnyIP(5001, listenOptions =>
                    {
                        listenOptions.UseHttps(cert);
                    });
                });

                builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(options =>
                    {
                        options.LoginPath = "/login";
                        options.ExpireTimeSpan = TimeSpan.FromHours(8);
                        options.Cookie.SameSite = SameSiteMode.Lax;
                        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                        options.Events.OnRedirectToLogin = context =>
                        {
                            if (context.Request.Path.StartsWithSegments("/api"))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                return Task.CompletedTask;
                            }
                            context.Response.Redirect(context.RedirectUri);
                            return Task.CompletedTask;
                        };
                    });
                builder.Services.AddAuthorization();
                builder.Services.AddCors();

                _app = builder.Build();

                _app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

                _app.Use(async (ctx, next) =>
                {
                    if (!ctx.Request.IsHttps && ctx.Request.Host.Host != "localhost" && ctx.Request.Host.Host != "127.0.0.1")
                    {
                        var host = ctx.Request.Host.Host;
                        var query = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : "";
                        var httpsUrl = $"https://{host}:5001{ctx.Request.Path}{query}";
                        ctx.Response.Redirect(httpsUrl, permanent: false);
                        return;
                    }
                    await next();
                });

                _app.UseAuthentication();
                _app.UseAuthorization();

                _app.MapGet("/logo", () =>
                {
                    string ruta1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Imagenes", "CHC_Mascota.png");
                    if (File.Exists(ruta1)) return Results.File(ruta1, "image/png");

                    string ruta2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHC_Mascota.png");
                    if (File.Exists(ruta2)) return Results.File(ruta2, "image/png");

                    try
                    {
                        var uri = new Uri("pack://application:,,,/Imagenes/CHC_Mascota.png");
                        var sri = Application.GetResourceStream(uri);
                        if (sri != null)
                        {
                            using var ms = new MemoryStream();
                            sri.Stream.CopyTo(ms);
                            return Results.File(ms.ToArray(), "image/png");
                        }
                    }
                    catch
                    {
                    }

                    return Results.NotFound();
                }).AllowAnonymous();

                _app.MapGet("/login", (HttpContext ctx) =>
                {
                    string errorMsg = ctx.Request.Query.ContainsKey("error") ? "<p style='color:#FF6666; font-weight:bold; margin-bottom:12px;'>PIN Incorrecto</p>" : "";

                    string html = $@"
                        <!DOCTYPE html>
                        <html lang='es'>
                        <head>
                            <meta charset='UTF-8'>
                            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                            <title>Acceso - Chip&Chapa</title>
                            <style>
                                * {{ box-sizing: border-box; margin: 0; padding: 0; }}
                                body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #000000; color: #FFFFFF; text-align: center; padding: 40px 16px; }}
                                .card {{ background: #FFFFFF; color: #000000; max-width: 360px; margin: 0 auto; padding: 24px; border-radius: 4px; }}
                                input {{ width: 140px; padding: 12px; font-size: 24px; text-align: center; background: #FFFFFF; color: #000000; border: 2px solid #000000; border-radius: 4px; margin-bottom: 16px; }}
                                button {{ width: 100%; padding: 12px; font-size: 15px; font-weight: bold; background: #000000; color: #FFFFFF; border: none; border-radius: 4px; cursor: pointer; }}
                            </style>
                        </head>
                        <body>
                            <div class='card'>
                                <h2 style='font-size: 20px; font-weight: bold; margin-bottom: 8px;'>Acceso Restringido</h2>
                                <p style='font-size: 13px; color: #555555; margin-bottom: 16px;'>Ingresa tu PIN de empleado</p>
                                {errorMsg}
                                <form method='POST' action='/login'>
                                    <input type='password' name='pin' pattern='[0-9]*' inputmode='numeric' autofocus /><br>
                                    <button type='submit'>Entrar</button>
                                </form>
                            </div>
                        </body>
                        </html>";
                    return Results.Content(html, "text/html");
                });

                _app.MapPost("/login", async (HttpContext ctx) =>
                {
                    var form = await ctx.Request.ReadFormAsync();
                    string? pinIngresado = form["pin"];

                    if (pinIngresado == "1234")
                    {
                        var claims = new[] { new Claim(ClaimTypes.Name, "EmpleadoCaja") };
                        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

                        return Results.Redirect("/");
                    }

                    return Results.Redirect("/login?error=1");
                });

                _app.MapGet("/", () =>
                {
                    string html = @"
                        <!DOCTYPE html>
                        <html lang='es'>
                        <head>
                            <meta charset='UTF-8'>
                            <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no'>
                            <title>Chip&Chapa - Inventario Movil</title>
                            <script src='https://unpkg.com/html5-qrcode'></script>
                            <style>
                                * { box-sizing: border-box; margin: 0; padding: 0; }
                                body {
                                    background: #000000;
                                    color: #FFFFFF;
                                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                                    padding: 16px;
                                    text-align: center;
                                }
                                .header {
                                    margin-bottom: 18px;
                                }
                                .header img {
                                    width: 72px;
                                    height: 72px;
                                    object-fit: contain;
                                    display: block;
                                    margin: 0 auto;
                                }
                                .header a {
                                    color: #FFFFFF;
                                    font-size: 18px;
                                    font-weight: bold;
                                    text-decoration: underline;
                                    margin-top: 6px;
                                    display: inline-block;
                                }
                                .header .sub {
                                    color: #888888;
                                    font-size: 12px;
                                    letter-spacing: 1px;
                                    margin-top: 3px;
                                    text-transform: uppercase;
                                }
                                .tabs {
                                    display: flex;
                                    gap: 8px;
                                    max-width: 480px;
                                    margin: 0 auto 16px auto;
                                }
                                .tab-btn {
                                    flex: 1;
                                    padding: 10px;
                                    font-size: 13px;
                                    font-weight: bold;
                                    cursor: pointer;
                                    border-radius: 4px;
                                    background: #000000;
                                    color: #FFFFFF;
                                    border: 1px solid #555555;
                                }
                                .tab-btn.active {
                                    background: #FFFFFF;
                                    color: #000000;
                                    border: 1px solid #FFFFFF;
                                }
                                .card {
                                    background: #FFFFFF;
                                    color: #000000;
                                    padding: 16px;
                                    border-radius: 4px;
                                    max-width: 480px;
                                    margin: 0 auto 16px auto;
                                    text-align: left;
                                }
                                .card-title {
                                    font-size: 18px;
                                    font-weight: bold;
                                    margin-bottom: 4px;
                                    color: #000000;
                                }
                                .card-subtitle {
                                    font-size: 13px;
                                    color: #444444;
                                    margin-bottom: 8px;
                                }
                                .input-field {
                                    width: 100%;
                                    padding: 11px;
                                    font-size: 14px;
                                    background: #FFFFFF;
                                    color: #000000;
                                    border: 1px solid #000000;
                                    border-radius: 4px;
                                    margin-bottom: 8px;
                                }
                                .btn {
                                    width: 100%;
                                    padding: 12px;
                                    font-size: 14px;
                                    font-weight: bold;
                                    cursor: pointer;
                                    border-radius: 4px;
                                    background: #FFFFFF;
                                    color: #000000;
                                    border: 2px solid #000000;
                                    margin-top: 8px;
                                    display: block;
                                    text-align: center;
                                }
                                .btn:active {
                                    background: #E5E5E5;
                                }
                                .btn-dark {
                                    background: #000000;
                                    color: #FFFFFF;
                                    border: 2px solid #000000;
                                }
                                .btn-dark:active {
                                    background: #222222;
                                }
                                .quick-grid {
                                    display: grid;
                                    grid-template-columns: repeat(4, 1fr);
                                    gap: 6px;
                                    margin: 8px 0;
                                }
                                .quick-btn {
                                    padding: 10px 0;
                                    font-size: 14px;
                                    font-weight: bold;
                                    background: #FFFFFF;
                                    color: #000000;
                                    border: 1px solid #000000;
                                    border-radius: 4px;
                                    cursor: pointer;
                                }
                                .quick-btn:active {
                                    background: #E5E5E5;
                                }
                                .stock-box {
                                    border: 2px solid #000000;
                                    padding: 12px;
                                    border-radius: 4px;
                                    text-align: center;
                                    margin: 12px 0;
                                    background: #FFFFFF;
                                }
                                .stock-label {
                                    font-size: 11px;
                                    font-weight: bold;
                                    color: #000000;
                                    letter-spacing: 1px;
                                    display: block;
                                }
                                .stock-val {
                                    font-size: 28px;
                                    font-weight: bold;
                                    color: #000000;
                                }
                                .item-row {
                                    background: #FFFFFF;
                                    color: #000000;
                                    border: 1px solid #000000;
                                    border-radius: 4px;
                                    padding: 12px;
                                    margin-bottom: 8px;
                                    cursor: pointer;
                                    text-align: left;
                                }
                                .item-row:active {
                                    background: #E5E5E5;
                                }
                                .status-msg {
                                    color: #FFFFFF;
                                    font-size: 14px;
                                    font-weight: bold;
                                    text-align: center;
                                    margin-top: 10px;
                                    min-height: 20px;
                                }
                                #reader {
                                    width: 100%;
                                    max-width: 480px;
                                    margin: 0 auto;
                                    border-radius: 4px;
                                    overflow: hidden;
                                    border: 1px solid #444444;
                                    background: #111111;
                                }
                            </style>
                        </head>
                        <body>
                            <div class='header'>
                                <img src='/logo' alt='Chip&Chapa' />
                                <a href='https://www.chipandchapa.com' target='_blank'>Chip&Chapa</a>
                                <div class='sub'>Inventario Movil</div>
                            </div>

                            <div class='tabs'>
                                <button id='tabBuscarBtn' type='button' class='tab-btn active' onclick=""cambiarTab('buscar')"">Buscar y Catalogo</button>
                                <button id='tabCamaraBtn' type='button' class='tab-btn' onclick=""cambiarTab('camara')"">Camara Scanner</button>
                            </div>

                            <div id='panelBuscar'>
                                <div class='card'>
                                    <input id='searchInput' type='text' class='input-field' placeholder='Escribe codigo o nombre...' onkeydown='if(event.key===""Enter"") buscarProductos();' />
                                    <div style='display:flex; gap:8px;'>
                                        <button type='button' class='btn' onclick='buscarProductos()'>Buscar</button>
                                        <button type='button' class='btn' onclick='cargarTodoElInventario()'>Ver Todo</button>
                                    </div>
                                </div>

                                <div id='listaContainer' style='max-width:480px; margin:0 auto;'></div>
                            </div>

                            <div id='panelCamara' style='display:none;'>
                                <div id='reader'></div>
                                <div style='margin-top:10px; color:#888888; font-size:12px;'>Apunta la camara al codigo de barras</div>
                            </div>

                            <div id='panelDetalle' class='card' style='display:none;'>
                                <div id='detNombre' class='card-title'></div>
                                <div id='detInfo' class='card-subtitle'></div>
                                <div id='detPrecios' class='card-subtitle'></div>

                                <div class='stock-box'>
                                    <span class='stock-label'>STOCK ACTUAL EN SISTEMA</span>
                                    <span id='detStock' class='stock-val'>0</span>
                                </div>

                                <label style='font-size:12px; font-weight:bold; color:#000000; display:block; margin-bottom:4px;'>CANTIDAD A APLICAR:</label>
                                <input id='inputCantidad' type='number' class='input-field' value='1' min='0.001' step='any' style='text-align:center; font-weight:bold; font-size:18px;' />

                                <div class='quick-grid'>
                                    <button type='button' class='quick-btn' onclick='sumarACantidad(1)'>+1</button>
                                    <button type='button' class='quick-btn' onclick='sumarACantidad(6)'>+6</button>
                                    <button type='button' class='quick-btn' onclick='sumarACantidad(12)'>+12</button>
                                    <button type='button' class='quick-btn' onclick='sumarACantidad(24)'>+24</button>
                                </div>

                                <button type='button' class='btn' onclick=""enviarCambioStock('sumar')"">Sumar al Stock (Reabastecer)</button>
                                <button type='button' class='btn' onclick=""enviarCambioStock('fijar')"">Fijar Stock Exacto</button>
                                <button type='button' class='btn' onclick='seleccionarEnPC()'>Seleccionar en PC (Inventario)</button>
                                <button type='button' class='btn btn-dark' style='margin-top:14px;' onclick='cerrarDetalle()'>Volver</button>
                            </div>

                            <div id='panelNoRegistrado' class='card' style='display:none;'>
                                <div class='card-title'>Producto no registrado</div>
                                <div id='noRegCodigo' class='card-subtitle'></div>

                                <button type='button' class='btn' onclick='enviarNuevoCodigoAPC()'>Enviar codigo a PC</button>
                                <button type='button' class='btn' onclick='toggleAltaForm()'>Dar de alta desde el movil</button>

                                <div id='altaForm' style='display:none; margin-top:14px; border-top:1px solid #000000; padding-top:12px;'>
                                    <label style='font-size:12px; font-weight:bold; color:#000000; display:block; margin-bottom:2px;'>NOMBRE:</label>
                                    <input id='nuevoNombre' type='text' class='input-field' placeholder='Nombre del producto' />

                                    <label style='font-size:12px; font-weight:bold; color:#000000; display:block; margin-bottom:2px;'>PRECIO DE VENTA ($):</label>
                                    <input id='nuevoPrecio' type='number' step='any' class='input-field' value='0.00' />

                                    <label style='font-size:12px; font-weight:bold; color:#000000; display:block; margin-bottom:2px;'>COSTO ($):</label>
                                    <input id='nuevoCosto' type='number' step='any' class='input-field' value='0.00' />

                                    <label style='font-size:12px; font-weight:bold; color:#000000; display:block; margin-bottom:2px;'>STOCK INICIAL:</label>
                                    <input id='nuevoStock' type='number' step='any' class='input-field' value='1' />

                                    <label style='font-size:12px; font-weight:bold; color:#000000; display:block; margin-bottom:2px;'>CATEGORIA:</label>
                                    <input id='nuevoCategoria' type='text' class='input-field' value='General' />

                                    <button type='button' class='btn' onclick='guardarAltaMovil()'>Guardar Producto</button>
                                </div>

                                <button type='button' class='btn btn-dark' style='margin-top:14px;' onclick='cerrarNoRegistrado()'>Volver</button>
                            </div>

                            <div id='statusBox' class='status-msg'></div>

                            <script>
                                let currentProduct = null;
                                let currentCode = '';
                                let scanner = null;
                                let scannerIniciado = false;

                                function cambiarTab(tab) {
                                    document.getElementById('statusBox').innerText = '';
                                    document.getElementById('panelDetalle').style.display = 'none';
                                    document.getElementById('panelNoRegistrado').style.display = 'none';

                                    if (tab === 'buscar') {
                                        document.getElementById('tabBuscarBtn').className = 'tab-btn active';
                                        document.getElementById('tabCamaraBtn').className = 'tab-btn';
                                        document.getElementById('panelBuscar').style.display = 'block';
                                        document.getElementById('panelCamara').style.display = 'none';
                                        detenerScanner();
                                    } else {
                                        document.getElementById('tabBuscarBtn').className = 'tab-btn';
                                        document.getElementById('tabCamaraBtn').className = 'tab-btn active';
                                        document.getElementById('panelBuscar').style.display = 'none';
                                        document.getElementById('panelCamara').style.display = 'block';
                                        iniciarScanner();
                                    }
                                }

                                function iniciarScanner() {
                                    if (scannerIniciado) {
                                        try { if (scanner) scanner.resume(); } catch(e) {}
                                        return;
                                    }
                                    try {
                                        scanner = new Html5QrcodeScanner('reader', { fps: 10, qrbox: { width: 280, height: 120 } }, false);
                                        scanner.render(onScanSuccess, onScanFailure);
                                        scannerIniciado = true;
                                    } catch(e) {}
                                }

                                function detenerScanner() {
                                    try { if (scanner) scanner.pause(true); } catch(e) {}
                                }

                                function onScanSuccess(decodedText) {
                                    detenerScanner();
                                    cargarProducto(decodedText);
                                }

                                function onScanFailure(err) {}

                                function buscarProductos() {
                                    const q = (document.getElementById('searchInput').value || '').trim();
                                    mostrarStatus('Buscando...', null);
                                    fetch('/api/inventario/lista?q=' + encodeURIComponent(q))
                                        .then(r => r.json())
                                        .then(data => {
                                            mostrarStatus('', null);
                                            renderizarLista(data.productos || []);
                                        })
                                        .catch(err => {
                                            mostrarStatus('Error al conectar con el servidor', false);
                                        });
                                }

                                function cargarTodoElInventario() {
                                    document.getElementById('searchInput').value = '';
                                    buscarProductos();
                                }

                                function renderizarLista(items) {
                                    const container = document.getElementById('listaContainer');
                                    container.innerHTML = '';
                                    if (!items || items.length === 0) {
                                        container.innerHTML = ""<div style='color:#888888; padding:20px; text-align:center;'>No se encontraron productos</div>"";
                                        return;
                                    }

                                    items.forEach(p => {
                                        const div = document.createElement('div');
                                        div.className = 'item-row';
                                        div.onclick = () => { abrirDetalle(p); };
                                        div.innerHTML = ""<div style='font-size:15px; font-weight:bold; margin-bottom:4px;'>"" + (p.nombre || 'Sin nombre') + ""</div>"" +
                                            ""<div style='font-size:12px; color:#555555; margin-bottom:4px;'>Codigo: "" + (p.codigo || 'N/A') + "" | Categoria: "" + (p.categoria || 'General') + ""</div>"" +
                                            ""<div style='font-size:14px; font-weight:bold; display:flex; justify-content:space-between;'>"" +
                                            ""<span>Precio: $"" + Number(p.precio || 0).toFixed(2) + ""</span>"" +
                                            ""<span>Stock: "" + (p.stock ?? 0) + ""</span>"" +
                                            ""</div>"";
                                        container.appendChild(div);
                                    });
                                }

                                function cargarProducto(codigo) {
                                    codigo = (codigo || '').toString().trim();
                                    currentCode = codigo;
                                    mostrarStatus('Buscando producto...', null);

                                    fetch('/api/producto?codigo=' + encodeURIComponent(codigo))
                                        .then(async r => {
                                            const data = await r.json();
                                            if (r.ok && data.ok) return data;
                                            throw new Error(data && data.mensaje ? data.mensaje : 'No registrado');
                                        })
                                        .then(data => {
                                            mostrarStatus('', null);
                                            abrirDetalle(data);
                                        })
                                        .catch(err => {
                                            mostrarStatus('', null);
                                            abrirNoRegistrado(codigo);
                                        });
                                }

                                function abrirDetalle(prod) {
                                    currentProduct = prod;
                                    currentCode = prod.codigo || '';

                                    document.getElementById('detNombre').innerText = prod.nombre || 'Sin nombre';
                                    document.getElementById('detInfo').innerText = 'Codigo: ' + (prod.codigo || 'N/A') + ' | Categoria: ' + (prod.categoria || 'General');
                                    document.getElementById('detPrecios').innerText = 'Precio: $' + Number(prod.precio || 0).toFixed(2) + ' | Costo: $' + Number(prod.costo || 0).toFixed(2);
                                    document.getElementById('detStock').innerText = prod.stock ?? prod.cantidad ?? 0;
                                    document.getElementById('inputCantidad').value = 1;

                                    document.getElementById('panelBuscar').style.display = 'none';
                                    document.getElementById('panelCamara').style.display = 'none';
                                    document.getElementById('panelNoRegistrado').style.display = 'none';
                                    document.getElementById('panelDetalle').style.display = 'block';
                                }

                                function cerrarDetalle() {
                                    document.getElementById('panelDetalle').style.display = 'none';
                                    if (document.getElementById('tabCamaraBtn').className.includes('active')) {
                                        document.getElementById('panelCamara').style.display = 'block';
                                        try { if (scanner) scanner.resume(); } catch(e) {}
                                    } else {
                                        document.getElementById('panelBuscar').style.display = 'block';
                                    }
                                }

                                function abrirNoRegistrado(codigo) {
                                    currentProduct = null;
                                    currentCode = codigo;

                                    document.getElementById('noRegCodigo').innerText = 'Codigo leido: ' + codigo;
                                    document.getElementById('altaForm').style.display = 'none';
                                    document.getElementById('nuevoNombre').value = '';
                                    document.getElementById('nuevoPrecio').value = '0.00';
                                    document.getElementById('nuevoCosto').value = '0.00';
                                    document.getElementById('nuevoStock').value = '1';

                                    document.getElementById('panelBuscar').style.display = 'none';
                                    document.getElementById('panelCamara').style.display = 'none';
                                    document.getElementById('panelDetalle').style.display = 'none';
                                    document.getElementById('panelNoRegistrado').style.display = 'block';
                                }

                                function cerrarNoRegistrado() {
                                    document.getElementById('panelNoRegistrado').style.display = 'none';
                                    if (document.getElementById('tabCamaraBtn').className.includes('active')) {
                                        document.getElementById('panelCamara').style.display = 'block';
                                        try { if (scanner) scanner.resume(); } catch(e) {}
                                    } else {
                                        document.getElementById('panelBuscar').style.display = 'block';
                                    }
                                }

                                function toggleAltaForm() {
                                    const f = document.getElementById('altaForm');
                                    f.style.display = f.style.display === 'none' ? 'block' : 'none';
                                }

                                function sumarACantidad(val) {
                                    let actual = parseFloat(document.getElementById('inputCantidad').value) || 0;
                                    document.getElementById('inputCantidad').value = actual + val;
                                }

                                function enviarCambioStock(modo) {
                                    if (!currentProduct) return;
                                    const cant = parseFloat(document.getElementById('inputCantidad').value);
                                    if (isNaN(cant) || cant <= 0) {
                                        mostrarStatus('Ingresa una cantidad valida mayor a 0', false);
                                        return;
                                    }

                                    mostrarStatus('Guardando cambios en sistema...', null);

                                    fetch('/api/producto/actualizar', {
                                        method: 'POST',
                                        headers: { 'Content-Type': 'application/json' },
                                        body: JSON.stringify({
                                            codigo: currentProduct.codigo,
                                            modo: modo,
                                            cantidad: cant
                                        })
                                    })
                                    .then(r => r.json())
                                    .then(res => {
                                        if (res.ok) {
                                            currentProduct.stock = res.stock;
                                            document.getElementById('detStock').innerText = res.stock;
                                            mostrarStatus(res.mensaje + ' (Reflejado en PC)', true);
                                        } else {
                                            mostrarStatus(res.mensaje || 'Error al actualizar', false);
                                        }
                                    })
                                    .catch(err => {
                                        mostrarStatus('Error de conexion con la PC', false);
                                    });
                                }

                                function seleccionarEnPC() {
                                    if (!currentProduct) return;
                                    mostrarStatus('Seleccionando en PC...', null);
                                    fetch('/api/inventario/seleccionar', {
                                        method: 'POST',
                                        headers: { 'Content-Type': 'application/json' },
                                        body: JSON.stringify({ codigo: currentProduct.codigo })
                                    })
                                    .then(r => r.json())
                                    .then(res => {
                                        if (res.ok) {
                                            mostrarStatus('Producto seleccionado en el Inventario de la PC', true);
                                        } else {
                                            mostrarStatus(res.mensaje || 'No se pudo seleccionar en PC', false);
                                        }
                                    })
                                    .catch(err => {
                                        mostrarStatus('Error de conexion con la PC', false);
                                    });
                                }

                                function enviarNuevoCodigoAPC() {
                                    mostrarStatus('Enviando codigo a PC...', null);
                                    fetch('/api/inventario/nuevo', {
                                        method: 'POST',
                                        headers: { 'Content-Type': 'application/json' },
                                        body: JSON.stringify({ codigo: currentCode })
                                    })
                                    .then(r => r.json())
                                    .then(res => {
                                        if (res.ok) {
                                            mostrarStatus('Codigo cargado en el formulario de la PC', true);
                                        } else {
                                            mostrarStatus(res.mensaje || 'No se pudo cargar en PC', false);
                                        }
                                    })
                                    .catch(err => {
                                        mostrarStatus('Error de conexion con la PC', false);
                                    });
                                }

                                function guardarAltaMovil() {
                                    const nombre = (document.getElementById('nuevoNombre').value || '').trim();
                                    if (!nombre) {
                                        mostrarStatus('Escribe el nombre del producto', false);
                                        return;
                                    }
                                    const precio = parseFloat(document.getElementById('nuevoPrecio').value) || 0;
                                    const costo = parseFloat(document.getElementById('nuevoCosto').value) || 0;
                                    const stock = parseFloat(document.getElementById('nuevoStock').value) || 0;
                                    const categoria = (document.getElementById('nuevoCategoria').value || 'General').trim();

                                    mostrarStatus('Registrando nuevo producto...', null);

                                    fetch('/api/inventario/crear', {
                                        method: 'POST',
                                        headers: { 'Content-Type': 'application/json' },
                                        body: JSON.stringify({
                                            codigo: currentCode,
                                            nombre: nombre,
                                            precio: precio,
                                            costo: costo,
                                            stock: stock,
                                            categoria: categoria
                                        })
                                    })
                                    .then(r => r.json())
                                    .then(res => {
                                        if (res.ok) {
                                            mostrarStatus(res.mensaje, true);
                                            setTimeout(() => { cargarProducto(currentCode); }, 1200);
                                        } else {
                                            mostrarStatus(res.mensaje || 'Error al guardar', false);
                                        }
                                    })
                                    .catch(err => {
                                        mostrarStatus('Error de conexion con la PC', false);
                                    });
                                }

                                function mostrarStatus(txt, exito) {
                                    const el = document.getElementById('statusBox');
                                    el.innerText = txt;
                                    if (exito === true) {
                                        el.style.color = '#FFFFFF';
                                    } else if (exito === false) {
                                        el.style.color = '#FF6666';
                                    } else {
                                        el.style.color = '#AAAAAA';
                                    }
                                }

                                buscarProductos();
                            </script>
                        </body>
                        </html>";
                    return Results.Content(html, "text/html");
                }).AllowAnonymous();

                _app.MapGet("/api/inventario/lista", (HttpContext ctx) =>
                {
                    string? q = ctx.Request.Query["q"];
                    var lista = BuscarProductosLista(q);
                    var resultado = lista.Select(p => new
                    {
                        id = p.Id,
                        codigo = p.Codigo,
                        nombre = p.Nombre,
                        categoria = p.Categoria,
                        precio = p.Precio,
                        costo = p.Costo,
                        stock = p.Stock
                    });
                    return Results.Ok(new { ok = true, productos = resultado });
                }).AllowAnonymous();

                _app.MapGet("/api/producto", (HttpContext ctx) =>
                {
                    string? codigo = ctx.Request.Query["codigo"];
                    if (string.IsNullOrWhiteSpace(codigo))
                    {
                        return Results.BadRequest(new { ok = false, mensaje = "Codigo no proporcionado" });
                    }

                    var producto = BuscarProducto(codigo);
                    if (producto == null)
                    {
                        return Results.NotFound(new { ok = false, mensaje = "Producto no encontrado en inventario" });
                    }

                    return Results.Ok(new
                    {
                        ok = true,
                        id = producto.Id,
                        codigo = producto.Codigo,
                        nombre = producto.Nombre,
                        categoria = producto.Categoria,
                        precio = producto.Precio,
                        costo = producto.Costo,
                        stock = producto.Stock,
                        cantidad = producto.Stock
                    });
                }).AllowAnonymous();

                _app.MapGet("/api/producto/{**codigo}", (string codigo) =>
                {
                    var producto = BuscarProducto(codigo);
                    if (producto == null)
                    {
                        return Results.NotFound(new { ok = false, mensaje = "Producto no encontrado en inventario" });
                    }

                    return Results.Ok(new
                    {
                        ok = true,
                        id = producto.Id,
                        codigo = producto.Codigo,
                        nombre = producto.Nombre,
                        categoria = producto.Categoria,
                        precio = producto.Precio,
                        costo = producto.Costo,
                        stock = producto.Stock,
                        cantidad = producto.Stock
                    });
                }).AllowAnonymous();

                _app.MapPost("/api/producto/actualizar", async (HttpContext ctx) =>
                {
                    try
                    {
                        var req = await ctx.Request.ReadFromJsonAsync<ActualizarProductoRequest>();
                        if (req == null || string.IsNullOrWhiteSpace(req.Codigo))
                        {
                            return Results.BadRequest(new { ok = false, mensaje = "Datos invalidos" });
                        }

                        var producto = BuscarProducto(req.Codigo);
                        if (producto == null)
                        {
                            return Results.NotFound(new { ok = false, mensaje = "Producto no encontrado" });
                        }

                        bool esSuma = string.Equals(req.Modo, "sumar", StringComparison.OrdinalIgnoreCase);
                        if (esSuma)
                        {
                            producto.Stock += req.Cantidad;
                        }
                        else
                        {
                            producto.Stock = req.Cantidad;
                        }

                        ProductoDB.Save(new[] { producto });

                        var codProd = producto.Codigo;
                        var nStock = producto.Stock;
                        var nomProd = producto.Nombre;
                        var cantMod = req.Cantidad;

                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            if (Application.Current.MainWindow is MainWindow mw)
                            {
                                mw.ActualizarStockDesdeMovil(codProd, nStock, nomProd, cantMod, esSuma);
                            }
                        }));

                        return Results.Ok(new
                        {
                            ok = true,
                            mensaje = esSuma ? $"+{cantMod} piezas sumadas" : $"Stock fijado en {nStock}",
                            nombre = producto.Nombre,
                            stock = producto.Stock,
                            cantidad = producto.Stock
                        });
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(new { ok = false, mensaje = "Error en base de datos: " + ex.Message }, statusCode: 500);
                    }
                }).AllowAnonymous();

                _app.MapPost("/api/inventario/seleccionar", async (HttpContext ctx) =>
                {
                    var req = await ctx.Request.ReadFromJsonAsync<SeleccionarInventarioRequest>();
                    if (req == null || string.IsNullOrWhiteSpace(req.Codigo))
                    {
                        return Results.BadRequest(new { ok = false, mensaje = "Codigo invalido" });
                    }

                    bool seleccionado = false;
                    if (Application.Current?.Dispatcher != null)
                    {
                        seleccionado = await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (Application.Current.MainWindow is MainWindow mw)
                            {
                                mw.AbrirInventarioYSeleccionar(req.Codigo);
                                return true;
                            }
                            return false;
                        });
                    }

                    return Results.Ok(new { ok = seleccionado, mensaje = seleccionado ? "Producto seleccionado en PC" : "No se pudo seleccionar" });
                }).AllowAnonymous();

                _app.MapPost("/api/inventario/nuevo", async (HttpContext ctx) =>
                {
                    var req = await ctx.Request.ReadFromJsonAsync<SeleccionarInventarioRequest>();
                    string cod = req?.Codigo ?? string.Empty;

                    bool abierto = false;
                    if (Application.Current?.Dispatcher != null)
                    {
                        abierto = await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (Application.Current.MainWindow is MainWindow mw)
                            {
                                mw.AbrirInventarioYNuevoCodigo(cod);
                                return true;
                            }
                            return false;
                        });
                    }

                    return Results.Ok(new { ok = abierto, mensaje = abierto ? "Codigo enviado al formulario en PC" : "No se pudo enviar" });
                }).AllowAnonymous();

                _app.MapPost("/api/inventario/crear", async (HttpContext ctx) =>
                {
                    var req = await ctx.Request.ReadFromJsonAsync<CrearProductoRequest>();
                    if (req == null || string.IsNullOrWhiteSpace(req.Nombre))
                    {
                        return Results.BadRequest(new { ok = false, mensaje = "Nombre de producto requerido" });
                    }

                    bool creado = false;
                    if (Application.Current?.Dispatcher != null)
                    {
                        creado = await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (Application.Current.MainWindow is MainWindow mw)
                            {
                                return mw.CrearProductoEnInventarioDesdeMovil(
                                    req.Codigo ?? string.Empty,
                                    req.Nombre,
                                    req.Precio,
                                    req.Costo,
                                    req.Stock,
                                    req.Categoria ?? "General");
                            }
                            return false;
                        });
                    }

                    return Results.Ok(new { ok = creado, mensaje = creado ? "Producto registrado con exito" : "No se pudo registrar el producto" });
                }).AllowAnonymous();

                await _app.StartAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ServidorLocal] Error al iniciar servidor: {ex.Message}");
            }
        }

        public async Task DetenerAsync()
        {
            if (_app != null)
            {
                try
                {
                    await _app.StopAsync();
                }
                catch
                {
                }
            }
        }
    }
}