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
            TurnoCajaManager.ValidarEsquemaBD(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FechaHora, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones, COALESCE(Estado, 'Cerrado'), FechaHoraCierre, COALESCE(VentasTarjeta, 0), COALESCE(VentasTransferencia, 0) 
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
                var estado = reader.IsDBNull(9) ? "Cerrado" : reader.GetString(9);
                var fechaCierreStr = reader.IsDBNull(10) ? null : reader.GetString(10);
                var ventasTarjeta = reader.IsDBNull(11) ? 0m : Convert.ToDecimal(reader.GetDouble(11));
                var ventasTransferencia = reader.IsDBNull(12) ? 0m : Convert.ToDecimal(reader.GetDouble(12));

                if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                {
                    DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
                }

                DateTime? fechaCierre = null;
                if (!string.IsNullOrEmpty(fechaCierreStr) && DateTime.TryParse(fechaCierreStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fCierre))
                {
                    fechaCierre = fCierre;
                }

                list.Add(new CorteCaja
                {
                    Id = id,
                    FechaHora = fecha,
                    FechaHoraCierre = fechaCierre,
                    Estado = estado,
                    FondoInicial = fondo,
                    VentasEfectivo = ventas,
                    VentasTarjeta = ventasTarjeta,
                    VentasTransferencia = ventasTransferencia,
                    EntradasEfectivo = entradas,
                    SalidasEfectivo = salidas,
                    TotalEsperado = esperado,
                    TotalContado = contado,
                    Observaciones = notas
                });
            }

            return list;
        }

        public static List<CorteCaja> LoadPorRangoFechas(DateTime inicio, DateTime fin)
        {
            var list = new List<CorteCaja>();
            DateTime inicioDia = inicio.Date;
            DateTime finExclusivo = fin.Date.AddDays(1);
            string inicioIso = inicioDia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string finIso = finExclusivo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            using var conn = DatabaseHelper.GetConnection();
            TurnoCajaManager.ValidarEsquemaBD(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FechaHora, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones, COALESCE(Estado, 'Cerrado'), FechaHoraCierre, COALESCE(VentasTarjeta, 0), COALESCE(VentasTransferencia, 0) 
                FROM CortesCaja 
                WHERE (FechaHora >= @inicioIso AND FechaHora < @finIso)
                   OR Estado = 'Abierto'
                   OR substr(FechaHora, 5, 1) != '-'
                ORDER BY Id ASC;";
            cmd.Parameters.AddWithValue("@inicioIso", inicioIso);
            cmd.Parameters.AddWithValue("@finIso", finIso);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var fechaStr = reader.GetString(1);
                var estado = reader.IsDBNull(9) ? "Cerrado" : reader.GetString(9);

                if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                {
                    DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
                }

                if (!string.Equals(estado, "Abierto", StringComparison.OrdinalIgnoreCase) &&
                    (fecha < inicioDia || fecha >= finExclusivo))
                {
                    continue;
                }

                var fondo = Convert.ToDecimal(reader.GetDouble(2));
                var ventas = Convert.ToDecimal(reader.GetDouble(3));
                var entradas = Convert.ToDecimal(reader.GetDouble(4));
                var salidas = Convert.ToDecimal(reader.GetDouble(5));
                var esperado = Convert.ToDecimal(reader.GetDouble(6));
                var contado = Convert.ToDecimal(reader.GetDouble(7));
                var notas = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
                var fechaCierreStr = reader.IsDBNull(10) ? null : reader.GetString(10);
                var ventasTarjeta = reader.IsDBNull(11) ? 0m : Convert.ToDecimal(reader.GetDouble(11));
                var ventasTransferencia = reader.IsDBNull(12) ? 0m : Convert.ToDecimal(reader.GetDouble(12));

                DateTime? fechaCierre = null;
                if (!string.IsNullOrEmpty(fechaCierreStr) && DateTime.TryParse(fechaCierreStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fCierre))
                {
                    fechaCierre = fCierre;
                }

                list.Add(new CorteCaja
                {
                    Id = id,
                    FechaHora = fecha,
                    FechaHoraCierre = fechaCierre,
                    Estado = estado,
                    FondoInicial = fondo,
                    VentasEfectivo = ventas,
                    VentasTarjeta = ventasTarjeta,
                    VentasTransferencia = ventasTransferencia,
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
            TurnoCajaManager.ValidarEsquemaBD(conn);
            using var tr = conn.BeginTransaction();

            try
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tr;
                insertCmd.CommandText = @"
                    INSERT INTO CortesCaja (Id, FechaHora, FechaHoraCierre, Estado, FondoInicial, VentasEfectivo, VentasTarjeta, VentasTransferencia, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones)
                    VALUES (@id, @fecha, @fechaCierre, @estado, @fondo, @ventas, @ventasTarj, @ventasTransf, @entradas, @salidas, @esperado, @contado, @obs)
                    ON CONFLICT(Id) DO UPDATE SET
                        FechaHora = excluded.FechaHora,
                        FechaHoraCierre = COALESCE(excluded.FechaHoraCierre, CortesCaja.FechaHoraCierre),
                        Estado = COALESCE(excluded.Estado, CortesCaja.Estado),
                        FondoInicial = excluded.FondoInicial,
                        VentasEfectivo = excluded.VentasEfectivo,
                        VentasTarjeta = excluded.VentasTarjeta,
                        VentasTransferencia = excluded.VentasTransferencia,
                        EntradasEfectivo = excluded.EntradasEfectivo,
                        SalidasEfectivo = excluded.SalidasEfectivo,
                        TotalEsperado = excluded.TotalEsperado,
                        TotalContado = excluded.TotalContado,
                        Observaciones = excluded.Observaciones;";

                var pId = insertCmd.Parameters.Add("@id", SqliteType.Integer);
                var pFecha = insertCmd.Parameters.Add("@fecha", SqliteType.Text);
                var pFechaCierre = insertCmd.Parameters.Add("@fechaCierre", SqliteType.Text);
                var pEstado = insertCmd.Parameters.Add("@estado", SqliteType.Text);
                var pFondo = insertCmd.Parameters.Add("@fondo", SqliteType.Real);
                var pVentas = insertCmd.Parameters.Add("@ventas", SqliteType.Real);
                var pVentasTarj = insertCmd.Parameters.Add("@ventasTarj", SqliteType.Real);
                var pVentasTransf = insertCmd.Parameters.Add("@ventasTransf", SqliteType.Real);
                var pEntradas = insertCmd.Parameters.Add("@entradas", SqliteType.Real);
                var pSalidas = insertCmd.Parameters.Add("@salidas", SqliteType.Real);
                var pEsperado = insertCmd.Parameters.Add("@esperado", SqliteType.Real);
                var pContado = insertCmd.Parameters.Add("@contado", SqliteType.Real);
                var pObs = insertCmd.Parameters.Add("@obs", SqliteType.Text);

                foreach (var c in cortes)
                {
                    pId.Value = c.Id;
                    pFecha.Value = c.FechaHora.ToString("o", CultureInfo.InvariantCulture);
                    pFechaCierre.Value = c.FechaHoraCierre.HasValue
                        ? c.FechaHoraCierre.Value.ToString("o", CultureInfo.InvariantCulture)
                        : DBNull.Value;
                    pEstado.Value = string.IsNullOrWhiteSpace(c.Estado) ? "Cerrado" : c.Estado;
                    pFondo.Value = Convert.ToDouble(c.FondoInicial);
                    pVentas.Value = Convert.ToDouble(c.VentasEfectivo);
                    pVentasTarj.Value = Convert.ToDouble(c.VentasTarjeta);
                    pVentasTransf.Value = Convert.ToDouble(c.VentasTransferencia);
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