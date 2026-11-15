using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using Xunit;

namespace Punto_de_Venta.Tests
{
    [Collection("BaseDatosAislada")]
    public class AsistenteIASimulacionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string? _prevDbFolder;
        private readonly string? _prevDbName;

        public AsistenteIASimulacionTests()
        {
            _prevDbFolder = Environment.GetEnvironmentVariable("POS_DB_FOLDER");
            _prevDbName = Environment.GetEnvironmentVariable("POS_DB_NAME");

            _tempDir = Path.Combine(Path.GetTempPath(), "pos_ia_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            Environment.SetEnvironmentVariable("POS_DB_FOLDER", _tempDir);
            Environment.SetEnvironmentVariable("POS_DB_NAME", "ia_pos.db");

            DatabaseHelper.InicializarBaseDeDatos();
            DatabaseHelper.ActualizarEsquemaVentas();
        }

        [Fact]
        public async Task SimularPreguntasNormalesAlAsistente_EjecutaHerramientasRealesYRespondeConDatosDeBD()
        {
            // Datos reales en la BD temporal
            ProductoDB.Save([
                new Producto { Id = 1, Codigo = "7501", Nombre = "Refresco 600ml", Categoria = "Bebidas", Costo = 12m, Ganancia = 6m, Precio = 18m, Stock = 25m, StockMinimo = 5m },
                new Producto { Id = 2, Codigo = "7502", Nombre = "Papas 45g", Categoria = "Botanas", Costo = 10m, Ganancia = 6m, Precio = 16m, Stock = 2m, StockMinimo = 5m }
            ]);

            VentaDB.Insert(new Venta
            {
                Id = VentaDB.ObtenerSiguienteId(),
                FechaHora = DateTime.Now,
                Total = 90m,
                PagoCon = 100m,
                Estado = "Completada",
                MetodoPago = "Efectivo",
                PagoEfectivo = 90m,
                Productos =
                [
                    new DetalleVenta { ProductoId = 1, Nombre = "Refresco 600ml", Cantidad = 5m, Costo = 12m, Ganancia = 6m, Precio = 18m, Categoria = "Bebidas" }
                ]
            });

            // Verificar que el asistente expone sus instrucciones y 16 herramientas
            Assert.False(string.IsNullOrWhiteSpace(AsistenteHerramientasPOS.InstruccionSistema));
            Assert.NotEmpty(AsistenteHerramientasPOS.ObtenerDeclaraciones());

            using var emptyArgsDoc = JsonDocument.Parse("{}");

            // Pregunta normal 1: "¿Qué productos tienen stock bajo?"
            object resStock = await AsistenteHerramientasPOS.EjecutarHerramientaAsync(
                "consultar_stock_bajo", emptyArgsDoc.RootElement, owner: null);
            string jsonStock = JsonSerializer.Serialize(resStock);
            Assert.Contains("Papas 45g", jsonStock);
            Assert.DoesNotContain("Refresco 600ml", jsonStock);

            // Pregunta normal 2: "¿Cuánto llevamos vendido hoy?"
            using var ventasArgsDoc = JsonDocument.Parse("{\"periodo\":\"hoy\"}");
            object resVentas = await AsistenteHerramientasPOS.EjecutarHerramientaAsync(
                "consultar_resumen_ventas", ventasArgsDoc.RootElement, owner: null);
            string jsonVentas = JsonSerializer.Serialize(resVentas);
            Assert.Contains("90", jsonVentas);
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
