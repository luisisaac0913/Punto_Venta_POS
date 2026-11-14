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

        public static ResultadoImportacion ImportarProductosCsv(string filePath, bool esPremium)
        {
            var preview = PrevisualizarImportacion(filePath, esPremium, ModoImportacion.CrearYActualizar);
            return ConfirmarImportacion(preview);
        }

        public static List<List<string>> LeerFilasCrudas(string filePath)
        {
            if (string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return LeerFilasXlsx(filePath);
            }

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
            var resultado = new ResultadoPreviewImportacion();
            var filas = LeerFilasCrudas(filePath);

            if (filas.Count == 0)
            {
                resultado.Errores.Add(string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase)
                    ? "El libro Excel no contiene hojas o filas validas."
                    : "El archivo esta vacio o no contiene filas validas.");
                return resultado;
            }

            var mapa = MapearEncabezados(filas[0], out bool tieneEncabezado);
            int filaInicio = tieneEncabezado ? 1 : 0;

            if (tieneEncabezado && !mapa.ContainsKey("nombre"))
            {
                string err = "Columnas no reconocidas automaticamente. Haz clic en '? Normalizar con IA' para mapear y limpiar este archivo.";
                resultado.Errores.Add(err);
                resultado.Filas.Add(new FilaImportacionPreview
                {
                    NumeroFila = 1,
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

                if (!TryConstruirProducto(cols, mapa, tieneEncabezado, numFila, out var prod, out string errorFila))
                {
                    resultado.Rechazados++;
                    resultado.Errores.Add(errorFila);
                    resultado.Filas.Add(new FilaImportacionPreview { NumeroFila = numFila, Estado = "Rechazado", Detalle = errorFila });
                    continue;
                }

                itemsValidos.Add((numFila, prod!, null));
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

            foreach (var (numFila, prod, observacion) in items)
            {

                string codigoNorm = prod!.Codigo.Trim();
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
                        Codigo = prod.Codigo,
                        Nombre = prod.Nombre,
                        Categoria = prod.Categoria,
                        Costo = prod.Costo,
                        Precio = prod.Precio,
                        Stock = prod.Stock
                    });
                    continue;
                }

                Producto? destino = null;
                if (prod.Id > 0 && porId.TryGetValue(prod.Id, out var porIdMatch))
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
                            Codigo = prod.Codigo,
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
                        resultado.Actualizados++;
                        resultado.Filas.Add(new FilaImportacionPreview
                        {
                            NumeroFila = numFila,
                            Estado = "Sumar Stock",
                            Detalle = $"Stock: {destino.Stock:0.###} -> {actualizado.Stock:0.###}",
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
                        resultado.Actualizados++;
                        resultado.Filas.Add(new FilaImportacionPreview
                        {
                            NumeroFila = numFila,
                            Estado = "Actualizar",
                            Detalle = string.IsNullOrWhiteSpace(observacion) ? $"Actualiza ID #{prod.Id}" : $"Actualiza ID #{prod.Id} | IA: {observacion}",
                            Codigo = prod.Codigo,
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
                            Codigo = prod.Codigo,
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
                    resultado.Creados++;
                    resultado.Filas.Add(new FilaImportacionPreview
                    {
                        NumeroFila = numFila,
                        Estado = "Nuevo",
                        Detalle = string.IsNullOrWhiteSpace(observacion) ? $"Se asignara ID #{prod.Id}" : $"Se asignara ID #{prod.Id} | IA: {observacion}",
                        Codigo = prod.Codigo,
                        Nombre = prod.Nombre,
                        Categoria = prod.Categoria,
                        Costo = prod.Costo,
                        Precio = prod.Precio,
                        Stock = prod.Stock,
                        ProductoFinal = prod
                    });
                }
            }

            return resultado;
        }

        public static ResultadoImportacion ConfirmarImportacion(ResultadoPreviewImportacion preview)
        {
            if (preview.ProductosParaGuardar.Count > 0)
            {
                ProductoDB.Save(preview.ProductosParaGuardar);
            }

            return new ResultadoImportacion
            {
                Creados = preview.Creados,
                Actualizados = preview.Actualizados,
                Rechazados = preview.Rechazados,
                LimiteFreeAlcanzado = preview.LimiteFreeAlcanzado,
                Errores = new List<string>(preview.Errores)
            };
        }

        public static void GenerarPlantillaProductosCsv(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,Codigo,Nombre,Categoria,Costo,Ganancia,Precio,Stock,PedirCantidadAlCobrar,SeVendePorPeso,ContarComoUnaPieza,StockMinimo");
            sb.AppendLine("0,750105531088,Coca Cola 600ml,Bebidas,12.00,6.00,18.00,24,false,false,false,5");
            sb.AppendLine("0,200001,Jamon de Pavo (Granel),Salchichoneria,120.00,60.00,180.00,10.5,false,true,false,5");
            File.WriteAllText(filePath, sb.ToString(), Utf8Bom);
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
            File.WriteAllText(filePath, sb.ToString(), Utf8Bom);
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
            File.WriteAllText(filePath, sb.ToString(), Utf8Bom);
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

            File.WriteAllText(filePath, sb.ToString(), Utf8Bom);
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
            File.WriteAllText(filePath, sb.ToString(), Utf8Bom);
        }

        private static string LeerTextoConEncoding(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0) return string.Empty;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
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

        private static char DetectarDelimitador(string contenido)
        {
            int idxFinLinea = contenido.IndexOfAny(new[] { '\r', '\n' });
            string primeraLinea = idxFinLinea >= 0 ? contenido[..idxFinLinea] : contenido;

            int comas = ContarFueraDeComillas(primeraLinea, ',');
            int puntoComas = ContarFueraDeComillas(primeraLinea, ';');
            int tabs = ContarFueraDeComillas(primeraLinea, '\t');

            if (tabs > comas && tabs > puntoComas) return '\t';
            if (puntoComas > comas) return ';';
            return ',';
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
            var filaActual = new List<string>();
            var campo = new StringBuilder();
            bool enComillas = false;

            for (int i = 0; i < contenido.Length; i++)
            {
                char c = contenido[i];
                if (enComillas)
                {
                    if (c == '"')
                    {
                        if (i + 1 < contenido.Length && contenido[i + 1] == '"')
                        {
                            campo.Append('"');
                            i++;
                        }
                        else
                        {
                            enComillas = false;
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
                        enComillas = true;
                    }
                    else if (c == delimitador)
                    {
                        filaActual.Add(campo.ToString().Trim());
                        campo.Clear();
                    }
                    else if (c == '\r' || c == '\n')
                    {
                        if (c == '\r' && i + 1 < contenido.Length && contenido[i + 1] == '\n') i++;
                        filaActual.Add(campo.ToString().Trim());
                        campo.Clear();
                        filas.Add(filaActual);
                        filaActual = new List<string>();
                    }
                    else
                    {
                        campo.Append(c);
                    }
                }
            }

            if (campo.Length > 0 || filaActual.Count > 0)
            {
                filaActual.Add(campo.ToString().Trim());
                filas.Add(filaActual);
            }

            return filas;
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
                        mapa.TryAdd("id", i);
                        break;
                    case "codigo":
                    case "codigobarras":
                    case "codigo de barras":
                    case "barcode":
                    case "ean":
                    case "upc":
                        mapa.TryAdd("codigo", i);
                        break;
                    case "nombre":
                    case "descripcion":
                    case "producto":
                    case "articulo":
                        mapa.TryAdd("nombre", i);
                        break;
                    case "categoria":
                    case "departamento":
                    case "depto":
                    case "familia":
                        mapa.TryAdd("categoria", i);
                        break;
                    case "costo":
                    case "precio costo":
                    case "preciocosto":
                    case "precio compra":
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
                        mapa.TryAdd("precio", i);
                        break;
                    case "stock":
                    case "existencia":
                    case "existencias":
                    case "cantidad":
                    case "inventario":
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

        private static bool TryConstruirProducto(List<string> cols, Dictionary<string, int> mapa, bool tieneEncabezado, int numFila, out Producto? producto, out string error)
        {
            producto = null;
            error = string.Empty;

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
                int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
            }

            string codigo = Obtener("codigo", 1);
            string nombre = Obtener("nombre", 2);
            if (string.IsNullOrWhiteSpace(nombre))
            {
                error = $"Fila {numFila}: falta el Nombre del producto.";
                return false;
            }

            string catRaw = Obtener("categoria", 3);
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
                if (!TryParseDecimalEstricto(precioStr, out precio) || precio < 0m)
                {
                    error = $"Fila {numFila} ({nombre}): Precio invalido '{precioStr}'.";
                    return false;
                }
            }
            else if (costo > 0m || ganancia > 0m)
            {
                precio = costo + ganancia;
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

        private static bool TryParseDecimalEstricto(string raw, out decimal valor)
        {
            string limpio = raw.Replace("$", string.Empty).Replace("MXN", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowDecimalPoint;
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
                   s.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string EscaparCsv(string? valor)
        {
            if (string.IsNullOrEmpty(valor)) return string.Empty;
            if (valor.Contains(',') || valor.Contains('"') || valor.Contains('\n') || valor.Contains('\r'))
            {
                return $"\"{valor.Replace("\"", "\"\"")}\"";
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
            var resultado = new List<List<string>>();
            using var zip = ZipFile.OpenRead(filePath);

            var sharedStrings = new List<string>();
            var sstEntry = zip.GetEntry("xl/sharedStrings.xml");
            if (sstEntry != null)
            {
                using var s = sstEntry.Open();
                var doc = XDocument.Load(s);
                XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                foreach (var si in doc.Descendants(ns + "si"))
                {
                    sharedStrings.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
                }
            }

            var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml")
                ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

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
                        valor = string.Concat(c.Descendants(sns + "t").Select(t => t.Value));
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

            return resultado;
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
            if (File.Exists(filePath)) File.Delete(filePath);
            using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);

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
                        string textStr = LimpiarTextoXml(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty);
                        sb.Append($"<c r=\"{colRef}\" t=\"inlineStr\"{styleAttr}><is><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(textStr)}</t></is></c>");
                    }
                }
                sb.Append("</row>");
            }

            sb.Append("</sheetData></worksheet>");
            EscribirEntradaTexto(zip, "xl/worksheets/sheet1.xml", sb.ToString());
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

