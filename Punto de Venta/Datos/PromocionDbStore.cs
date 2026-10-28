using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Punto_de_Venta.Modelos;

namespace Punto_de_Venta.Datos
{
    public static class PromocionDbStore
    {
        private static bool _tablaAsegurada = false;

        private static void AsegurarTabla(SqliteConnection conn)
        {
            if (_tablaAsegurada) return;
            using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA foreign_keys = OFF;";
                pragmaCmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
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
                    );";
                cmd.ExecuteNonQuery();
            }
            _tablaAsegurada = true;
        }
        public static List<Promocion> LoadAll()
        {
            var list = new List<Promocion>();
            try
            {
                using var conn = DatabaseHelper.GetConnection();
                AsegurarTabla(conn);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT Id, Nombre, Tipo, Activa, FechaInicio, FechaFin,
                           ProductoIdPrincipal, CantidadMinima, PrecioEspecial,
                           ProductoIdSecundario, PrecioComboTotal, PorcentajeDescuento
                    FROM Promociones
                    ORDER BY Id DESC;";
                using var leer = cmd.ExecuteReader();
                while (leer.Read())
                {
                    list.Add(new Promocion
                    {
                        Id = leer.GetInt32(0),
                        Nombre = leer.GetString(1),
                        Tipo = Enum.TryParse<TipoPromocion>(leer.GetString(2), out var t) ? t : TipoPromocion.Volumen,
                        Activa = leer.GetInt32(3) == 1,
                        FechaInicio = leer.IsDBNull(4) ? null : DateTime.Parse(leer.GetString(4)),
                        FechaFin = leer.IsDBNull(5) ? null : DateTime.Parse(leer.GetString(5)),
                        ProductoIdPrincipal = leer.GetInt32(6),
                        CantidadMinima = leer.GetInt32(7),
                        PrecioEspecial = Convert.ToDecimal(leer.GetDouble(8)),
                        ProductoIdSecundario = leer.IsDBNull(9) ? 0 : leer.GetInt32(9),
                        PrecioComboTotal = Convert.ToDecimal(leer.GetDouble(10)),
                        PorcentajeDescuento = Convert.ToDecimal(leer.GetDouble(11))
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Promociones] Error cargando: {ex.Message}");
            }
            return list;
        }

        public static bool Save(Promocion promo, out string mensajeError)
        {
            mensajeError = string.Empty;
            try
            {
                using var conn = DatabaseHelper.GetConnection();
                using (var pragmaCmd = conn.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA foreign_keys = OFF;";
                    pragmaCmd.ExecuteNonQuery();
                }
                AsegurarTabla(conn);
                using var cmd = conn.CreateCommand();
                if (promo.Id == 0)
                {
                    cmd.CommandText = @"
                        INSERT INTO Promociones (
                            Nombre, Tipo, Activa, FechaInicio, FechaFin,
                            ProductoIdPrincipal, CantidadMinima, PrecioEspecial,
                            ProductoIdSecundario, PrecioComboTotal, PorcentajeDescuento
                        ) VALUES (
                            @nom, @tipo, @act, @fi, @ff,
                            @p1, @cant, @esp,
                            @p2, @combo, @desc
                        );";
                }
                else
                {
                    cmd.CommandText = @"
                        UPDATE Promociones SET
                            Nombre = @nom,
                            Tipo = @tipo,
                            Activa = @act,
                            FechaInicio = @fi,
                            FechaFin = @ff,
                            ProductoIdPrincipal = @p1,
                            CantidadMinima = @cant,
                            PrecioEspecial = @esp,
                            ProductoIdSecundario = @p2,
                            PrecioComboTotal = @combo,
                            PorcentajeDescuento = @desc
                        WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", promo.Id);
                }
                cmd.Parameters.AddWithValue("@nom", promo.Nombre.Trim());
                cmd.Parameters.AddWithValue("@tipo", promo.Tipo.ToString());
                cmd.Parameters.AddWithValue("@act", promo.Activa ? 1 : 0);
                cmd.Parameters.AddWithValue("@fi", promo.FechaInicio.HasValue ? promo.FechaInicio.Value.ToString("yyyy-MM-dd") : DBNull.Value);
                cmd.Parameters.AddWithValue("@ff", promo.FechaFin.HasValue ? promo.FechaFin.Value.ToString("yyyy-MM-dd") : DBNull.Value);
                cmd.Parameters.AddWithValue("@p1", promo.ProductoIdPrincipal);
                cmd.Parameters.AddWithValue("@cant", promo.CantidadMinima);
                cmd.Parameters.AddWithValue("@esp", Convert.ToDouble(promo.PrecioEspecial));
                if (promo.ProductoIdSecundario > 0)
                    cmd.Parameters.AddWithValue("@p2", promo.ProductoIdSecundario);
                else
                    cmd.Parameters.AddWithValue("@p2", DBNull.Value);
                cmd.Parameters.AddWithValue("@combo", Convert.ToDouble(promo.PrecioComboTotal));
                cmd.Parameters.AddWithValue("@desc", Convert.ToDouble(promo.PorcentajeDescuento));
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                mensajeError = ex.Message;
                return false;
            }
        }
        public static bool Update(Promocion promo, out string mensajeError)
        {
            return Save(promo, out mensajeError);
        }
        public static bool Delete(int id)
        {
            try
            {
                using var conn = DatabaseHelper.GetConnection();
                AsegurarTabla(conn);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Promociones WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Promociones] Error eliminando: {ex.Message}");
                return false;
            }
        }
    }
}