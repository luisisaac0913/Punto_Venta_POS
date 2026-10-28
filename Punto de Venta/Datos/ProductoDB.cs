using Microsoft.Data.Sqlite;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;

namespace Punto_de_Venta.Datos
{
    public static class ProductoDB
    {
        public static List<Producto> Load()
        {
            var list = new List<Producto>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar, SeVendePorPeso 
                FROM Productos 
                ORDER BY Id ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Producto
                {
                    Id = reader.GetInt32(0),
                    Codigo = reader.GetString(1),
                    Nombre = reader.GetString(2),
                    Categoria = reader.GetString(3),
                    Costo = Convert.ToDecimal(reader.GetDouble(4)),
                    Ganancia = Convert.ToDecimal(reader.GetDouble(5)),
                    Precio = Convert.ToDecimal(reader.GetDouble(6)),
                    Stock = reader.GetInt32(7),
                    PedirCantidadAlCobrar = reader.GetInt32(8) == 1,
                    SeVendePorPeso = reader.GetInt32(9) == 1 
                });
            }

            return list;
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
                    INSERT INTO Productos (Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar, SeVendePorPeso)
                    VALUES (@id, @codigo, @nombre, @categoria, @costo, @ganancia, @precio, @stock, @pedirCantidad, @seVendePorPeso)
                    ON CONFLICT(Id) DO UPDATE SET 
                        Codigo = excluded.Codigo,
                        Nombre = excluded.Nombre,
                        Categoria = excluded.Categoria,
                        Costo = excluded.Costo,
                        Ganancia = excluded.Ganancia,
                        Precio = excluded.Precio,
                        Stock = excluded.Stock,
                        PedirCantidadAlCobrar = excluded.PedirCantidadAlCobrar,
                        SeVendePorPeso = excluded.SeVendePorPeso;";

                var pId = insertar.Parameters.Add("@id", SqliteType.Integer);
                var pCodigo = insertar.Parameters.Add("@codigo", SqliteType.Text);
                var pNombre = insertar.Parameters.Add("@nombre", SqliteType.Text);
                var pCategoria = insertar.Parameters.Add("@categoria", SqliteType.Text);
                var pCosto = insertar.Parameters.Add("@costo", SqliteType.Real);
                var pGanancia = insertar.Parameters.Add("@ganancia", SqliteType.Real);
                var pPrecio = insertar.Parameters.Add("@precio", SqliteType.Real);
                var pStock = insertar.Parameters.Add("@stock", SqliteType.Integer);
                var pPedir = insertar.Parameters.Add("@pedirCantidad", SqliteType.Integer);
                var pPeso = insertar.Parameters.Add("@seVendePorPeso", SqliteType.Integer);
                foreach (var prod in products)
                {
                    pId.Value = prod.Id;
                    pCodigo.Value = prod.Codigo ?? string.Empty;
                    pNombre.Value = prod.Nombre ?? string.Empty;
                    pCategoria.Value = prod.Categoria ?? "N/A";
                    pCosto.Value = Convert.ToDouble(prod.Costo);
                    pGanancia.Value = Convert.ToDouble(prod.Ganancia);
                    pPrecio.Value = Convert.ToDouble(prod.Precio);
                    pStock.Value = prod.Stock;
                    pPedir.Value = prod.PedirCantidadAlCobrar ? 1 : 0;
                    pPeso.Value = prod.SeVendePorPeso ? 1 : 0;
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

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar, SeVendePorPeso 
                FROM Productos 
                WHERE Codigo = @query OR Id = @queryInt
                LIMIT 1;";
            cmd.Parameters.AddWithValue("@query", query.Trim());
            cmd.Parameters.AddWithValue("@queryInt", int.TryParse(query.Trim(), out var parsedId) ? parsedId : -1);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new Producto
                {
                    Id = reader.GetInt32(0),
                    Codigo = reader.GetString(1),
                    Nombre = reader.GetString(2),
                    Categoria = reader.GetString(3),
                    Costo = Convert.ToDecimal(reader.GetDouble(4)),
                    Ganancia = Convert.ToDecimal(reader.GetDouble(5)),
                    Precio = Convert.ToDecimal(reader.GetDouble(6)),
                    Stock = reader.GetInt32(7),
                    PedirCantidadAlCobrar = reader.GetInt32(8) == 1,
                    SeVendePorPeso = reader.GetInt32(9) == 1 
                };
            }
            return null;
        }
    }
}