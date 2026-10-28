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
                cmd.CommandText = "SELECT Id, Nombre, Telefono, Concepto, MontoOriginal, TotalAbonado FROM Deudores ORDER BY Id ASC;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);
                    var nom = reader.GetString(1);
                    var tel = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    var con = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                    var monto = Convert.ToDecimal(reader.GetDouble(4));
                    var abonado = Convert.ToDecimal(reader.GetDouble(5));

                    deudores[id] = new Deudor
                    {
                        Id = id,
                        Nombre = nom,
                        Telefono = tel,
                        Concepto = con,
                        MontoOriginal = monto,
                        TotalAbonado = abonado,
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
                using (var delCmd = conn.CreateCommand())
                {
                    delCmd.Transaction = tr;
                    delCmd.CommandText = "DELETE FROM Abonos; DELETE FROM Deudores;";
                    delCmd.ExecuteNonQuery();
                }
                using var insertDeudor = conn.CreateCommand();
                insertDeudor.Transaction = tr;
                insertDeudor.CommandText = @"
                    INSERT INTO Deudores (Id, Nombre, Telefono, Concepto, MontoOriginal, TotalAbonado)
                    VALUES (@id, @nom, @tel, @con, @monto, @abonado);";
                var dId = insertDeudor.Parameters.Add("@id", SqliteType.Integer);
                var dNom = insertDeudor.Parameters.Add("@nom", SqliteType.Text);
                var dTel = insertDeudor.Parameters.Add("@tel", SqliteType.Text);
                var dCon = insertDeudor.Parameters.Add("@con", SqliteType.Text);
                var dMonto = insertDeudor.Parameters.Add("@monto", SqliteType.Real);
                var dAbonado = insertDeudor.Parameters.Add("@abonado", SqliteType.Real);
                using var insertAbono = conn.CreateCommand();
                insertAbono.Transaction = tr;
                insertAbono.CommandText = @"
                    INSERT INTO Abonos (Id, DeudorId, Fecha, Monto)
                    VALUES (@aId, @dId, @fecha, @monto);";
                var aId = insertAbono.Parameters.Add("@aId", SqliteType.Integer);
                var aDId = insertAbono.Parameters.Add("@dId", SqliteType.Integer);
                var aFecha = insertAbono.Parameters.Add("@fecha", SqliteType.Text);
                var aMonto = insertAbono.Parameters.Add("@monto", SqliteType.Real);

                foreach (var debtor in debtors)
                {
                    dId.Value = debtor.Id;
                    dNom.Value = debtor.Nombre ?? string.Empty;
                    dTel.Value = debtor.Telefono ?? string.Empty;
                    dCon.Value = debtor.Concepto ?? string.Empty;
                    dMonto.Value = Convert.ToDouble(debtor.MontoOriginal);
                    dAbonado.Value = Convert.ToDouble(debtor.TotalAbonado);
                    insertDeudor.ExecuteNonQuery();

                    if (debtor.Abonos != null)
                    {
                        foreach (var ab in debtor.Abonos)
                        {
                            aId.Value = ab.Id;
                            aDId.Value = debtor.Id;
                            aFecha.Value = ab.Fecha.ToString("o", CultureInfo.InvariantCulture);
                            aMonto.Value = Convert.ToDouble(ab.Monto);
                            insertAbono.ExecuteNonQuery();
                        }
                    }
                }
                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }
    }
}