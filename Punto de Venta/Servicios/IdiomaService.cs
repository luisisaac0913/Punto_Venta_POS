using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Punto_de_Venta.Servicios
{
    public static class IdiomaService
    {
        public static string IdiomaActual { get; private set; } = "es";

        public static event EventHandler? IdiomaCambiado;

        public static string NormalizarCodigo(string? codigoIdioma)
        {
            if (string.IsNullOrWhiteSpace(codigoIdioma))
                return "es";

            string limpio = codigoIdioma.Trim().ToLowerInvariant();
            return limpio.StartsWith("en") ? "en" : "es";
        }

        public static bool EsIngles() => IdiomaActual == "en";

        public static void AplicarIdioma(string? codigoIdioma)
        {
            string codigo = NormalizarCodigo(codigoIdioma);
            IdiomaActual = codigo;

            if (Application.Current == null)
                return;

            string uriStr = $"pack://application:,,,/Recursos/Idiomas/Strings.{codigo}.xaml";
            try
            {
                var nuevoDiccionario = new ResourceDictionary
                {
                    Source = new Uri(uriStr, UriKind.Absolute)
                };

                var diccionarios = Application.Current.Resources.MergedDictionaries;
                var existentes = diccionarios
                    .Where(d => d.Source != null && d.Source.OriginalString.Contains("Recursos/Idiomas/Strings.", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var antiguo in existentes)
                {
                    diccionarios.Remove(antiguo);
                }

                diccionarios.Add(nuevoDiccionario);
                IdiomaCambiado?.Invoke(null, EventArgs.Empty);
            }
            catch
            {
            }
        }

        public static string Obtener(string clave, string respaldo = "")
        {
            if (Application.Current != null && Application.Current.TryFindResource(clave) is string texto)
            {
                return texto;
            }
            return respaldo;
        }

        public static string ObtenerFormato(string clave, string respaldo, params object[] args)
        {
            string plantilla = Obtener(clave, respaldo);
            try
            {
                return string.Format(plantilla, args);
            }
            catch
            {
                return plantilla;
            }
        }

        private static readonly Dictionary<string, string> TraduccionesEn = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Cobro", "Checkout" },
            { "Ventas", "Sales" },
            { "Inventario", "Inventory" },
            { "Promociones", "Promotions" },
            { "Prestamos", "Credits" },
            { "Etiquetas", "Barcodes" },
            { "Graficas", "Charts" },
            { "Libreta", "Notebook" },
            { "Configuracion", "Settings" },
            { "Activacion", "Activation" },
            { "Aviso", "Notice" },
            { "Error", "Error" },
            { "Exito", "Success" },
            { "Confirmacion", "Confirmation" },
            { "Advertencia", "Warning" },
            { "Seguridad", "Security" },
            { "Turno Pendiente de Cierre", "Pending Shift Close" },
            { "Licencia Requerida", "License Required" },
            { "Version Premium", "Premium Version" },
            { "Configuracion guardada correctamente.", "Settings saved successfully." },
            { "Configuracion guardada correctamente", "Settings saved successfully" },
            { "Operacion completada", "Operation completed" },
            { "Respaldo manual generado exitosamente", "Manual backup generated successfully" },
            { "Respaldo subido a Google Drive exitosamente", "Backup uploaded to Google Drive successfully" },
            { "Enlace copiado al portapapeles", "Link copied to clipboard" },
            { "Telefono copiado al portapapeles", "Phone copied to clipboard" },
            { "ID copiado al portapapeles", "ID copied to clipboard" },
            { "Correo copiado al portapapeles", "Email copied to clipboard" },
            { "Sitio web copiado al portapapeles", "Website copied to clipboard" },
            { "Producto agregado correctamente", "Product added successfully" },
            { "Producto modificado correctamente", "Product updated successfully" },
            { "Producto eliminado correctamente", "Product deleted successfully" },
            { "Producto no encontrado", "Product not found" },
            { "Stock actualizado correctamente", "Stock updated successfully" },
            { "Venta realizada con exito", "Sale completed successfully" },
            { "Venta pausada y guardada en espera", "Sale paused and placed on hold" },
            { "Venta recuperada en el mostrador", "Sale restored to checkout" },
            { "Venta en espera descartada", "Held sale discarded" },
            { "El carrito esta vacio", "Cart is empty" },
            { "No hay productos en el carrito", "There are no products in the cart" },
            { "Dinero insuficiente", "Insufficient cash" },
            { "Movimiento registrado correctamente", "Movement recorded successfully" },
            { "Abono registrado correctamente", "Payment recorded successfully" },
            { "Cuenta liquidada correctamente", "Account settled successfully" },
            { "Prestamo eliminado correctamente", "Credit deleted successfully" },
            { "Promocion guardada correctamente", "Promotion saved successfully" },
            { "Promocion eliminada correctamente", "Promotion deleted successfully" },
            { "Nota guardada", "Note saved" },
            { "Nota eliminada", "Note deleted" },
            { "PIN incorrecto", "Incorrect PIN" },
            { "PIN actualizado correctamente", "PIN updated successfully" },
            { "Bascula conectada", "Scale connected" },
            { "No se detecto lectura de la bascula", "No reading detected from scale" },
            { "Impresora no configurada", "Printer not configured" },
            { "Ticket impreso correctamente", "Receipt printed successfully" },
            { "Exportacion completada", "Export completed" },
            { "Importacion completada", "Import completed" },
            { "Funcion exclusiva de la Version Premium.", "This feature is exclusive to the Premium Version." },
            { "Cambios guardados", "Changes saved" },
            { "Guardado", "Saved" }
        };

        public static string TraducirTexto(string? texto)
        {
            if (string.IsNullOrEmpty(texto) || !EsIngles())
                return texto ?? string.Empty;

            if (TraduccionesEn.TryGetValue(texto.Trim(), out string? exacto))
                return exacto;

            if (texto.StartsWith("Tiene un turno pendiente por cerrar del dia ", StringComparison.OrdinalIgnoreCase))
            {
                return texto
                    .Replace("Tiene un turno pendiente por cerrar del dia ", "You have a pending shift to close from ")
                    .Replace(". Por favor realiza el corte de caja en Ventas.", ". Please perform the cash drawer close in Sales.");
            }

            if (texto.StartsWith("Error al ", StringComparison.OrdinalIgnoreCase))
                return "Error: " + texto.Substring(9);

            return texto;
        }
    }
}

