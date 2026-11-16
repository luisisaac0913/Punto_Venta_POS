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

        [Fact]
        public void PrestamosConInteres_CalculoYPersistenciaEnBD_FuncionaCorrectamente()
        {
            var deudorSaldo = new Deudor
            {
                Id = 0,
                Nombre = "Carlos Interes Saldo",
                MontoOriginal = 200m,
                TotalAbonado = 50m,
                TieneInteres = true,
                PorcentajeInteres = 10m,
                TipoInteres = "PorcentajeSaldo"
            };
            // Saldo base 150 * 10% = 15 -> TotalConInteres 215 -> SaldoRestante 165
            Assert.Equal(15m, deudorSaldo.InteresCalculado);
            Assert.Equal(215m, deudorSaldo.TotalConInteres);
            Assert.Equal(165m, deudorSaldo.SaldoRestante);

            var deudorOriginal = new Deudor
            {
                Id = 0,
                Nombre = "Ana Interes Original",
                MontoOriginal = 200m,
                TotalAbonado = 50m,
                TieneInteres = true,
                PorcentajeInteres = 10m,
                TipoInteres = "PorcentajeOriginal"
            };
            Assert.Equal(20m, deudorOriginal.InteresCalculado);
            Assert.Equal(170m, deudorOriginal.SaldoRestante);

            var deudorFijo = new Deudor
            {
                Id = 0,
                Nombre = "Luis Monto Fijo",
                MontoOriginal = 200m,
                TotalAbonado = 0m,
                TieneInteres = true,
                PorcentajeInteres = 35m,
                TipoInteres = "MontoFijo"
            };
            Assert.Equal(35m, deudorFijo.InteresCalculado);
            Assert.Equal(235m, deudorFijo.SaldoRestante);

            PrestamoDB.Save([deudorSaldo, deudorOriginal, deudorFijo]);
            var cargados = PrestamoDB.Load();
            var saldoBd = Assert.Single(cargados, d => d.Nombre == "Carlos Interes Saldo");
            Assert.True(saldoBd.TieneInteres);
            Assert.Equal(10m, saldoBd.PorcentajeInteres);
            Assert.Equal("PorcentajeSaldo", saldoBd.TipoInteres);
            Assert.Equal(165m, saldoBd.SaldoRestante);

            // Frecuencias: Diario (2% diario x 3 dias = 6% sobre 100 = 6), Semanal (2% x 2 semanas = 4), Mensual (5% x 2 meses = 10)
            var deudorDiario = new Deudor
            {
                Nombre = "Pedro Diario",
                MontoOriginal = 100m,
                TieneInteres = true,
                PorcentajeInteres = 2m,
                TipoInteres = "PorcentajeOriginal",
                FrecuenciaInteres = "Diario",
                FechaInicioInteres = DateTime.Now.AddDays(-3).AddMinutes(-5)
            };
            Assert.Equal(3, deudorDiario.PeriodosInteresTranscurridos);
            Assert.Equal(6m, deudorDiario.InteresCalculado);
            Assert.Equal(106m, deudorDiario.SaldoRestante);

            // Cambio de frecuencia Diario -> Semanal -> Mensual conserva el interes ya acumulado ($6)
            deudorDiario.ConsolidarInteresYReiniciarPeriodo(DateTime.Now.AddDays(-7).AddMinutes(-5));
            deudorDiario.FrecuenciaInteres = "Semanal";
            Assert.Equal(6m, deudorDiario.InteresAcumulado);
            Assert.Equal(1, deudorDiario.PeriodosInteresTranscurridos);
            Assert.Equal(8m, deudorDiario.InteresCalculado); // 6 acumulado + 2 de 1 semana

            deudorDiario.ConsolidarInteresYReiniciarPeriodo(DateTime.Now);
            deudorDiario.FrecuenciaInteres = "Mensual";
            Assert.Equal(8m, deudorDiario.InteresAcumulado);
            Assert.Equal(0, deudorDiario.PeriodosInteresTranscurridos);
            Assert.Equal(8m, deudorDiario.InteresCalculado);

            var deudorSemanal = new Deudor
            {
                Nombre = "Sofia Semanal",
                MontoOriginal = 100m,
                TieneInteres = true,
                PorcentajeInteres = 2m,
                TipoInteres = "PorcentajeOriginal",
                FrecuenciaInteres = "Semanal",
                FechaInicioInteres = DateTime.Now.AddDays(-14).AddMinutes(-5)
            };
            Assert.Equal(2, deudorSemanal.PeriodosInteresTranscurridos);
            Assert.Equal(4m, deudorSemanal.InteresCalculado);

            var deudorMensual = new Deudor
            {
                Nombre = "Mario Mensual",
                MontoOriginal = 100m,
                TieneInteres = true,
                PorcentajeInteres = 5m,
                TipoInteres = "PorcentajeOriginal",
                FrecuenciaInteres = "Mensual",
                FechaInicioInteres = DateTime.Now.AddMonths(-2).AddMinutes(-5)
            };
            Assert.Equal(2, deudorMensual.PeriodosInteresTranscurridos);
            Assert.Equal(10m, deudorMensual.InteresCalculado);

            PrestamoDB.Save([deudorDiario]);
            var diarioBd = Assert.Single(PrestamoDB.Load(), d => d.Nombre == "Pedro Diario");
            Assert.Equal("Mensual", diarioBd.FrecuenciaInteres);
            Assert.Equal(8m, diarioBd.InteresAcumulado);
            Assert.Equal(8m, diarioBd.InteresCalculado);

            // Escenario legado: deudor existente sin interes (como registros previos a la actualizacion)
            var legado = new Deudor
            {
                Nombre = "Cliente Legado",
                MontoOriginal = 150m,
                TotalAbonado = 50m,
                TieneInteres = false,
                PorcentajeInteres = 0m
            };
            Assert.Equal(0m, legado.InteresCalculado);
            Assert.Equal(150m, legado.TotalConInteres);
            Assert.Equal(100m, legado.SaldoRestante);
            legado.TotalAbonado = 150m;
            Assert.True(legado.EstaSaldada);

            // Escenario 0 periodos transcurridos (recien creado hoy con Diario/Semanal) -> 0 interes hoy
            var recienCreadoDiario = new Deudor
            {
                Nombre = "Hoy Diario",
                MontoOriginal = 100m,
                TieneInteres = true,
                PorcentajeInteres = 2m,
                TipoInteres = "PorcentajeSaldo",
                FrecuenciaInteres = "Diario",
                FechaInicioInteres = DateTime.Today
            };
            Assert.Equal(0, recienCreadoDiario.PeriodosInteresTranscurridos);
            Assert.Equal(0m, recienCreadoDiario.InteresCalculado);
            Assert.Equal(100m, recienCreadoDiario.SaldoRestante);

            // Escenario deshabilitar interes despues de tenerlo activo -> vuelve a 0
            deudorDiario.TieneInteres = false;
            Assert.Equal(0m, deudorDiario.InteresCalculado);
            Assert.Equal(100m, deudorDiario.SaldoRestante);
        }

        [Fact]
        public void ImportacionCatalogos_TodosLosFormatos_BienvenidaYPosIniciado_FuncionaCorrectamente()
        {
            // 1. Formato .csv (coma) - flujo Bienvenida / POS iniciado
            string csvPath = Path.Combine(_tempDir, "catalogo.csv");
            File.WriteAllText(csvPath,
                "Codigo,Nombre,Categoria,Costo,Precio,Stock\n" +
                "750001,Producto CSV,Bebidas,10.00,18.00,12\n");
            var prevCsv = ImportarExportarService.PrevisualizarImportacion(csvPath, esPremium: true, ModoImportacion.CrearYActualizar);
            Assert.Equal(1, prevCsv.Creados);
            Assert.Equal(1, ImportarExportarService.ConfirmarImportacion(prevCsv).Creados);

            // 2. Formato .tsv (tabulador)
            string tsvPath = Path.Combine(_tempDir, "catalogo.tsv");
            File.WriteAllText(tsvPath,
                "Codigo\tNombre\tCategoria\tCosto\tPrecio\tStock\n" +
                "750002\tProducto TSV\tBotanas\t7.50\t15.00\t20\n");
            var prevTsv = ImportarExportarService.PrevisualizarImportacion(tsvPath, esPremium: true, ModoImportacion.CrearYActualizar);
            Assert.Equal(1, prevTsv.Creados);
            Assert.Equal(1, ImportarExportarService.ConfirmarImportacion(prevTsv).Creados);

            // 3. Formato .txt (punto y coma)
            string txtPath = Path.Combine(_tempDir, "catalogo.txt");
            File.WriteAllText(txtPath,
                "Codigo;Nombre;Categoria;Costo;Precio;Stock\n" +
                "750003;Producto TXT;Abarrotes;20.00;32.00;8\n");
            var prevTxt = ImportarExportarService.PrevisualizarImportacion(txtPath, esPremium: true, ModoImportacion.CrearYActualizar);
            Assert.Equal(1, prevTxt.Creados);
            Assert.Equal(1, ImportarExportarService.ConfirmarImportacion(prevTxt).Creados);

            // 4. Formato .xlsx (exportado e importado en modo SumarStockYCrear con POS iniciado)
            string xlsxPath = Path.Combine(_tempDir, "catalogo.xlsx");
            ImportarExportarService.ExportarProductosXlsx(
            [
                new Producto { Codigo = "750001", Nombre = "Producto CSV", Categoria = "Bebidas", Costo = 10m, Precio = 18m, Stock = 5m },
                new Producto { Codigo = "750004", Nombre = "Producto XLSX", Categoria = "Lacteos", Costo = 14m, Precio = 24m, Stock = 10m }
            ], xlsxPath);
            Assert.True(File.Exists(xlsxPath));

            var prevXlsx = ImportarExportarService.PrevisualizarImportacion(xlsxPath, esPremium: true, ModoImportacion.SumarStockYCrear);
            Assert.Equal(1, prevXlsx.Creados);      // 750004 nuevo
            Assert.Equal(1, prevXlsx.Actualizados); // 750001 suma 5 al stock 12 -> 17
            var resXlsx = ImportarExportarService.ConfirmarImportacion(prevXlsx);
            Assert.Equal(1, resXlsx.Creados);
            Assert.Equal(1, resXlsx.Actualizados);

            var catalogo = ProductoDB.Load();
            Assert.Equal(4, catalogo.Count);
            Assert.Equal(17m, Assert.Single(catalogo, p => p.Codigo == "750001").Stock);
            Assert.Equal(24m, Assert.Single(catalogo, p => p.Codigo == "750004").Precio);

            // 5. Formato .xls binario antiguo debe rechazarse limpiamente
            string xlsBinPath = Path.Combine(_tempDir, "legacy.xls");
            File.WriteAllBytes(xlsBinPath, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
            Assert.Throws<InvalidOperationException>(() =>
                ImportarExportarService.PrevisualizarImportacion(xlsBinPath, esPremium: true, ModoImportacion.CrearYActualizar));

            // 6. Formato .db (respaldo completo en Bienvenida)
            string backupDbPath = Path.Combine(_tempDir, "respaldo_bienvenida.db");
            SqliteConnection.ClearAllPools();
            File.Copy(DatabaseHelper.DbPath, backupDbPath, overwrite: true);

            // Modificar BD actual y luego restaurar desde el .db
            ProductoDB.Save([new Producto { Id = 99, Codigo = "999", Nombre = "Temporal", Precio = 1m }]);
            Assert.Equal(5, ProductoDB.Load().Count);

            SqliteConnection.ClearAllPools();
            File.Copy(backupDbPath, DatabaseHelper.DbPath, overwrite: true);
            DatabaseHelper.InicializarBaseDeDatos();
            DatabaseHelper.ActualizarEsquemaVentas();

            var restaurado = ProductoDB.Load();
            Assert.Equal(4, restaurado.Count);
            Assert.DoesNotContain(restaurado, p => p.Codigo == "999");
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

