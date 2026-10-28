using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace Punto_de_Venta.Datos
{
    public static class DatabaseHelper
    {
        public static string DbFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POS_DataStorage");
        public static string DbPath => Path.Combine(DbFolder, "pos_data.db");
        public static string ConnectionString => new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        public static void ActualizarEsquemaVentas()
        {
            using var conn = GetConnection();
            string[] queries = {
                "ALTER TABLE Ventas ADD COLUMN PagoEfectivo REAL DEFAULT 0;",
                "ALTER TABLE Ventas ADD COLUMN PagoTarjeta REAL DEFAULT 0;",
                "ALTER TABLE Ventas ADD COLUMN PagoTransferencia REAL DEFAULT 0;",
                "ALTER TABLE Ventas ADD COLUMN Estado TEXT NOT NULL DEFAULT 'Completada';"
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
            return conn;
        }
        /// <summary>
        /// 
        /// </summary>
        public static void InicializarBaseDeDatos()
        {
            if (!Directory.Exists(DbFolder))
            {
                Directory.CreateDirectory(DbFolder);
            }
            using var conn = GetConnection();
            using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                pragmaCmd.ExecuteNonQuery();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                -- 1. PRODUCTOS (Actualizado)
                CREATE TABLE IF NOT EXISTS Productos (
                    Id INTEGER PRIMARY KEY,
                    Codigo TEXT NOT NULL,
                    Nombre TEXT NOT NULL,
                    Categoria TEXT NOT NULL,
                    Costo REAL NOT NULL,
                    Ganancia REAL NOT NULL,
                    Precio REAL NOT NULL,
                    Stock INTEGER NOT NULL,
                    PedirCantidadAlCobrar INTEGER NOT NULL DEFAULT 0,
                    SeVendePorPeso INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_Productos_Codigo ON Productos(Codigo);
                CREATE INDEX IF NOT EXISTS IX_Productos_Nombre ON Productos(Nombre);

                -- 2. VENTAS (Con Estado garantizado)
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

                -- 3. DETALLE DE VENTAS
                CREATE TABLE IF NOT EXISTS VentasDetalle (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    VentaId INTEGER NOT NULL,
                    ProductoId INTEGER NOT NULL,
                    Nombre TEXT NOT NULL,
                    Cantidad INTEGER NOT NULL,
                    Costo REAL NOT NULL,
                    Ganancia REAL NOT NULL,
                    Precio REAL NOT NULL,
                    Categoria TEXT NOT NULL,
                    FOREIGN KEY (VentaId) REFERENCES Ventas(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_VentasDetalle_VentaId ON VentasDetalle(VentaId);

                -- 4. MOVIMIENTOS DE CAJA
                CREATE TABLE IF NOT EXISTS MovimientosCaja (
                    Id INTEGER PRIMARY KEY,
                    FechaHora TEXT NOT NULL,
                    Tipo TEXT NOT NULL, -- 'Entrada', 'Salida' o 'Fondo'
                    Monto REAL NOT NULL,
                    Concepto TEXT NOT NULL,
                    CorteCajaId INTEGER NULL,
                    FOREIGN KEY (CorteCajaId) REFERENCES CortesCaja(Id) ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Movimientos_FechaHora ON MovimientosCaja(FechaHora);

                -- 5. DEUDORES / PRÉSTAMOS
                CREATE TABLE IF NOT EXISTS Deudores (
                    Id INTEGER PRIMARY KEY,
                    Nombre TEXT NOT NULL,
                    Telefono TEXT,
                    Concepto TEXT,
                    MontoOriginal REAL NOT NULL,
                    TotalAbonado REAL NOT NULL DEFAULT 0
                );

                -- 6. ABONOS DE PRÉSTAMOS
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
            ";
            cmd.ExecuteNonQuery();
            ActualizarEsquemaVentas();
            using (var alterCmd2 = conn.CreateCommand())
            {
                alterCmd2.CommandText = "PRAGMA table_info(MovimientosCaja);";
                bool tieneCorteId = false;
                using (var reader = alterCmd2.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(1).Equals("CorteCajaId", StringComparison.OrdinalIgnoreCase))
                        {
                            tieneCorteId = true;
                            break;
                        }
                    }
                }
                if (!tieneCorteId)
                {
                    using var updateCmd2 = conn.CreateCommand();
                    updateCmd2.CommandText = "ALTER TABLE MovimientosCaja ADD COLUMN CorteCajaId INTEGER NULL;";
                    updateCmd2.ExecuteNonQuery();
                }
            }
            using (var alterCmd = conn.CreateCommand())
            {
                alterCmd.CommandText = "PRAGMA table_info(Productos);";
                bool tieneSeVendePorPeso = false;
                using (var reader = alterCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(1).Equals("SeVendePorPeso", StringComparison.OrdinalIgnoreCase))
                        {
                            tieneSeVendePorPeso = true;
                            break;
                        }
                    }
                }
                if (!tieneSeVendePorPeso)
                {
                    using var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = "ALTER TABLE Productos ADD COLUMN SeVendePorPeso INTEGER NOT NULL DEFAULT 0;";
                    updateCmd.ExecuteNonQuery();
                }
            }
        }
        public static void MigrarVentasYMovimientosSiEsNecesario()
        {
            using var conn = GetConnection();
            using (var checkMovCmd = conn.CreateCommand())
            {
                checkMovCmd.CommandText = "SELECT COUNT(1) FROM MovimientosCaja;";
                long countMov = (long)(checkMovCmd.ExecuteScalar() ?? 0L);

                string txtMov = Path.Combine(DbFolder, "movimientos_caja.txt");
                if (countMov == 0 && File.Exists(txtMov))
                {
                    using var tr = conn.BeginTransaction();
                    try
                    {
                        var lines = File.ReadAllLines(txtMov);
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(new[] { "//" }, StringSplitOptions.None);
                            if (parts.Length < 5) continue;

                            if (!int.TryParse(parts[0], out var id)) continue;
                            var fechaStr = parts[1].Trim();
                            var tipo = parts[2].Trim();
                            decimal.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var monto);
                            var concepto = parts[4].Trim();

                            using var insertMov = conn.CreateCommand();
                            insertMov.Transaction = tr;
                            insertMov.CommandText = @"
                                INSERT INTO MovimientosCaja (Id, FechaHora, Tipo, Monto, Concepto)
                                VALUES (@id, @fecha, @tipo, @monto, @concepto);";
                            insertMov.Parameters.AddWithValue("@id", id);
                            insertMov.Parameters.AddWithValue("@fecha", fechaStr);
                            insertMov.Parameters.AddWithValue("@tipo", tipo);
                            insertMov.Parameters.AddWithValue("@monto", monto);
                            insertMov.Parameters.AddWithValue("@concepto", concepto);
                            insertMov.ExecuteNonQuery();
                        }
                        tr.Commit();
                    }
                    catch
                    {
                        tr.Rollback();
                    }
                }
            }
            using (var checkVentasCmd = conn.CreateCommand())
            {
                checkVentasCmd.CommandText = "SELECT COUNT(1) FROM Ventas;";
                long countVentas = (long)(checkVentasCmd.ExecuteScalar() ?? 0L);
                string txtVentas = Path.Combine(DbFolder, "ventas.txt");
                if (countVentas == 0 && File.Exists(txtVentas))
                {
                    using var tr = conn.BeginTransaction();
                    try
                    {
                        var lines = File.ReadAllLines(txtVentas);
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(new[] { "//" }, StringSplitOptions.None);
                            if (parts.Length < 7) continue;
                            if (!int.TryParse(parts[0], out var id)) continue;
                            var fechaStr = parts[1].Trim();
                            decimal.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var total);
                            decimal.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pagoCon);
                            decimal.TryParse(parts[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cambio);
                            var estado = parts[5].Trim();
                            var metodoPago = parts[6].Trim();

                            using var insertVenta = conn.CreateCommand();
                            insertVenta.Transaction = tr;
                            insertVenta.CommandText = @"
                                INSERT INTO Ventas (Id, FechaHora, Total, PagoCon, Cambio, Estado, MetodoPago)
                                VALUES (@id, @fecha, @total, @pago, @cambio, @estado, @metodo);";
                            insertVenta.Parameters.AddWithValue("@id", id);
                            insertVenta.Parameters.AddWithValue("@fecha", fechaStr);
                            insertVenta.Parameters.AddWithValue("@total", total);
                            insertVenta.Parameters.AddWithValue("@pago", pagoCon);
                            insertVenta.Parameters.AddWithValue("@cambio", cambio);
                            insertVenta.Parameters.AddWithValue("@estado", estado);
                            insertVenta.Parameters.AddWithValue("@metodo", metodoPago);
                            insertVenta.ExecuteNonQuery();
                        }
                        tr.Commit();
                    }
                    catch
                    {
                        tr.Rollback();
                    }
                }
            }
        }
        public static void MigrarCortesYPrestamosSiEsNecesario()
        {
            using var conn = GetConnection();
            using (var checkCortesCmd = conn.CreateCommand())
            {
                checkCortesCmd.CommandText = "SELECT COUNT(1) FROM CortesCaja;";
                long countCortes = (long)(checkCortesCmd.ExecuteScalar() ?? 0L);

                string txtCortes = Path.Combine(DbFolder, "cortes.txt");
                if (countCortes == 0 && File.Exists(txtCortes))
                {
                    using var tr = conn.BeginTransaction();
                    try
                    {
                        var lines = File.ReadAllLines(txtCortes);
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(new[] { "//" }, StringSplitOptions.None);
                            if (parts.Length < 8) continue;

                            if (!int.TryParse(parts[0], out var id)) continue;
                            var fechaStr = parts[1].Trim();
                            decimal.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var fondo);
                            decimal.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ventas);
                            decimal.TryParse(parts[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var entradas);
                            decimal.TryParse(parts[5], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var salidas);
                            decimal.TryParse(parts[6], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var esperado);
                            decimal.TryParse(parts[7], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var contado);
                            var notas = parts.Length > 8 ? parts[8].Trim() : string.Empty;

                            using var insertCmd = conn.CreateCommand();
                            insertCmd.Transaction = tr;
                            insertCmd.CommandText = @"
                                INSERT INTO CortesCaja (Id, FechaHora, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones)
                                VALUES (@id, @fecha, @fondo, @ventas, @entradas, @salidas, @esperado, @contado, @obs);";
                            insertCmd.Parameters.AddWithValue("@id", id);
                            insertCmd.Parameters.AddWithValue("@fecha", fechaStr);
                            insertCmd.Parameters.AddWithValue("@fondo", fondo);
                            insertCmd.Parameters.AddWithValue("@ventas", ventas);
                            insertCmd.Parameters.AddWithValue("@entradas", entradas);
                            insertCmd.Parameters.AddWithValue("@salidas", salidas);
                            insertCmd.Parameters.AddWithValue("@esperado", esperado);
                            insertCmd.Parameters.AddWithValue("@contado", contado);
                            insertCmd.Parameters.AddWithValue("@obs", notas);
                            insertCmd.ExecuteNonQuery();
                        }
                        tr.Commit();
                    }
                    catch
                    {
                        tr.Rollback();
                    }
                }
            }

            using (var checkDeudoresCmd = conn.CreateCommand())
            {
                checkDeudoresCmd.CommandText = "SELECT COUNT(1) FROM Deudores;";
                long countDeudores = (long)(checkDeudoresCmd.ExecuteScalar() ?? 0L);

                string txtPrestamos = Path.Combine(DbFolder, "prestamos.txt");
                if (countDeudores == 0 && File.Exists(txtPrestamos))
                {
                    using var tr = conn.BeginTransaction();
                    try
                    {
                        var lines = File.ReadAllLines(txtPrestamos);

                        using var InsertarDeudor = conn.CreateCommand();
                        InsertarDeudor.Transaction = tr;
                        InsertarDeudor.CommandText = @"
                            INSERT INTO Deudores (Id, Nombre, Telefono, Concepto, MontoOriginal, TotalAbonado)
                            VALUES (@id, @nom, @tel, @con, @monto, @abonado);";

                        var pId = InsertarDeudor.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var pNom = InsertarDeudor.Parameters.Add("@nom", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pTel = InsertarDeudor.Parameters.Add("@tel", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pCon = InsertarDeudor.Parameters.Add("@con", Microsoft.Data.Sqlite.SqliteType.Text);
                        var pMonto = InsertarDeudor.Parameters.Add("@monto", Microsoft.Data.Sqlite.SqliteType.Real);
                        var pAbonado = InsertarDeudor.Parameters.Add("@abonado", Microsoft.Data.Sqlite.SqliteType.Real);

                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(new[] { "//" }, StringSplitOptions.None);
                            if (parts.Length < 4) continue;

                            if (!int.TryParse(parts[0].Trim(), out var id)) continue;
                            var nombre = parts[1].Trim();
                            var concepto = parts[2].Trim();
                            var telefono = string.Empty;

                            decimal montoOriginal = 0m;
                            decimal totalAbonado = 0m;

                            if (parts.Length > 3)
                            {
                                decimal.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out montoOriginal);
                            }

                            if (parts.Length > 4)
                            {
                                decimal.TryParse(parts[4].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out totalAbonado);
                            }

                            if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
                            {
                                telefono = parts[6].Trim();
                            }

                            pId.Value = id;
                            pNom.Value = nombre;
                            pTel.Value = telefono;
                            pCon.Value = concepto;
                            pMonto.Value = Convert.ToDouble(montoOriginal);
                            pAbonado.Value = Convert.ToDouble(totalAbonado);

                            InsertarDeudor.ExecuteNonQuery();
                        }

                        tr.Commit();
                    }
                    catch
                    {
                        tr.Rollback();
                    }
                }
            }
        }

        public static void MigrarDesdeArchivosPlanosSiEsNecesario()
        {
            using var conn = GetConnection();
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(1) FROM Productos;";
            long count = (long)(checkCmd.ExecuteScalar() ?? 0L);

            if (count > 0)
            {
                return;
            }

            string txtProductos = Path.Combine(DbFolder, "productos.txt");
            if (!File.Exists(txtProductos))
            {
                return;
            }

            using var transaccion = conn.BeginTransaction();
            try
            {
                var lines = File.ReadAllLines(txtProductos);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var partes = line.Split(new[] { "//" }, StringSplitOptions.None);
                    if (partes.Length < 7) continue;

                    if (!int.TryParse(partes[0], out var id)) continue;
                    var codigo = partes[1].Trim();
                    var nombre = partes[2].Trim();
                    var categoria = partes.Length > 3 ? partes[3].Trim() : "General";

                    decimal.TryParse(partes[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var costo);
                    decimal.TryParse(partes[5], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ganancia);
                    decimal.TryParse(partes[6], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var precio);
                    int cant = 0;
                    if (partes.Length > 7) int.TryParse(partes[7], out cant);
                    bool pedirCantidad = false;
                    if (partes.Length > 8) bool.TryParse(partes[8], out pedirCantidad);
                    using var insertCmd = conn.CreateCommand();
                    insertCmd.Transaction = transaccion;
                    insertCmd.CommandText = @"
                        INSERT INTO Productos (Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar)
                        VALUES (@id, @codigo, @nombre, @categoria, @costo, @ganancia, @precio, @stock, @pedirCantidad);";
                    insertCmd.Parameters.AddWithValue("@id", id);
                    insertCmd.Parameters.AddWithValue("@codigo", codigo);
                    insertCmd.Parameters.AddWithValue("@nombre", nombre);
                    insertCmd.Parameters.AddWithValue("@categoria", categoria);
                    insertCmd.Parameters.AddWithValue("@costo", costo);
                    insertCmd.Parameters.AddWithValue("@ganancia", ganancia);
                    insertCmd.Parameters.AddWithValue("@precio", precio);
                    insertCmd.Parameters.AddWithValue("@stock", cant);
                    insertCmd.Parameters.AddWithValue("@pedirCantidad", pedirCantidad ? 1 : 0);
                    insertCmd.ExecuteNonQuery();
                }

                transaccion.Commit();
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }
        }
    }
}