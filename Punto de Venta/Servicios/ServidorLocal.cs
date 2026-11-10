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
            return prod;
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

                _app.MapGet("/login", (HttpContext ctx) =>
                {
                    string errorMsg = ctx.Request.Query.ContainsKey("error") ? "<p style='color:#EF4444; font-weight:bold;'>PIN Incorrecto</p>" : "";

                    string html = $@"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <meta charset='UTF-8'>
                            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                            <title>Acceso Restringido</title>
                        </head>
                        <body style='font-family: sans-serif; background: #121212; color: #fff; text-align: center; padding-top: 40px;'>
                            <h2>🔒 Acceso Restringido</h2>
                            <p>Ingresa tu PIN de empleado</p>
                            {errorMsg}
                            <form method='POST' action='/login'>
                                <input type='password' name='pin' pattern='[0-9]*' inputmode='numeric' autofocus
                                       style='padding: 12px; font-size: 24px; text-align: center; width: 150px; border-radius: 8px; border: none;'><br><br>
                                <button type='submit' style='padding: 12px 30px; font-size: 16px; background: #007AFF; color: white; border: none; border-radius: 8px; font-weight:bold;'>Entrar</button>
                            </form>
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
                            <title>Inventario Movil Chip&Chapa</title>
                            <script src='https://unpkg.com/html5-qrcode'></script>
                            <style>
                                * { box-sizing: border-box; }
                                body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0F172A; color: #F8FAFC; margin: 0; padding: 14px; text-align: center; }
                                h2 { margin: 6px 0 14px 0; font-size: 19px; font-weight: bold; color: #38BDF8; }
                                .search-bar { display: flex; gap: 8px; max-width: 480px; margin: 0 auto 12px auto; }
                                .search-input { flex: 1; padding: 12px; font-size: 15px; border-radius: 8px; border: 1px solid #475569; background: #1E293B; color: #fff; text-align: center; }
                                .search-btn { padding: 12px 18px; font-size: 15px; font-weight: bold; background: #0284C7; color: #fff; border: none; border-radius: 8px; cursor: pointer; }
                                #reader { width: 100%; max-width: 480px; margin: 0 auto; border-radius: 12px; overflow: hidden; border: 1px solid #334155; background: #1E293B; }
                                .result-card { background: #1E293B; border: 1px solid #334155; padding: 18px; margin: 14px auto 0 auto; max-width: 480px; border-radius: 12px; display: none; text-align: left; }
                                .stock-badge { background: #064E3B; border: 1px solid #059669; color: #6EE7B7; padding: 10px; border-radius: 8px; text-align: center; margin: 12px 0; }
                                .quick-btns { display: grid; grid-template-columns: repeat(4, 1fr); gap: 6px; margin: 10px 0; }
                                .quick-btn { background: #334155; color: #F1F5F9; border: 1px solid #475569; padding: 10px 0; border-radius: 6px; font-size: 14px; font-weight: bold; cursor: pointer; }
                                .quick-btn:active { background: #0284C7; color: #fff; }
                                .input-cant { width: 100%; padding: 12px; font-size: 18px; font-weight: bold; text-align: center; border-radius: 8px; border: 1px solid #475569; background: #0F172A; color: #fff; margin-top: 6px; }
                                .btn-action { width: 100%; border: none; padding: 13px; border-radius: 8px; font-size: 14px; font-weight: bold; cursor: pointer; margin-top: 8px; display: block; }
                                .btn-add { background: #059669; color: white; }
                                .btn-set { background: #2563EB; color: white; }
                                .btn-pc { background: #7C3AED; color: white; }
                                .btn-nuevo { background: #D97706; color: white; }
                                .btn-scan { background: #475569; color: white; margin-top: 14px; }
                                .status-box { font-size: 14px; font-weight: bold; text-align: center; margin-top: 12px; min-height: 20px; }
                                .form-group { margin-bottom: 10px; }
                                .form-label { display: block; font-size: 12px; color: #94A3B8; margin-bottom: 4px; font-weight: bold; }
                                .form-control { width: 100%; padding: 10px; font-size: 14px; border-radius: 6px; border: 1px solid #475569; background: #0F172A; color: #fff; }
                            </style>
                        </head>
                        <body>
                            <h2>📦 Inventario Movil - Chip&Chapa</h2>

                            <div class='search-bar'>
                                <input id='manualCodeBox' type='text' class='search-input' placeholder='Escribe o pega un codigo...' />
                                <button type='button' class='search-btn' onclick='buscarManual()'>Buscar</button>
                            </div>
                            
                            <div id='reader'></div>
                            
                            <div id='result' class='result-card'>
                                <div id='foundSection' style='display: none;'>
                                    <h3 id='prodName' style='margin:0 0 6px 0; font-size: 19px; color:#F8FAFC;'>Cargando...</h3>
                                    <p id='prodCode' style='color: #94A3B8; font-size: 13px; margin:0 0 10px 0;'></p>
                                    <p id='prodPrice' style='color: #38BDF8; font-size: 13px; margin:0 0 10px 0;'></p>
                                    
                                    <div class='stock-badge'>
                                        <span style='font-size: 12px; font-weight: 600; display:block; letter-spacing: 0.5px;'>STOCK ACTUAL EN SISTEMA</span>
                                        <span id='prodStock' style='font-size: 28px; font-weight: 800;'>0</span>
                                    </div>

                                    <div id='actionControls'>
                                        <label style='font-size: 12px; font-weight: bold; color:#94A3B8;'>CANTIDAD A APLICAR:</label>
                                        <input id='inputCantidad' type='number' class='input-cant' value='1' min='0.001' step='any' />
                                        
                                        <div class='quick-btns'>
                                            <button type='button' class='quick-btn' onclick='sumarACantidad(1)'>+1</button>
                                            <button type='button' class='quick-btn' onclick='sumarACantidad(6)'>+6</button>
                                            <button type='button' class='quick-btn' onclick='sumarACantidad(12)'>+12</button>
                                            <button type='button' class='quick-btn' onclick='sumarACantidad(24)'>+24</button>
                                        </div>

                                        <button type='button' class='btn-action btn-add' onclick=""enviarCambioStock('sumar')"">📦 Sumar al Stock (Reabastecer)</button>
                                        <button type='button' class='btn-action btn-set' onclick=""enviarCambioStock('fijar')"">✏️ Fijar Stock Exacto</button>
                                        <button type='button' class='btn-action btn-pc' onclick='seleccionarEnPC()'>🖥️ Seleccionar en PC (Inventario)</button>
                                    </div>
                                </div>

                                <div id='notFoundSection' style='display: none;'>
                                    <h3 style='margin:0 0 6px 0; font-size: 19px; color:#F87171;'>⚠️ Producto no registrado</h3>
                                    <p id='unregisteredCodeLabel' style='color: #94A3B8; font-size: 13px; margin:0 0 14px 0;'></p>

                                    <button type='button' class='btn-action btn-pc' onclick='enviarNuevoCodigoAPC()'>🖥️ Enviar a Formulario en PC</button>
                                    <button type='button' class='btn-action btn-nuevo' onclick='mostrarFormularioAlta()'>➕ Dar de Alta desde el Movil</button>

                                    <div id='altaForm' style='display: none; margin-top: 14px; border-top: 1px solid #334155; padding-top: 14px;'>
                                        <div class='form-group'>
                                            <label class='form-label'>NOMBRE DEL PRODUCTO:</label>
                                            <input id='nuevoNombre' type='text' class='form-control' placeholder='Ej. Martillo de bola 16oz' />
                                        </div>
                                        <div class='form-group'>
                                            <label class='form-label'>PRECIO DE VENTA ($):</label>
                                            <input id='nuevoPrecio' type='number' step='any' class='form-control' value='0.00' />
                                        </div>
                                        <div class='form-group'>
                                            <label class='form-label'>COSTO ($):</label>
                                            <input id='nuevoCosto' type='number' step='any' class='form-control' value='0.00' />
                                        </div>
                                        <div class='form-group'>
                                            <label class='form-label'>STOCK INICIAL:</label>
                                            <input id='nuevoStock' type='number' step='any' class='form-control' value='1' />
                                        </div>
                                        <div class='form-group'>
                                            <label class='form-label'>CATEGORIA:</label>
                                            <input id='nuevoCategoria' type='text' class='form-control' value='General' />
                                        </div>
                                        <button type='button' class='btn-action btn-add' onclick='guardarAltaMovil()'>💾 Guardar y Crear Producto</button>
                                    </div>
                                </div>

                                <div id='actionStatus' class='status-box'></div>

                                <button type='button' class='btn-action btn-scan' onclick='escanearDeNuevo()'>📷 Escanear otro codigo</button>
                            </div>

                            <script>
                                let currentProduct = null;
                                let scannedCode = '';
                                let html5QrcodeScanner = null;

                                try {
                                    html5QrcodeScanner = new Html5QrcodeScanner(
                                        'reader', 
                                        { fps: 10, qrbox: { width: 280, height: 120 } }, 
                                        false
                                    );
                                    html5QrcodeScanner.render(onScanSuccess, onScanFailure);
                                } catch(e) {}

                                function onScanSuccess(decodedText, decodedResult) {
                                    try { html5QrcodeScanner.pause(true); } catch(e) {}
                                    cargarProducto(decodedText);
                                }

                                function buscarManual() {
                                    const val = (document.getElementById('manualCodeBox').value || '').trim();
                                    if (!val) return;
                                    try { if (html5QrcodeScanner) html5QrcodeScanner.pause(true); } catch(e) {}
                                    cargarProducto(val);
                                }

                                function cargarProducto(codigo) {
                                    codigo = (codigo || '').toString().trim();
                                    scannedCode = codigo;
                                    mostrarStatus('Buscando producto...', null);

                                    fetch('/api/producto?codigo=' + encodeURIComponent(codigo), {
                                        headers: { 'Accept': 'application/json' }
                                    })
                                        .then(async r => {
                                            const ct = r.headers.get('content-type') || '';
                                            if (ct.includes('application/json')) {
                                                const data = await r.json();
                                                if (r.ok && data.ok) return data;
                                                throw new Error(data && data.mensaje ? data.mensaje : 'Producto no registrado');
                                            }
                                            throw new Error('Respuesta no reconocida');
                                        })
                                        .then(data => {
                                            currentProduct = data;
                                            document.getElementById('prodName').innerText = data.nombre;
                                            document.getElementById('prodCode').innerText = 'Codigo: ' + data.codigo + ' | Categoria: ' + (data.categoria || 'General');
                                            document.getElementById('prodPrice').innerText = 'Precio: $' + Number(data.precio).toFixed(2) + ' | Costo: $' + Number(data.costo || 0).toFixed(2);
                                            document.getElementById('prodStock').innerText = data.stock ?? data.cantidad ?? 0;
                                            document.getElementById('inputCantidad').value = 1;

                                            document.getElementById('foundSection').style.display = 'block';
                                            document.getElementById('notFoundSection').style.display = 'none';
                                            document.getElementById('actionStatus').innerText = '';
                                            mostrarResultado();
                                        })
                                        .catch(err => {
                                            currentProduct = null;
                                            document.getElementById('unregisteredCodeLabel').innerText = 'Codigo leido: ' + codigo;
                                            document.getElementById('altaForm').style.display = 'none';
                                            document.getElementById('nuevoNombre').value = '';
                                            document.getElementById('nuevoPrecio').value = '0.00';
                                            document.getElementById('nuevoCosto').value = '0.00';
                                            document.getElementById('nuevoStock').value = '1';

                                            document.getElementById('foundSection').style.display = 'none';
                                            document.getElementById('notFoundSection').style.display = 'block';
                                            document.getElementById('actionStatus').innerText = '';
                                            mostrarResultado();
                                        });
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
                                            document.getElementById('prodStock').innerText = res.stock;
                                            mostrarStatus('✅ ' + res.mensaje + ' (Reflejado en PC)', true);
                                        } else {
                                            mostrarStatus('❌ ' + (res.mensaje || 'Error al actualizar'), false);
                                        }
                                    })
                                    .catch(err => {
                                        mostrarStatus('❌ Error de conexion con la PC', false);
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
                                            mostrarStatus('🖥️ Producto seleccionado en el Inventario de la PC', true);
                                        } else {
                                            mostrarStatus('⚠️ ' + (res.mensaje || 'No se pudo seleccionar en PC'), false);
                                        }
                                    })
                                    .catch(err => {
                                        mostrarStatus('❌ Error de conexion con la PC', false);
                                    });
                                }

                                function enviarNuevoCodigoAPC() {
                                    mostrarStatus('Enviando codigo a PC...', null);
                                    fetch('/api/inventario/nuevo', {
                                        method: 'POST',
                                        headers: { 'Content-Type': 'application/json' },
                                        body: JSON.stringify({ codigo: scannedCode })
                                    })
                                    .then(r => r.json())
                                    .then(res => {
                                        if (res.ok) {
                                            mostrarStatus('🖥️ Codigo cargado en el formulario de la PC', true);
                                        } else {
                                            mostrarStatus('⚠️ ' + (res.mensaje || 'No se pudo cargar en PC'), false);
                                        }
                                    })
                                    .catch(err => {
                                        mostrarStatus('❌ Error de conexion con la PC', false);
                                    });
                                }

                                function mostrarFormularioAlta() {
                                    const form = document.getElementById('altaForm');
                                    form.style.display = form.style.display === 'none' ? 'block' : 'none';
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
                                            codigo: scannedCode,
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
                                            mostrarStatus('✅ ' + res.mensaje, true);
                                            setTimeout(() => { cargarProducto(scannedCode); }, 1200);
                                        } else {
                                            mostrarStatus('❌ ' + (res.mensaje || 'Error al guardar'), false);
                                        }
                                    })
                                    .catch(err => {
                                        mostrarStatus('❌ Error de conexion con la PC', false);
                                    });
                                }

                                function mostrarStatus(txt, exito) {
                                    const el = document.getElementById('actionStatus');
                                    el.innerText = txt;
                                    if (exito === true) {
                                        el.style.color = '#34D399';
                                    } else if (exito === false) {
                                        el.style.color = '#F87171';
                                    } else {
                                        el.style.color = '#94A3B8';
                                    }
                                }

                                function onScanFailure(error) {
                                }

                                function mostrarResultado() {
                                    document.getElementById('reader').style.display = 'none';
                                    document.getElementById('result').style.display = 'block';
                                }

                                function escanearDeNuevo() {
                                    document.getElementById('result').style.display = 'none';
                                    document.getElementById('reader').style.display = 'block';
                                    try {
                                        if (html5QrcodeScanner) html5QrcodeScanner.resume();
                                    } catch(e) {}
                                }
                            </script>
                        </body>
                        </html>";
                    return Results.Content(html, "text/html");
                }).RequireAuthorization();

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