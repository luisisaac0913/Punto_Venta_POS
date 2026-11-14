using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Punto_de_Venta.Vistas;

namespace Punto_de_Venta.Servicios
{
    public static class AsistenteHerramientasPOS
    {
        public static string InstruccionSistema =>
            "Eres el asistente integrado de Chip y Chapa Punto de Venta (version 1.0.16, aplicacion de escritorio Windows con SQLite local). " +
            "Responde en espanol claro, breve y util para el dueno o cajero de la tienda.\n\n" +
            "CONOCIMIENTO REAL DEL SISTEMA:\n" +
            "- Licencia Premium: NO es suscripcion mensual ni clave manual escrita. Se activa mediante pago unico en linea con Mercado Pago desde el apartado 'Activar Premium', queda vinculada al hardware (HWID) de esa PC y se valida en la nube.\n" +
            "- Version Gratuita incluye: Cobro completo (efectivo, tarjeta, transferencia, mixto, fiado), bascula digital (F8 en Cobro), hasta 400 productos en Inventario, hasta 5 deudores en Prestamos, hasta 5 promociones en Promociones, Ventas y Ganancia Neta del dia, Graficas del dia, Respaldos locales en Caja y Seguridad, Idioma, Modo oscuro y Atajos de teclado.\n" +
            "- Version Premium desbloquea: productos, deudores y promociones ilimitados, historial avanzado y exportacion a PDF/Excel/rangos en Ventas y Graficas, reimpresion de tickets historicos, Servidor Local movil, plantilla de WhatsApp, Color de Acento, PIN de administrador, datos extra en ticket (subtitulo, direccion, pie), respaldo automatico en Google Drive, Asistente IA (Gemini) y normalizacion inteligente de importaciones CSV/Excel.\n" +
            "- Asistente IA (Gemini): funcion exclusiva de la Version Premium (requiere ingresar tu propia API Key gratuita de Google Gemini en Configuracion -> Asistente IA).\n" +
            "- Inventario y alertas de stock: cada producto tiene su propio StockMinimo configurable (por defecto 5); un producto tiene stock bajo cuando su stock es mayor a 0 y menor o igual a su StockMinimo.\n" +
            "- Navegacion: doble clic sobre registros (ventas, productos, deudores, cortes) para ver detalles; ESC para volver o cerrar modales; F12 para abrir este asistente.\n\n" +
            "REGLAS ESTRICTAS:\n" +
            "1. Usa las herramientas disponibles para consultar o modificar inventario, productos, ventas, prestamos/deudores (incluidos abonos) y promociones reales.\n" +
            "2. Puedes crear, actualizar o eliminar productos, deudores, abonos y promociones cuando el usuario lo pida; cada cambio pedira confirmacion visual en pantalla antes de aplicarse.\n" +
            "3. Para promociones o deudores referidos por nombre de producto/cliente, busca primero el ID real con las herramientas antes de modificar.\n" +
            "4. NUNCA inventes cifras, nombres de productos, precios de licencia ni modalidades que no esten aqui.\n" +
            "5. Si el usuario pregunta algo sobre el programa que no este en este contexto ni en las herramientas, di claramente que no tienes ese dato en lugar de inventarlo.\n" +
            "6. Formatea montos en pesos mexicanos ($).";

