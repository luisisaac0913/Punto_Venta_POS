using Microsoft.Data.Sqlite;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Punto_de_Venta.Datos
{
    public static class VentaEnEsperaDB
    {
        public static List<VentaEnEspera> Load()
        {
            var list = new List<VentaEnEspera>();

            using var conn = DatabaseHelper.GetConnection();

            try
            {
                var limitePurga = DateTime.Now.AddHours(-48).ToString("o", CultureInfo.InvariantCulture);
                using var purgeCmd = conn.CreateCommand();
                purgeCmd.CommandText = "DELETE FROM VentasEnEspera WHERE FechaHora < @limite;";
                purgeCmd.Parameters.AddWithValue("@limite", limitePurga);
                purgeCmd.ExecuteNonQuery();
            }
            catch
            {
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, FechaHora, Identificador, TotalNeto, TotalItems, CarritoJson 
                    FROM VentasEnEspera 
                    ORDER BY Id DESC;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);
                    var fechaStr = reader.GetString(1);
                    var ident = reader.GetString(2);
                    var totalNeto = Convert.ToDecimal(reader.GetDouble(3));
                    var totalItems = Convert.ToDecimal(reader.GetDouble(4));
                    var json = reader.GetString(5);

                    if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                    {
                        DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
                    }

                    List<ItemCarrito>? items = null;
                    try
                    {
                        items = JsonSerializer.Deserialize<List<ItemCarrito>>(json);
                    }
                    catch
                    {
                    }

                    list.Add(new VentaEnEspera
                    {
                        Id = id,
                        FechaHora = fecha,
                        Identificador = ident,
                        TotalNeto = totalNeto,
                        TotalItems = totalItems,
                        Items = items ?? new List<ItemCarrito>()
                    });
                }
            }

            return list;
        }

        public static int Save(VentaEnEspera venta)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();

            string json = JsonSerializer.Serialize(venta.Items);

            cmd.CommandText = @"
                INSERT INTO VentasEnEspera (FechaHora, Identificador, TotalNeto, TotalItems, CarritoJson)
                VALUES (@fecha, @ident, @total, @items, @json);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@fecha", venta.FechaHora.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@ident", venta.Identificador ?? string.Empty);
            cmd.Parameters.AddWithValue("@total", Convert.ToDouble(venta.TotalNeto));
            cmd.Parameters.AddWithValue("@items", Convert.ToDouble(venta.TotalItems));
            cmd.Parameters.AddWithValue("@json", json);

            var idGenerado = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            venta.Id = idGenerado;
            return idGenerado;
        }

        public static void Delete(int id)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM VentasEnEspera WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public static int ObtenerCantidad()
        {
            try
            {
                using var conn = DatabaseHelper.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM VentasEnEspera;";
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }
            catch
            {
                return 0;
            }
        }
    }
}

