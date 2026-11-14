using System;
using System.IO;
using System.Text;

namespace Punto_de_Venta.Servicios
{
    public static class EtiquetaPrinter
    {
        private const int MaxAnchoLineaEtiqueta = 30;
        private const int MaxCaracteresProducto = 56;
        private const int MaxLongitudCodigo128 = 32;

        static EtiquetaPrinter()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch
            {
            }
        }

        public static byte[] GenerarBytesEtiqueta(string negocio, string nombreProducto, string codigo, decimal precio, int copias = 1)
        {
            string codigoLimpio = LimpiarCodigoBarrasAscii(codigo);
            if (string.IsNullOrWhiteSpace(codigoLimpio))
            {
                throw new ArgumentException("El producto no tiene un codigo valido para imprimir etiqueta.");
            }

            if (codigoLimpio.Length > MaxLongitudCodigo128)
            {
                codigoLimpio = codigoLimpio.Substring(0, MaxLongitudCodigo128);
            }

            // En CODE128 Subset B de ESC/POS, el caracter '{' debe escaparse como "{{"
            string codigoSeguro = codigoLimpio.Replace("{", "{{", StringComparison.Ordinal);
            byte[] datosCodigo = Encoding.ASCII.GetBytes("{B" + codigoSeguro);
            if (datosCodigo.Length > 255)
            {
                Array.Resize(ref datosCodigo, 255);
            }

            string negocioFormateado = TruncarTextoSeguro(negocio, MaxAnchoLineaEtiqueta);
            string productoFormateado = FormatearNombreProductoEtiqueta(nombreProducto, MaxAnchoLineaEtiqueta, MaxCaracteresProducto);

            int totalCopias = Math.Clamp(copias, 1, 100);
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            for (int i = 0; i < totalCopias; i++)
            {
                // Inicializar impresora (ESC @) y seleccionar tabla de caracteres PC850 Multilingue (ESC t 2)
                bw.Write(new byte[] { 0x1B, 0x40 });
                bw.Write(new byte[] { 0x1B, 0x74, 0x02 });
                bw.Write(new byte[] { 0x1B, 0x61, 0x01 }); // Centrado

                if (!string.IsNullOrWhiteSpace(negocioFormateado))
                {
                    bw.Write(new byte[] { 0x1B, 0x45, 0x00 });
                    bw.Write(CodificarTextoTermicoCp850(negocioFormateado + "\n"));
                }

                bw.Write(new byte[] { 0x1B, 0x45, 0x01 }); // Negrita ON
                bw.Write(CodificarTextoTermicoCp850(productoFormateado + "\n\n"));
                bw.Write(new byte[] { 0x1B, 0x45, 0x00 }); // Negrita OFF

                // Configuracion del codigo de barras CODE128
                bw.Write(new byte[] { 0x1D, 0x68, 60 });   // Altura = 60 puntos
                bw.Write(new byte[] { 0x1D, 0x77, 2 });    // Ancho modulo = 2
                bw.Write(new byte[] { 0x1D, 0x48, 0x02 }); // HRI debajo del codigo
                bw.Write(new byte[] { 0x1D, 0x66, 0x00 }); // Fuente A para HRI
                bw.Write(new byte[] { 0x1D, 0x6B, 73, (byte)datosCodigo.Length });
                bw.Write(datosCodigo);
                bw.Write(CodificarTextoTermicoCp850("\n"));

                // Precio en doble alto y ancho
                bw.Write(new byte[] { 0x1B, 0x21, 0x30 });
                bw.Write(new byte[] { 0x1B, 0x45, 0x01 });
                bw.Write(CodificarTextoTermicoCp850($"\n${precio:0.00}\n"));
                bw.Write(new byte[] { 0x1B, 0x21, 0x00 });
                bw.Write(new byte[] { 0x1B, 0x45, 0x00 });

                // Avance y corte parcial
                bw.Write(new byte[] { 0x1B, 0x64, 0x03 });
                bw.Write(new byte[] { 0x1D, 0x56, 0x42, 0x00 });
            }

            return ms.ToArray();
        }

        private static string LimpiarCodigoBarrasAscii(string? codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return string.Empty;
            var sb = new StringBuilder(codigo.Length);
            foreach (char c in codigo.Trim())
            {
                if (c >= 0x20 && c <= 0x7E)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim();
        }

        private static string LimpiarCaracteresControl(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            var sb = new StringBuilder(texto.Length);
            bool ultimoFueEspacio = false;
            foreach (char c in texto.Trim())
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c))
                {
                    if (!ultimoFueEspacio)
                    {
                        sb.Append(' ');
                        ultimoFueEspacio = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    ultimoFueEspacio = false;
                }
            }
            return sb.ToString().Trim();
        }

        public static string TruncarTextoSeguro(string? texto, int maxCaracteres)
        {
            string limpio = LimpiarCaracteresControl(texto);
            if (string.IsNullOrEmpty(limpio) || maxCaracteres <= 0) return string.Empty;
            if (limpio.Length <= maxCaracteres) return limpio;
            if (maxCaracteres <= 3) return limpio.Substring(0, maxCaracteres);
            return limpio.Substring(0, maxCaracteres - 3).TrimEnd() + "...";
        }

        private static string FormatearNombreProductoEtiqueta(string? nombreProducto, int anchoLinea, int maxTotal)
        {
            string limpio = TruncarTextoSeguro(nombreProducto, maxTotal);
            if (string.IsNullOrEmpty(limpio)) return "PRODUCTO";
            if (limpio.Length <= anchoLinea) return limpio;

            int corte = limpio.LastIndexOf(' ', anchoLinea);
            if (corte <= 0)
            {
                corte = anchoLinea;
            }

            string linea1 = limpio.Substring(0, corte).Trim();
            string linea2 = limpio.Substring(corte).Trim();
            if (linea2.Length > anchoLinea)
            {
                linea2 = TruncarTextoSeguro(linea2, anchoLinea);
            }

            return string.IsNullOrEmpty(linea2) ? linea1 : $"{linea1}\n{linea2}";
        }

        public static byte[] CodificarTextoTermicoCp850(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return Array.Empty<byte>();

            try
            {
                var cp850 = Encoding.GetEncoding(850);
                return cp850.GetBytes(texto);
            }
            catch
            {
                // Fallback manual determinista a CP850/CP437 para espanol si el proveedor de paginas de codigos no esta activo
                var bytes = new byte[texto.Length];
                for (int i = 0; i < texto.Length; i++)
                {
                    char c = texto[i];
                    bytes[i] = c switch
                    {
                        'á' => 0xA0,
                        'í' => 0xA1,
                        'ó' => 0xA2,
                        'ú' => 0xA3,
                        'ñ' => 0xA4,
                        'Ñ' => 0xA5,
                        'ª' => 0xA6,
                        'º' => 0xA7,
                        '¿' => 0xA8,
                        '¡' => 0xAD,
                        'Á' => 0xB5,
                        'Â' => 0xB6,
                        'À' => 0xB7,
                        'é' => 0x82,
                        'É' => 0x90,
                        'ü' => 0x81,
                        'Ü' => 0x9A,
                        'Í' => 0xD6,
                        'Ó' => 0xE0,
                        'Ú' => 0xE9,
                        <= '\x7F' => (byte)c,
                        _ => (byte)'?'
                    };
                }
                return bytes;
            }
        }
    }
}
