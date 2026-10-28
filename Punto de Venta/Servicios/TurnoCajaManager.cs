using Microsoft.Data.Sqlite;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using System;
using System.Globalization;

namespace Punto_de_Venta
{
    public static class TurnoCajaManager
    {
        private static bool _esquemaValidado = false;
        public static void ValidarEsquemaBD(SqliteConnection conn)
        {
            if (_esquemaValidado) return;
            try { using var cmd = conn.CreateCommand(); cmd.CommandText = "ALTER TABLE CortesCaja ADD COLUMN FechaHoraCierre TEXT;"; cmd.ExecuteNonQuery(); } catch { }
            try { using var cmd = conn.CreateCommand(); cmd.CommandText = "ALTER TABLE MovimientosCaja ADD COLUMN CorteCajaId INTEGER DEFAULT 0;"; cmd.ExecuteNonQuery(); } catch { }
            _esquemaValidado = true;
        }
        public static void RegistrarMovimientoEfectivo(string tipo, decimal monto, string concepto)
        {
            var turnoActivo = ObtenerTurnoActivo();
            if (turnoActivo == null) throw new InvalidOperationException("No se pueden registrar movimientos. La caja está cerrada.");
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();
            try
            {
                using (var insertMovCmd = conn.CreateCommand())
                {
                    insertMovCmd.Transaction = tr;
                    insertMovCmd.CommandText = @"INSERT INTO MovimientosCaja (FechaHora, Tipo, Monto, Concepto, CorteCajaId) VALUES (@fecha, @tipo, @monto, @concepto, @corteId);";
                    insertMovCmd.Parameters.AddWithValue("@fecha", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                    insertMovCmd.Parameters.AddWithValue("@tipo", tipo);
                    insertMovCmd.Parameters.AddWithValue("@monto", Convert.ToDouble(monto));
                    insertMovCmd.Parameters.AddWithValue("@concepto", concepto);
                    insertMovCmd.Parameters.AddWithValue("@corteId", turnoActivo.Id);
                    insertMovCmd.ExecuteNonQuery();
                }

                using (var updateCorteCmd = conn.CreateCommand())
                {
                    updateCorteCmd.Transaction = tr;
                    if (tipo.Equals("Entrada", StringComparison.OrdinalIgnoreCase)) updateCorteCmd.CommandText = @"UPDATE CortesCaja SET EntradasEfectivo = EntradasEfectivo + @monto, TotalEsperado = TotalEsperado + @monto WHERE Id = @id;";
                    else if (tipo.Equals("Salida", StringComparison.OrdinalIgnoreCase) || tipo.Equals("Gasto", StringComparison.OrdinalIgnoreCase)) updateCorteCmd.CommandText = @"UPDATE CortesCaja SET SalidasEfectivo = SalidasEfectivo + @monto, TotalEsperado = TotalEsperado - @monto WHERE Id = @id;";

                    updateCorteCmd.Parameters.AddWithValue("@monto", Convert.ToDouble(monto));
                    updateCorteCmd.Parameters.AddWithValue("@id", turnoActivo.Id);
                    updateCorteCmd.ExecuteNonQuery();
                }
                tr.Commit();
            }
            catch { tr.Rollback(); throw; }
        }

        public static CorteCaja ObtenerTurnoActivo()
        {
            using var conn = DatabaseHelper.GetConnection();
            ValidarEsquemaBD(conn);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT Id, FechaHora, FechaHoraCierre, Estado, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones FROM CortesCaja WHERE Estado = 'Abierto' ORDER BY Id DESC LIMIT 1;";

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var id = reader.GetInt32(0);
                var fechaStr = reader.GetString(1);
                var fechaCierreStr = reader.IsDBNull(2) ? null : reader.GetString(2);
                DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaApertura);
                DateTime? fechaCierre = null;
                if (!string.IsNullOrEmpty(fechaCierreStr) && DateTime.TryParse(fechaCierreStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fCierre)) fechaCierre = fCierre;

                return new CorteCaja
                {
                    Id = id,
                    FechaHora = fechaApertura,
                    FechaHoraCierre = fechaCierre,
                    Estado = reader.GetString(3),
                    FondoInicial = Convert.ToDecimal(reader.GetDouble(4)),
                    VentasEfectivo = Convert.ToDecimal(reader.GetDouble(5)),
                    EntradasEfectivo = Convert.ToDecimal(reader.GetDouble(6)),
                    SalidasEfectivo = Convert.ToDecimal(reader.GetDouble(7)),
                    TotalEsperado = Convert.ToDecimal(reader.GetDouble(8)),
                    TotalContado = Convert.ToDecimal(reader.GetDouble(9)),
                    Observaciones = reader.IsDBNull(10) ? string.Empty : reader.GetString(10)
                };
            }
            return null;
        }

