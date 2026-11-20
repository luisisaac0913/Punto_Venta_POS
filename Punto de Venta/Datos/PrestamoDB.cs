using Microsoft.Data.Sqlite;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Punto_de_Venta.Datos
{
    public static class PrestamoDB
    {
        public static List<Deudor> Load()
        {
            var deudores = new Dictionary<int, Deudor>();
            using var conn = DatabaseHelper.GetConnection();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Nombre, Telefono, Concepto, MontoOriginal, TotalAbonado, COALESCE(TieneInteres, 0), COALESCE(PorcentajeInteres, 0), COALESCE(TipoInteres, 'PorcentajeSaldo'), COALESCE(FrecuenciaInteres, 'Unico'), FechaInicioInteres, COALESCE(InteresAcumulado, 0), COALESCE(LimiteCredito, 0) FROM Deudores ORDER BY Id ASC;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);
                    var nom = reader.GetString(1);
                    var tel = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    var con = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                    var monto = Convert.ToDecimal(reader.GetDouble(4));
                    var abonado = Convert.ToDecimal(reader.GetDouble(5));
                    var tieneInteres = !reader.IsDBNull(6) && reader.GetInt32(6) != 0;
                    var pctInteres = reader.IsDBNull(7) ? 0m : Convert.ToDecimal(reader.GetDouble(7));
                    var tipoInteres = reader.IsDBNull(8) ? "PorcentajeSaldo" : reader.GetString(8);
                    var frecInteres = reader.IsDBNull(9) ? "Unico" : reader.GetString(9);
                    var fechaInicioStr = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);
                    if (!DateTime.TryParse(fechaInicioStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fechaInicio))
                    {
                        fechaInicio = DateTime.Now;
                    }
                    var interesAcumulado = reader.IsDBNull(11) ? 0m : Convert.ToDecimal(reader.GetDouble(11));
                    var limiteCredito = reader.IsDBNull(12) ? 0m : Convert.ToDecimal(reader.GetDouble(12));

                    deudores[id] = new Deudor
                    {
                        Id = id,
                        Nombre = nom,
                        Telefono = tel,
                        Concepto = con,
                        MontoOriginal = monto,
                        TotalAbonado = abonado,
                        TieneInteres = tieneInteres,
                        PorcentajeInteres = pctInteres,
                        TipoInteres = tipoInteres,
                        FrecuenciaInteres = frecInteres,
                        FechaInicioInteres = fechaInicio,
                        InteresAcumulado = interesAcumulado,
                        LimiteCredito = limiteCredito,
                        Abonos = []
                    };
                }
            }
            using (var cmdAbonos = conn.CreateCommand())
            {
                cmdAbonos.CommandText = "SELECT Id, DeudorId, Fecha, Monto FROM Abonos ORDER BY Id DESC;";
                using var rAb = cmdAbonos.ExecuteReader();
                while (rAb.Read())
                {
                    var deudorId = rAb.GetInt32(1);
                    if (deudores.TryGetValue(deudorId, out var deudor))
                    {
                        var fechaStr = rAb.GetString(2);
                        if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
                        {
                            DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out f);
                        }
                        deudor.Abonos.Add(new Abono
                        {
                            Id = rAb.GetInt32(0),
                            DeudorId = deudorId,
                            Fecha = f,
                            Monto = Convert.ToDecimal(rAb.GetDouble(3))
                        });
                    }
                }
            }
            return deudores.Values.ToList();
        }

        public static void Save(IEnumerable<Deudor> debtors)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();

            try
            {
                SaveInternal(conn, tr, debtors);
                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }

        public static void RegistrarAbonoAtomico(IEnumerable<Deudor> debtors, string tipoMovimiento, decimal monto, string conceptoMovimiento, Venta ventaAbono)
        {
            var turnoActivo = Punto_de_Venta.TurnoCajaManager.ObtenerTurnoActivo();
            if (turnoActivo == null)
            {
                throw new InvalidOperationException("No se pueden registrar movimientos. La caja esta cerrada.");
            }

            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();
            try
            {
                SaveInternal(conn, tr, debtors);
                Punto_de_Venta.TurnoCajaManager.RegistrarMovimientoEnTransaccion(conn, tr, turnoActivo.Id, tipoMovimiento, monto, conceptoMovimiento);
                int nextVentaId = VentaDB.ObtenerSiguienteIdInternal(conn, tr);
                ventaAbono.Id = nextVentaId;
                foreach (var prod in ventaAbono.Productos)
                {
                    prod.VentaId = nextVentaId;
                }
                VentaDB.InsertInternal(conn, tr, ventaAbono);
                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }

        internal static int ObtenerSiguienteDeudorIdInternal(SqliteConnection conn, SqliteTransaction tr)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tr;
            cmd.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Deudores;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 1);
        }

        internal static int ObtenerSiguienteAbonoIdInternal(SqliteConnection conn, SqliteTransaction tr)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tr;
            cmd.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Abonos;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 1);
        }

        internal static void SaveInternal(SqliteConnection conn, SqliteTransaction tr, IEnumerable<Deudor> debtors)
        {
            using var insertDeudor = conn.CreateCommand();
            insertDeudor.Transaction = tr;
            insertDeudor.CommandText = @"
                INSERT INTO Deudores (Id, Nombre, Telefono, Concepto, MontoOriginal, TotalAbonado, TieneInteres, PorcentajeInteres, TipoInteres, FrecuenciaInteres, FechaInicioInteres, InteresAcumulado, LimiteCredito)
                VALUES (@id, @nom, @tel, @con, @monto, @abonado, @tieneInt, @pctInt, @tipoInt, @frecInt, @fechaInt, @intAcum, @limiteCredito)
                ON CONFLICT(Id) DO UPDATE SET 
                    Nombre = excluded.Nombre,
                    Telefono = excluded.Telefono,
                    Concepto = excluded.Concepto,
                    MontoOriginal = excluded.MontoOriginal,
                    TotalAbonado = excluded.TotalAbonado,
                    TieneInteres = excluded.TieneInteres,
                    PorcentajeInteres = excluded.PorcentajeInteres,
                    TipoInteres = excluded.TipoInteres,
                    FrecuenciaInteres = excluded.FrecuenciaInteres,
                    FechaInicioInteres = excluded.FechaInicioInteres,
                    InteresAcumulado = excluded.InteresAcumulado,
                    LimiteCredito = excluded.LimiteCredito;";

            var dId = insertDeudor.Parameters.Add("@id", SqliteType.Integer);
            var dNom = insertDeudor.Parameters.Add("@nom", SqliteType.Text);
            var dTel = insertDeudor.Parameters.Add("@tel", SqliteType.Text);
            var dCon = insertDeudor.Parameters.Add("@con", SqliteType.Text);
            var dMonto = insertDeudor.Parameters.Add("@monto", SqliteType.Real);
            var dAbonado = insertDeudor.Parameters.Add("@abonado", SqliteType.Real);
            var dTieneInt = insertDeudor.Parameters.Add("@tieneInt", SqliteType.Integer);
            var dPctInt = insertDeudor.Parameters.Add("@pctInt", SqliteType.Real);
            var dTipoInt = insertDeudor.Parameters.Add("@tipoInt", SqliteType.Text);
            var dFrecInt = insertDeudor.Parameters.Add("@frecInt", SqliteType.Text);
            var dFechaInt = insertDeudor.Parameters.Add("@fechaInt", SqliteType.Text);
            var dIntAcum = insertDeudor.Parameters.Add("@intAcum", SqliteType.Real);
            var dLimite = insertDeudor.Parameters.Add("@limiteCredito", SqliteType.Real);

            using var insertAbono = conn.CreateCommand();
            insertAbono.Transaction = tr;
            insertAbono.CommandText = @"
                INSERT INTO Abonos (Id, DeudorId, Fecha, Monto)
                VALUES (@aId, @dId, @fecha, @monto)
                ON CONFLICT(Id) DO UPDATE SET
                    DeudorId = excluded.DeudorId,
                    Fecha = excluded.Fecha,
                    Monto = excluded.Monto;";

            var aId = insertAbono.Parameters.Add("@aId", SqliteType.Integer);
            var aDId = insertAbono.Parameters.Add("@dId", SqliteType.Integer);
            var aFecha = insertAbono.Parameters.Add("@fecha", SqliteType.Text);
            var aMonto = insertAbono.Parameters.Add("@monto", SqliteType.Real);

            foreach (var debtor in debtors)
            {
                if (debtor.Id <= 0)
                {
                    debtor.Id = ObtenerSiguienteDeudorIdInternal(conn, tr);
                }

                dId.Value = debtor.Id;
                dNom.Value = debtor.Nombre ?? string.Empty;
                dTel.Value = debtor.Telefono ?? string.Empty;
                dCon.Value = debtor.Concepto ?? string.Empty;
                dMonto.Value = Convert.ToDouble(debtor.MontoOriginal);
                dAbonado.Value = Convert.ToDouble(debtor.TotalAbonado);
                dTieneInt.Value = debtor.TieneInteres ? 1 : 0;
                dPctInt.Value = Convert.ToDouble(debtor.PorcentajeInteres);
                dTipoInt.Value = string.IsNullOrWhiteSpace(debtor.TipoInteres) ? "PorcentajeSaldo" : debtor.TipoInteres;
                dFrecInt.Value = string.IsNullOrWhiteSpace(debtor.FrecuenciaInteres) ? "Unico" : debtor.FrecuenciaInteres;
                DateTime fechaInicio = debtor.FechaInicioInteres == default ? DateTime.Now : debtor.FechaInicioInteres;
                dFechaInt.Value = fechaInicio.ToString("o", CultureInfo.InvariantCulture);
                dIntAcum.Value = Convert.ToDouble(debtor.InteresAcumulado);
                dLimite.Value = Convert.ToDouble(debtor.LimiteCredito);
                insertDeudor.ExecuteNonQuery();

                if (debtor.Abonos != null)
                {
                    foreach (var ab in debtor.Abonos)
                    {
                        if (ab.Id <= 0)
                        {
                            ab.Id = ObtenerSiguienteAbonoIdInternal(conn, tr);
                        }
                        ab.DeudorId = debtor.Id;

                        aId.Value = ab.Id;
                        aDId.Value = debtor.Id;
                        aFecha.Value = ab.Fecha.ToString("o", CultureInfo.InvariantCulture);
                        aMonto.Value = Convert.ToDouble(ab.Monto);
                        insertAbono.ExecuteNonQuery();
                    }
                }
            }
        }

        public static bool Delete(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();
            try
            {
                using (var cmdAbonos = conn.CreateCommand())
                {
                    cmdAbonos.Transaction = tr;
                    cmdAbonos.CommandText = "DELETE FROM Abonos WHERE DeudorId = @id;";
                    cmdAbonos.Parameters.AddWithValue("@id", id);
                    cmdAbonos.ExecuteNonQuery();
                }
                int afectados;
                using (var cmdDeudor = conn.CreateCommand())
                {
                    cmdDeudor.Transaction = tr;
                    cmdDeudor.CommandText = "DELETE FROM Deudores WHERE Id = @id;";
                    cmdDeudor.Parameters.AddWithValue("@id", id);
                    afectados = cmdDeudor.ExecuteNonQuery();
                }
                tr.Commit();
                return afectados > 0;
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }
    }
}