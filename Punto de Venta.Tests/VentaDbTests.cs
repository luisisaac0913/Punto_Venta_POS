using System.IO;
using Microsoft.Data.Sqlite;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Xunit;

namespace Punto_de_Venta.Tests
{
    public class VentaDbTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string? _prevDbFolder;
        private readonly string? _prevDbName;

        public VentaDbTests()
        {
            _prevDbFolder = Environment.GetEnvironmentVariable("POS_DB_FOLDER");
            _prevDbName = Environment.GetEnvironmentVariable("POS_DB_NAME");

            _tempDir = Path.Combine(Path.GetTempPath(), "pos_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            Environment.SetEnvironmentVariable("POS_DB_FOLDER", _tempDir);
            Environment.SetEnvironmentVariable("POS_DB_NAME", "test_pos.db");

            DatabaseHelper.InicializarBaseDeDatos();
            DatabaseHelper.ActualizarEsquemaVentas();
        }

        [Fact]
        public void InsertYLoad_VentaConDetalleYPagoMixto_PersisteYRecuperaCorrectamente()
        {
            int nextId = VentaDB.ObtenerSiguienteId();
            var venta = new Venta
            {
                Id = nextId,
                FechaHora = DateTime.Now,
                Total = 150.00m,
                PagoCon = 150.00m,
                Cambio = 0m,
                Estado = "Completada",
                MetodoPago = "Mixto",
                PagoEfectivo = 100.00m,
                PagoTarjeta = 50.00m,
                PagoTransferencia = 0m,
                Productos =
                [
                    new DetalleVenta
                    {
                        ProductoId = 1,
                        Nombre = "Producto Prueba",
                        Cantidad = 2m,
                        Costo = 40m,
                        Ganancia = 35m,
                        Precio = 75m,
                        Categoria = "General"
                    }
                ]
            };

            VentaDB.Insert(venta);
            var cargadas = VentaDB.Load();

            var recuperada = Assert.Single(cargadas, v => v.Id == nextId);
            Assert.Equal(150.00m, recuperada.Total);
            Assert.Equal("Mixto", recuperada.MetodoPago);
            Assert.Equal(100.00m, recuperada.PagoEfectivo);
            Assert.Equal(50.00m, recuperada.PagoTarjeta);
            Assert.Single(recuperada.Productos);
            Assert.Equal("Producto Prueba", recuperada.Productos[0].Nombre);
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
