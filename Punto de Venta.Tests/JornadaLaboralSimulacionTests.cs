using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using Xunit;

namespace Punto_de_Venta.Tests
{
    [Collection("BaseDatosAislada")]
    public class JornadaLaboralSimulacionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string? _prevDbFolder;
        private readonly string? _prevDbName;

        public JornadaLaboralSimulacionTests()
        {
            _prevDbFolder = Environment.GetEnvironmentVariable("POS_DB_FOLDER");
            _prevDbName = Environment.GetEnvironmentVariable("POS_DB_NAME");

            _tempDir = Path.Combine(Path.GetTempPath(), "pos_jornada_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            Environment.SetEnvironmentVariable("POS_DB_FOLDER", _tempDir);
            Environment.SetEnvironmentVariable("POS_DB_NAME", "jornada_pos.db");

            DatabaseHelper.InicializarBaseDeDatos();
            DatabaseHelper.ActualizarEsquemaVentas();
        }

        [Fact]
        public void SimulacionCompleta_CajeroYAdministrador_CuadraInventarioVentasYCorte()
        {
            // =========================================================================
            // 1. PREPARACION DEL DUEÑO / ADMIN: Alta de catálogo e inventario inicial
            // =========================================================================
            var coca = new Producto
            {
                Id = 1, Codigo = "7501055300075", Nombre = "Refresco 600ml",
                Categoria = "Bebidas", Costo = 12m, Ganancia = 6m, Precio = 18m,
                Stock = 20m, StockMinimo = 5m
            };
            var papas = new Producto
            {
                Id = 2, Codigo = "7501011101111", Nombre = "Papas 45g",
                Categoria = "Botanas", Costo = 10m, Ganancia = 6m, Precio = 16m,
                Stock = 6m, StockMinimo = 5m
            };
            var manzana = new Producto
            {
                Id = 3, Codigo = "2001", Nombre = "Manzana Roja kg",
                Categoria = "Frutas", Costo = 25m, Ganancia = 15m, Precio = 40m,
                Stock = 10m, StockMinimo = 3m, SeVendePorPeso = true
            };
            ProductoDB.Save([coca, papas, manzana]);

            var promoCombo = new Promocion
            {
                Id = 1,
                Nombre = "Combo Refresco+Papas",
                Tipo = TipoPromocion.Combo,
                Activa = true,
                ProductoIdPrincipal = coca.Id,
                ProductoIdSecundario = papas.Id,
                PrecioComboTotal = 30m // Normal: 18 + 16 = 34 -> Ahorro: 4
            };

            // =========================================================================
            // 2. ROL CAJERO: Apertura de turno, ventas variadas, cancelación y corte
            // =========================================================================
            decimal fondoInicial = 500m;
            var corte = new CorteCaja
            {
                Id = 1,
                FechaHora = DateTime.Now,
                Estado = "Abierto",
                FondoInicial = fondoInicial
            };
            CorteCajaDB.Save([corte]);

            // Venta 1 (Efectivo + Combo): 1 Refresco + 1 Papas = 34 - 4 (combo) = 30
            var carrito1 = new List<ItemCarrito>
            {
                new() { ProductoId = coca.Id, Nombre = coca.Nombre, Costo = coca.Costo, Ganancia = coca.Ganancia, Precio = coca.Precio, Cantidad = 1m },
                new() { ProductoId = papas.Id, Nombre = papas.Nombre, Costo = papas.Costo, Ganancia = papas.Ganancia, Precio = papas.Precio, Cantidad = 1m }
            };
            var resPromo1 = PromocionEngine.Evaluar(carrito1, [promoCombo]);
            Assert.Equal(4m, resPromo1.DescuentoTotal);

            decimal totalVenta1 = carrito1.Sum(x => x.Subtotal) - resPromo1.DescuentoTotal; // 30m
            coca.Stock -= 1m; // 19
            papas.Stock -= 1m; // 5 (llega justo a StockMinimo!)

            var venta1 = new Venta
            {
                Id = VentaDB.ObtenerSiguienteId(),
                CorteCajaId = corte.Id,
                FechaHora = DateTime.Now,
                Total = totalVenta1,
                PagoCon = 50m,
                Estado = "Completada",
                MetodoPago = "Efectivo",
                PagoEfectivo = totalVenta1,
                Productos =
                [
                    new DetalleVenta { ProductoId = coca.Id, Nombre = coca.Nombre, Cantidad = 1m, Costo = coca.Costo, Ganancia = coca.Ganancia, Precio = coca.Precio, Categoria = coca.Categoria },
                    new DetalleVenta { ProductoId = papas.Id, Nombre = papas.Nombre, Cantidad = 1m, Costo = papas.Costo, Ganancia = papas.Ganancia, Precio = papas.Precio, Categoria = papas.Categoria }
                ]
            };
            VentaDB.RegistrarVentaConStock(venta1, [coca, papas]);

            // Validar generación de ticket 80mm sin desbordamiento
            var config80 = new AppConfig { NombreNegocio = "Chip & Chapa", AnchoTicketCaracteres = 42 };
            string ticket1 = TicketService.GenerarTextoTicket(venta1, venta1.Productos, 50m, 20m, config80);
            Assert.Contains("TOTAL: $30.00", ticket1.Replace(",", ""));

            // Venta 2 (Báscula + Pago Mixto): Trama Torrey "ST,GS,+ 1.250 kg" -> 1.250 * 40 = 50m
            Assert.True(BasculaService.TryParsePeso("ST,GS,+ 1.250 kg", out decimal pesoManzana));
            Assert.Equal(1.250m, pesoManzana);
            decimal totalVenta2 = pesoManzana * manzana.Precio; // 50m
            manzana.Stock -= pesoManzana; // 8.750

            var venta2 = new Venta
            {
                Id = VentaDB.ObtenerSiguienteId(),
                CorteCajaId = corte.Id,
                FechaHora = DateTime.Now,
                Total = totalVenta2,
                PagoCon = totalVenta2,
                Estado = "Completada",
                MetodoPago = "Mixto",
                PagoEfectivo = 20m,
                PagoTarjeta = 30m,
                Productos =
                [
                    new DetalleVenta
                    {
                        ProductoId = manzana.Id, Nombre = manzana.Nombre, Cantidad = pesoManzana,
                        Costo = manzana.Costo, Ganancia = manzana.Ganancia, Precio = manzana.Precio,
                        Categoria = manzana.Categoria, SeVendePorPeso = true
                    }
                ]
            };
            VentaDB.RegistrarVentaConStock(venta2, [manzana]);

            // Venta 3 (Se cobra en efectivo y luego el cliente la cancela -> debe devolver stock)
            coca.Stock -= 2m; // 17
            var venta3 = new Venta
            {
                Id = VentaDB.ObtenerSiguienteId(),
                CorteCajaId = corte.Id,
                FechaHora = DateTime.Now,
                Total = 36m,
                PagoCon = 50m,
                Estado = "Completada",
                MetodoPago = "Efectivo",
                PagoEfectivo = 36m,
                Productos =
                [
                    new DetalleVenta { ProductoId = coca.Id, Nombre = coca.Nombre, Cantidad = 2m, Costo = coca.Costo, Ganancia = coca.Ganancia, Precio = coca.Precio, Categoria = coca.Categoria }
                ]
            };
            VentaDB.RegistrarVentaConStock(venta3, [coca]);

            coca.Stock += 2m; // 19 restaurado
            VentaDB.CancelarVentaConStock(venta3.Id, [coca]);

            // Cierre de turno del cajero: Fondo 500 + Efectivo Venta1 (30) + Efectivo Venta2 (20) - Retiro (100) = 450
            decimal ventasEfectivo = 30m + 20m;
            decimal salidas = 100m;
            corte.Estado = "Cerrado";
            corte.FechaHoraCierre = DateTime.Now;
            corte.VentasEfectivo = ventasEfectivo;
            corte.SalidasEfectivo = salidas;
            corte.TotalEsperado = fondoInicial + ventasEfectivo - salidas; // 450
            corte.TotalContado = 450m;
            CorteCajaDB.Save([corte]);

            // =========================================================================
            // 3. ROL ADMINISTRADOR / DUEÑO: Auditoría de BD, inventario y cuadre
            // =========================================================================
            var ventasBd = VentaDB.Load();
            var productosBd = ProductoDB.Load();
            var cortesBd = CorteCajaDB.Load();

            // Solo 2 ventas completadas y 1 cancelada
            Assert.Equal(2, ventasBd.Count(v => v.Estado == "Completada"));
            Assert.Single(ventasBd, v => v.Estado == "Cancelada");

            // Total vendido real del día: 30 + 50 = 80 (tarjeta 30, efectivo 50)
            decimal totalVendidoReal = ventasBd.Where(v => v.Estado == "Completada").Sum(v => v.Total);
            Assert.Equal(80m, totalVendidoReal);

            // Inventario exacto tras ventas y cancelación + alerta de stock bajo en Papas (5 <= 5)
            var cocaBd = Assert.Single(productosBd, p => p.Id == coca.Id);
            var papasBd = Assert.Single(productosBd, p => p.Id == papas.Id);
            var manzanaBd = Assert.Single(productosBd, p => p.Id == manzana.Id);
            Assert.Equal(19m, cocaBd.Stock);
            Assert.Equal(5m, papasBd.Stock);
            Assert.True(papasBd.EsStockBajo);
            Assert.Equal(8.750m, manzanaBd.Stock);

            // Corte de caja cuadrado sin diferencia
            var corteBd = Assert.Single(cortesBd);
            Assert.Equal("Cerrado", corteBd.Estado);
            Assert.Equal(450m, corteBd.TotalEsperado);
            Assert.Equal(0m, corteBd.Diferencia);
        }

        [Fact]
        public async Task SimulacionJornada_FlujosEquivalentesAMetodosEliminados_FuncionanSinRegresion()
        {
            // 1. Licencia (reemplazo real de LicenciaExiste): validar estado y hardware ID sin errores
            LicenciaService.InvalidarCache();
            string hwId = LicenciaService.ObtenerIdentificadorHardware();
            Assert.False(string.IsNullOrWhiteSpace(hwId));
            _ = LicenciaService.EsPremium();

            // 2. Importacion de catalogo (reemplazo real de ImportarProductosCsv): Previsualizar + Confirmar
            string csvImportPath = Path.Combine(_tempDir, "catalogo_jornada.csv");
            File.WriteAllText(
                csvImportPath,
                "Codigo,Nombre,Categoria,Costo,Precio,Stock\n" +
                "7501000100011,Agua 1L,Bebidas,8.00,15.00,25\n" +
                "2002,Naranja kg,Frutas,18.00,30.00,12\n");

            var preview = ImportarExportarService.PrevisualizarImportacion(
                csvImportPath,
                esPremium: true,
                ModoImportacion.CrearYActualizar);
            Assert.Equal(2, preview.Creados);
            Assert.Equal(0, preview.Rechazados);

            var resImport = ImportarExportarService.ConfirmarImportacion(preview);
            Assert.Equal(2, resImport.Creados);

            var productos = ProductoDB.Load();
            var agua = Assert.Single(productos, p => p.Codigo == "7501000100011");
            var naranja = Assert.Single(productos, p => p.Codigo == "2002");

            // 3. Bascula (reemplazo real de ProbarConexion sincrono): ProbarConexionAsync + trama continua
            var pruebaBascula = await BasculaService.ProbarConexionAsync("", 9600);
            Assert.False(pruebaBascula.Exito);
            Assert.False(string.IsNullOrWhiteSpace(pruebaBascula.Error));

            Assert.True(BasculaService.IntentarExtraerUltimaTramaCompletaValida(
                "ST,GS,+ 1.500 kg\r\n",
                false,
                "\r\n",
                out decimal pesoNaranja));
            Assert.Equal(1.500m, pesoNaranja);

            // 4. Apertura de turno
            var corte = new CorteCaja
            {
                Id = 1,
                FechaHora = DateTime.Now,
                Estado = "Abierto",
                FondoInicial = 300m
            };
            CorteCajaDB.Save([corte]);

            // 5. Venta en espera (reemplazo real de VentaEnEsperaDB.ObtenerCantidad): Save + Load.Count + Delete
            var enEspera = new VentaEnEspera
            {
                Identificador = "Cliente Mostrador",
                TotalNeto = 30m,
                TotalItems = 2m,
                Items =
                [
                    new ItemCarrito
                    {
                        ProductoId = agua.Id,
                        Nombre = agua.Nombre,
                        Costo = agua.Costo,
                        Ganancia = agua.Ganancia,
                        Precio = agua.Precio,
                        Cantidad = 2m
                    }
                ]
            };
            int esperaId = VentaEnEsperaDB.Save(enEspera);
            Assert.True(esperaId > 0);
            Assert.Single(VentaEnEsperaDB.Load());

            // Se retoma y elimina de espera al cobrarla
            VentaEnEsperaDB.Delete(esperaId);
            Assert.Empty(VentaEnEsperaDB.Load());

            // 6. Cobro y cancelacion (reemplazo real de VentaDB.ActualizarEstado): RegistrarVentaConStock + CancelarVentaConStock
            agua.Stock -= 2m;
            var ventaCompletada = new Venta
            {
                Id = VentaDB.ObtenerSiguienteId(),
                CorteCajaId = corte.Id,
                FechaHora = DateTime.Now,
                Total = 30m,
                PagoCon = 50m,
                Estado = "Completada",
                MetodoPago = "Efectivo",
                PagoEfectivo = 30m,
                Productos =
                [
                    new DetalleVenta
                    {
                        ProductoId = agua.Id, Nombre = agua.Nombre, Cantidad = 2m,
                        Costo = agua.Costo, Ganancia = agua.Ganancia, Precio = agua.Precio, Categoria = agua.Categoria
                    }
                ]
            };
            VentaDB.RegistrarVentaConStock(ventaCompletada, [agua]);

            naranja.Stock -= pesoNaranja;
            var ventaACancelar = new Venta
            {
                Id = VentaDB.ObtenerSiguienteId(),
                CorteCajaId = corte.Id,
                FechaHora = DateTime.Now,
                Total = pesoNaranja * naranja.Precio, // 45m
                PagoCon = 50m,
                Estado = "Completada",
                MetodoPago = "Efectivo",
                PagoEfectivo = 45m,
                Productos =
                [
                    new DetalleVenta
                    {
                        ProductoId = naranja.Id, Nombre = naranja.Nombre, Cantidad = pesoNaranja,
                        Costo = naranja.Costo, Ganancia = naranja.Ganancia, Precio = naranja.Precio,
                        Categoria = naranja.Categoria, SeVendePorPeso = true
                    }
                ]
            };
            VentaDB.RegistrarVentaConStock(ventaACancelar, [naranja]);

            naranja.Stock += pesoNaranja;
            VentaDB.CancelarVentaConStock(ventaACancelar.Id, [naranja]);

            var ventasRango = VentaDB.LoadPorRangoFechas(DateTime.Today, DateTime.Today.AddDays(1));
            Assert.Single(ventasRango, v => v.Estado == "Completada");
            Assert.Single(ventasRango, v => v.Estado == "Cancelada");

            // Ticket con configuracion de idioma/negocio sin _actualizandoIdioma
            var config = new AppConfig { NombreNegocio = "Tienda Prueba", Idioma = "es-MX", AnchoTicketCaracteres = 42 };
            string ticket = TicketService.GenerarTextoTicket(ventaCompletada, ventaCompletada.Productos, 50m, 20m, config);
            Assert.Contains("Tienda Prueba", ticket);

            // 7. Cierre de turno y exportacion de reportes (sin ReporteDiarioPdf)
            corte.Estado = "Cerrado";
            corte.FechaHoraCierre = DateTime.Now;
            corte.VentasEfectivo = 30m;
            corte.TotalEsperado = 330m;
            corte.TotalContado = 330m;
            CorteCajaDB.Save([corte]);

            string cortesCsv = Path.Combine(_tempDir, "cortes_jornada.csv");
            string productosCsv = Path.Combine(_tempDir, "productos_jornada.csv");
            ImportarExportarService.ExportarCortesCsv(cortesCsv);
            ImportarExportarService.ExportarProductosCsv(ProductoDB.Load(), productosCsv);

            Assert.True(File.Exists(cortesCsv) && new FileInfo(cortesCsv).Length > 0);
            Assert.True(File.Exists(productosCsv) && new FileInfo(productosCsv).Length > 0);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("POS_DB_FOLDER", _prevDbFolder);
            Environment.SetEnvironmentVariable("POS_DB_NAME", _prevDbName);

            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}

