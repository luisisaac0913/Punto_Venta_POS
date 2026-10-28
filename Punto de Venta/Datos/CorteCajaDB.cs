using Microsoft.Data.Sqlite;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Punto_de_Venta.Datos
{
    public static class CorteCajaDB
    {
        public static List<CorteCaja> Load()
        {
            var list = new List<CorteCaja>();

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FechaHora, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones 
                FROM CortesCaja 
                ORDER BY Id ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var fechaStr = reader.GetString(1);
                var fondo = Convert.ToDecimal(reader.GetDouble(2));
                var ventas = Convert.ToDecimal(reader.GetDouble(3));
                var entradas = Convert.ToDecimal(reader.GetDouble(4));
                var salidas = Convert.ToDecimal(reader.GetDouble(5));
                var esperado = Convert.ToDecimal(reader.GetDouble(6));
                var contado = Convert.ToDecimal(reader.GetDouble(7));
                var notas = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
                if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                {
                    DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
                }
                list.Add(new CorteCaja
                {
                    Id = id,
                    FechaHora = fecha,
                    FondoInicial = fondo,
                    VentasEfectivo = ventas,
                    EntradasEfectivo = entradas,
                    SalidasEfectivo = salidas,
                    TotalEsperado = esperado,
                    TotalContado = contado,
                    Observaciones = notas
                });
            }

            return list;
        }

        public static void Save(IEnumerable<CorteCaja> cortes)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();

            try
            {
                using (var delCmd = conn.CreateCommand())
                {
                    delCmd.Transaction = tr;
                    delCmd.CommandText = "DELETE FROM CortesCaja;";
                    delCmd.ExecuteNonQuery();
                }

                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tr;
                insertCmd.CommandText = @"
                    INSERT INTO CortesCaja (Id, FechaHora, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones)
                    VALUES (@id, @fecha, @fondo, @ventas, @entradas, @salidas, @esperado, @contado, @obs);";

                var pId = insertCmd.Parameters.Add("@id", SqliteType.Integer);
                var pFecha = insertCmd.Parameters.Add("@fecha", SqliteType.Text);
                var pFondo = insertCmd.Parameters.Add("@fondo", SqliteType.Real);
                var pVentas = insertCmd.Parameters.Add("@ventas", SqliteType.Real);
                var pEntradas = insertCmd.Parameters.Add("@entradas", SqliteType.Real);
                var pSalidas = insertCmd.Parameters.Add("@salidas", SqliteType.Real);
                var pEsperado = insertCmd.Parameters.Add("@esperado", SqliteType.Real);
                var pContado = insertCmd.Parameters.Add("@contado", SqliteType.Real);
                var pObs = insertCmd.Parameters.Add("@obs", SqliteType.Text);

                foreach (var c in cortes)
                {
                    pId.Value = c.Id;
                    pFecha.Value = c.FechaHora.ToString("o", CultureInfo.InvariantCulture);
                    pFondo.Value = Convert.ToDouble(c.FondoInicial);
                    pVentas.Value = Convert.ToDouble(c.VentasEfectivo);
                    pEntradas.Value = Convert.ToDouble(c.EntradasEfectivo);
                    pSalidas.Value = Convert.ToDouble(c.SalidasEfectivo);
                    pEsperado.Value = Convert.ToDouble(c.TotalEsperado);
                    pContado.Value = Convert.ToDouble(c.TotalContado);
                    pObs.Value = c.Observaciones ?? string.Empty;

                    insertCmd.ExecuteNonQuery();
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