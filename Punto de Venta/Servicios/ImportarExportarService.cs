using Microsoft.Data.Sqlite;
using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Punto_de_Venta.Servicios
{
    public enum ModoImportacion
    {
        CrearYActualizar,
        SoloNuevos,
        SumarStockYCrear
    }

    public class FilaImportacionPreview
    {
        public int NumeroFila { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public decimal Costo { get; set; }
        public decimal Precio { get; set; }
        public decimal Stock { get; set; }
        internal Producto? ProductoFinal { get; set; }
    }

    internal sealed class ItemImportacionConfirmable
    {
        public int NumeroFila { get; init; }
        public int IdOriginalArchivo { get; init; }
        public Producto ProductoImportado { get; init; } = null!;
    }

    public class ResultadoPreviewImportacion
    {
        public int Creados { get; set; }
        public int Actualizados { get; set; }
        public int Rechazados { get; set; }
        public int Omitidos { get; set; }
        public bool LimiteFreeAlcanzado { get; set; }
        public List<FilaImportacionPreview> Filas { get; set; } = new();
        public List<string> Errores { get; set; } = new();
        internal List<Producto> ProductosParaGuardar { get; set; } = new();
        internal List<ItemImportacionConfirmable> ItemsConfirmables { get; set; } = new();
        internal ModoImportacion ModoAplicado { get; set; } = ModoImportacion.CrearYActualizar;
        internal bool EsPremiumAplicado { get; set; } = true;
    }

    public class ResultadoImportacion
    {
        public int Creados { get; set; }
        public int Actualizados { get; set; }
        public int Rechazados { get; set; }
        public bool LimiteFreeAlcanzado { get; set; }
        public List<string> Errores { get; set; } = new();
    }

    public static class ImportarExportarService
    {
        private static readonly UTF8Encoding Utf8Bom = new(true);

        public static List<List<string>> LeerFilasCrudas(string filePath)
        {
            if (string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return LeerFilasXlsx(filePath);
            }

            // Si un archivo .xls binario se intenta abrir como CSV/texto, avisar con claridad
            VerificarNoSeaBinarioOle(filePath);

            string contenido = LeerTextoConEncoding(filePath);
            if (string.IsNullOrWhiteSpace(contenido))
            {
                return new List<List<string>>();
            }

            char delimitador = DetectarDelimitador(contenido);
            return ParsearFilasCsv(contenido, delimitador);
        }

        public static ResultadoPreviewImportacion PrevisualizarImportacion(string filePath, bool esPremium, ModoImportacion modo)
        {
            var resultado = new ResultadoPreviewImportacion
            {
                ModoAplicado = modo,
                EsPremiumAplicado = esPremium
            };

            var filas = LeerFilasCrudas(filePath);

            if (filas.Count == 0)
            {
                resultado.Errores.Add(string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase)
                    ? "El libro Excel no contiene hojas o filas validas."
                    : "El archivo esta vacio o no contiene filas validas.");
                return resultado;
            }

            // Buscar el encabezado real en las primeras filas (por si la fila 1 es un titulo del proveedor)
            int indiceFilaEncabezado = 0;
            var mapa = MapearEncabezados(filas[0], out bool tieneEncabezado);
            if (!tieneEncabezado)
            {
                for (int k = 1; k < Math.Min(filas.Count, 6); k++)
                {
                    if (filas[k].All(string.IsNullOrWhiteSpace)) continue;
                    var mapaCandidato = MapearEncabezados(filas[k], out bool candidatoTieneEnc);
                    if (candidatoTieneEnc && mapaCandidato.ContainsKey("nombre"))
                    {
                        indiceFilaEncabezado = k;
                        mapa = mapaCandidato;
                        tieneEncabezado = true;
                        break;
                    }
                }
            }

            int filaInicio = tieneEncabezado ? indiceFilaEncabezado + 1 : 0;

            if (tieneEncabezado && !mapa.ContainsKey("nombre"))
            {
                string err = "Columnas no reconocidas automaticamente. Haz clic en '✨ Normalizar con IA' para mapear y limpiar este archivo.";
                resultado.Errores.Add(err);
                resultado.Filas.Add(new FilaImportacionPreview
                {
                    NumeroFila = indiceFilaEncabezado + 1,
                    Estado = "Aviso",
                    Detalle = err
                });
                return resultado;
            }

            var itemsValidos = new List<(int NumFila, Producto Prod, string? Observacion)>();
            for (int i = filaInicio; i < filas.Count; i++)
            {
                var cols = filas[i];
                int numFila = i + 1;
                if (cols.All(string.IsNullOrWhiteSpace)) continue;

                if (!tieneEncabezado && cols.Count < 4)
                {
                    string err = $"Fila {numFila}: tiene menos de 4 columnas.";
                    resultado.Rechazados++;
                    resultado.Errores.Add(err);
                    resultado.Filas.Add(new FilaImportacionPreview { NumeroFila = numFila, Estado = "Rechazado", Detalle = err });
                    continue;
                }

                if (!TryConstruirProducto(cols, mapa, tieneEncabezado, numFila, out var prod, out string errorFila, out string? advertenciaFila))
                {
                    resultado.Rechazados++;
                    resultado.Errores.Add(errorFila);
                    resultado.Filas.Add(new FilaImportacionPreview { NumeroFila = numFila, Estado = "Rechazado", Detalle = errorFila });
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(advertenciaFila))
                {
                    resultado.Errores.Add($"[Aviso] Fila {numFila} ({prod!.Nombre}): {advertenciaFila}");
                }

                itemsValidos.Add((numFila, prod!, advertenciaFila));
            }

            return EvaluarProductosParaPreview(itemsValidos, esPremium, modo, resultado);
        }

        public static ResultadoPreviewImportacion EvaluarProductosParaPreview(
            IEnumerable<(int NumFila, Producto Prod, string? Observacion)> items,
            bool esPremium,
            ModoImportacion modo,
            ResultadoPreviewImportacion? resultadoExistente = null)
        {
            var resultado = resultadoExistente ?? new ResultadoPreviewImportacion();
            resultado.ModoAplicado = modo;
            resultado.EsPremiumAplicado = esPremium;

            var existentes = ProductoDB.Load();
            var porId = existentes.ToDictionary(p => p.Id);
            var porCodigo = new Dictionary<string, Producto>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in existentes)
            {
                if (!string.IsNullOrWhiteSpace(p.Codigo) && !porCodigo.ContainsKey(p.Codigo.Trim()))
                {
                    porCodigo[p.Codigo.Trim()] = p;
                }
            }

            int maxId = Math.Max(porId.Keys.DefaultIfEmpty(0).Max(), ProductoDB.ObtenerSiguienteId() - 1);
            int totalEnCatalogo = existentes.Count;
            var codigosEnArchivo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (numFila, prodOriginal, observacion) in items)
            {
                if (prodOriginal == null) continue;

                // Clonamos el producto de entrada para conservar intacto su Id original del archivo y su Stock importado
                int idOriginalArchivo = prodOriginal.Id;
                var prod = ClonarProducto(prodOriginal);
                var copiaInmutableConfirmacion = ClonarProducto(prodOriginal);

                string codigoNorm = (prod.Codigo ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(codigoNorm) && !codigosEnArchivo.Add(codigoNorm))
                {
                    string err = $"Fila {numFila}: codigo de barras duplicado en el archivo ('{codigoNorm}').";
                    resultado.Rechazados++;
                    resultado.Errores.Add(err);
                    resultado.Filas.Add(new FilaImportacionPreview
                    {
                        NumeroFila = numFila,
                        Estado = "Rechazado",
                        Detalle = err,
                        Codigo = prod.Codigo ?? string.Empty,
                        Nombre = prod.Nombre,
                        Categoria = prod.Categoria,
                        Costo = prod.Costo,
                        Precio = prod.Precio,
                        Stock = prod.Stock
                    });
                    continue;
                }

                string? notaAnomalia = ConstruirAdvertenciaValoresAnomalos(prod, observacion);

                Producto? destino = null;
                if (idOriginalArchivo > 0 && porId.TryGetValue(idOriginalArchivo, out var porIdMatch))
                {
                    destino = porIdMatch;
                }
                else if (!string.IsNullOrEmpty(codigoNorm) && porCodigo.TryGetValue(codigoNorm, out var porCodMatch))
                {
                    destino = porCodMatch;
                }

                if (destino != null)
                {
                    if (modo == ModoImportacion.SoloNuevos)
                    {
                        resultado.Omitidos++;
                        resultado.Filas.Add(new FilaImportacionPreview
                        {
                            NumeroFila = numFila,
                            Estado = "Omitido",
                            Detalle = "Ya existe (modo Solo Nuevos).",
                            Codigo = prod.Codigo ?? string.Empty,
                            Nombre = prod.Nombre,
                            Categoria = prod.Categoria,
                            Costo = prod.Costo,
                            Precio = prod.Precio,
                            Stock = prod.Stock
                        });
                        continue;
                    }

                    if (modo == ModoImportacion.SumarStockYCrear)
                    {
                        var actualizado = new Producto
                        {
                            Id = destino.Id,
                            Codigo = destino.Codigo,
                            Nombre = destino.Nombre,
                            Categoria = destino.Categoria,
                            Costo = destino.Costo,
                            Ganancia = destino.Ganancia,
                            Precio = destino.Precio,
                            Stock = destino.Stock + prod.Stock,
                            StockMinimo = destino.StockMinimo,
                            PedirCantidadAlCobrar = destino.PedirCantidadAlCobrar,
                            SeVendePorPeso = destino.SeVendePorPeso,
                            ContarComoUnaPieza = destino.ContarComoUnaPieza
                        };
                        porId[actualizado.Id] = actualizado;
                        resultado.ProductosParaGuardar.Add(actualizado);
                        resultado.ItemsConfirmables.Add(new ItemImportacionConfirmable
                        {
                            NumeroFila = numFila,
                            IdOriginalArchivo = idOriginalArchivo,
                            ProductoImportado = copiaInmutableConfirmacion
                        });
                        resultado.Actualizados++;

                        string detalleSuma = $"Stock: {destino.Stock:0.###} -> {actualizado.Stock:0.###}";
                        if (!string.IsNullOrWhiteSpace(notaAnomalia))
                        {
                            detalleSuma += $" | {notaAnomalia}";
                        }

                        resultado.Filas.Add(new FilaImportacionPreview
                        {
                            NumeroFila = numFila,
                            Estado = "Sumar Stock",
                            Detalle = detalleSuma,
                            Codigo = actualizado.Codigo,
                            Nombre = actualizado.Nombre,
                            Categoria = actualizado.Categoria,
                            Costo = actualizado.Costo,
                            Precio = actualizado.Precio,
                            Stock = actualizado.Stock,
                            ProductoFinal = actualizado
                        });
                    }
                    else
                    {
                        prod.Id = destino.Id;
                        porId[prod.Id] = prod;
                        if (!string.IsNullOrEmpty(codigoNorm)) porCodigo[codigoNorm] = prod;
                        resultado.ProductosParaGuardar.Add(prod);
                        resultado.ItemsConfirmables.Add(new ItemImportacionConfirmable
                        {
                            NumeroFila = numFila,
                            IdOriginalArchivo = idOriginalArchivo,
                            ProductoImportado = copiaInmutableConfirmacion
                        });
                        resultado.Actualizados++;
                        resultado.Filas.Add(new FilaImportacionPreview
                        {
                            NumeroFila = numFila,
                            Estado = "Actualizar",
                            Detalle = string.IsNullOrWhiteSpace(notaAnomalia) ? $"Actualiza ID #{prod.Id}" : $"Actualiza ID #{prod.Id} | {notaAnomalia}",
                            Codigo = prod.Codigo ?? string.Empty,
                            Nombre = prod.Nombre,
                            Categoria = prod.Categoria,
                            Costo = prod.Costo,
                            Precio = prod.Precio,
                            Stock = prod.Stock,
                            ProductoFinal = prod
                        });
                    }
                }
                else
                {
                    if (!esPremium && totalEnCatalogo >= 400)
                    {
                        resultado.LimiteFreeAlcanzado = true;
                        string err = $"Fila {numFila}: omitida por limite de 400 productos en version gratuita.";
                        resultado.Rechazados++;
                        resultado.Errores.Add(err);
                        resultado.Filas.Add(new FilaImportacionPreview
                        {
                            NumeroFila = numFila,
                            Estado = "Rechazado",
                            Detalle = err,
                            Codigo = prod.Codigo ?? string.Empty,
                            Nombre = prod.Nombre,
                            Categoria = prod.Categoria,
                            Costo = prod.Costo,
                            Precio = prod.Precio,
                            Stock = prod.Stock
                        });
                        continue;
                    }

                    maxId++;
                    prod.Id = maxId;
                    porId[prod.Id] = prod;
                    if (!string.IsNullOrEmpty(codigoNorm)) porCodigo[codigoNorm] = prod;
                    totalEnCatalogo++;
                    resultado.ProductosParaGuardar.Add(prod);
                    resultado.ItemsConfirmables.Add(new ItemImportacionConfirmable
                    {
                        NumeroFila = numFila,
                        IdOriginalArchivo = idOriginalArchivo,
                        ProductoImportado = copiaInmutableConfirmacion
                    });
                    resultado.Creados++;
                    resultado.Filas.Add(new FilaImportacionPreview
                    {
                        NumeroFila = numFila,
                        Estado = "Nuevo",
                        Detalle = string.IsNullOrWhiteSpace(notaAnomalia) ? $"Se asignara ID #{prod.Id}" : $"Se asignara ID #{prod.Id} | {notaAnomalia}",
                        Codigo = prod.Codigo ?? string.Empty,
                        Nombre = prod.Nombre,
                        Categoria = prod.Categoria,
                        Costo = prod.Costo,
                        Precio = prod.Precio,
                        Stock = prod.Stock,
                        ProductoFinal = prod
                    });
                }
            }

            // Ordenar filas por NumeroFila para que la vista previa siempre sea coherente
            resultado.Filas.Sort((a, b) => a.NumeroFila.CompareTo(b.NumeroFila));
            return resultado;
        }

        public static ResultadoImportacion ConfirmarImportacion(ResultadoPreviewImportacion preview)
        {
            if (preview == null)
            {
                return new ResultadoImportacion();
            }

            // Si no hay items confirmables, retornamos los contadores del preview
            if (preview.ItemsConfirmables.Count == 0 && preview.ProductosParaGuardar.Count == 0)
            {
                return new ResultadoImportacion
                {
                    Creados = 0,
                    Actualizados = 0,
                    Rechazados = preview.Rechazados,
                    LimiteFreeAlcanzado = preview.LimiteFreeAlcanzado,
                    Errores = new List<string>(preview.Errores)
                };
            }

            // Si algun flujo externo lleno solo ProductosParaGuardar sin ItemsConfirmables, convertimos de forma segura
            var itemsConfirmar = preview.ItemsConfirmables.Count > 0
                ? preview.ItemsConfirmables
                : preview.ProductosParaGuardar.Select((p, idx) => new ItemImportacionConfirmable
                {
                    NumeroFila = idx + 1,
                    IdOriginalArchivo = p.Id,
                    ProductoImportado = ClonarProducto(p)
                }).ToList();

            var erroresFinales = new List<string>(preview.Errores);
            int creadosReales = 0;
            int actualizadosReales = 0;
            int rechazadosReales = preview.Rechazados;
            bool limiteFreeAlcanzado = preview.LimiteFreeAlcanzado;
            ModoImportacion modo = preview.ModoAplicado;
            bool esPremium = preview.EsPremiumAplicado || LicenciaService.EsPremium();

            // Ejecutamos toda la recarga de catalogo actual, reasignacion de IDs y actualizacion de stock
            // dentro de UNA SOLA transaccion SQLite para eliminar condiciones de carrera TOCTOU.
            using var conn = DatabaseHelper.GetConnection();
            using var transaction = conn.BeginTransaction();

            try
            {
                var existentesFrescos = CargarProductosEnTransaccion(conn, transaction);
                var porId = existentesFrescos.ToDictionary(p => p.Id);
                var porCodigo = new Dictionary<string, Producto>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in existentesFrescos)
                {
                    if (!string.IsNullOrWhiteSpace(p.Codigo) && !porCodigo.ContainsKey(p.Codigo.Trim()))
                    {
                        porCodigo[p.Codigo.Trim()] = p;
                    }
                }

                int maxId = Math.Max(
                    porId.Keys.DefaultIfEmpty(0).Max(),
                    ProductoDB.ObtenerSiguienteIdInternal(conn, transaction) - 1);
                int totalEnCatalogo = existentesFrescos.Count;

                using var upsertCmd = conn.CreateCommand();
                upsertCmd.Transaction = transaction;
                upsertCmd.CommandText = @"
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

                var pId = upsertCmd.Parameters.Add("@id", SqliteType.Integer);
                var pCodigo = upsertCmd.Parameters.Add("@codigo", SqliteType.Text);
                var pNombre = upsertCmd.Parameters.Add("@nombre", SqliteType.Text);
                var pCategoria = upsertCmd.Parameters.Add("@categoria", SqliteType.Text);
                var pCosto = upsertCmd.Parameters.Add("@costo", SqliteType.Real);
                var pGanancia = upsertCmd.Parameters.Add("@ganancia", SqliteType.Real);
                var pPrecio = upsertCmd.Parameters.Add("@precio", SqliteType.Real);
                var pStock = upsertCmd.Parameters.Add("@stock", SqliteType.Real);
                var pPedir = upsertCmd.Parameters.Add("@pedirCantidad", SqliteType.Integer);
                var pPeso = upsertCmd.Parameters.Add("@seVendePorPeso", SqliteType.Integer);
                var pContar1 = upsertCmd.Parameters.Add("@contarComoUnaPieza", SqliteType.Integer);
                var pStockMin = upsertCmd.Parameters.Add("@stockMinimo", SqliteType.Real);

                void EjecutarUpsert(Producto prodGuardar)
                {
                    pId.Value = prodGuardar.Id;
                    pCodigo.Value = prodGuardar.Codigo ?? string.Empty;
                    pNombre.Value = prodGuardar.Nombre ?? string.Empty;
                    pCategoria.Value = prodGuardar.Categoria ?? "General";
                    pCosto.Value = Convert.ToDouble(prodGuardar.Costo);
                    pGanancia.Value = Convert.ToDouble(prodGuardar.Ganancia);
                    pPrecio.Value = Convert.ToDouble(prodGuardar.Precio);
                    pStock.Value = Convert.ToDouble(prodGuardar.Stock);
                    pPedir.Value = prodGuardar.PedirCantidadAlCobrar ? 1 : 0;
                    pPeso.Value = prodGuardar.SeVendePorPeso ? 1 : 0;
                    pContar1.Value = prodGuardar.ContarComoUnaPieza ? 1 : 0;
                    pStockMin.Value = Convert.ToDouble(prodGuardar.StockMinimo);
                    upsertCmd.ExecuteNonQuery();
                }

                foreach (var item in itemsConfirmar)
                {
                    var prod = ClonarProducto(item.ProductoImportado);
                    string codigoNorm = (prod.Codigo ?? string.Empty).Trim();

                    Producto? destinoFresco = null;
                    if (item.IdOriginalArchivo > 0 && porId.TryGetValue(item.IdOriginalArchivo, out var matchId))
                    {
                        destinoFresco = matchId;
                    }
                    else if (!string.IsNullOrEmpty(codigoNorm) && porCodigo.TryGetValue(codigoNorm, out var matchCodigo))
                    {
                        destinoFresco = matchCodigo;
                    }

                    if (destinoFresco != null)
                    {
                        if (modo == ModoImportacion.SoloNuevos)
                        {
                            continue;
                        }

                        if (modo == ModoImportacion.SumarStockYCrear)
                        {
                            // Sumamos al stock FRESCO de la BD para no pisar ventas ocurridas durante la vista previa
                            var actualizado = new Producto
                            {
                                Id = destinoFresco.Id,
                                Codigo = destinoFresco.Codigo,
                                Nombre = destinoFresco.Nombre,
                                Categoria = destinoFresco.Categoria,
                                Costo = destinoFresco.Costo,
                                Ganancia = destinoFresco.Ganancia,
                                Precio = destinoFresco.Precio,
                                Stock = Math.Max(0m, destinoFresco.Stock + prod.Stock),
                                StockMinimo = destinoFresco.StockMinimo,
                                PedirCantidadAlCobrar = destinoFresco.PedirCantidadAlCobrar,
                                SeVendePorPeso = destinoFresco.SeVendePorPeso,
                                ContarComoUnaPieza = destinoFresco.ContarComoUnaPieza
                            };
                            EjecutarUpsert(actualizado);
                            porId[actualizado.Id] = actualizado;
                            if (!string.IsNullOrEmpty(codigoNorm)) porCodigo[codigoNorm] = actualizado;
                            actualizadosReales++;
                        }
                        else
                        {
                            prod.Id = destinoFresco.Id;
                            EjecutarUpsert(prod);
                            porId[prod.Id] = prod;
                            if (!string.IsNullOrEmpty(codigoNorm)) porCodigo[codigoNorm] = prod;
                            actualizadosReales++;
                        }
                    }
                    else
                    {
                        if (!esPremium && totalEnCatalogo >= 400)
                        {
                            limiteFreeAlcanzado = true;
                            rechazadosReales++;
                            erroresFinales.Add($"Fila {item.NumeroFila}: omitida al confirmar por limite de 400 productos en version gratuita.");
                            continue;
                        }

                        maxId++;
                        prod.Id = maxId;
                        EjecutarUpsert(prod);
                        porId[prod.Id] = prod;
                        if (!string.IsNullOrEmpty(codigoNorm)) porCodigo[codigoNorm] = prod;
                        totalEnCatalogo++;
                        creadosReales++;
                    }
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                PosLogger.Error("ImportarExportarService.ConfirmarImportacion", ex);
                throw;
            }

            return new ResultadoImportacion
            {
                Creados = creadosReales,
                Actualizados = actualizadosReales,
                Rechazados = rechazadosReales,
                LimiteFreeAlcanzado = limiteFreeAlcanzado,
                Errores = erroresFinales
            };
        }

        private static List<Producto> CargarProductosEnTransaccion(SqliteConnection conn, SqliteTransaction tr)
        {
            var list = new List<Producto>();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tr;
            cmd.CommandText = @"
                SELECT Id, Codigo, Nombre, Categoria, Costo, Ganancia, Precio, Stock, PedirCantidadAlCobrar, SeVendePorPeso, COALESCE(ContarComoUnaPieza, 0), COALESCE(StockMinimo, 5)
                FROM Productos
                ORDER BY Id ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Producto
                {
                    Id = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                    Codigo = reader.IsDBNull(1) ? string.Empty : (Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty),
                    Nombre = reader.IsDBNull(2) ? string.Empty : (Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty),
                    Categoria = reader.IsDBNull(3) ? "General" : (Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? "General"),
                    Costo = reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                    Ganancia = reader.IsDBNull(5) ? 0m : Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
                    Precio = reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                    Stock = reader.IsDBNull(7) ? 0m : Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture),
                    PedirCantidadAlCobrar = !reader.IsDBNull(8) && Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture) == 1,
                    SeVendePorPeso = !reader.IsDBNull(9) && Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture) == 1,
                    ContarComoUnaPieza = !reader.IsDBNull(10) && Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture) == 1,
                    StockMinimo = reader.IsDBNull(11) ? 5m : Convert.ToDecimal(reader.GetValue(11), CultureInfo.InvariantCulture)
                });
            }
            return list;
        }

        private static Producto ClonarProducto(Producto p)
        {
            return new Producto
            {
                Id = p.Id,
                Codigo = p.Codigo ?? string.Empty,
                Nombre = p.Nombre ?? string.Empty,
                Categoria = string.IsNullOrWhiteSpace(p.Categoria) ? "General" : p.Categoria,
                Costo = p.Costo,
                Ganancia = p.Ganancia,
                Precio = p.Precio,
                Stock = p.Stock,
                StockMinimo = p.StockMinimo,
                PedirCantidadAlCobrar = p.PedirCantidadAlCobrar,
                SeVendePorPeso = p.SeVendePorPeso,
                ContarComoUnaPieza = p.ContarComoUnaPieza
            };
        }

        private static string? ConstruirAdvertenciaValoresAnomalos(Producto prod, string? observacionPrevia)
        {
            var notas = new List<string>();
            if (!string.IsNullOrWhiteSpace(observacionPrevia))
            {
                notas.Add(observacionPrevia.Trim());
            }

            if (prod.Costo > prod.Precio && prod.Precio > 0m)
            {
                string alertaCosto = $"⚠️ Costo (${prod.Costo:0.00}) supera Precio (${prod.Precio:0.00})";
                if (!notas.Any(n => n.Contains("Costo", StringComparison.OrdinalIgnoreCase) && n.Contains("Precio", StringComparison.OrdinalIgnoreCase)))
                {
                    notas.Add(alertaCosto);
                }
            }

            return notas.Count > 0 ? string.Join(" | ", notas) : null;
        }

        public static void GenerarPlantillaProductosCsv(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,Codigo,Nombre,Categoria,Costo,Ganancia,Precio,Stock,PedirCantidadAlCobrar,SeVendePorPeso,ContarComoUnaPieza,StockMinimo");
            sb.AppendLine("0,750105531088,Coca Cola 600ml,Bebidas,12.00,6.00,18.00,24,false,false,false,5");
            sb.AppendLine("0,200001,Jamon de Pavo (Granel),Salchichoneria,120.00,60.00,180.00,10.5,false,true,false,5");
            EscribirTextoAtomico(filePath, sb.ToString());
        }

        public static void ExportarProductosCsv(IEnumerable<Producto> products, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,Codigo,Nombre,Categoria,Costo,Ganancia,Precio,Stock,PedirCantidadAlCobrar,SeVendePorPeso,ContarComoUnaPieza,StockMinimo");
            foreach (var p in products.OrderBy(x => x.Id))
            {
                sb.AppendLine(string.Join(",",
                    p.Id.ToString(CultureInfo.InvariantCulture),
                    EscaparCsv(p.Codigo),
                    EscaparCsv(p.Nombre),
                    EscaparCsv(p.Categoria),
                    p.Costo.ToString("0.00", CultureInfo.InvariantCulture),
                    p.Ganancia.ToString("0.00", CultureInfo.InvariantCulture),
                    p.Precio.ToString("0.00", CultureInfo.InvariantCulture),
                    p.Stock.ToString("0.###", CultureInfo.InvariantCulture),
                    p.PedirCantidadAlCobrar ? "true" : "false",
                    p.SeVendePorPeso ? "true" : "false",
                    p.ContarComoUnaPieza ? "true" : "false",
                    p.StockMinimo.ToString("0.###", CultureInfo.InvariantCulture)));
            }
            EscribirTextoAtomico(filePath, sb.ToString());
        }

        public static void ExportarVentasCsv(IEnumerable<Venta> sales, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Folio,FechaHora,Estado,MetodoPago,PagoEfectivo,PagoTarjeta,PagoTransferencia,PagoCon,Cambio,Total,Productos");
            foreach (var v in sales.OrderByDescending(x => x.Id))
            {
                sb.AppendLine(string.Join(",",
                    v.Id.ToString(CultureInfo.InvariantCulture),
                    EscaparCsv(v.FechaHora.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    EscaparCsv(v.Estado),
                    EscaparCsv(v.MetodoPago),
                    v.PagoEfectivo.ToString("0.00", CultureInfo.InvariantCulture),
                    v.PagoTarjeta.ToString("0.00", CultureInfo.InvariantCulture),
                    v.PagoTransferencia.ToString("0.00", CultureInfo.InvariantCulture),
                    v.PagoCon.ToString("0.00", CultureInfo.InvariantCulture),
                    v.Cambio.ToString("0.00", CultureInfo.InvariantCulture),
                    v.Total.ToString("0.00", CultureInfo.InvariantCulture),
                    EscaparCsv(v.ProductosResumen)));
            }
            EscribirTextoAtomico(filePath, sb.ToString());
        }

        public static void ExportarCortesCsv(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,FechaApertura,FechaCierre,Estado,FondoInicial,VentasEfectivo,EntradasEfectivo,SalidasEfectivo,TotalEsperado,TotalContado,Diferencia,Observaciones");

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FechaHora, FechaHoraCierre, Estado, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones
                FROM CortesCaja
                ORDER BY Id DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                string apertura = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                string cierre = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                string estado = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                decimal fondo = reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetDouble(4));
                decimal ventasEfec = reader.IsDBNull(5) ? 0m : Convert.ToDecimal(reader.GetDouble(5));
                decimal entradas = reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetDouble(6));
                decimal salidas = reader.IsDBNull(7) ? 0m : Convert.ToDecimal(reader.GetDouble(7));
                decimal esperado = reader.IsDBNull(8) ? 0m : Convert.ToDecimal(reader.GetDouble(8));
                decimal contado = reader.IsDBNull(9) ? 0m : Convert.ToDecimal(reader.GetDouble(9));
                string obs = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);
                decimal diferencia = estado == "Cerrado" ? contado - esperado : 0m;

                sb.AppendLine(string.Join(",",
                    id.ToString(CultureInfo.InvariantCulture),
                    EscaparCsv(apertura),
                    EscaparCsv(cierre),
                    EscaparCsv(estado),
                    fondo.ToString("0.00", CultureInfo.InvariantCulture),
                    ventasEfec.ToString("0.00", CultureInfo.InvariantCulture),
                    entradas.ToString("0.00", CultureInfo.InvariantCulture),
                    salidas.ToString("0.00", CultureInfo.InvariantCulture),
                    esperado.ToString("0.00", CultureInfo.InvariantCulture),
                    contado.ToString("0.00", CultureInfo.InvariantCulture),
                    diferencia.ToString("0.00", CultureInfo.InvariantCulture),
                    EscaparCsv(obs)));
            }

            EscribirTextoAtomico(filePath, sb.ToString());
        }

        public static void ExportarPrestamosCsv(IEnumerable<Deudor> debtors, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,Nombre,Telefono,Concepto,MontoOriginal,TotalAbonado,SaldoRestante,TotalAbonos");
            foreach (var d in debtors.OrderByDescending(x => x.SaldoRestante).ThenBy(x => x.Nombre))
            {
                sb.AppendLine(string.Join(",",
                    d.Id.ToString(CultureInfo.InvariantCulture),
                    EscaparCsv(d.Nombre),
                    EscaparCsv(d.Telefono),
                    EscaparCsv(d.Concepto),
                    d.MontoOriginal.ToString("0.00", CultureInfo.InvariantCulture),
                    d.TotalAbonado.ToString("0.00", CultureInfo.InvariantCulture),
                    d.SaldoRestante.ToString("0.00", CultureInfo.InvariantCulture),
                    (d.Abonos?.Count ?? 0).ToString(CultureInfo.InvariantCulture)));
            }
            EscribirTextoAtomico(filePath, sb.ToString());
        }

        private static void EscribirTextoAtomico(string filePath, string contenido)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, contenido, Utf8Bom);
                File.Move(tempPath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static void VerificarNoSeaBinarioOle(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 8) return;
            Span<byte> header = stackalloc byte[8];
            int read = fs.Read(header);
            if (read == 8 &&
                header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0 &&
                header[4] == 0xA1 && header[5] == 0xB1 && header[6] == 0x1A && header[7] == 0xE1)
            {
                throw new InvalidOperationException(
                    "El archivo es un documento binario antiguo de Excel (.xls 97-2003) o está protegido con contraseña. " +
                    "Ábrelo en Excel y guárdalo como 'Libro de Excel (*.xlsx)' sin contraseña o 'CSV UTF-8 (*.csv)'.");
            }
        }

        private static string LeerTextoConEncoding(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0) return string.Empty;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }

        public static char DetectarDelimitador(string contenido)
        {
            if (string.IsNullOrWhiteSpace(contenido)) return ',';

            // Analizamos hasta las primeras 15 lineas no vacias (por si las primeras filas son un titulo del proveedor)
            var lineasMuestra = new List<string>(15);
            using (var sr = new StringReader(contenido))
            {
                string? linea;
                while ((linea = sr.ReadLine()) != null && lineasMuestra.Count < 15)
                {
                    if (!string.IsNullOrWhiteSpace(linea))
                    {
                        lineasMuestra.Add(linea);
                    }
                }
            }

            if (lineasMuestra.Count == 0) return ',';

            char[] candidatos = [',', ';', '\t', '|'];
            char mejorDelimitador = ',';
            double mejorPuntaje = -1;

            foreach (char cand in candidatos)
            {
                int lineasConDelimitador = 0;
                int totalApariciones = 0;
                var conteosPorLinea = new List<int>();

                foreach (string lin in lineasMuestra)
                {
                    int c = ContarFueraDeComillas(lin, cand);
                    if (c > 0)
                    {
                        lineasConDelimitador++;
                        totalApariciones += c;
                        conteosPorLinea.Add(c);
                    }
                }

                if (lineasConDelimitador == 0) continue;

                // Bonificamos consistencia en el numero de columnas entre filas
                int modaConteo = conteosPorLinea
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .First().Count();

                double puntaje = (lineasConDelimitador * 10.0) + (modaConteo * 15.0) + Math.Min(totalApariciones, 150);
                if (puntaje > mejorPuntaje)
                {
                    mejorPuntaje = puntaje;
                    mejorDelimitador = cand;
                }
            }

            return mejorDelimitador;
        }

        private static int ContarFueraDeComillas(string linea, char objetivo)
        {
            bool enComillas = false;
            int count = 0;
            foreach (char c in linea)
            {
                if (c == '"') enComillas = !enComillas;
                else if (!enComillas && c == objetivo) count++;
            }
            return count;
        }

        private static List<List<string>> ParsearFilasCsv(string contenido, char delimitador)
        {
            var filas = new List<List<string>>();
            using var reader = new StringReader(contenido);
            string? lineaFisica;

            while ((lineaFisica = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(lineaFisica))
                {
                    continue;
                }

                // Parseamos la linea respetando comillas; si hay una comilla huerfana sin cerrar en la linea,
                // ParsearLineaCsvTolerante se recupera localmente sin absorber el resto del archivo.
                var campos = ParsearLineaCsvTolerante(lineaFisica, delimitador);
                filas.Add(campos);
            }

            return filas;
        }

        private static List<string> ParsearLineaCsvTolerante(string linea, char delimitador)
        {
            var fila = new List<string>();
            var campo = new StringBuilder();
            bool enComillas = false;
            int indiceAperturaComilla = -1;

            for (int i = 0; i < linea.Length; i++)
            {
                char c = linea[i];
                if (enComillas)
                {
                    if (c == '"')
                    {
                        if (i + 1 < linea.Length && linea[i + 1] == '"')
                        {
                            campo.Append('"');
                            i++;
                        }
                        else if (i + 1 == linea.Length || linea[i + 1] == delimitador || char.IsWhiteSpace(linea[i + 1]))
                        {
                            enComillas = false;
                        }
                        else
                        {
                            // Comilla intermedia no escapada (ej. pulgadas 1/2" dentro de texto)
                            campo.Append('"');
                        }
                    }
                    else
                    {
                        campo.Append(c);
                    }
                }
                else
                {
                    if (c == '"' && campo.Length == 0)
                    {
                        // Verificamos si existe una comilla de cierre posterior en la misma linea;
                        // si no existe, es una comilla huerfana y la tratamos como caracter literal.
                        if (TieneComillaDeCierreEnLinea(linea, i + 1, delimitador))
                        {
                            enComillas = true;
                            indiceAperturaComilla = i;
                        }
                        else
                        {
                            campo.Append(c);
                        }
                    }
                    else if (c == delimitador)
                    {
                        fila.Add(campo.ToString().Trim());
                        campo.Clear();
                    }
                    else
                    {
                        campo.Append(c);
                    }
                }
            }

            fila.Add(campo.ToString().Trim());
            return fila;
        }

        private static bool TieneComillaDeCierreEnLinea(string linea, int desdeIndice, char delimitador)
        {
            for (int j = desdeIndice; j < linea.Length; j++)
            {
                if (linea[j] == '"')
                {
                    if (j + 1 < linea.Length && linea[j + 1] == '"')
                    {
                        j++;
                        continue;
                    }
                    if (j + 1 == linea.Length || linea[j + 1] == delimitador || char.IsWhiteSpace(linea[j + 1]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static Dictionary<string, int> MapearEncabezados(List<string> primeraFila, out bool tieneEncabezado)
        {
            var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < primeraFila.Count; i++)
            {
                string norm = NormalizarEncabezado(primeraFila[i]);
                switch (norm)
                {
                    case "id":
                    case "folio":
                        mapa.TryAdd("id", i);
                        break;
                    case "codigo":
                    case "codigobarras":
                    case "codigo de barras":
                    case "barcode":
                    case "ean":
                    case "upc":
                    case "sku":
                    case "clave":
                        mapa.TryAdd("codigo", i);
                        break;
                    case "nombre":
                    case "descripcion":
                    case "producto":
                    case "articulo":
                    case "concepto":
                        mapa.TryAdd("nombre", i);
                        break;
                    case "categoria":
                    case "departamento":
                    case "depto":
                    case "familia":
                    case "linea":
                        mapa.TryAdd("categoria", i);
                        break;
                    case "costo":
                    case "precio costo":
                    case "preciocosto":
                    case "precio compra":
                    case "compra":
                        mapa.TryAdd("costo", i);
                        break;
                    case "ganancia":
                    case "margen":
                    case "utilidad":
                        mapa.TryAdd("ganancia", i);
                        break;
                    case "precio":
                    case "precio venta":
                    case "precioventa":
                    case "pvp":
                    case "venta":
                    case "precio publico":
                        mapa.TryAdd("precio", i);
                        break;
                    case "stock":
                    case "existencia":
                    case "existencias":
                    case "cantidad":
                    case "inventario":
                    case "disponible":
                        mapa.TryAdd("stock", i);
                        break;
                    case "pedircantidadalcobrar":
                    case "pedircantidad":
                    case "pedir cantidad":
                        mapa.TryAdd("pedircantidad", i);
                        break;
                    case "sevendeporpeso":
                    case "granel":
                    case "pesable":
                    case "por peso":
                        mapa.TryAdd("sevendeporpeso", i);
                        break;
                    case "contarcomounapieza":
                    case "contar como una pieza":
                    case "contar1":
                        mapa.TryAdd("contar1", i);
                        break;
                    case "stockminimo":
                    case "stock minimo":
                    case "minimo":
                    case "minstock":
                    case "min stock":
                        mapa.TryAdd("stockminimo", i);
                        break;
                }
            }

            tieneEncabezado = mapa.ContainsKey("nombre") || mapa.ContainsKey("codigo") || mapa.ContainsKey("precio");
            return mapa;
        }

        private static string NormalizarEncabezado(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch == '_' ? ' ' : ch);
                }
            }
            return sb.ToString().Trim();
        }

        private static bool TryConstruirProducto(
            List<string> cols,
            Dictionary<string, int> mapa,
            bool tieneEncabezado,
            int numFila,
            out Producto? producto,
            out string error,
            out string? advertencia)
        {
            producto = null;
            error = string.Empty;
            advertencia = null;

            string Obtener(string clave, int fallbackIdx)
            {
                if (tieneEncabezado)
                {
                    return mapa.TryGetValue(clave, out int idx) && idx < cols.Count ? cols[idx] : string.Empty;
                }
                return fallbackIdx >= 0 && fallbackIdx < cols.Count ? cols[fallbackIdx] : string.Empty;
            }

            int id = 0;
            string idStr = Obtener("id", 0);
            if (!string.IsNullOrWhiteSpace(idStr))
            {
                if (!int.TryParse(idStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id < 0)
                {
                    error = $"Fila {numFila}: Id invalido '{idStr}'. Debe ser un numero entero positivo (o 0 para nuevo).";
                    return false;
                }
            }

            string codigo = Obtener("codigo", 1).Trim();
            string nombre = Obtener("nombre", 2).Trim();
            if (string.IsNullOrWhiteSpace(nombre))
            {
                error = $"Fila {numFila}: falta el Nombre del producto.";
                return false;
            }

            if (EsErrorFormulaExcel(nombre) || EsErrorFormulaExcel(codigo))
            {
                error = $"Fila {numFila} ({nombre}): contiene un error de formula de Excel ({nombre} / {codigo}).";
                return false;
            }

            string catRaw = Obtener("categoria", 3).Trim();
            string categoria = string.IsNullOrWhiteSpace(catRaw) ? "General" : catRaw;

            string costoStr = Obtener("costo", 4);
            decimal costo = 0m;
            if (!string.IsNullOrWhiteSpace(costoStr))
            {
                if (!TryParseDecimalEstricto(costoStr, out costo) || costo < 0m)
                {
                    error = $"Fila {numFila} ({nombre}): Costo invalido '{costoStr}'.";
                    return false;
                }
            }

            string ganStr = Obtener("ganancia", 5);
            decimal ganancia = 0m;
            bool tieneGananciaExplicita = !string.IsNullOrWhiteSpace(ganStr);
            if (tieneGananciaExplicita)
            {
                if (!TryParseDecimalEstricto(ganStr, out ganancia))
                {
                    error = $"Fila {numFila} ({nombre}): Ganancia invalida '{ganStr}'.";
                    return false;
                }
            }

            string precioStr = Obtener("precio", 6);
            decimal precio = 0m;
            if (!string.IsNullOrWhiteSpace(precioStr))
            {
                if (!TryParseDecimalEstricto(precioStr, out precio) || precio <= 0m)
                {
                    error = $"Fila {numFila} ({nombre}): Precio invalido o en cero '{precioStr}'.";
                    return false;
                }
            }
            else if (costo > 0m || ganancia > 0m)
            {
                precio = costo + ganancia;
                if (precio <= 0m)
                {
                    error = $"Fila {numFila} ({nombre}): el Precio calculado ({precio:0.00}) debe ser mayor a 0.";
                    return false;
                }
            }
            else
            {
                error = $"Fila {numFila} ({nombre}): falta el Precio.";
                return false;
            }

            if (!tieneGananciaExplicita || ganancia == 0m)
            {
                ganancia = precio - costo;
            }

            if (costo > precio)
            {
                advertencia = $"⚠️ Costo (${costo:0.00}) mayor que Precio de venta (${precio:0.00})";
            }

            string stockStr = Obtener("stock", 7);
            decimal stock = 0m;
            if (!string.IsNullOrWhiteSpace(stockStr))
            {
                if (!TryParseDecimalEstricto(stockStr, out stock) || stock < 0m)
                {
                    error = $"Fila {numFila} ({nombre}): Stock invalido '{stockStr}'.";
                    return false;
                }
            }

            bool pedirCant = ParseBoolFlexible(Obtener("pedircantidad", 8));
            bool porPeso = ParseBoolFlexible(Obtener("sevendeporpeso", 9));
            bool contar1 = ParseBoolFlexible(Obtener("contar1", 10));

            string stockMinStr = Obtener("stockminimo", 11);
            decimal stockMinimo = 5m;
            if (!string.IsNullOrWhiteSpace(stockMinStr))
            {
                if (!TryParseDecimalEstricto(stockMinStr, out stockMinimo) || stockMinimo < 0m)
                {
                    error = $"Fila {numFila} ({nombre}): Stock minimo invalido '{stockMinStr}'.";
                    return false;
                }
            }

            producto = new Producto
            {
                Id = id,
                Codigo = codigo,
                Nombre = nombre,
                Categoria = categoria,
                Costo = costo,
                Ganancia = ganancia,
                Precio = precio,
                Stock = stock,
                StockMinimo = stockMinimo,
                PedirCantidadAlCobrar = pedirCant,
                SeVendePorPeso = porPeso,
                ContarComoUnaPieza = contar1
            };
            return true;
        }

        private static bool EsErrorFormulaExcel(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return false;
            string v = valor.Trim();
            return v.Equals("#REF!", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("#DIV/0!", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("#VALUE!", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("#VALOR!", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("#NAME?", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("#¿NOMBRE?", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("#N/A", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("#NUM!", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseDecimalEstricto(string raw, out decimal valor)
        {
            valor = 0m;
            if (string.IsNullOrWhiteSpace(raw) || EsErrorFormulaExcel(raw)) return false;
            string limpio = raw.Replace("$", string.Empty)
                               .Replace("MXN", string.Empty, StringComparison.OrdinalIgnoreCase)
                               .Trim();
            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
            return decimal.TryParse(limpio, styles, CultureInfo.InvariantCulture, out valor) ||
                   decimal.TryParse(limpio, styles, CultureInfo.GetCultureInfo("es-MX"), out valor);
        }

        private static bool ParseBoolFlexible(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim();
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   s == "1" ||
                   s.Equals("si", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("sí", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   s.Equals("verdadero", StringComparison.OrdinalIgnoreCase);
        }

        public static string EscaparCsv(string? valor, char delimitador = ',')
        {
            if (string.IsNullOrEmpty(valor)) return string.Empty;

            string sanitizado = NeutralizarFormulaInjection(valor);

            if (sanitizado.Contains(delimitador) ||
                sanitizado.Contains(',') ||
                sanitizado.Contains(';') ||
                sanitizado.Contains('"') ||
                sanitizado.Contains('\n') ||
                sanitizado.Contains('\r'))
            {
                return $"\"{sanitizado.Replace("\"", "\"\"")}\"";
            }
            return sanitizado;
        }

        private static string NeutralizarFormulaInjection(string valor)
        {
            if (string.IsNullOrEmpty(valor)) return string.Empty;
            char primero = valor[0];
            if (primero == '=' || primero == '@' || primero == '\t' || primero == '\r')
            {
                return "'" + valor;
            }

            if (primero == '+' || primero == '-')
            {
                // Si no es un numero puro (ej. "+52..." o "-15.50"), neutralizar formula
                if (!decimal.TryParse(valor, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                {
                    return "'" + valor;
                }
            }

            return valor;
        }

        public static void GenerarPlantillaProductosXlsx(string filePath)
        {
            var filas = new List<object[]>
            {
                new object[] { "Id", "Codigo", "Nombre", "Categoria", "Costo", "Ganancia", "Precio", "Stock", "PedirCantidadAlCobrar", "SeVendePorPeso", "ContarComoUnaPieza", "StockMinimo" },
                new object[] { 0, "750105531088", "Coca Cola 600ml", "Bebidas", 12.00m, 6.00m, 18.00m, 24m, "false", "false", "false", 5m },
                new object[] { 0, "200001", "Jamon de Pavo (Granel)", "Salchichoneria", 120.00m, 60.00m, 180.00m, 10.5m, "false", "true", "false", 5m }
            };
            EscribirXlsx(filePath, "Productos", filas);
        }

        public static void ExportarProductosXlsx(IEnumerable<Producto> products, string filePath)
        {
            var filas = new List<object[]>
            {
                new object[] { "Id", "Codigo", "Nombre", "Categoria", "Costo", "Ganancia", "Precio", "Stock", "PedirCantidadAlCobrar", "SeVendePorPeso", "ContarComoUnaPieza", "StockMinimo" }
            };
            foreach (var p in products.OrderBy(x => x.Id))
            {
                filas.Add(new object[]
                {
                    p.Id,
                    p.Codigo,
                    p.Nombre,
                    p.Categoria,
                    p.Costo,
                    p.Ganancia,
                    p.Precio,
                    p.Stock,
                    p.PedirCantidadAlCobrar ? "true" : "false",
                    p.SeVendePorPeso ? "true" : "false",
                    p.ContarComoUnaPieza ? "true" : "false",
                    p.StockMinimo
                });
            }
            EscribirXlsx(filePath, "Productos", filas);
        }

        public static void ExportarVentasXlsx(IEnumerable<Venta> sales, string filePath)
        {
            var filas = new List<object[]>
            {
                new object[] { "Folio", "FechaHora", "Estado", "MetodoPago", "PagoEfectivo", "PagoTarjeta", "PagoTransferencia", "PagoCon", "Cambio", "Total", "Productos" }
            };
            foreach (var v in sales.OrderByDescending(x => x.Id))
            {
                filas.Add(new object[]
                {
                    v.Id,
                    v.FechaHora.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    v.Estado,
                    v.MetodoPago,
                    v.PagoEfectivo,
                    v.PagoTarjeta,
                    v.PagoTransferencia,
                    v.PagoCon,
                    v.Cambio,
                    v.Total,
                    v.ProductosResumen
                });
            }
            EscribirXlsx(filePath, "Ventas", filas);
        }

        public static void ExportarCortesXlsx(string filePath)
        {
            var filas = new List<object[]>
            {
                new object[] { "Id", "FechaApertura", "FechaCierre", "Estado", "FondoInicial", "VentasEfectivo", "EntradasEfectivo", "SalidasEfectivo", "TotalEsperado", "TotalContado", "Diferencia", "Observaciones" }
            };

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FechaHora, FechaHoraCierre, Estado, FondoInicial, VentasEfectivo, EntradasEfectivo, SalidasEfectivo, TotalEsperado, TotalContado, Observaciones
                FROM CortesCaja
                ORDER BY Id DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                string apertura = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                string cierre = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                string estado = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                decimal fondo = reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetDouble(4));
                decimal ventasEfec = reader.IsDBNull(5) ? 0m : Convert.ToDecimal(reader.GetDouble(5));
                decimal entradas = reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetDouble(6));
                decimal salidas = reader.IsDBNull(7) ? 0m : Convert.ToDecimal(reader.GetDouble(7));
                decimal esperado = reader.IsDBNull(8) ? 0m : Convert.ToDecimal(reader.GetDouble(8));
                decimal contado = reader.IsDBNull(9) ? 0m : Convert.ToDecimal(reader.GetDouble(9));
                string obs = reader.IsDBNull(10) ? string.Empty : reader.GetString(10);
                decimal diferencia = estado == "Cerrado" ? contado - esperado : 0m;

                filas.Add(new object[] { id, apertura, cierre, estado, fondo, ventasEfec, entradas, salidas, esperado, contado, diferencia, obs });
            }

            EscribirXlsx(filePath, "CortesCaja", filas);
        }

        public static void ExportarPrestamosXlsx(IEnumerable<Deudor> debtors, string filePath)
        {
            var filas = new List<object[]>
            {
                new object[] { "Id", "Nombre", "Telefono", "Concepto", "MontoOriginal", "TotalAbonado", "SaldoRestante", "TotalAbonos" }
            };
            foreach (var d in debtors.OrderByDescending(x => x.SaldoRestante).ThenBy(x => x.Nombre))
            {
                filas.Add(new object[]
                {
                    d.Id,
                    d.Nombre,
                    d.Telefono,
                    d.Concepto,
                    d.MontoOriginal,
                    d.TotalAbonado,
                    d.SaldoRestante,
                    d.Abonos?.Count ?? 0
                });
            }
            EscribirXlsx(filePath, "Prestamos", filas);
        }

        private static List<List<string>> LeerFilasXlsx(string filePath)
        {
            VerificarNoSeaBinarioOle(filePath);

            var resultado = new List<List<string>>();
            ZipArchive zip;
            try
            {
                zip = ZipFile.OpenRead(filePath);
            }
            catch (InvalidDataException ex)
            {
                PosLogger.Warn("ImportarExportarService.LeerFilasXlsx", ex);
                throw new InvalidOperationException("El archivo .xlsx no tiene un formato ZIP/OpenXML válido o está dañado.");
            }

            using (zip)
            {
                var sharedStrings = new List<string>();
                var sstEntry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
                if (sstEntry != null)
                {
                    using var s = sstEntry.Open();
                    var doc = XDocument.Load(s);
                    XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                    foreach (var si in doc.Descendants(ns + "si"))
                    {
                        // Ignoramos texto fonetico <rPh> para no concatenar anotaciones ruby al texto real de la celda
                        var textosValidos = si.Descendants(ns + "t")
                            .Where(t => !t.Ancestors(ns + "rPh").Any())
                            .Select(t => t.Value);
                        sharedStrings.Add(string.Concat(textosValidos));
                    }
                }

                var sheetEntry = ResolverPrimeraHojaDisponible(zip);
                if (sheetEntry == null) return resultado;

                using var sheetStream = sheetEntry.Open();
                var sheetDoc = XDocument.Load(sheetStream);
                XNamespace sns = sheetDoc.Root?.Name.Namespace ?? XNamespace.None;

                foreach (var row in sheetDoc.Descendants(sns + "row"))
                {
                    var celdas = new SortedDictionary<int, string>();
                    int indiceSecuencial = 0;

                    foreach (var c in row.Elements(sns + "c"))
                    {
                        string? rRef = c.Attribute("r")?.Value;
                        int colIdx = string.IsNullOrWhiteSpace(rRef) ? indiceSecuencial : ConvertirReferenciaColumna(rRef);
                        indiceSecuencial = colIdx + 1;

                        string tipo = c.Attribute("t")?.Value ?? string.Empty;
                        string valor = string.Empty;

                        if (tipo == "s")
                        {
                            if (int.TryParse(c.Element(sns + "v")?.Value, out int sstIdx) && sstIdx >= 0 && sstIdx < sharedStrings.Count)
                            {
                                valor = sharedStrings[sstIdx];
                            }
                        }
                        else if (tipo == "inlineStr")
                        {
                            var textosInline = c.Descendants(sns + "t")
                                .Where(t => !t.Ancestors(sns + "rPh").Any())
                                .Select(t => t.Value);
                            valor = string.Concat(textosInline);
                        }
                        else if (tipo == "b")
                        {
                            string rawBool = (c.Element(sns + "v")?.Value ?? string.Empty).Trim();
                            valor = (rawBool == "1" || rawBool.Equals("true", StringComparison.OrdinalIgnoreCase)) ? "true" : "false";
                        }
                        else if (tipo == "e")
                        {
                            valor = c.Element(sns + "v")?.Value ?? "#ERROR!";
                        }
                        else
                        {
                            valor = c.Element(sns + "v")?.Value ?? string.Empty;
                        }

                        celdas[colIdx] = valor.Trim();
                    }

                    if (celdas.Count == 0) continue;
                    int maxCol = celdas.Keys.Max();
                    var fila = new List<string>(maxCol + 1);
                    for (int k = 0; k <= maxCol; k++)
                    {
                        fila.Add(celdas.TryGetValue(k, out var val) ? val : string.Empty);
                    }
                    resultado.Add(fila);
                }
            }

            return resultado;
        }

        private static ZipArchiveEntry? ResolverPrimeraHojaDisponible(ZipArchive zip)
        {
            try
            {
                var wbEntry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase));
                var relsEntry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, "xl/_rels/workbook.xml.rels", StringComparison.OrdinalIgnoreCase));

                if (wbEntry != null && relsEntry != null)
                {
                    using var wbStream = wbEntry.Open();
                    var wbDoc = XDocument.Load(wbStream);
                    XNamespace wbNs = wbDoc.Root?.Name.Namespace ?? XNamespace.None;
                    XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

                    using var relsStream = relsEntry.Open();
                    var relsDoc = XDocument.Load(relsStream);
                    XNamespace relPkgNs = relsDoc.Root?.Name.Namespace ?? XNamespace.None;

                    var mapaRels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var rel in relsDoc.Descendants(relPkgNs + "Relationship"))
                    {
                        string? id = rel.Attribute("Id")?.Value;
                        string? target = rel.Attribute("Target")?.Value;
                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(target))
                        {
                            mapaRels[id] = target!;
                        }
                    }

                    var sheets = wbDoc.Descendants(wbNs + "sheet").ToList();
                    // Preferimos la primera hoja visible; si todas estan ocultas, tomamos la primera
                    var hojaElegida = sheets.FirstOrDefault(s =>
                    {
                        string? state = s.Attribute("state")?.Value;
                        return !string.Equals(state, "hidden", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(state, "veryHidden", StringComparison.OrdinalIgnoreCase);
                    }) ?? sheets.FirstOrDefault();

                    string? rId = hojaElegida?.Attribute(rNs + "id")?.Value
                               ?? hojaElegida?.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;

                    if (!string.IsNullOrWhiteSpace(rId) && mapaRels.TryGetValue(rId, out string? targetPath))
                    {
                        string rutaNormalizada = targetPath.Replace('\\', '/').TrimStart('/');
                        if (!rutaNormalizada.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                        {
                            rutaNormalizada = "xl/" + rutaNormalizada;
                        }

                        var entryPorRel = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, rutaNormalizada, StringComparison.OrdinalIgnoreCase));
                        if (entryPorRel != null)
                        {
                            return entryPorRel;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PosLogger.Warn("ImportarExportarService.ResolverPrimeraHojaDisponible", ex);
            }

            return zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, "xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase))
                ?? zip.Entries
                    .Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
        }

        private static int ConvertirReferenciaColumna(string cellRef)
        {
            int col = 0;
            foreach (char ch in cellRef)
            {
                if (!char.IsLetter(ch)) break;
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return Math.Max(0, col - 1);
        }

        private static void EscribirXlsx(string filePath, string nombreHoja, List<object[]> filas)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Escritura atomica: generamos primero en un archivo .tmp y reemplazamos solo cuando el ZIP esta completo
            string tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var zip = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    EscribirEntradaTexto(zip, "[Content_Types].xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                        "</Types>");

                    EscribirEntradaTexto(zip, "_rels/.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                        "</Relationships>");

                    EscribirEntradaTexto(zip, "xl/_rels/workbook.xml.rels",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                        "</Relationships>");

                    string hojaEsc = System.Security.SecurityElement.Escape(nombreHoja) ?? "Datos";
                    EscribirEntradaTexto(zip, "xl/workbook.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                        $"<sheets><sheet name=\"{hojaEsc}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                        "</workbook>");

                    EscribirEntradaTexto(zip, "xl/styles.xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                        "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                        "</styleSheet>");

                    var sb = new StringBuilder();
                    sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                    sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

                    for (int r = 0; r < filas.Count; r++)
                    {
                        int rowNum = r + 1;
                        sb.Append($"<row r=\"{rowNum}\">");
                        var row = filas[r];
                        for (int c = 0; c < row.Length; c++)
                        {
                            string colRef = ObtenerNombreColumna(c) + rowNum;
                            object val = row[c] ?? string.Empty;
                            string styleAttr = r == 0 ? " s=\"1\"" : string.Empty;

                            if (r > 0 && (val is int || val is decimal || val is double || val is long))
                            {
                                string numStr = Convert.ToString(val, CultureInfo.InvariantCulture) ?? "0";
                                sb.Append($"<c r=\"{colRef}\"{styleAttr}><v>{numStr}</v></c>");
                            }
                            else
                            {
                                string rawText = Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty;
                                string textStr = NeutralizarFormulaInjection(LimpiarTextoXml(rawText));
                                sb.Append($"<c r=\"{colRef}\" t=\"inlineStr\"{styleAttr}><is><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(textStr)}</t></is></c>");
                            }
                        }
                        sb.Append("</row>");
                    }

                    sb.Append("</sheetData></worksheet>");
                    EscribirEntradaTexto(zip, "xl/worksheets/sheet1.xml", sb.ToString());
                }

                File.Move(tempPath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static void EscribirEntradaTexto(ZipArchive zip, string entryName, string content)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private static string ObtenerNombreColumna(int index)
        {
            var sb = new StringBuilder();
            int n = index;
            while (n >= 0)
            {
                sb.Insert(0, (char)('A' + (n % 26)));
                n = (n / 26) - 1;
            }
            return sb.ToString();
        }

        private static string LimpiarTextoXml(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            foreach (char ch in input)
            {
                if (ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20)
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
