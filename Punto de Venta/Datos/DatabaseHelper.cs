using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace Punto_de_Venta.Datos
{
    public static class DatabaseHelper
    {
        public static string DbFolder =>
            Environment.GetEnvironmentVariable("POS_DB_FOLDER") is { Length: > 0 } customFolder
                ? customFolder
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "POS_DataStorage");
        public static string DbPath
        {
            get
            {
                string subDb = Path.Combine(DbFolder, "BaseDatos", "pos_data.db");
                if (File.Exists(subDb)) return subDb;
                return Path.Combine(DbFolder, "pos_data.db");
            }
        }
        public static string ConnectionString => new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            DefaultTimeout = 30
        }.ToString();

        public static void ActualizarEsquemaVentas()
        {
            using var conn = GetConnection();
            string[] queries = {
                "ALTER TABLE Ventas ADD COLUMN PagoEfectivo REAL DEFAULT 0;",
                "ALTER TABLE Ventas ADD COLUMN PagoTarjeta REAL DEFAULT 0;",
                "ALTER TABLE Ventas ADD COLUMN PagoTransferencia REAL DEFAULT 0;",
                "ALTER TABLE Ventas ADD COLUMN Estado TEXT NOT NULL DEFAULT 'Completada';",
                "ALTER TABLE MovimientosCaja ADD COLUMN CorteCajaId INTEGER NULL;",
                "ALTER TABLE Productos ADD COLUMN PedirCantidadAlCobrar INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE Productos ADD COLUMN SeVendePorPeso INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE Productos ADD COLUMN ContarComoUnaPieza INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE Productos ADD COLUMN StockMinimo REAL NOT NULL DEFAULT 5;",
                "ALTER TABLE VentasDetalle ADD COLUMN SeVendePorPeso INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE VentasDetalle ADD COLUMN ContarComoUnaPieza INTEGER NOT NULL DEFAULT 0;"
            };
            foreach (var query in queries)
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = query;
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                }
            }
        }

        public static SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA busy_timeout = 5000;";
            cmd.ExecuteNonQuery();
            return conn;
        }

        public static void InicializarBaseDeDatos()
        {
            if (!Directory.Exists(DbFolder))
            {
                Directory.CreateDirectory(DbFolder);
            }
            using var conn = GetConnection();
            using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
                pragmaCmd.ExecuteNonQuery();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Productos (
                    Id INTEGER PRIMARY KEY,
                    Codigo TEXT NOT NULL,
                    Nombre TEXT NOT NULL,
                    Categoria TEXT NOT NULL,
                    Costo REAL NOT NULL,
                    Ganancia REAL NOT NULL,
                    Precio REAL NOT NULL,
                    Stock REAL NOT NULL,
                    PedirCantidadAlCobrar INTEGER NOT NULL DEFAULT 0,
                    SeVendePorPeso INTEGER NOT NULL DEFAULT 0,
                    ContarComoUnaPieza INTEGER NOT NULL DEFAULT 0,
                    StockMinimo REAL NOT NULL DEFAULT 5
                );
                CREATE INDEX IF NOT EXISTS IX_Productos_Codigo ON Productos(Codigo);
                CREATE INDEX IF NOT EXISTS IX_Productos_Nombre ON Productos(Nombre);

                CREATE TABLE IF NOT EXISTS Ventas (
                    Id INTEGER PRIMARY KEY,
                    FechaHora TEXT NOT NULL,
                    Total REAL NOT NULL,
                    PagoCon REAL NOT NULL DEFAULT 0,
                    Cambio REAL NOT NULL DEFAULT 0,
                    Estado TEXT NOT NULL DEFAULT 'Completada',
                    MetodoPago TEXT NOT NULL,
                    PagoEfectivo REAL DEFAULT 0,
                    PagoTarjeta REAL DEFAULT 0,
                    PagoTransferencia REAL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_Ventas_FechaHora ON Ventas(FechaHora);

                CREATE TABLE IF NOT EXISTS VentasDetalle (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    VentaId INTEGER NOT NULL,
                    ProductoId INTEGER NOT NULL,
                    Nombre TEXT NOT NULL,
                    Cantidad REAL NOT NULL,
                    Costo REAL NOT NULL,
                    Ganancia REAL NOT NULL,
                    Precio REAL NOT NULL,
                    Categoria TEXT NOT NULL,
                    SeVendePorPeso INTEGER NOT NULL DEFAULT 0,
                    ContarComoUnaPieza INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (VentaId) REFERENCES Ventas(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_VentasDetalle_VentaId ON VentasDetalle(VentaId);

                CREATE TABLE IF NOT EXISTS MovimientosCaja (
                    Id INTEGER PRIMARY KEY,
                    FechaHora TEXT NOT NULL,
                    Tipo TEXT NOT NULL,
                    Monto REAL NOT NULL,
                    Concepto TEXT NOT NULL,
                    CorteCajaId INTEGER NULL,
                    FOREIGN KEY (CorteCajaId) REFERENCES CortesCaja(Id) ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Movimientos_FechaHora ON MovimientosCaja(FechaHora);

                CREATE TABLE IF NOT EXISTS Deudores (
                    Id INTEGER PRIMARY KEY,
                    Nombre TEXT NOT NULL,
                    Telefono TEXT,
                    Concepto TEXT,
                    MontoOriginal REAL NOT NULL,
                    TotalAbonado REAL NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS Abonos (
                    Id INTEGER PRIMARY KEY,
                    DeudorId INTEGER NOT NULL,
                    Fecha TEXT NOT NULL,
                    Monto REAL NOT NULL,
                    FOREIGN KEY (DeudorId) REFERENCES Deudores(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS CortesCaja (
                    Id INTEGER PRIMARY KEY,
                    FechaHora TEXT NOT NULL,
                    FechaHoraCierre TEXT NULL,
                    Estado TEXT NOT NULL DEFAULT 'Cerrado',
                    FondoInicial REAL NOT NULL,
                    VentasEfectivo REAL NOT NULL DEFAULT 0,
                    EntradasEfectivo REAL NOT NULL DEFAULT 0,
                    SalidasEfectivo REAL NOT NULL DEFAULT 0,
                    TotalEsperado REAL NOT NULL DEFAULT 0,
                    TotalContado REAL NOT NULL DEFAULT 0,
                    Observaciones TEXT
                );

                CREATE TABLE IF NOT EXISTS Promociones (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nombre TEXT NOT NULL,
                    Tipo TEXT NOT NULL,
                    Activa INTEGER NOT NULL DEFAULT 1,
                    FechaInicio TEXT NULL,
                    FechaFin TEXT NULL,
                    ProductoIdPrincipal INTEGER NOT NULL,
                    CantidadMinima INTEGER DEFAULT 1,
                    PrecioEspecial REAL DEFAULT 0.0,
                    ProductoIdSecundario INTEGER NULL,
                    PrecioComboTotal REAL DEFAULT 0.0,
                    PorcentajeDescuento REAL DEFAULT 0.0
                );

                CREATE TABLE IF NOT EXISTS VentasEnEspera (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FechaHora TEXT NOT NULL,
                    Identificador TEXT NOT NULL,
                    TotalNeto REAL NOT NULL,
                    TotalItems REAL NOT NULL,
                    CarritoJson TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_VentasEnEspera_FechaHora ON VentasEnEspera(FechaHora);

                CREATE TABLE IF NOT EXISTS NotasContactos (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Titulo TEXT NOT NULL,
                    Telefono TEXT,
                    Contenido TEXT,
                    FechaModificacion TEXT NOT NULL,
                    EsFijado INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_NotasContactos_Fecha ON NotasContactos(FechaModificacion);
            ";
            cmd.ExecuteNonQuery();
            ActualizarEsquemaVentas();
        }
    }
}