        public static List<object> ObtenerDeclaraciones()
        {
            return new List<object>
            {
                new
                {
                    name = "consultar_stock_bajo",
                    description = "Obtiene productos cuyo stock actual es menor o igual a su stock minimo configurado (por defecto 5) o menor o igual al limite indicado.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            limite = new { type = "NUMBER", description = "Opcional: limite maximo de stock fijo. Si se omite, usa el stock minimo individual de cada producto (por defecto 5)." }
                        }
                    }
                },
                new
                {
                    name = "buscar_productos",
                    description = "Busca productos en el catalogo por nombre, codigo de barras o categoria.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            consulta = new { type = "STRING", description = "Texto a buscar en nombre, codigo o categoria." }
                        },
                        required = new[] { "consulta" }
                    }
                },
                new
                {
                    name = "consultar_resumen_ventas",
                    description = "Obtiene el total vendido, ganancia neta, numero de ventas y productos mas vendidos en los ultimos N dias.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            dias = new { type = "INTEGER", description = "Cantidad de dias hacia atras (1 = hoy, 7 = ultima semana, 30 = ultimo mes)." }
                        }
                    }
                },
                new
                {
                    name = "consultar_deudores",
                    description = "Consulta la lista de clientes con fiados/prestamos pendientes o busca uno por nombre.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            nombre = new { type = "STRING", description = "Nombre opcional del cliente a buscar. Vacio para ver todos los pendientes." }
                        }
                    }
                },
                new
                {
                    name = "crear_producto",
                    description = "Agrega un nuevo producto al inventario previa confirmacion visual del usuario.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            codigo = new { type = "STRING", description = "Codigo de barras o clave (opcional)." },
                            nombre = new { type = "STRING", description = "Nombre del producto." },
                            categoria = new { type = "STRING", description = "Categoria del producto." },
                            costo = new { type = "NUMBER", description = "Costo de compra." },
                            precio = new { type = "NUMBER", description = "Precio de venta al publico." },
                            stock = new { type = "NUMBER", description = "Existencia inicial." },
                            stockMinimo = new { type = "NUMBER", description = "Stock minimo para alerta de inventario bajo (por defecto 5)." },
                            seVendePorPeso = new { type = "BOOLEAN", description = "True si se vende por kilo/granel." }
                        },
                        required = new[] { "nombre", "precio" }
                    }
                },
                new
                {
                    name = "actualizar_producto",
                    description = "Modifica datos de un producto existente (nombre, codigo, categoria, costo, precio, stock, stock minimo) previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            id = new { type = "INTEGER", description = "ID del producto si se conoce." },
                            busqueda = new { type = "STRING", description = "Codigo de barras o nombre actual para ubicar el producto si no se tiene el ID." },
                            nuevoNombre = new { type = "STRING", description = "Nuevo nombre (opcional)." },
                            nuevoCodigo = new { type = "STRING", description = "Nuevo codigo de barras (opcional)." },
                            nuevaCategoria = new { type = "STRING", description = "Nueva categoria (opcional)." },
                            nuevoCosto = new { type = "NUMBER", description = "Nuevo costo de compra (opcional)." },
                            nuevoPrecio = new { type = "NUMBER", description = "Nuevo precio de venta (opcional)." },
                            nuevoStock = new { type = "NUMBER", description = "Fija el stock total exacto (opcional)." },
                            sumarStock = new { type = "NUMBER", description = "Cantidad a sumar (positivo) o restar (negativo) al stock actual (opcional)." },
                            nuevoStockMinimo = new { type = "NUMBER", description = "Nuevo stock minimo para alerta de inventario bajo (opcional)." },
                            seVendePorPeso = new { type = "BOOLEAN", description = "Cambiar si se vende por peso (opcional)." },
                            pedirCantidadAlCobrar = new { type = "BOOLEAN", description = "Cambiar si pide cantidad manual al cobrar (opcional)." }
                        }
                    }
                },
                new
                {
                    name = "ajustar_precios_categoria",
                    description = "Aumenta o reduce los precios de todos los productos de una categoria por porcentaje o monto fijo, previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            categoria = new { type = "STRING", description = "Nombre de la categoria a modificar." },
                            porcentaje = new { type = "NUMBER", description = "Porcentaje de cambio, ej. 10 para +10% o -5 para -5%." },
                            montoFijo = new { type = "NUMBER", description = "Monto fijo en pesos a sumar o restar al precio, ej. 2 o -1.5." }
                        },
                        required = new[] { "categoria" }
                    }
                },
                new
                {
                    name = "eliminar_producto",
                    description = "Elimina un producto del inventario por ID, codigo o nombre exacto, previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            id = new { type = "INTEGER", description = "ID del producto si se conoce." },
                            busqueda = new { type = "STRING", description = "Codigo de barras o nombre del producto a eliminar." }
                        }
                    }
                },
                new
                {
                    name = "crear_deudor",
                    description = "Registra un nuevo prestamo/fiado o suma monto a una cuenta pendiente existente del mismo cliente, previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            nombre = new { type = "STRING", description = "Nombre completo del cliente." },
                            monto = new { type = "NUMBER", description = "Monto prestado o fiado en pesos (mayor a 0)." },
                            concepto = new { type = "STRING", description = "Concepto o articulos fiados." },
                            telefono = new { type = "STRING", description = "Telefono o WhatsApp opcional del cliente." }
                        },
                        required = new[] { "nombre", "monto" }
                    }
                },
                new
                {
                    name = "actualizar_deudor",
                    description = "Modifica datos de un cliente deudor existente (nombre, telefono, concepto o monto original de deuda), previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            id = new { type = "INTEGER", description = "ID del deudor si se conoce." },
                            busqueda = new { type = "STRING", description = "Nombre o telefono del deudor a buscar." },
                            nuevoNombre = new { type = "STRING", description = "Nuevo nombre del deudor (opcional)." },
                            nuevoTelefono = new { type = "STRING", description = "Nuevo telefono (opcional)." },
                            nuevoConcepto = new { type = "STRING", description = "Nuevo concepto (opcional)." },
                            nuevoMontoOriginal = new { type = "NUMBER", description = "Nuevo monto original total de la deuda (opcional)." }
                        }
                    }
                },
                new
                {
                    name = "registrar_abono_deudor",
                    description = "Registra un abono a la deuda de un cliente (requiere turno de caja abierto), previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            id = new { type = "INTEGER", description = "ID del deudor si se conoce." },
                            busqueda = new { type = "STRING", description = "Nombre o telefono del deudor." },
                            monto = new { type = "NUMBER", description = "Monto del abono en pesos (mayor a 0 y menor o igual al saldo restante)." },
                            metodoPago = new { type = "STRING", description = "Metodo de pago: Efectivo, Tarjeta o Transferencia (por defecto Efectivo)." }
                        },
                        required = new[] { "monto" }
                    }
                },
                new
                {
                    name = "eliminar_deudor",
                    description = "Elimina permanentemente a un deudor y su historial de abonos, previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            id = new { type = "INTEGER", description = "ID del deudor si se conoce." },
                            busqueda = new { type = "STRING", description = "Nombre o telefono del deudor a eliminar." }
                        }
                    }
                },
                new
                {
                    name = "consultar_promociones",
                    description = "Consulta las promociones configuradas en la tienda y sus condiciones.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            consulta = new { type = "STRING", description = "Texto opcional para filtrar por nombre o tipo de promocion." },
                            soloActivas = new { type = "BOOLEAN", description = "Si es true, devuelve solo promociones activas y vigentes." }
                        }
                    }
                },
                new
                {
                    name = "crear_promocion",
                    description = "Crea una nueva promocion (Volumen, Mayoreo, Combo o PorcentajeSegundo), previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            nombre = new { type = "STRING", description = "Nombre descriptivo de la promocion." },
                            tipo = new { type = "STRING", description = "Tipo exacto: Volumen, Mayoreo, Combo o PorcentajeSegundo." },
                            productoPrincipalId = new { type = "INTEGER", description = "ID del producto principal." },
                            productoPrincipalBusqueda = new { type = "STRING", description = "Codigo o nombre del producto principal si no se dio ID." },
                            productoSecundarioId = new { type = "INTEGER", description = "ID del segundo producto (obligatorio para Combo)." },
                            productoSecundarioBusqueda = new { type = "STRING", description = "Codigo o nombre del segundo producto (opcional si se da ID, solo Combo)." },
                            cantidadMinima = new { type = "INTEGER", description = "Cantidad minima (al menos 2 para Volumen o Mayoreo)." },
                            precioEspecial = new { type = "NUMBER", description = "Para Volumen: precio total por paquete. Para Mayoreo: precio unitario especial." },
                            precioComboTotal = new { type = "NUMBER", description = "Precio total del combo (solo Combo)." },
                            porcentajeDescuento = new { type = "NUMBER", description = "Porcentaje de descuento en la segunda unidad, de 1 a 100 (solo PorcentajeSegundo)." },
                            fechaInicio = new { type = "STRING", description = "Fecha inicio opcional formato yyyy-MM-dd." },
                            fechaFin = new { type = "STRING", description = "Fecha fin opcional formato yyyy-MM-dd." },
                            activa = new { type = "BOOLEAN", description = "Si inicia activa (true por defecto)." }
                        },
                        required = new[] { "nombre", "tipo" }
                    }
                },
                new
                {
                    name = "actualizar_promocion",
                    description = "Modifica una promocion existente (nombre, estado activa, tipo, productos, precios, cantidades o fechas), previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            id = new { type = "INTEGER", description = "ID de la promocion si se conoce." },
                            busqueda = new { type = "STRING", description = "Nombre de la promocion a editar." },
                            nuevoNombre = new { type = "STRING", description = "Nuevo nombre opcional." },
                            activa = new { type = "BOOLEAN", description = "Activar (true) o pausar/desactivar (false)." },
                            nuevoTipo = new { type = "STRING", description = "Nuevo tipo opcional: Volumen, Mayoreo, Combo o PorcentajeSegundo." },
                            productoPrincipalId = new { type = "INTEGER", description = "Nuevo ID del producto principal (opcional)." },
                            productoPrincipalBusqueda = new { type = "STRING", description = "Codigo o nombre del nuevo producto principal (opcional)." },
                            productoSecundarioId = new { type = "INTEGER", description = "Nuevo ID del segundo producto (Combo, opcional)." },
                            productoSecundarioBusqueda = new { type = "STRING", description = "Codigo o nombre del segundo producto (Combo, opcional)." },
                            cantidadMinima = new { type = "INTEGER", description = "Nueva cantidad minima (Volumen/Mayoreo)." },
                            precioEspecial = new { type = "NUMBER", description = "Nuevo precio especial (Volumen/Mayoreo)." },
                            precioComboTotal = new { type = "NUMBER", description = "Nuevo precio total combo (Combo)." },
                            porcentajeDescuento = new { type = "NUMBER", description = "Nuevo porcentaje 1-100 en segunda unidad (PorcentajeSegundo)." },
                            fechaInicio = new { type = "STRING", description = "Nueva fecha inicio yyyy-MM-dd." },
                            fechaFin = new { type = "STRING", description = "Nueva fecha fin yyyy-MM-dd." },
                            quitarLimiteFechas = new { type = "BOOLEAN", description = "Si es true, quita las fechas de vigencia." }
                        }
                    }
                },
                new
                {
                    name = "eliminar_promocion",
                    description = "Elimina permanentemente una promocion por ID o nombre, previa confirmacion visual.",
                    parameters = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            id = new { type = "INTEGER", description = "ID de la promocion si se conoce." },
                            busqueda = new { type = "STRING", description = "Nombre de la promocion a eliminar." }
                        }
                    }
                }
            };
        }

        public static async Task<object> EjecutarHerramientaAsync(string nombre, JsonElement args, Window? owner)
        {
            switch (nombre)
            {
                case "consultar_stock_bajo":
                {
                    bool tieneLimite = TienePropiedad(args, "limite");
                    decimal limite = ExtraerDecimal(args, "limite", 5m);
                    var prods = ProductoDB.Load()
                        .Where(p => tieneLimite ? p.Stock <= limite : p.Stock <= p.StockMinimo)
                        .OrderBy(p => p.Stock)
                        .Take(25)
                        .Select(p => new { p.Id, p.Codigo, p.Nombre, p.Categoria, p.Stock, p.StockMinimo, p.Precio })
                        .ToList();
                    return new { totalEncontrados = prods.Count, limiteFijo = tieneLimite ? (decimal?)limite : null, productos = prods };
                }

                case "buscar_productos":
                {
                    string q = ExtraerTexto(args, "consulta");
                    var prods = ProductoDB.Load()
                        .Where(p =>
                            p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            p.Codigo.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            p.Categoria.Contains(q, StringComparison.OrdinalIgnoreCase))
                        .Take(20)
                        .Select(p => new { p.Id, p.Codigo, p.Nombre, p.Categoria, p.Costo, p.Precio, p.Ganancia, p.Stock, p.StockMinimo })
                        .ToList();
                    return new { totalEncontrados = prods.Count, productos = prods };
                }

                case "consultar_resumen_ventas":
                {
                    int dias = Math.Max(1, ExtraerEntero(args, "dias", 1));
                    DateTime inicio = DateTime.Today.AddDays(-(dias - 1));
                    var ventas = VentaDB.Load()
                        .Where(v => v.FechaHora >= inicio &&
                                    !string.Equals(v.Estado, "Cancelada", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(v.Estado, "Abono", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    decimal totalVendido = ventas.Sum(v => v.Total);
                    decimal gananciaNeta = ventas.Sum(v => v.GananciaTotal);
                    var top = ventas
                        .SelectMany(v => v.Productos)
                        .GroupBy(d => d.Nombre, StringComparer.OrdinalIgnoreCase)
                        .Select(g => new { nombre = g.Key, cantidad = g.Sum(x => x.Cantidad), total = g.Sum(x => x.Precio * x.Cantidad) })
                        .OrderByDescending(x => x.total)
                        .Take(5)
                        .ToList();

                    return new
                    {
                        diasConsultados = dias,
                        fechaInicio = inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        cantidadVentas = ventas.Count,
                        totalVendido,
                        gananciaNeta,
                        topProductos = top
                    };
                }

                case "consultar_deudores":
                {
                    string filtro = ExtraerTexto(args, "nombre").Trim();
                    bool filtrarNombre = !string.IsNullOrWhiteSpace(filtro);
                    var deudores = PrestamoDB.Load()
                        .Where(d => filtrarNombre
                            ? d.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase)
                            : d.SaldoRestante > 0m)
                        .OrderByDescending(d => d.SaldoRestante)
                        .Take(25)
                        .Select(d => new { d.Id, d.Nombre, d.Telefono, d.Concepto, d.MontoOriginal, d.TotalAbonado, d.SaldoRestante })
                        .ToList();
                    return new
                    {
                        totalEncontrados = deudores.Count,
                        sumaAdeudos = deudores.Sum(d => d.SaldoRestante),
                        deudores
                    };
                }

                case "crear_producto":
                {
                    string nom = ExtraerTexto(args, "nombre").Trim();
                    if (string.IsNullOrWhiteSpace(nom))
                    {
                        return new { creado = false, mensaje = "El nombre del producto es obligatorio." };
                    }
                    string cod = ExtraerTexto(args, "codigo").Trim();
                    var todos = ProductoDB.Load();
                    if (!LicenciaService.EsPremium() && todos.Count >= 400)
                    {
                        return new { creado = false, mensaje = "Limite gratuito de 400 productos alcanzado. Activa Premium para agregar mas." };
                    }
                    if (todos.Any(p => string.Equals(p.Nombre?.Trim(), nom, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new { creado = false, mensaje = $"Ya existe un producto llamado '{nom}' en el inventario." };
                    }
                    if (!string.IsNullOrWhiteSpace(cod) &&
                        todos.Any(p => !string.IsNullOrWhiteSpace(p.Codigo) && string.Equals(p.Codigo.Trim(), cod, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new { creado = false, mensaje = $"Ya existe un producto con el codigo de barras '{cod}'." };
                    }

                    string cat = ExtraerTexto(args, "categoria").Trim();
                    if (string.IsNullOrWhiteSpace(cat)) cat = "General";
                    decimal costo = Math.Max(0m, ExtraerDecimal(args, "costo", 0m));
                    decimal precio = Math.Max(0m, ExtraerDecimal(args, "precio", 0m));
                    decimal stock = Math.Max(0m, ExtraerDecimal(args, "stock", 0m));
                    decimal stockMinimo = Math.Max(0m, ExtraerDecimal(args, "stockMinimo", 5m));
                    bool porPeso = ExtraerBool(args, "seVendePorPeso", false);

                    bool confirmado = await ConfirmarEnPantallaAsync(
                        owner,
                        "Confirmar Alta por IA",
                        $"¿Deseas agregar este producto al inventario?\n\nNombre: {nom}\nCategoria: {cat}\nCosto: {costo:C2}\nPrecio: {precio:C2}\nStock: {stock:0.###}\nStock minimo: {stockMinimo:0.###}",
                        MessageBoxImage.Question).ConfigureAwait(false);

                    if (!confirmado)
                    {
                        return new { creado = false, mensaje = "El usuario cancelo la creacion del producto." };
                    }

                    var nuevo = new Producto
                    {
                        Id = ProductoDB.ObtenerSiguienteId(),
                        Codigo = cod,
                        Nombre = nom,
                        Categoria = cat,
                        Costo = costo,
                        Precio = precio,
                        Ganancia = Math.Round(precio - costo, 2),
                        Stock = stock,
                        StockMinimo = stockMinimo,
                        SeVendePorPeso = porPeso
                    };
                    ProductoDB.Save(new[] { nuevo });
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { creado = true, id = nuevo.Id, nombre = nuevo.Nombre, precio = nuevo.Precio, stockMinimo = nuevo.StockMinimo };
                }

                case "actualizar_producto":
                {
                    var (objetivo, candidatos) = BuscarProductoObjetivo(args);
                    if (objetivo == null)
                    {
                        if (candidatos.Count > 1)
                        {
                            return new
                            {
                                actualizado = false,
                                mensaje = "Se encontraron varios productos parecidos. Indica el ID o nombre exacto.",
                                coincidencias = candidatos.Select(p => new { p.Id, p.Codigo, p.Nombre, p.Precio, p.Stock, p.StockMinimo })
                            };
                        }
                        return new { actualizado = false, mensaje = "No se encontro ningun producto que coincida." };
                    }

                    var editado = new Producto
                    {
                        Id = objetivo.Id,
                        Codigo = TienePropiedad(args, "nuevoCodigo") ? ExtraerTexto(args, "nuevoCodigo").Trim() : objetivo.Codigo,
                        Nombre = TienePropiedad(args, "nuevoNombre") && !string.IsNullOrWhiteSpace(ExtraerTexto(args, "nuevoNombre"))
                            ? ExtraerTexto(args, "nuevoNombre").Trim()
                            : objetivo.Nombre,
                        Categoria = TienePropiedad(args, "nuevaCategoria") && !string.IsNullOrWhiteSpace(ExtraerTexto(args, "nuevaCategoria"))
                            ? ExtraerTexto(args, "nuevaCategoria").Trim()
                            : objetivo.Categoria,
                        Costo = TienePropiedad(args, "nuevoCosto") ? Math.Max(0m, Math.Round(ExtraerDecimal(args, "nuevoCosto", objetivo.Costo), 2)) : objetivo.Costo,
                        Precio = TienePropiedad(args, "nuevoPrecio") ? Math.Max(0m, Math.Round(ExtraerDecimal(args, "nuevoPrecio", objetivo.Precio), 2)) : objetivo.Precio,
                        Stock = objetivo.Stock,
                        StockMinimo = TienePropiedad(args, "nuevoStockMinimo")
                            ? Math.Max(0m, Math.Round(ExtraerDecimal(args, "nuevoStockMinimo", objetivo.StockMinimo), 3))
                            : objetivo.StockMinimo,
                        SeVendePorPeso = TienePropiedad(args, "seVendePorPeso") ? ExtraerBool(args, "seVendePorPeso", objetivo.SeVendePorPeso) : objetivo.SeVendePorPeso,
                        PedirCantidadAlCobrar = TienePropiedad(args, "pedirCantidadAlCobrar") ? ExtraerBool(args, "pedirCantidadAlCobrar", objetivo.PedirCantidadAlCobrar) : objetivo.PedirCantidadAlCobrar,
                        ContarComoUnaPieza = objetivo.ContarComoUnaPieza
                    };

                    var catalogoValidacion = ProductoDB.Load();
                    if (catalogoValidacion.Any(p => p.Id != editado.Id && string.Equals(p.Nombre?.Trim(), editado.Nombre, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new { actualizado = false, mensaje = $"Ya existe otro producto llamado '{editado.Nombre}'." };
                    }
                    if (!string.IsNullOrWhiteSpace(editado.Codigo) &&
                        catalogoValidacion.Any(p => p.Id != editado.Id && !string.IsNullOrWhiteSpace(p.Codigo) && string.Equals(p.Codigo.Trim(), editado.Codigo, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new { actualizado = false, mensaje = $"Ya existe otro producto con el codigo de barras '{editado.Codigo}'." };
                    }

                    if (TienePropiedad(args, "nuevoStock"))
                    {
                        editado.Stock = Math.Max(0m, Math.Round(ExtraerDecimal(args, "nuevoStock", objetivo.Stock), 3));
                    }
                    else if (TienePropiedad(args, "sumarStock"))
                    {
                        decimal delta = ExtraerDecimal(args, "sumarStock", 0m);
                        editado.Stock = Math.Max(0m, Math.Round(objetivo.Stock + delta, 3));
                    }

                    editado.Ganancia = Math.Round(editado.Precio - editado.Costo, 2);

                    string resumenCambios = $"¿Aplicar estos cambios a #{objetivo.Id} ({objetivo.Nombre})?\n\n" +
                        $"Nombre: {objetivo.Nombre} -> {editado.Nombre}\n" +
                        $"Categoria: {objetivo.Categoria} -> {editado.Categoria}\n" +
                        $"Costo: {objetivo.Costo:C2} -> {editado.Costo:C2}\n" +
                        $"Precio: {objetivo.Precio:C2} -> {editado.Precio:C2}\n" +
                        $"Stock: {objetivo.Stock:0.###} -> {editado.Stock:0.###}\n" +
                        $"Stock minimo: {objetivo.StockMinimo:0.###} -> {editado.StockMinimo:0.###}";

                    bool confirmado = await ConfirmarEnPantallaAsync(owner, "Confirmar Edicion por IA", resumenCambios, MessageBoxImage.Question).ConfigureAwait(false);
                    if (!confirmado)
                    {
                        return new { actualizado = false, mensaje = "El usuario cancelo la modificacion." };
                    }

                    ProductoDB.Save(new[] { editado });
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new
                    {
                        actualizado = true,
                        id = editado.Id,
                        nombre = editado.Nombre,
                        categoria = editado.Categoria,
                        costo = editado.Costo,
                        precio = editado.Precio,
                        stock = editado.Stock,
                        stockMinimo = editado.StockMinimo
                    };
                }

                case "ajustar_precios_categoria":
                {
                    string categoria = ExtraerTexto(args, "categoria").Trim();
                    if (string.IsNullOrWhiteSpace(categoria))
                    {
                        return new { ajustado = false, mensaje = "Debes indicar la categoria." };
                    }

                    bool tienePct = TienePropiedad(args, "porcentaje");
                    bool tieneFijo = TienePropiedad(args, "montoFijo");
                    if (!tienePct && !tieneFijo)
                    {
                        return new { ajustado = false, mensaje = "Indica un porcentaje o un montoFijo." };
                    }

                    decimal porcentaje = ExtraerDecimal(args, "porcentaje", 0m);
                    decimal montoFijo = ExtraerDecimal(args, "montoFijo", 0m);

                    var afectados = ProductoDB.Load()
                        .Where(p => string.Equals(p.Categoria?.Trim(), categoria, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (afectados.Count == 0)
                    {
                        return new { ajustado = false, mensaje = $"No se encontraron productos en la categoria '{categoria}'." };
                    }

                    var modificados = new List<Producto>();
                    var ejemplos = new List<string>();
                    foreach (var p in afectados)
                    {
                        decimal nuevoPrecio = p.Precio;
                        if (tienePct) nuevoPrecio = nuevoPrecio * (1m + (porcentaje / 100m));
                        if (tieneFijo) nuevoPrecio += montoFijo;
                        nuevoPrecio = Math.Max(0m, Math.Round(nuevoPrecio, 2));

                        if (ejemplos.Count < 8)
                        {
                            ejemplos.Add($"• {p.Nombre}: {p.Precio:C2} -> {nuevoPrecio:C2}");
                        }

                        modificados.Add(new Producto
                        {
                            Id = p.Id,
                            Codigo = p.Codigo,
                            Nombre = p.Nombre,
                            Categoria = p.Categoria,
                            Costo = p.Costo,
                            Precio = nuevoPrecio,
                            Ganancia = Math.Round(nuevoPrecio - p.Costo, 2),
                            Stock = p.Stock,
                            StockMinimo = p.StockMinimo,
                            PedirCantidadAlCobrar = p.PedirCantidadAlCobrar,
                            SeVendePorPeso = p.SeVendePorPeso,
                            ContarComoUnaPieza = p.ContarComoUnaPieza
                        });
                    }

                    string detalleModal = $"Se actualizaran {modificados.Count} productos de '{categoria}':\n\n" +
                        string.Join("\n", ejemplos) +
                        (modificados.Count > 8 ? $"\n... y {modificados.Count - 8} mas." : string.Empty);

                    bool confirmado = await ConfirmarEnPantallaAsync(owner, "Confirmar Ajuste Masivo por IA", detalleModal, MessageBoxImage.Question).ConfigureAwait(false);
                    if (!confirmado)
                    {
                        return new { ajustado = false, mensaje = "El usuario cancelo el ajuste masivo." };
                    }

                    ProductoDB.Save(modificados);
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { ajustado = true, categoria, productosActualizados = modificados.Count };
                }

                case "eliminar_producto":
                {
                    var (objetivo, candidatos) = BuscarProductoObjetivo(args);
                    if (objetivo == null)
                    {
                        if (candidatos.Count > 1)
                        {
                            return new
                            {
                                eliminado = false,
                                mensaje = "Se encontraron varios productos parecidos. Indica el ID o nombre exacto.",
                                coincidencias = candidatos.Select(p => new { p.Id, p.Codigo, p.Nombre, p.Precio })
                            };
                        }
                        return new { eliminado = false, mensaje = "No se encontro el producto a eliminar." };
                    }

                    bool confirmado = await ConfirmarEnPantallaAsync(
                        owner,
                        "Confirmar Eliminacion por IA",
                        $"¿Seguro que deseas eliminar permanentemente del inventario?\n\nID #{objetivo.Id}: {objetivo.Nombre}\nCodigo: {objetivo.Codigo}\nPrecio: {objetivo.Precio:C2}\nStock: {objetivo.Stock:0.###}",
                        MessageBoxImage.Warning).ConfigureAwait(false);

                    if (!confirmado)
                    {
                        return new { eliminado = false, mensaje = "El usuario cancelo la eliminacion." };
                    }

                    ProductoDB.Delete(objetivo.Id);
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { eliminado = true, id = objetivo.Id, nombre = objetivo.Nombre };
                }

                case "crear_deudor":
                {
                    string nom = ExtraerTexto(args, "nombre").Trim();
                    if (string.IsNullOrWhiteSpace(nom))
                    {
                        return new { creado = false, mensaje = "El nombre del cliente es obligatorio." };
                    }
                    decimal monto = Math.Round(ExtraerDecimal(args, "monto", 0m), 2);
                    if (monto <= 0m)
                    {
                        return new { creado = false, mensaje = "El monto debe ser mayor a $0.00." };
                    }
                    string tel = ExtraerTexto(args, "telefono").Trim();
                    string concepto = ExtraerTexto(args, "concepto").Trim();
                    if (string.IsNullOrWhiteSpace(concepto)) concepto = "Prestamo / Fiado";

                    var todos = PrestamoDB.Load();
                    var pendiente = todos
                        .Where(d => string.Equals(d.Nombre?.Trim(), nom, StringComparison.OrdinalIgnoreCase) && d.SaldoRestante > 0m)
                        .OrderByDescending(d => d.Id)
                        .FirstOrDefault();

                    if (pendiente != null)
                    {
                        decimal nuevoTotal = pendiente.MontoOriginal + monto;
                        decimal nuevoSaldo = pendiente.SaldoRestante + monto;
                        bool confirmadoSuma = await ConfirmarEnPantallaAsync(
                            owner,
                            "Confirmar Suma a Deuda por IA",
                            $"El cliente '{pendiente.Nombre}' ya tiene una cuenta pendiente.\n\nSaldo actual: {pendiente.SaldoRestante:C2}\nMonto a sumar: {monto:C2}\nConcepto: {concepto}\nNuevo saldo restante: {nuevoSaldo:C2}\n\n¿Deseas sumar este monto a su cuenta?",
                            MessageBoxImage.Question).ConfigureAwait(false);

                        if (!confirmadoSuma)
                        {
                            return new { creado = false, mensaje = "El usuario cancelo la operacion." };
                        }

                        pendiente.MontoOriginal = nuevoTotal;
                        pendiente.Concepto = string.IsNullOrWhiteSpace(pendiente.Concepto) ? concepto : $"{pendiente.Concepto}; {concepto}";
                        if (!string.IsNullOrWhiteSpace(tel)) pendiente.Telefono = tel;
                        PrestamoDB.Save(new[] { pendiente });
                        await RefrescarVistaActivaAsync().ConfigureAwait(false);
                        return new { creado = true, sumadoACuentaExistente = true, id = pendiente.Id, nombre = pendiente.Nombre, saldoRestante = pendiente.SaldoRestante };
                    }

                    if (!LicenciaService.EsPremium() && todos.Count >= 5)
                    {
                        return new { creado = false, mensaje = "Limite gratuito de 5 deudores alcanzado. Activa Premium para registrar mas." };
                    }

                    bool confirmadoNuevo = await ConfirmarEnPantallaAsync(
                        owner,
                        "Confirmar Nuevo Deudor por IA",
                        $"¿Deseas registrar este prestamo?\n\nCliente: {nom}\nTelefono: {(string.IsNullOrWhiteSpace(tel) ? "(sin telefono)" : tel)}\nConcepto: {concepto}\nMonto: {monto:C2}",
                        MessageBoxImage.Question).ConfigureAwait(false);

                    if (!confirmadoNuevo)
                    {
                        return new { creado = false, mensaje = "El usuario cancelo el registro del deudor." };
                    }

                    int nextId = todos.Count > 0 ? todos.Max(d => d.Id) + 1 : 1;
                    var nuevo = new Deudor
                    {
                        Id = nextId,
                        Nombre = nom,
                        Telefono = tel,
                        Concepto = concepto,
                        MontoOriginal = monto,
                        TotalAbonado = 0m
                    };
                    PrestamoDB.Save(new[] { nuevo });
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { creado = true, id = nuevo.Id, nombre = nuevo.Nombre, monto = nuevo.MontoOriginal, saldoRestante = nuevo.SaldoRestante };
                }

                case "actualizar_deudor":
                {
                    var (objetivo, candidatos) = BuscarDeudorObjetivo(args);
                    if (objetivo == null)
                    {
                        if (candidatos.Count > 1)
                        {
                            return new
                            {
                                actualizado = false,
                                mensaje = "Se encontraron varios deudores parecidos. Indica el ID o nombre exacto.",
                                coincidencias = candidatos.Select(d => new { d.Id, d.Nombre, d.Telefono, d.SaldoRestante })
                            };
                        }
                        return new { actualizado = false, mensaje = "No se encontro ningun deudor que coincida." };
                    }

                    string nuevoNom = TienePropiedad(args, "nuevoNombre") && !string.IsNullOrWhiteSpace(ExtraerTexto(args, "nuevoNombre"))
                        ? ExtraerTexto(args, "nuevoNombre").Trim()
                        : objetivo.Nombre;
                    string nuevoTel = TienePropiedad(args, "nuevoTelefono")
                        ? ExtraerTexto(args, "nuevoTelefono").Trim()
                        : objetivo.Telefono;
                    string nuevoConcepto = TienePropiedad(args, "nuevoConcepto") && !string.IsNullOrWhiteSpace(ExtraerTexto(args, "nuevoConcepto"))
                        ? ExtraerTexto(args, "nuevoConcepto").Trim()
                        : objetivo.Concepto;
                    decimal nuevoMonto = TienePropiedad(args, "nuevoMontoOriginal")
                        ? Math.Round(ExtraerDecimal(args, "nuevoMontoOriginal", objetivo.MontoOriginal), 2)
                        : objetivo.MontoOriginal;

                    if (nuevoMonto <= 0m)
                    {
                        return new { actualizado = false, mensaje = "El monto original debe ser mayor a $0.00." };
                    }
                    if (nuevoMonto < objetivo.TotalAbonado)
                    {
                        return new { actualizado = false, mensaje = $"El monto original ({nuevoMonto:C2}) no puede ser menor a lo ya abonado ({objetivo.TotalAbonado:C2})." };
                    }

                    string resumen = $"Deudor ID #{objetivo.Id}: {objetivo.Nombre}\n\n" +
                        $"Nombre: {objetivo.Nombre} -> {nuevoNom}\n" +
                        $"Telefono: {objetivo.Telefono} -> {nuevoTel}\n" +
                        $"Concepto: {objetivo.Concepto} -> {nuevoConcepto}\n" +
                        $"Monto original: {objetivo.MontoOriginal:C2} -> {nuevoMonto:C2}\n" +
                        $"Nuevo saldo restante: {(nuevoMonto - objetivo.TotalAbonado):C2}";

                    bool confirmado = await ConfirmarEnPantallaAsync(owner, "Confirmar Edicion de Deudor por IA", resumen, MessageBoxImage.Question).ConfigureAwait(false);
                    if (!confirmado)
                    {
                        return new { actualizado = false, mensaje = "El usuario cancelo la modificacion." };
                    }

                    objetivo.Nombre = nuevoNom;
                    objetivo.Telefono = nuevoTel;
                    objetivo.Concepto = nuevoConcepto;
                    objetivo.MontoOriginal = nuevoMonto;
                    PrestamoDB.Save(new[] { objetivo });
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { actualizado = true, id = objetivo.Id, nombre = objetivo.Nombre, montoOriginal = objetivo.MontoOriginal, saldoRestante = objetivo.SaldoRestante };
                }

                case "registrar_abono_deudor":
                {
                    var turnoActual = TurnoCajaManager.ObtenerTurnoActivo();
                    if (turnoActual == null)
                    {
                        return new { abonado = false, mensaje = "Debes abrir turno de caja (ingresar un fondo) para registrar abonos." };
                    }

                    var (objetivo, candidatos) = BuscarDeudorObjetivo(args);
                    if (objetivo == null)
                    {
                        if (candidatos.Count > 1)
                        {
                            return new
                            {
                                abonado = false,
                                mensaje = "Se encontraron varios deudores parecidos. Indica el ID o nombre exacto.",
                                coincidencias = candidatos.Select(d => new { d.Id, d.Nombre, d.Telefono, d.SaldoRestante })
                            };
                        }
                        return new { abonado = false, mensaje = "No se encontro ningun deudor que coincida." };
                    }

                    if (objetivo.SaldoRestante <= 0m)
                    {
                        return new { abonado = false, mensaje = $"El deudor '{objetivo.Nombre}' ya tiene su cuenta liquidada ($0.00)." };
                    }

                    decimal monto = Math.Round(ExtraerDecimal(args, "monto", 0m), 2);
                    if (monto <= 0m)
                    {
                        return new { abonado = false, mensaje = "El monto del abono debe ser mayor a $0.00." };
                    }
                    if (monto > objetivo.SaldoRestante)
                    {
                        return new { abonado = false, mensaje = $"El abono ({monto:C2}) no puede superar el saldo restante ({objetivo.SaldoRestante:C2})." };
                    }

                    string metodoRaw = ExtraerTexto(args, "metodoPago").Trim();
                    bool esTarjeta = metodoRaw.Equals("Tarjeta", StringComparison.OrdinalIgnoreCase);
                    bool esTransferencia = metodoRaw.Equals("Transferencia", StringComparison.OrdinalIgnoreCase);
                    bool esEfectivo = !esTarjeta && !esTransferencia;
                    string metodoPagoVenta = esEfectivo ? "Efectivo" : (esTarjeta ? "Tarjeta" : "Transferencia");
                    string metodoTexto = metodoPagoVenta.ToLowerInvariant();
                    string tipoMov = esEfectivo ? "Entrada" : "Abono";
                    decimal nuevoSaldo = objetivo.SaldoRestante - monto;

                    bool confirmado = await ConfirmarEnPantallaAsync(
                        owner,
                        "Confirmar Abono por IA",
                        $"¿Deseas registrar este abono a caja y al historial del cliente?\n\nDeudor: {objetivo.Nombre} (ID #{objetivo.Id})\nAbono: {monto:C2} ({metodoPagoVenta})\nSaldo anterior: {objetivo.SaldoRestante:C2}\nNuevo saldo: {nuevoSaldo:C2}",
                        MessageBoxImage.Question).ConfigureAwait(false);

                    if (!confirmado)
                    {
                        return new { abonado = false, mensaje = "El usuario cancelo el abono." };
                    }

                    var todos = PrestamoDB.Load();
                    var target = todos.FirstOrDefault(d => d.Id == objetivo.Id);
                    if (target == null)
                    {
                        return new { abonado = false, mensaje = "El deudor ya no existe en la base de datos." };
                    }

                    int nextAbonoId = todos.SelectMany(d => d.Abonos).Select(a => a.Id).DefaultIfEmpty(0).Max() + 1;
                    target.Abonos.Insert(0, new Abono
                    {
                        Id = nextAbonoId,
                        DeudorId = target.Id,
                        Fecha = DateTime.Now,
                        Monto = monto
                    });
                    target.TotalAbonado += monto;

                    var culture = CultureInfo.GetCultureInfo("es-MX");
                    var ventaAbono = new Venta
                    {
                        Id = 0,
                        FechaHora = DateTime.Now,
                        Total = monto,
                        PagoCon = monto,
                        Cambio = 0m,
                        Estado = "Abono",
                        MetodoPago = metodoPagoVenta,
                        PagoEfectivo = esEfectivo ? monto : 0m,
                        PagoTarjeta = esTarjeta ? monto : 0m,
                        PagoTransferencia = esTransferencia ? monto : 0m,
                        Productos = [new DetalleVenta { Id = 0, VentaId = 0, Nombre = $"Abono de {target.Nombre}", Cantidad = 1m, Costo = 0m, Ganancia = 0m, Precio = monto, Categoria = "Abono" }]
                    };

                    PrestamoDB.RegistrarAbonoAtomico(
                        new[] { target },
                        tipoMov,
                        monto,
                        $"Abono de {target.Nombre} de {monto.ToString("C2", culture)} en {metodoTexto}",
                        ventaAbono);

                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { abonado = true, id = target.Id, nombre = target.Nombre, montoAbonado = monto, metodoPago = metodoPagoVenta, saldoRestante = target.SaldoRestante };
                }

                case "eliminar_deudor":
                {
                    var (objetivo, candidatos) = BuscarDeudorObjetivo(args);
                    if (objetivo == null)
                    {
                        if (candidatos.Count > 1)
                        {
                            return new
                            {
                                eliminado = false,
                                mensaje = "Se encontraron varios deudores parecidos. Indica el ID o nombre exacto.",
                                coincidencias = candidatos.Select(d => new { d.Id, d.Nombre, d.Telefono, d.SaldoRestante })
                            };
                        }
                        return new { eliminado = false, mensaje = "No se encontro el deudor a eliminar." };
                    }

                    bool confirmado = await ConfirmarEnPantallaAsync(
                        owner,
                        "Confirmar Eliminacion de Deudor por IA",
                        $"¿Seguro que deseas eliminar permanentemente a este deudor y sus abonos?\n\nID #{objetivo.Id}: {objetivo.Nombre}\nConcepto: {objetivo.Concepto}\nMonto original: {objetivo.MontoOriginal:C2}\nSaldo restante: {objetivo.SaldoRestante:C2}",
                        MessageBoxImage.Warning).ConfigureAwait(false);

                    if (!confirmado)
                    {
                        return new { eliminado = false, mensaje = "El usuario cancelo la eliminacion." };
                    }

                    PrestamoDB.Delete(objetivo.Id);
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { eliminado = true, id = objetivo.Id, nombre = objetivo.Nombre };
                }

                case "consultar_promociones":
                {
                    string q = ExtraerTexto(args, "consulta").Trim();
                    bool soloActivas = ExtraerBool(args, "soloActivas", false);
                    var catalogo = ProductoDB.Load().ToDictionary(p => p.Id, p => p.Nombre);
                    var promos = PromocionDbStore.LoadAll()
                        .Where(p => !soloActivas || (p.Activa && !p.EstaVencida))
                        .Where(p => string.IsNullOrWhiteSpace(q) ||
                                    p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                    p.Tipo.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                        .Take(30)
                        .Select(p => new
                        {
                            p.Id,
                            p.Nombre,
                            tipo = p.Tipo.ToString(),
                            p.Activa,
                            vencida = p.EstaVencida,
                            p.ProductoIdPrincipal,
                            productoPrincipal = catalogo.GetValueOrDefault(p.ProductoIdPrincipal, "(no encontrado)"),
                            p.CantidadMinima,
                            p.PrecioEspecial,
                            p.ProductoIdSecundario,
                            productoSecundario = p.ProductoIdSecundario > 0 ? catalogo.GetValueOrDefault(p.ProductoIdSecundario, "(no encontrado)") : null,
                            p.PrecioComboTotal,
                            porcentajeDescuento = p.PorcentajeDescuento * 100m,
                            fechaInicio = p.FechaInicio?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            fechaFin = p.FechaFin?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        })
                        .ToList();

                    return new { totalEncontradas = promos.Count, promociones = promos };
                }

                case "crear_promocion":
                {
                    var existentes = PromocionDbStore.LoadAll();
                    if (!LicenciaService.EsPremium() && existentes.Count >= 5)
                    {
                        return new { creado = false, mensaje = "Limite gratuito de 5 promociones alcanzado. Activa Premium para crear mas." };
                    }

                    string nom = ExtraerTexto(args, "nombre").Trim();
                    if (string.IsNullOrWhiteSpace(nom))
                    {
                        return new { creado = false, mensaje = "El nombre de la promocion es obligatorio." };
                    }

                    string tipoRaw = ExtraerTexto(args, "tipo").Trim();
                    if (!Enum.TryParse<TipoPromocion>(tipoRaw, true, out var tipo))
                    {
                        return new { creado = false, mensaje = "Tipo invalido. Usa exactamente: Volumen, Mayoreo, Combo o PorcentajeSegundo." };
                    }

                    var catalogo = ProductoDB.Load();
                    var (prod1, cand1) = ResolverProductoPromocion(args, "productoPrincipalId", "productoPrincipalBusqueda", catalogo);
                    if (prod1 == null)
                    {
                        return cand1.Count > 1
                            ? new { creado = false, mensaje = "Varios productos coinciden con el producto principal. Indica el ID exacto.", coincidencias = cand1.Select(p => new { p.Id, p.Codigo, p.Nombre, p.Precio }) }
                            : new { creado = false, mensaje = "No se encontro el producto principal para la promocion." };
                    }

                    var promo = new Promocion
                    {
                        Nombre = nom,
                        Tipo = tipo,
                        Activa = ExtraerBool(args, "activa", true),
                        ProductoIdPrincipal = prod1.Id
                    };

                    var (okCampos, errorCampos) = ValidarYAplicarCamposPromocion(promo, tipo, args, catalogo, prod1, esActualizacion: false);
                    if (!okCampos)
                    {
                        return new { creado = false, mensaje = errorCampos };
                    }

                    var (okFechas, errorFechas) = AplicarFechasPromocion(promo, args);
                    if (!okFechas)
                    {
                        return new { creado = false, mensaje = errorFechas };
                    }

                    string detalle = $"¿Deseas crear esta promocion?\n\nNombre: {promo.Nombre}\nTipo: {promo.Tipo}\nProducto principal: #{prod1.Id} {prod1.Nombre}\nActiva: {(promo.Activa ? "Si" : "No")}";
                    bool confirmado = await ConfirmarEnPantallaAsync(owner, "Confirmar Nueva Promocion por IA", detalle, MessageBoxImage.Question).ConfigureAwait(false);
                    if (!confirmado)
                    {
                        return new { creado = false, mensaje = "El usuario cancelo la creacion de la promocion." };
                    }

                    if (!PromocionDbStore.Save(promo, out var errorDb))
                    {
                        return new { creado = false, mensaje = $"Error al guardar promocion: {errorDb}" };
                    }
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { creado = true, nombre = promo.Nombre, tipo = promo.Tipo.ToString(), activa = promo.Activa };
                }

                case "actualizar_promocion":
                {
                    var (objetivo, candidatos) = BuscarPromocionObjetivo(args);
                    if (objetivo == null)
                    {
                        if (candidatos.Count > 1)
                        {
                            return new
                            {
                                actualizado = false,
                                mensaje = "Se encontraron varias promociones parecidas. Indica el ID o nombre exacto.",
                                coincidencias = candidatos.Select(p => new { p.Id, p.Nombre, tipo = p.Tipo.ToString(), p.Activa })
                            };
                        }
                        return new { actualizado = false, mensaje = "No se encontro ninguna promocion que coincida." };
                    }

                    var catalogo = ProductoDB.Load();
                    TipoPromocion nuevoTipo = objetivo.Tipo;
                    if (TienePropiedad(args, "nuevoTipo"))
                    {
                        string tipoRaw = ExtraerTexto(args, "nuevoTipo").Trim();
                        if (!Enum.TryParse<TipoPromocion>(tipoRaw, true, out nuevoTipo))
                        {
                            return new { actualizado = false, mensaje = "Tipo invalido. Usa exactamente: Volumen, Mayoreo, Combo o PorcentajeSegundo." };
                        }
                    }

                    Producto? prod1 = catalogo.FirstOrDefault(p => p.Id == objetivo.ProductoIdPrincipal);
                    if (TienePropiedad(args, "productoPrincipalId") || TienePropiedad(args, "productoPrincipalBusqueda"))
                    {
                        var (resuelto, cand1) = ResolverProductoPromocion(args, "productoPrincipalId", "productoPrincipalBusqueda", catalogo);
                        if (resuelto == null)
                        {
                            return cand1.Count > 1
                                ? new { actualizado = false, mensaje = "Varios productos coinciden con el producto principal. Indica el ID exacto.", coincidencias = cand1.Select(p => new { p.Id, p.Codigo, p.Nombre, p.Precio }) }
                                : new { actualizado = false, mensaje = "No se encontro el nuevo producto principal." };
                        }
                        prod1 = resuelto;
                    }
                    if (prod1 == null)
                    {
                        return new { actualizado = false, mensaje = "El producto principal asociado ya no existe en el inventario." };
                    }

                    var editada = new Promocion
                    {
                        Id = objetivo.Id,
                        Nombre = TienePropiedad(args, "nuevoNombre") && !string.IsNullOrWhiteSpace(ExtraerTexto(args, "nuevoNombre"))
                            ? ExtraerTexto(args, "nuevoNombre").Trim()
                            : objetivo.Nombre,
                        Tipo = nuevoTipo,
                        Activa = TienePropiedad(args, "activa") ? ExtraerBool(args, "activa", objetivo.Activa) : objetivo.Activa,
                        ProductoIdPrincipal = prod1.Id,
                        CantidadMinima = objetivo.CantidadMinima,
                        PrecioEspecial = objetivo.PrecioEspecial,
                        ProductoIdSecundario = objetivo.ProductoIdSecundario,
                        PrecioComboTotal = objetivo.PrecioComboTotal,
                        PorcentajeDescuento = objetivo.PorcentajeDescuento,
                        FechaInicio = objetivo.FechaInicio,
                        FechaFin = objetivo.FechaFin
                    };

                    var (okCampos, errorCampos) = ValidarYAplicarCamposPromocion(editada, nuevoTipo, args, catalogo, prod1, esActualizacion: true);
                    if (!okCampos)
                    {
                        return new { actualizado = false, mensaje = errorCampos };
                    }

                    var (okFechas, errorFechas) = AplicarFechasPromocion(editada, args);
                    if (!okFechas)
                    {
                        return new { actualizado = false, mensaje = errorFechas };
                    }

                    string resumen = $"Promocion ID #{editada.Id}\n\nNombre: {objetivo.Nombre} -> {editada.Nombre}\nTipo: {objetivo.Tipo} -> {editada.Tipo}\nActiva: {(objetivo.Activa ? "Si" : "No")} -> {(editada.Activa ? "Si" : "No")}";
                    bool confirmado = await ConfirmarEnPantallaAsync(owner, "Confirmar Edicion de Promocion por IA", resumen, MessageBoxImage.Question).ConfigureAwait(false);
                    if (!confirmado)
                    {
                        return new { actualizado = false, mensaje = "El usuario cancelo la modificacion." };
                    }

                    if (!PromocionDbStore.Update(editada, out var errorDb))
                    {
                        return new { actualizado = false, mensaje = $"Error al actualizar promocion: {errorDb}" };
                    }
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { actualizado = true, id = editada.Id, nombre = editada.Nombre, tipo = editada.Tipo.ToString(), activa = editada.Activa };
                }

                case "eliminar_promocion":
                {
                    var (objetivo, candidatos) = BuscarPromocionObjetivo(args);
                    if (objetivo == null)
                    {
                        if (candidatos.Count > 1)
                        {
                            return new
                            {
                                eliminado = false,
                                mensaje = "Se encontraron varias promociones parecidas. Indica el ID o nombre exacto.",
                                coincidencias = candidatos.Select(p => new { p.Id, p.Nombre, tipo = p.Tipo.ToString(), p.Activa })
                            };
                        }
                        return new { eliminado = false, mensaje = "No se encontro la promocion a eliminar." };
                    }

                    bool confirmado = await ConfirmarEnPantallaAsync(
                        owner,
                        "Confirmar Eliminacion de Promocion por IA",
                        $"¿Seguro que deseas eliminar permanentemente esta promocion?\n\nID #{objetivo.Id}: {objetivo.Nombre}\nTipo: {objetivo.Tipo}\nActiva: {(objetivo.Activa ? "Si" : "No")}",
                        MessageBoxImage.Warning).ConfigureAwait(false);

                    if (!confirmado)
                    {
                        return new { eliminado = false, mensaje = "El usuario cancelo la eliminacion." };
                    }

                    if (!PromocionDbStore.Delete(objetivo.Id))
                    {
                        return new { eliminado = false, mensaje = "No se pudo eliminar la promocion de la base de datos." };
                    }
                    await RefrescarVistaActivaAsync().ConfigureAwait(false);
                    return new { eliminado = true, id = objetivo.Id, nombre = objetivo.Nombre };
                }

                default:
                    return new { error = $"Herramienta desconocida: {nombre}" };
            }
        }

        private static (Deudor? Objetivo, List<Deudor> Candidatos) BuscarDeudorObjetivo(JsonElement args)
        {
            var todos = PrestamoDB.Load();
            int id = ExtraerEntero(args, "id", 0);
            if (id > 0)
            {
                var porId = todos.FirstOrDefault(d => d.Id == id);
                return (porId, porId != null ? new List<Deudor> { porId } : new List<Deudor>());
            }

            string q = ExtraerTexto(args, "busqueda").Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                return (null, new List<Deudor>());
            }

            var porNombreExacto = todos.FirstOrDefault(d => string.Equals(d.Nombre?.Trim(), q, StringComparison.OrdinalIgnoreCase));
            if (porNombreExacto != null) return (porNombreExacto, new List<Deudor> { porNombreExacto });

            var parciales = todos.Where(d =>
                d.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Telefono.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToList();
            if (parciales.Count == 1) return (parciales[0], parciales);
            return (null, parciales);
        }

        private static (Promocion? Objetivo, List<Promocion> Candidatos) BuscarPromocionObjetivo(JsonElement args)
        {
            var todas = PromocionDbStore.LoadAll();
            int id = ExtraerEntero(args, "id", 0);
            if (id > 0)
            {
                var porId = todas.FirstOrDefault(p => p.Id == id);
                return (porId, porId != null ? new List<Promocion> { porId } : new List<Promocion>());
            }

            string q = ExtraerTexto(args, "busqueda").Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                return (null, new List<Promocion>());
            }

            var porNombreExacto = todas.FirstOrDefault(p => string.Equals(p.Nombre?.Trim(), q, StringComparison.OrdinalIgnoreCase));
            if (porNombreExacto != null) return (porNombreExacto, new List<Promocion> { porNombreExacto });

            var parciales = todas.Where(p => p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
            if (parciales.Count == 1) return (parciales[0], parciales);
            return (null, parciales);
        }

        private static (Producto? Objetivo, List<Producto> Candidatos) ResolverProductoPromocion(
            JsonElement args,
            string idProp,
            string busquedaProp,
            List<Producto> catalogo)
        {
            int id = ExtraerEntero(args, idProp, 0);
            if (id > 0)
            {
                var porId = catalogo.FirstOrDefault(p => p.Id == id);
                return (porId, porId != null ? new List<Producto> { porId } : new List<Producto>());
            }

            string q = ExtraerTexto(args, busquedaProp).Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                return (null, new List<Producto>());
            }

            var porCodigo = catalogo.FirstOrDefault(p => string.Equals(p.Codigo?.Trim(), q, StringComparison.OrdinalIgnoreCase));
            if (porCodigo != null) return (porCodigo, new List<Producto> { porCodigo });

            var porNombre = catalogo.FirstOrDefault(p => string.Equals(p.Nombre?.Trim(), q, StringComparison.OrdinalIgnoreCase));
            if (porNombre != null) return (porNombre, new List<Producto> { porNombre });

            var parciales = catalogo.Where(p => p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
            if (parciales.Count == 1) return (parciales[0], parciales);
            return (null, parciales);
        }

        private static (bool Ok, string? Error) ValidarYAplicarCamposPromocion(
            Promocion promo,
            TipoPromocion tipo,
            JsonElement args,
            List<Producto> catalogo,
            Producto prodPrincipal,
            bool esActualizacion)
        {
            if (tipo == TipoPromocion.Volumen || tipo == TipoPromocion.Mayoreo)
            {
                int cant = TienePropiedad(args, "cantidadMinima")
                    ? ExtraerEntero(args, "cantidadMinima", promo.CantidadMinima)
                    : (esActualizacion ? promo.CantidadMinima : 3);
                decimal precioEsp = TienePropiedad(args, "precioEspecial")
                    ? Math.Round(ExtraerDecimal(args, "precioEspecial", promo.PrecioEspecial), 2)
                    : promo.PrecioEspecial;

                if (cant < 2)
                {
                    return (false, "La cantidad minima debe ser al menos 2.");
                }
                if (precioEsp <= 0m)
                {
                    return (false, "El precio especial debe ser mayor a $0.00.");
                }
                if (tipo == TipoPromocion.Mayoreo && precioEsp >= prodPrincipal.Precio)
                {
                    return (false, $"En Mayoreo el precio especial unitario ({precioEsp:C2}) debe ser menor al precio normal ({prodPrincipal.Precio:C2}).");
                }

                promo.CantidadMinima = cant;
                promo.PrecioEspecial = precioEsp;
                promo.ProductoIdSecundario = 0;
                promo.PrecioComboTotal = 0m;
                promo.PorcentajeDescuento = 0m;
                return (true, null);
            }

            if (tipo == TipoPromocion.Combo)
            {
                int p2Id = promo.ProductoIdSecundario;
                if (TienePropiedad(args, "productoSecundarioId") || TienePropiedad(args, "productoSecundarioBusqueda"))
                {
                    var (prod2, cand2) = ResolverProductoPromocion(args, "productoSecundarioId", "productoSecundarioBusqueda", catalogo);
                    if (prod2 == null)
                    {
                        string msg = cand2.Count > 1
                            ? "Varios productos coinciden con el segundo producto del combo. Indica su ID exacto."
                            : "No se encontro el segundo producto del combo.";
                        return (false, msg);
                    }
                    p2Id = prod2.Id;
                }
                if (p2Id <= 0)
                {
                    return (false, "Para promocion tipo Combo se requiere un segundo producto valido.");
                }

                decimal precioCombo = TienePropiedad(args, "precioComboTotal")
                    ? Math.Round(ExtraerDecimal(args, "precioComboTotal", promo.PrecioComboTotal), 2)
                    : promo.PrecioComboTotal;
                if (precioCombo <= 0m)
                {
                    return (false, "El precio total del combo debe ser mayor a $0.00.");
                }

                promo.ProductoIdSecundario = p2Id;
                promo.PrecioComboTotal = precioCombo;
                promo.CantidadMinima = 0;
                promo.PrecioEspecial = 0m;
                promo.PorcentajeDescuento = 0m;
                return (true, null);
            }

            decimal pctNormalizado;
            if (TienePropiedad(args, "porcentajeDescuento"))
            {
                decimal pctInput = ExtraerDecimal(args, "porcentajeDescuento", 0m);
                pctNormalizado = pctInput >= 1m ? pctInput / 100m : pctInput;
            }
            else
            {
                pctNormalizado = promo.PorcentajeDescuento > 0m ? promo.PorcentajeDescuento : 0.5m;
            }
            if (pctNormalizado <= 0m || pctNormalizado > 1m)
            {
                return (false, "El porcentaje de descuento en la segunda unidad debe estar entre 1 y 100.");
            }

            promo.PorcentajeDescuento = Math.Round(pctNormalizado, 4);
            promo.CantidadMinima = 0;
            promo.PrecioEspecial = 0m;
            promo.ProductoIdSecundario = 0;
            promo.PrecioComboTotal = 0m;
            return (true, null);
        }

        private static (bool Ok, string? Error) AplicarFechasPromocion(Promocion promo, JsonElement args)
        {
            if (ExtraerBool(args, "quitarLimiteFechas", false))
            {
                bool estabaVencida = promo.EstaVencida;
                promo.FechaInicio = null;
                promo.FechaFin = null;
                if (estabaVencida) promo.Activa = true;
                return (true, null);
            }

            if (TienePropiedad(args, "fechaInicio"))
            {
                string rawFi = ExtraerTexto(args, "fechaInicio").Trim();
                if (string.IsNullOrEmpty(rawFi))
                {
                    promo.FechaInicio = null;
                }
                else if (DateTime.TryParseExact(rawFi, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fi))
                {
                    promo.FechaInicio = fi.Date;
                }
                else
                {
                    return (false, "Formato invalido en fechaInicio. Usa yyyy-MM-dd.");
                }
            }

            if (TienePropiedad(args, "fechaFin"))
            {
                string rawFf = ExtraerTexto(args, "fechaFin").Trim();
                if (string.IsNullOrEmpty(rawFf))
                {
                    promo.FechaFin = null;
                }
                else if (DateTime.TryParseExact(rawFf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ff))
                {
                    promo.FechaFin = ff.Date;
                }
                else
                {
                    return (false, "Formato invalido en fechaFin. Usa yyyy-MM-dd.");
                }
            }

            if (promo.FechaInicio.HasValue && promo.FechaFin.HasValue && promo.FechaFin.Value < promo.FechaInicio.Value)
            {
                return (false, "La fecha de fin no puede ser menor a la fecha de inicio.");
            }
            if ((TienePropiedad(args, "fechaInicio") || TienePropiedad(args, "fechaFin")) && promo.FechaFin.HasValue && promo.FechaFin.Value < DateTime.Today)
            {
                return (false, "La fecha limite no puede ser una fecha pasada.");
            }
            return (true, null);
        }

        private static (Producto? Objetivo, List<Producto> Candidatos) BuscarProductoObjetivo(JsonElement args)
        {
            var todos = ProductoDB.Load();
            int id = ExtraerEntero(args, "id", 0);
            if (id > 0)
            {
                var porId = todos.FirstOrDefault(p => p.Id == id);
                return (porId, porId != null ? new List<Producto> { porId } : new List<Producto>());
            }

            string q = ExtraerTexto(args, "busqueda").Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                return (null, new List<Producto>());
            }

            var porCodigo = todos.FirstOrDefault(p => string.Equals(p.Codigo?.Trim(), q, StringComparison.OrdinalIgnoreCase));
            if (porCodigo != null) return (porCodigo, new List<Producto> { porCodigo });

            var porNombreExacto = todos.FirstOrDefault(p => string.Equals(p.Nombre?.Trim(), q, StringComparison.OrdinalIgnoreCase));
            if (porNombreExacto != null) return (porNombreExacto, new List<Producto> { porNombreExacto });

            var parciales = todos.Where(p => p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
            if (parciales.Count == 1) return (parciales[0], parciales);
            return (null, parciales);
        }

        private static async Task<bool> ConfirmarEnPantallaAsync(Window? owner, string titulo, string mensaje, MessageBoxImage icono)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (owner != null && (!owner.IsLoaded || PresentationSource.FromVisual(owner) == null))
                {
                    return false;
                }
                var res = owner != null
                    ? CustomMessageBox.Show(owner, mensaje, titulo, MessageBoxButton.YesNo, icono)
                    : CustomMessageBox.Show(mensaje, titulo, MessageBoxButton.YesNo, icono);
                return res == MessageBoxResult.Yes;
            });
        }

        private static async Task RefrescarVistaActivaAsync()
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (Application.Current?.MainWindow is MainWindow mw)
                {
                    mw.RefrescarCatalogoVistaActiva();
                }
            });
        }

        private static bool TienePropiedad(JsonElement args, string prop) =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(prop, out var val) && val.ValueKind != JsonValueKind.Null;

        private static string ExtraerTexto(JsonElement args, string prop)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
            {
                return val.GetString() ?? string.Empty;
            }
            return string.Empty;
        }

        private static decimal ExtraerDecimal(JsonElement args, string prop, decimal defecto)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(prop, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number && val.TryGetDecimal(out var d)) return d;
                if (val.ValueKind == JsonValueKind.String && decimal.TryParse(val.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds)) return ds;
            }
            return defecto;
        }

        private static int ExtraerEntero(JsonElement args, string prop, int defecto)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(prop, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number)
                {
                    if (val.TryGetInt32(out var n)) return n;
                    if (val.TryGetDecimal(out var dn)) return (int)Math.Truncate(dn);
                }
                if (val.ValueKind == JsonValueKind.String)
                {
                    string s = (val.GetString() ?? string.Empty).Trim();
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ns)) return ns;
                    if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var ds)) return (int)Math.Truncate(ds);
                }
            }
            return defecto;
        }

        private static bool ExtraerBool(JsonElement args, string prop, bool defecto)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(prop, out var val))
            {
                if (val.ValueKind == JsonValueKind.True) return true;
                if (val.ValueKind == JsonValueKind.False) return false;
                if (val.ValueKind == JsonValueKind.String && bool.TryParse((val.GetString() ?? string.Empty).Trim(), out var bs)) return bs;
            }
            return defecto;
        }
    }
}

