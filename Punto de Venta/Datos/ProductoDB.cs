using Microsoft.Data.Sqlite;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Punto_de_Venta.Datos
{
    public static class ProductoDB
    {
        private static int LeerEntero(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return 0;
            try
            {
                return Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static string LeerTexto(SqliteDataReader reader, int ordinal, string valorPorDefecto = "")
        {
            if (reader.IsDBNull(ordinal)) return valorPorDefecto;
            try
            {
                return Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? valorPorDefecto;
            }
            catch
            {
                return valorPorDefecto;
            }
        }

        private static decimal LeerDecimal(SqliteDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return 0m;
            try
            {
                var val = reader.GetValue(ordinal);
                if (val is decimal d) return d;
                if (val is double db) return Convert.ToDecimal(db);
                if (val is float f) return Convert.ToDecimal(f);
                if (val is long l) return Convert.ToDecimal(l);
                if (val is int i) return Convert.ToDecimal(i);
                var s = Convert.ToString(val, CultureInfo.InvariantCulture);
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
                return 0m;
            }
            catch
            {
                return 0m;
            }
        }

        private static Producto LeerProducto(SqliteDataReader reader)
        {
            return new Producto
            {
                Id = LeerEntero(reader, 0),
                Codigo = LeerTexto(reader, 1, string.Empty),
                Nombre = LeerTexto(reader, 2, string.Empty),
                Categoria = LeerTexto(reader, 3, "General"),
                Costo = LeerDecimal(reader, 4),
                Ganancia = LeerDecimal(reader, 5),
                Precio = LeerDecimal(reader, 6),
                Stock = LeerDecimal(reader, 7),
                PedirCantidadAlCobrar = LeerEntero(reader, 8) == 1,
                SeVendePorPeso = LeerEntero(reader, 9) == 1,
                ContarComoUnaPieza = reader.FieldCount > 10 && LeerEntero(reader, 10) == 1,
                StockMinimo = reader.FieldCount > 11 && !reader.IsDBNull(11) ? LeerDecimal(reader, 11) : 5m
            };
        }

        public static List<Producto> Load()
        {
            var list = new List<Producto>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar, SeVendePorPeso, COALESCE(ContarComoUnaPieza, 0), COALESCE(StockMinimo, 5) 
                FROM Productos 
                ORDER BY Id ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(LeerProducto(reader));
            }

            return list;
        }

        public static List<Producto> BuscarListaMovil(string? query, int limite = 150)
        {
            var list = new List<Producto>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();

            if (string.IsNullOrWhiteSpace(query))
            {
                cmd.CommandText = @"
                    SELECT Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar, SeVendePorPeso, COALESCE(ContarComoUnaPieza, 0), COALESCE(StockMinimo, 5)
                    FROM Productos
                    ORDER BY Id ASC
                    LIMIT @limite;";
                cmd.Parameters.AddWithValue("@limite", limite);
            }
            else
            {
                string limpio = query.Trim();
                string sinCeros = limpio.TrimStart('0');
                int idBuscado = int.TryParse(limpio, out var parsedId) ? parsedId : -1;

                cmd.CommandText = @"
                    SELECT Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar, SeVendePorPeso, COALESCE(ContarComoUnaPieza, 0), COALESCE(StockMinimo, 5)
                    FROM Productos
                    WHERE Codigo LIKE @like
                       OR Nombre LIKE @like
                       OR (@sinCeros <> '' AND Codigo LIKE @likeSinCeros)
                       OR Id = @idBuscado
                    ORDER BY
                        CASE
                            WHEN Codigo = @exacto THEN 0
                            WHEN @sinCeros <> '' AND LTRIM(Codigo, '0') = @sinCeros THEN 1
                            WHEN Nombre LIKE @empieza THEN 2
                            ELSE 3
                        END,
                        Nombre ASC
                    LIMIT @limite;";
                cmd.Parameters.AddWithValue("@exacto", limpio);
                cmd.Parameters.AddWithValue("@sinCeros", sinCeros);
                cmd.Parameters.AddWithValue("@like", "%" + limpio + "%");
                cmd.Parameters.AddWithValue("@likeSinCeros", "%" + sinCeros + "%");
                cmd.Parameters.AddWithValue("@empieza", limpio + "%");
                cmd.Parameters.AddWithValue("@idBuscado", idBuscado);
                cmd.Parameters.AddWithValue("@limite", limite);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(LeerProducto(reader));
            }

            return list;
        }

        public static int ObtenerSiguienteId()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Productos;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 1);
        }

        public static bool ExisteCodigo(string codigo, int? excluirId = null)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return false;
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = excluirId.HasValue
                ? "SELECT 1 FROM Productos WHERE LOWER(TRIM(Codigo)) = LOWER(TRIM(@codigo)) AND Id <> @excluirId LIMIT 1;"
                : "SELECT 1 FROM Productos WHERE LOWER(TRIM(Codigo)) = LOWER(TRIM(@codigo)) LIMIT 1;";
            cmd.Parameters.AddWithValue("@codigo", codigo.Trim());
            if (excluirId.HasValue) cmd.Parameters.AddWithValue("@excluirId", excluirId.Value);
            return cmd.ExecuteScalar() != null;
        }

        public static void Delete(int id)
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM Productos WHERE Id = @id";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void Save(IEnumerable<Producto> products)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var transaction = conn.BeginTransaction();

            try
            {
                using var insertar = conn.CreateCommand();
                insertar.Transaction = transaction;
                insertar.CommandText = @"
                    INSERT INTO Productos (Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar, SeVendePorPeso, ContarComoUnaPieza, StockMinimo)
                    VALUES (@id, @codigo, @nombre, @categoria, @costo, @ganancia, @precio, @stock, @pedirCantidad, @seVendePorPeso, @contarComoUnaPieza, @stockMinimo)
                    ON CONFLICT(Id) DO UPDATE SET 
                        Codigo = excluded.Codigo,
                        Nombre = excluded.Nombre,
                        Categoria = excluded.Categoria,
                        Costo = excluded.Costo,
                        Ganancia = excluded.Ganancia,
                        Precio = excluded.Precio,
                        Stock = excluded.Stock,
                        PedirCantidadAlCobrar = excluded.PedirCantidadAlCobrar,
                        SeVendePorPeso = excluded.SeVendePorPeso,
                        ContarComoUnaPieza = excluded.ContarComoUnaPieza,
                        StockMinimo = excluded.StockMinimo;";

                var pId = insertar.Parameters.Add("@id", SqliteType.Integer);
                var pCodigo = insertar.Parameters.Add("@codigo", SqliteType.Text);
                var pNombre = insertar.Parameters.Add("@nombre", SqliteType.Text);
                var pCategoria = insertar.Parameters.Add("@categoria", SqliteType.Text);
                var pCosto = insertar.Parameters.Add("@costo", SqliteType.Real);
                var pGanancia = insertar.Parameters.Add("@ganancia", SqliteType.Real);
                var pPrecio = insertar.Parameters.Add("@precio", SqliteType.Real);
                var pStock = insertar.Parameters.Add("@stock", SqliteType.Real);
                var pPedir = insertar.Parameters.Add("@pedirCantidad", SqliteType.Integer);
                var pPeso = insertar.Parameters.Add("@seVendePorPeso", SqliteType.Integer);
                var pContar1 = insertar.Parameters.Add("@contarComoUnaPieza", SqliteType.Integer);
                var pStockMin = insertar.Parameters.Add("@stockMinimo", SqliteType.Real);
                foreach (var prod in products)
                {
                    pId.Value = prod.Id;
                    pCodigo.Value = prod.Codigo ?? string.Empty;
                    pNombre.Value = prod.Nombre ?? string.Empty;
                    pCategoria.Value = prod.Categoria ?? "General";
                    pCosto.Value = Convert.ToDouble(prod.Costo);
                    pGanancia.Value = Convert.ToDouble(prod.Ganancia);
                    pPrecio.Value = Convert.ToDouble(prod.Precio);
                    pStock.Value = Convert.ToDouble(prod.Stock);
                    pPedir.Value = prod.PedirCantidadAlCobrar ? 1 : 0;
                    pPeso.Value = prod.SeVendePorPeso ? 1 : 0;
                    pContar1.Value = prod.ContarComoUnaPieza ? 1 : 0;
                    pStockMin.Value = Convert.ToDouble(prod.StockMinimo);
                    insertar.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public static Producto? BuscarPorCodigoOId(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            string limpio = query.Trim();
            string sinCeros = limpio.TrimStart('0');
            string conCero = "0" + limpio;
            int idBuscado = int.TryParse(limpio, out var parsedId) ? parsedId : -1;

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar, SeVendePorPeso, COALESCE(ContarComoUnaPieza, 0), COALESCE(StockMinimo, 5) 
                FROM Productos 
                WHERE TRIM(Codigo) = @limpio COLLATE NOCASE
                   OR (@sinCeros <> '' AND LTRIM(TRIM(Codigo), '0') = @sinCeros COLLATE NOCASE)
                   OR TRIM(Codigo) = @conCero COLLATE NOCASE
                   OR Id = @idBuscado
                   OR TRIM(Nombre) = @limpio COLLATE NOCASE
                ORDER BY
                    CASE
                        WHEN TRIM(Codigo) = @limpio COLLATE NOCASE THEN 0
                        WHEN @sinCeros <> '' AND LTRIM(TRIM(Codigo), '0') = @sinCeros COLLATE NOCASE THEN 1
                        WHEN Id = @idBuscado THEN 2
                        ELSE 3
                    END
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@limpio", limpio);
            cmd.Parameters.AddWithValue("@sinCeros", sinCeros);
            cmd.Parameters.AddWithValue("@conCero", conCero);
            cmd.Parameters.AddWithValue("@idBuscado", idBuscado);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return LeerProducto(reader);
            }
            return null;
        }
    }
}