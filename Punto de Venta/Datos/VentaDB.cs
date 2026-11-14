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
            Punto_de_Venta.TurnoCajaManager.ValidarEsquemaBD(conn);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, FechaHora, Total, PagoCon, Cambio, Estado, MetodoPago, COALESCE(PagoEfectivo, 0), COALESCE(PagoTarjeta, 0), COALESCE(PagoTransferencia, 0), CorteCajaId FROM Ventas ORDER BY Id ASC;";
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
                    int? corteCajaId = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetInt32(10) : null;
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
                        CorteCajaId = corteCajaId,
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

        internal static int ObtenerSiguienteIdInternal(SqliteConnection conn, SqliteTransaction tr)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tr;
            cmd.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Ventas;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 1);
        }

        public static void Insert(Venta sale)
        {
            using var conn = DatabaseHelper.GetConnection();
            Punto_de_Venta.TurnoCajaManager.ValidarEsquemaBD(conn);
            using var tr = conn.BeginTransaction();
            try
            {
                if (sale.Id <= 0 || ExisteVentaIdInternal(conn, tr, sale.Id))
                {
                    sale.Id = ObtenerSiguienteIdInternal(conn, tr);
                }
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
            Punto_de_Venta.TurnoCajaManager.ValidarEsquemaBD(conn);
            using var tr = conn.BeginTransaction();
            try
            {
                sale.Id = ObtenerSiguienteIdInternal(conn, tr);
                InsertInternal(conn, tr, sale);
                DescontarStockInternal(conn, tr, sale.Productos);
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
            Punto_de_Venta.TurnoCajaManager.ValidarEsquemaBD(conn);
            using var tr = conn.BeginTransaction();
            try
            {
                PrestamoDB.SaveInternal(conn, tr, debtors);
                sale.Id = ObtenerSiguienteIdInternal(conn, tr);
                InsertInternal(conn, tr, sale);
                DescontarStockInternal(conn, tr, sale.Productos);
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
                int filasAfectadas;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tr;
                    cmd.CommandText = "UPDATE Ventas SET Estado = 'Cancelada' WHERE Id = @id AND Estado != 'Cancelada';";
                    cmd.Parameters.AddWithValue("@id", ventaId);
                    filasAfectadas = cmd.ExecuteNonQuery();
                }

                if (filasAfectadas == 0)
                {
                    throw new InvalidOperationException("La venta ya se encuentra cancelada o no existe.");
                }

                var detalles = new List<(int ProductoId, string Nombre, decimal Cantidad)>();
                using (var cmdDet = conn.CreateCommand())
                {
                    cmdDet.Transaction = tr;
                    cmdDet.CommandText = "SELECT ProductoId, Nombre, Cantidad FROM VentasDetalle WHERE VentaId = @id;";
                    cmdDet.Parameters.AddWithValue("@id", ventaId);
                    using var r = cmdDet.ExecuteReader();
                    while (r.Read())
                    {
                        int pId = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                        string nom = r.IsDBNull(1) ? string.Empty : r.GetString(1);
                        decimal cant = r.IsDBNull(2) ? 0m : Convert.ToDecimal(r.GetDouble(2));
                        if (cant > 0m)
                        {
                            detalles.Add((pId, nom, cant));
                        }
                    }
                }

                if (detalles.Count > 0)
                {
                    ReponerStockPorDeltaInternal(conn, tr, detalles);
                }
                else if (productosModificados != null)
                {
                    ActualizarStockInternal(conn, tr, productosModificados);
                }

                tr.Commit();
            }
            catch
            {
                tr.Rollback();
                throw;
            }
        }

        private static bool ExisteVentaIdInternal(SqliteConnection conn, SqliteTransaction tr, int id)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tr;
            cmd.CommandText = "SELECT 1 FROM Ventas WHERE Id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteScalar() != null;
        }

        private static void ReponerStockPorDeltaInternal(SqliteConnection conn, SqliteTransaction tr, IEnumerable<(int ProductoId, string Nombre, decimal Cantidad)> devueltos)
        {
            using var updatePorId = conn.CreateCommand();
            updatePorId.Transaction = tr;
            updatePorId.CommandText = "UPDATE Productos SET Stock = Stock + @cantidad WHERE Id = @id;";
            var pId = updatePorId.Parameters.Add("@id", SqliteType.Integer);
            var pCantId = updatePorId.Parameters.Add("@cantidad", SqliteType.Real);

            using var updatePorNombre = conn.CreateCommand();
            updatePorNombre.Transaction = tr;
            updatePorNombre.CommandText = "UPDATE Productos SET Stock = Stock + @cantidad WHERE UPPER(TRIM(Nombre)) = UPPER(TRIM(@nombre));";
            var pNom = updatePorNombre.Parameters.Add("@nombre", SqliteType.Text);
            var pCantNom = updatePorNombre.Parameters.Add("@cantidad", SqliteType.Real);

            foreach (var (prodId, nombre, cantidad) in devueltos)
            {
                if (cantidad <= 0m) continue;
                int actualizados = 0;
                if (prodId > 0)
                {
                    pId.Value = prodId;
                    pCantId.Value = Convert.ToDouble(cantidad);
                    actualizados = updatePorId.ExecuteNonQuery();
                }
                if (actualizados == 0 && !string.IsNullOrWhiteSpace(nombre))
                {
                    pNom.Value = nombre.Trim();
                    pCantNom.Value = Convert.ToDouble(cantidad);
                    updatePorNombre.ExecuteNonQuery();
                }
            }
        }

        private static void DescontarStockInternal(SqliteConnection conn, SqliteTransaction tr, IEnumerable<DetalleVenta> vendidos)
        {
            using var updateProd = conn.CreateCommand();
            updateProd.Transaction = tr;
            updateProd.CommandText = "UPDATE Productos SET Stock = MAX(0, Stock - @cantidad) WHERE Id = @id;";
            var pId = updateProd.Parameters.Add("@id", SqliteType.Integer);
            var pCant = updateProd.Parameters.Add("@cantidad", SqliteType.Real);

            foreach (var item in vendidos)
            {
                int prodId = item.ProductoId > 0 ? item.ProductoId : item.Id;
                if (prodId <= 0 || item.Cantidad <= 0) continue;
                pId.Value = prodId;
                pCant.Value = Convert.ToDouble(item.Cantidad);
                updateProd.ExecuteNonQuery();
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

        internal static void InsertInternal(SqliteConnection conn, SqliteTransaction tr, Venta sale)
        {
            if (sale.Id <= 0)
            {
                sale.Id = ObtenerSiguienteIdInternal(conn, tr);
            }

            string estadoGuardar = sale.Estado ?? "Abierta";
            if ((sale.CorteCajaId == null || sale.CorteCajaId <= 0) &&
                (string.Equals(estadoGuardar, "Abierta", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(estadoGuardar, "Abono", StringComparison.OrdinalIgnoreCase)))
            {
                using var cmdTurno = conn.CreateCommand();
                cmdTurno.Transaction = tr;
                cmdTurno.CommandText = "SELECT Id FROM CortesCaja WHERE Estado = 'Abierto' ORDER BY Id DESC LIMIT 1;";
                var turnoObj = cmdTurno.ExecuteScalar();
                if (turnoObj != null && turnoObj != DBNull.Value)
                {
                    sale.CorteCajaId = Convert.ToInt32(turnoObj);
                }
            }

            string metodoGuardar = sale.MetodoPago ?? "Efectivo";
            if (metodoGuardar == "Mixto")
            {
                metodoGuardar = $"Mixto|E:{sale.PagoEfectivo.ToString(CultureInfo.InvariantCulture)}|T:{sale.PagoTarjeta.ToString(CultureInfo.InvariantCulture)}|Tr:{sale.PagoTransferencia.ToString(CultureInfo.InvariantCulture)}";
            }

            using (var insertVenta = conn.CreateCommand())
            {
                insertVenta.Transaction = tr;
                insertVenta.CommandText = @"
                    INSERT INTO Ventas (Id, FechaHora, Total, PagoCon, Cambio, Estado, MetodoPago, PagoEfectivo, PagoTarjeta, PagoTransferencia, CorteCajaId)
                    VALUES (@id, @fecha, @total, @pago, @cambio, @estado, @metodo, @pEfec, @pTarj, @pTrans, @corteId);";
                insertVenta.Parameters.AddWithValue("@id", sale.Id);
                insertVenta.Parameters.AddWithValue("@fecha", sale.FechaHora.ToString("o", CultureInfo.InvariantCulture));
                insertVenta.Parameters.AddWithValue("@total", Convert.ToDouble(sale.Total));
                insertVenta.Parameters.AddWithValue("@pago", Convert.ToDouble(sale.PagoCon));
                insertVenta.Parameters.AddWithValue("@cambio", Convert.ToDouble(sale.Cambio));
                insertVenta.Parameters.AddWithValue("@estado", estadoGuardar);
                insertVenta.Parameters.AddWithValue("@metodo", metodoGuardar);
                insertVenta.Parameters.AddWithValue("@pEfec", Convert.ToDouble(sale.PagoEfectivo));
                insertVenta.Parameters.AddWithValue("@pTarj", Convert.ToDouble(sale.PagoTarjeta));
                insertVenta.Parameters.AddWithValue("@pTrans", Convert.ToDouble(sale.PagoTransferencia));
                insertVenta.Parameters.AddWithValue("@corteId", sale.CorteCajaId.HasValue && sale.CorteCajaId.Value > 0 ? sale.CorteCajaId.Value : DBNull.Value);
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
                    int prodId = det.ProductoId > 0 ? det.ProductoId : det.Id;
                    det.ProductoId = prodId;
                    det.Id = prodId;
                    det.VentaId = sale.Id;

                    dVId.Value = sale.Id;
                    dPId.Value = prodId;
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
