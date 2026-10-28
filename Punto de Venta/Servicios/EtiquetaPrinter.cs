using System;
using System.IO;
using System.Text;

namespace Punto_de_Venta.Servicios
{
    public static class EtiquetaPrinter
    {
        public static byte[] GenerarBytesEtiqueta(string negocio, string nombreProducto, string codigo, decimal precio, int copias = 1)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            var encoding = Encoding.Latin1;
            for (int i = 0; i < copias; i++)
            {
                bw.Write(new byte[] { 0x1B, 0x40 });
                bw.Write(new byte[] { 0x1B, 0x61, 0x01 });
                if (!string.IsNullOrWhiteSpace(negocio))
                {
                    bw.Write(new byte[] { 0x1B, 0x45, 0x00 }); 
                    bw.Write(encoding.GetBytes(negocio + "\n"));
                }
                bw.Write(new byte[] { 0x1B, 0x45, 0x01 }); 
                bw.Write(encoding.GetBytes(nombreProducto + "\n\n"));
                bw.Write(new byte[] { 0x1B, 0x45, 0x00 }); 
                bw.Write(new byte[] { 0x1D, 0x68, 60 });
                bw.Write(new byte[] { 0x1D, 0x77, 2 });
                bw.Write(new byte[] { 0x1D, 0x48, 0x02 });
                bw.Write(new byte[] { 0x1D, 0x66, 0x00 });
                byte[] datosCodigo = encoding.GetBytes("{B" + codigo);
                bw.Write(new byte[] { 0x1D, 0x6B, 73, (byte)datosCodigo.Length });
                bw.Write(datosCodigo);
                bw.Write(encoding.GetBytes("\n"));
                bw.Write(new byte[] { 0x1B, 0x21, 0x30 });
                bw.Write(new byte[] { 0x1B, 0x45, 0x01 });
                bw.Write(encoding.GetBytes($"\n${precio:0.00}\n"));
                bw.Write(new byte[] { 0x1B, 0x21, 0x00 });
                bw.Write(new byte[] { 0x1B, 0x45, 0x00 });
                bw.Write(new byte[] { 0x1B, 0x64, 0x03 });
                bw.Write(new byte[] { 0x1D, 0x56, 0x42, 0x00 });
            }
            return ms.ToArray();
        }
    }
}