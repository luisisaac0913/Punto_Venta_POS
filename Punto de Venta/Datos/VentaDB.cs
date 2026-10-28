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
                cmd.CommandText = "SELECT Id, FechaHora, Total, PagoCon, Cambio, Estado, MetodoPago FROM Ventas ORDER BY Id ASC;";
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
                    decimal pEfectivo = 0, pTarjeta = 0, pTransf = 0;
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
                cmdDetalle.CommandText = "SELECT Id, VentaId, ProductoId, Nombre, Cantidad, Costo, Ganancia, Precio, Categoria FROM VentasDetalle;";
                using var rDet = cmdDetalle.ExecuteReader();
                while (rDet.Read())
                {
                    var ventaId = rDet.GetInt32(1);
                    if (ventas.TryGetValue(ventaId, out var venta))
                    {
                        venta.Productos.Add(new DetalleVenta
                        {
                            Id = rDet.GetInt32(2),
                            VentaId = ventaId,
                            Nombre = rDet.GetString(3),
                            Cantidad = rDet.GetInt32(4),
                            Costo = Convert.ToDecimal(rDet.GetDouble(5)),
                            Ganancia = Convert.ToDecimal(rDet.GetDouble(6)),
                            Precio = Convert.ToDecimal(rDet.GetDouble(7)),
                            Categoria = rDet.GetString(8)
                        });
                    }
                }
            }
            return ventas.Values.ToList();
        }

        public static void Save(IEnumerable<Venta> ventas)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var tr = conn.BeginTransaction();
            try
            {
                using (var delCmd = conn.CreateCommand())
                {
                    delCmd.Transaction = tr;
                    delCmd.CommandText = "DELETE FROM VentasDetalle; DELETE FROM Ventas;";
                    delCmd.ExecuteNonQuery();
                }
                using var insertVenta = conn.CreateCommand();
                insertVenta.Transaction = tr;
                insertVenta.CommandText = @"
                    INSERT INTO Ventas (Id, FechaHora, Total, PagoCon, Cambio, Estado, MetodoPago)
                    VALUES (@id, @fecha, @total, @pago, @cambio, @estado, @metodo);";
                var vId = insertVenta.Parameters.Add("@id", SqliteType.Integer);
                var vFecha = insertVenta.Parameters.Add("@fecha", SqliteType.Text);
                var vTotal = insertVenta.Parameters.Add("@total", SqliteType.Real);
                var vPago = insertVenta.Parameters.Add("@pago", SqliteType.Real);
                var vCambio = insertVenta.Parameters.Add("@cambio", SqliteType.Real);
                var vEstado = insertVenta.Parameters.Add("@estado", SqliteType.Text);
                var vMetodo = insertVenta.Parameters.Add("@metodo", SqliteType.Text);
                using var insertDetalle = conn.CreateCommand();
                insertDetalle.Transaction = tr;
                insertDetalle.CommandText = @"
                    INSERT INTO VentasDetalle (VentaId, ProductoId, Nombre, Cantidad, Costo, Ganancia, Precio, Categoria)
                    VALUES (@vId, @pId, @nom, @cant, @costo, @gan, @precio, @cat);";
                var dVId = insertDetalle.Parameters.Add("@vId", SqliteType.Integer);
                var dPId = insertDetalle.Parameters.Add("@pId", SqliteType.Integer);
                var dNom = insertDetalle.Parameters.Add("@nom", SqliteType.Text);
                var dCant = insertDetalle.Parameters.Add("@cant", SqliteType.Integer);
                var dCosto = insertDetalle.Parameters.Add("@costo", SqliteType.Real);
                var dGan = insertDetalle.Parameters.Add("@gan", SqliteType.Real);
                var dPrecio = insertDetalle.Parameters.Add("@precio", SqliteType.Real);
                var dCat = insertDetalle.Parameters.Add("@cat", SqliteType.Text);
                foreach (var sale in ventas)
                {
                    vId.Value = sale.Id;
                    vFecha.Value = sale.FechaHora.ToString("o", CultureInfo.InvariantCulture);
                    vTotal.Value = Convert.ToDouble(sale.Total);
                    vPago.Value = Convert.ToDouble(sale.PagoCon);
                    vCambio.Value = Convert.ToDouble(sale.Cambio);
                    vEstado.Value = sale.Estado ?? "Abierta";
                    string metodoGuardar = sale.MetodoPago ?? "Efectivo";
                    if (metodoGuardar == "Mixto")
                    {
                        metodoGuardar = $"Mixto|E:{sale.PagoEfectivo.ToString(CultureInfo.InvariantCulture)}|T:{sale.PagoTarjeta.ToString(CultureInfo.InvariantCulture)}|Tr:{sale.PagoTransferencia.ToString(CultureInfo.InvariantCulture)}";
                    }
                    vMetodo.Value = metodoGuardar;
                    insertVenta.ExecuteNonQuery();
                    if (sale.Productos != null && sale.Productos.Count > 0)
                    {
                        foreach (var det in sale.Productos)
                        {
                            dVId.Value = sale.Id;
                            dPId.Value = det.Id;
                            dNom.Value = det.Nombre ?? string.Empty;
                            dCant.Value = det.Cantidad;
                            dCosto.Value = Convert.ToDouble(det.Costo);
                            dGan.Value = Convert.ToDouble(det.Ganancia);
                            dPrecio.Value = Convert.ToDouble(det.Precio);
                            dCat.Value = det.Categoria ?? "N/A";
                            insertDetalle.ExecuteNonQuery();
                        }
                    }
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