        public static void CerrarTurno(CorteCaja turnoActivo, decimal totalContadoFisico, string observacionesCierre, decimal ventasEfectivo)
        {
            using var conn = DatabaseHelper.GetConnection();
            ValidarEsquemaBD(conn);

            using var cmdMovimientos = conn.CreateCommand();
            cmdMovimientos.CommandText = @"SELECT COALESCE(SUM(CASE WHEN Tipo = 'Entrada' THEN Monto ELSE 0 END), 0) AS TotalEntradas, COALESCE(SUM(CASE WHEN Tipo = 'Salida' OR Tipo = 'Gasto' THEN Monto ELSE 0 END), 0) AS TotalSalidas FROM MovimientosCaja WHERE CorteCajaId = @idTurno;";
            cmdMovimientos.Parameters.AddWithValue("@idTurno", turnoActivo.Id);

            decimal totalEntradas = 0, totalSalidas = 0;
            using (var reader = cmdMovimientos.ExecuteReader())
            {
                if (reader.Read()) { totalEntradas = Convert.ToDecimal(reader.GetDouble(0)); totalSalidas = Convert.ToDecimal(reader.GetDouble(1)); }
            }

            decimal totalEsperado = turnoActivo.FondoInicial + ventasEfectivo + totalEntradas - totalSalidas;

            using var cmdCierre = conn.CreateCommand();
            cmdCierre.CommandText = @"UPDATE CortesCaja SET FechaHoraCierre = @fechaCierre, Estado = 'Cerrado', VentasEfectivo = @ventasEfectivo, EntradasEfectivo = @entradas, SalidasEfectivo = @salidas, TotalEsperado = @esperado, TotalContado = @contado, Observaciones = @obs WHERE Id = @idTurno;";
            cmdCierre.Parameters.AddWithValue("@fechaCierre", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            cmdCierre.Parameters.AddWithValue("@ventasEfectivo", Convert.ToDouble(ventasEfectivo));
            cmdCierre.Parameters.AddWithValue("@entradas", Convert.ToDouble(totalEntradas));
            cmdCierre.Parameters.AddWithValue("@salidas", Convert.ToDouble(totalSalidas));
            cmdCierre.Parameters.AddWithValue("@esperado", Convert.ToDouble(totalEsperado));
            cmdCierre.Parameters.AddWithValue("@contado", Convert.ToDouble(totalContadoFisico));
            cmdCierre.Parameters.AddWithValue("@obs", observacionesCierre);
            cmdCierre.Parameters.AddWithValue("@idTurno", turnoActivo.Id);
            cmdCierre.ExecuteNonQuery();
        }

        public static CorteCaja AbrirTurno(decimal fondoInicial, string concepto = "Fondo de apertura")
        {
            var turnoActivo = ObtenerTurnoActivo();
            if (turnoActivo != null) return turnoActivo;

            using var conn = DatabaseHelper.GetConnection();
            ValidarEsquemaBD(conn);

            using var tr = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tr;
                cmd.CommandText = @"INSERT INTO CortesCaja (FechaHora, Estado, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones) VALUES (@fecha, 'Abierto', @fondo, 0, 0, 0, @fondo, 0, @obs); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@fecha", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@fondo", Convert.ToDouble(fondoInicial));
                cmd.Parameters.AddWithValue("@obs", concepto);
                long idGenerado = (long)cmd.ExecuteScalar();
                using var cmdMov = conn.CreateCommand();
                cmdMov.Transaction = tr;
                cmdMov.CommandText = @"INSERT INTO MovimientosCaja (FechaHora, Tipo, Monto, Concepto, CorteCajaId) VALUES (@fecha, 'Fondo', @monto, @concepto, @corteId);";
                cmdMov.Parameters.AddWithValue("@fecha", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                cmdMov.Parameters.AddWithValue("@monto", Convert.ToDouble(fondoInicial));
                cmdMov.Parameters.AddWithValue("@concepto", concepto);
                cmdMov.Parameters.AddWithValue("@corteId", idGenerado);
                cmdMov.ExecuteNonQuery();
                tr.Commit();
                return new CorteCaja { Id = (int)idGenerado, FechaHora = DateTime.Now, Estado = "Abierto", FondoInicial = fondoInicial, TotalEsperado = fondoInicial, Observaciones = concepto };
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }
    }
}