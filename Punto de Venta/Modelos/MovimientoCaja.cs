using Microsoft.Data.Sqlite;
using Punto_de_Venta.Datos;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Punto_de_Venta.Modelos
{
    public class MovimientoCaja
    {
        public int Id { get; set; }
        public DateTime FechaHora { get; set; } = DateTime.Now;
        public string Tipo { get; set; } = "Salida";
        public decimal Monto { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public int CorteCajaId { get; set; }
    }

    public static class MovimientoCajaDB
    {
        public static List<MovimientoCaja> Load()
        {
            var list = new List<MovimientoCaja>();
            using var conn = DatabaseHelper.GetConnection();
            TurnoCajaManager.ValidarEsquemaBD(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, FechaHora, Tipo, Monto, Concepto, CorteCajaId FROM MovimientosCaja ORDER BY Id ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fechaStr = reader.GetString(1);
                if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                {
                    DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
                }

                list.Add(new MovimientoCaja
                {
                    Id = reader.GetInt32(0),
                    FechaHora = fecha,
                    Tipo = reader.GetString(2),
                    Monto = Convert.ToDecimal(reader.GetDouble(3)),
                    Concepto = reader.GetString(4),
                    CorteCajaId = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
                });
            }
            return list;
        }

        public static List<MovimientoCaja> LoadPorRangoFechas(DateTime inicio, DateTime fin, int? corteActivoId = null)
        {
            var list = new List<MovimientoCaja>();
            DateTime inicioDia = inicio.Date;
            DateTime finExclusivo = fin.Date.AddDays(1);
            string inicioIso = inicioDia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string finIso = finExclusivo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            using var conn = DatabaseHelper.GetConnection();
            TurnoCajaManager.ValidarEsquemaBD(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FechaHora, Tipo, Monto, Concepto, CorteCajaId
                FROM MovimientosCaja
                WHERE (FechaHora >= @inicioIso AND FechaHora < @finIso)
                   OR (@corteId > 0 AND CorteCajaId = @corteId)
                   OR substr(FechaHora, 5, 1) != '-'
                ORDER BY Id ASC;";
            cmd.Parameters.AddWithValue("@inicioIso", inicioIso);
            cmd.Parameters.AddWithValue("@finIso", finIso);
            cmd.Parameters.AddWithValue("@corteId", corteActivoId ?? 0);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fechaStr = reader.GetString(1);
                if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                {
                    DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
                }

                int corteId = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                bool esDelTurnoActivo = corteActivoId.HasValue && corteActivoId.Value > 0 && corteId == corteActivoId.Value;
                if (!esDelTurnoActivo && (fecha < inicioDia || fecha >= finExclusivo))
                {
                    continue;
                }

                list.Add(new MovimientoCaja
                {
                    Id = reader.GetInt32(0),
                    FechaHora = fecha,
                    Tipo = reader.GetString(2),
                    Monto = Convert.ToDecimal(reader.GetDouble(3)),
                    Concepto = reader.GetString(4),
                    CorteCajaId = corteId
                });
            }
            return list;
        }

        public static List<MovimientoCaja> LoadPorCorteCajaId(int corteId)
        {
            var list = new List<MovimientoCaja>();
            if (corteId <= 0) return list;

            using var conn = DatabaseHelper.GetConnection();
            TurnoCajaManager.ValidarEsquemaBD(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, FechaHora, Tipo, Monto, Concepto, CorteCajaId FROM MovimientosCaja WHERE CorteCajaId = @corteId ORDER BY Id ASC;";
            cmd.Parameters.AddWithValue("@corteId", corteId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fechaStr = reader.GetString(1);
                if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                {
                    DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
                }

                list.Add(new MovimientoCaja
                {
                    Id = reader.GetInt32(0),
                    FechaHora = fecha,
                    Tipo = reader.GetString(2),
                    Monto = Convert.ToDecimal(reader.GetDouble(3)),
                    Concepto = reader.GetString(4),
                    CorteCajaId = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
                });
            }
            return list;
        }

        public static void Save(IEnumerable<MovimientoCaja> movimientos)
        {
        }
    }
}