using System;
using System.Collections.Generic;
using System.Globalization;
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
            var idsPorDesactivar = new List<int>();
            try
            {
                using var conn = DatabaseHelper.GetConnection();
                AsegurarTabla(conn);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT Id, Nombre, Tipo, Activa, FechaInicio, FechaFin,
                               ProductoIdPrincipal, CantidadMinima, PrecioEspecial,
                               ProductoIdSecundario, PrecioComboTotal, PorcentajeDescuento
                        FROM Promociones
                        ORDER BY Id DESC;";
                    using var leer = cmd.ExecuteReader();
                    while (leer.Read())
                    {
                        DateTime? fInicio = null;
                        if (!leer.IsDBNull(4) && DateTime.TryParse(leer.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.None, out var fi))
                            fInicio = fi.Date;

                        DateTime? fFin = null;
                        if (!leer.IsDBNull(5) && DateTime.TryParse(leer.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ff))
                            fFin = ff.Date;

                        var promo = new Promocion
                        {
                            Id = leer.GetInt32(0),
                            Nombre = leer.GetString(1),
                            Tipo = Enum.TryParse<TipoPromocion>(leer.GetString(2), out var t) ? t : TipoPromocion.Volumen,
                            Activa = leer.GetInt32(3) == 1,
                            FechaInicio = fInicio,
                            FechaFin = fFin,
                            ProductoIdPrincipal = leer.GetInt32(6),
                            CantidadMinima = leer.GetInt32(7),
                            PrecioEspecial = Convert.ToDecimal(leer.GetDouble(8)),
                            ProductoIdSecundario = leer.IsDBNull(9) ? 0 : leer.GetInt32(9),
                            PrecioComboTotal = Convert.ToDecimal(leer.GetDouble(10)),
                            PorcentajeDescuento = Convert.ToDecimal(leer.GetDouble(11))
                        };

                        if (promo.Activa && promo.EstaVencida)
                        {
                            promo.Activa = false;
                            idsPorDesactivar.Add(promo.Id);
                        }

                        list.Add(promo);
                    }
                }

                if (idsPorDesactivar.Count > 0)
                {
                    using var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = "UPDATE Promociones SET Activa = 0 WHERE Id = @id;";
                    var pId = updateCmd.Parameters.Add("@id", SqliteType.Integer);
                    foreach (var id in idsPorDesactivar)
                    {
                        pId.Value = id;
                        updateCmd.ExecuteNonQuery();
                    }
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
                AsegurarTabla(conn);
                using var cmd = conn.CreateCommand();
                bool esNuevo = promo.Id == 0;
                if (esNuevo)
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
                        );
                        SELECT last_insert_rowid();";
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
                cmd.Parameters.AddWithValue("@fi", promo.FechaInicio.HasValue ? promo.FechaInicio.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
                cmd.Parameters.AddWithValue("@ff", promo.FechaFin.HasValue ? promo.FechaFin.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
                cmd.Parameters.AddWithValue("@p1", promo.ProductoIdPrincipal);
                cmd.Parameters.AddWithValue("@cant", promo.CantidadMinima);
                cmd.Parameters.AddWithValue("@esp", Convert.ToDouble(promo.PrecioEspecial));
                if (promo.ProductoIdSecundario > 0)
                    cmd.Parameters.AddWithValue("@p2", promo.ProductoIdSecundario);
                else
                    cmd.Parameters.AddWithValue("@p2", DBNull.Value);
                cmd.Parameters.AddWithValue("@combo", Convert.ToDouble(promo.PrecioComboTotal));
                cmd.Parameters.AddWithValue("@desc", Convert.ToDouble(promo.PorcentajeDescuento));
                if (esNuevo)
                {
                    var nuevoId = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                    if (nuevoId > 0) promo.Id = nuevoId;
                    return nuevoId > 0;
                }
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