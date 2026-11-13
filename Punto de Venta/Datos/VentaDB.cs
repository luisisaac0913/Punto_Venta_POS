using Microsoft.Data.Sqlite;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Punto_de_Venta.Datos
{
    public static class VentaDB
    {
        public static List<Venta> Load()
        {
            var ventas = new Dictionary<int, Venta>();
            using var conn = DatabaseHelper.GetConnection();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, FechaHora, Total, PagoCon, Cambio, Estado, MetodoPago, COALESCE(PagoEfectivo, 0), COALESCE(PagoTarjeta, 0), COALESCE(PagoTransferencia, 0) FROM Ventas ORDER BY Id ASC;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);
                    var fechaStr = reader.GetString(1);
                    var total = Convert.ToDecimal(reader.GetDouble(2));
                    var pagoCon = Convert.ToDecimal(reader.GetDouble(3));
                    var cambio = Convert.ToDecimal(reader.GetDouble(4));
                    var estado = reader.GetString(5);
                    var metodoPagoRaw = reader.GetString(6);
                    var metodoPagoFinal = metodoPagoRaw;
                    decimal pEfectivo = Convert.ToDecimal(reader.GetDouble(7));
                    decimal pTarjeta = Convert.ToDecimal(reader.GetDouble(8));
                    decimal pTransf = Convert.ToDecimal(reader.GetDouble(9));
                    if (metodoPagoRaw != null && metodoPagoRaw.StartsWith("Mixto|"))
                    {
                        metodoPagoFinal = "Mixto";
                        var partes = metodoPagoRaw.Split('|');
                        foreach (var parte in partes)
                        {
                            if (parte.StartsWith("E:")) decimal.TryParse(parte.Substring(2), NumberStyles.Any, CultureInfo.InvariantCulture, out pEfectivo);
                            if (parte.StartsWith("T:")) decimal.TryParse(parte.Substring(2), NumberStyles.Any, CultureInfo.InvariantCulture, out pTarjeta);
                            if (parte.StartsWith("Tr:")) decimal.TryParse(parte.Substring(3), NumberStyles.Any, CultureInfo.InvariantCulture, out pTransf);
                        }
                    }
                    if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                    {
                        DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha);
                    }
                    ventas[id] = new Venta
                    {
                        Id = id,
                        FechaHora = fecha,
                        Total = total,
                        PagoCon = pagoCon,
                        Cambio = cambio,
                        Estado = estado,
                        MetodoPago = metodoPagoFinal,
                        PagoEfectivo = pEfectivo,
                        PagoTarjeta = pTarjeta,
                        PagoTransferencia = pTransf,
                        Productos = []
                    };
                }
            }
            using (var cmdDetalle = conn.CreateCommand())
            {
                cmdDetalle.CommandText = "SELECT Id, VentaId, ProductoId, Nombre, Cantidad, Costo, Ganancia, Precio, Categoria, COALESCE(SeVendePorPeso, 0), COALESCE(ContarComoUnaPieza, 0) FROM VentasDetalle;";
                using var rDet = cmdDetalle.ExecuteReader();
                while (rDet.Read())
                {
                    var ventaId = rDet.GetInt32(1);
                    if (ventas.TryGetValue(ventaId, out var venta))
                    {
                        venta.Productos.Add(new DetalleVenta
                        {
                            Id = rDet.GetInt32(2),
                            ProductoId = rDet.GetInt32(2),
                            VentaId = ventaId,
                            Nombre = rDet.GetString(3),
                            Cantidad = Convert.ToDecimal(rDet.GetDouble(4)),
                            Costo = Convert.ToDecimal(rDet.GetDouble(5)),
                            Ganancia = Convert.ToDecimal(rDet.GetDouble(6)),
                            Precio = Convert.ToDecimal(rDet.GetDouble(7)),
                            Categoria = rDet.GetString(8),
                            SeVendePorPeso = rDet.FieldCount > 9 && rDet.GetInt32(9) == 1,
                            ContarComoUnaPieza = rDet.FieldCount > 10 && rDet.GetInt32(10) == 1
                        });
                    }
                }
            }
            return ventas.Values.ToList();
        }

        public static int ObtenerSiguienteId()
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Ventas;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 1);
        }

        public static void Insert(Venta sale)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();
            try
            {
                InsertInternal(conn, tr, sale);
                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }

        public static void RegistrarVentaConStock(Venta sale, IEnumerable<Producto> productosModificados)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();
            try
            {
                InsertInternal(conn, tr, sale);
                ActualizarStockInternal(conn, tr, productosModificados);
                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }

        public static void RegistrarFiadoConStock(Venta sale, IEnumerable<Producto> productosModificados, IEnumerable<Deudor> debtors)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();
            try
            {
                PrestamoDB.SaveInternal(conn, tr, debtors);
                InsertInternal(conn, tr, sale);
                ActualizarStockInternal(conn, tr, productosModificados);
                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }

        public static void CancelarVentaConStock(int ventaId, IEnumerable<Producto> productosModificados)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tr;
                    cmd.CommandText = "UPDATE Ventas SET Estado = 'Cancelada' WHERE Id = @id;";
                    cmd.Parameters.AddWithValue("@id", ventaId);
                    cmd.ExecuteNonQuery();
                }

                ActualizarStockInternal(conn, tr, productosModificados);
                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }

        private static void ActualizarStockInternal(SqliteConnection conn, SqliteTransaction tr, IEnumerable<Producto> productosModificados)
        {
            using var updateProd = conn.CreateCommand();
            updateProd.Transaction = tr;
            updateProd.CommandText = "UPDATE Productos SET Stock = @stock WHERE Id = @id;";
            var pId = updateProd.Parameters.Add("@id", SqliteType.Integer);
            var pStock = updateProd.Parameters.Add("@stock", SqliteType.Real);

            foreach (var prod in productosModificados)
            {
                pId.Value = prod.Id;
                pStock.Value = Convert.ToDouble(prod.Stock);
                updateProd.ExecuteNonQuery();
            }
        }

        public static void ActualizarEstado(int ventaId, string nuevoEstado)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Ventas SET Estado = @estado WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@estado", nuevoEstado);
            cmd.Parameters.AddWithValue("@id", ventaId);
            cmd.ExecuteNonQuery();
        }

        private static void InsertInternal(SqliteConnection conn, SqliteTransaction tr, Venta sale)
        {
            string metodoGuardar = sale.MetodoPago ?? "Efectivo";
            if (metodoGuardar == "Mixto")
            {
                metodoGuardar = $"Mixto|E:{sale.PagoEfectivo.ToString(CultureInfo.InvariantCulture)}|T:{sale.PagoTarjeta.ToString(CultureInfo.InvariantCulture)}|Tr:{sale.PagoTransferencia.ToString(CultureInfo.InvariantCulture)}";
            }

            using (var insertVenta = conn.CreateCommand())
            {
                insertVenta.Transaction = tr;
                insertVenta.CommandText = @"
                    INSERT INTO Ventas (Id, FechaHora, Total, PagoCon, Cambio, Estado, MetodoPago, PagoEfectivo, PagoTarjeta, PagoTransferencia)
                    VALUES (@id, @fecha, @total, @pago, @cambio, @estado, @metodo, @pEfec, @pTarj, @pTrans);";
                insertVenta.Parameters.AddWithValue("@id", sale.Id);
                insertVenta.Parameters.AddWithValue("@fecha", sale.FechaHora.ToString("o", CultureInfo.InvariantCulture));
                insertVenta.Parameters.AddWithValue("@total", Convert.ToDouble(sale.Total));
                insertVenta.Parameters.AddWithValue("@pago", Convert.ToDouble(sale.PagoCon));
                insertVenta.Parameters.AddWithValue("@cambio", Convert.ToDouble(sale.Cambio));
                insertVenta.Parameters.AddWithValue("@estado", sale.Estado ?? "Abierta");
                insertVenta.Parameters.AddWithValue("@metodo", metodoGuardar);
                insertVenta.Parameters.AddWithValue("@pEfec", Convert.ToDouble(sale.PagoEfectivo));
                insertVenta.Parameters.AddWithValue("@pTarj", Convert.ToDouble(sale.PagoTarjeta));
                insertVenta.Parameters.AddWithValue("@pTrans", Convert.ToDouble(sale.PagoTransferencia));
                insertVenta.ExecuteNonQuery();
            }

            if (sale.Productos != null && sale.Productos.Count > 0)
            {
                using var insertDetalle = conn.CreateCommand();
                insertDetalle.Transaction = tr;
                insertDetalle.CommandText = @"
                    INSERT INTO VentasDetalle (VentaId, ProductoId, Nombre, Cantidad, Costo, Ganancia, Precio, Categoria, SeVendePorPeso, ContarComoUnaPieza)
                    VALUES (@vId, @pId, @nom, @cant, @costo, @gan, @precio, @cat, @peso, @contar1);";
                var dVId = insertDetalle.Parameters.Add("@vId", SqliteType.Integer);
                var dPId = insertDetalle.Parameters.Add("@pId", SqliteType.Integer);
                var dNom = insertDetalle.Parameters.Add("@nom", SqliteType.Text);
                var dCant = insertDetalle.Parameters.Add("@cant", SqliteType.Real);
                var dCosto = insertDetalle.Parameters.Add("@costo", SqliteType.Real);
                var dGan = insertDetalle.Parameters.Add("@gan", SqliteType.Real);
                var dPrecio = insertDetalle.Parameters.Add("@precio", SqliteType.Real);
                var dCat = insertDetalle.Parameters.Add("@cat", SqliteType.Text);
                var dPeso = insertDetalle.Parameters.Add("@peso", SqliteType.Integer);
                var dContar1 = insertDetalle.Parameters.Add("@contar1", SqliteType.Integer);

                foreach (var det in sale.Productos)
                {
                    dVId.Value = sale.Id;
                    dPId.Value = det.Id;
                    dNom.Value = det.Nombre ?? string.Empty;
                    dCant.Value = Convert.ToDouble(det.Cantidad);
                    dCosto.Value = Convert.ToDouble(det.Costo);
                    dGan.Value = Convert.ToDouble(det.Ganancia);
                    dPrecio.Value = Convert.ToDouble(det.Precio);
                    dCat.Value = det.Categoria ?? "N/A";
                    dPeso.Value = det.SeVendePorPeso ? 1 : 0;
                    dContar1.Value = det.ContarComoUnaPieza ? 1 : 0;
                    insertDetalle.ExecuteNonQuery();
                }
            }
        }

    }
}
