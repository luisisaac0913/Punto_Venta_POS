using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Punto_de_Venta.Datos
{
    public static class SafeFileStore
    {
        #region Control de Concurrencia
        private static readonly object FileLock = new();
        #endregion

        #region Escritura Atómica Segura
        public static void WriteAllLinesAtomic(string destinationPath, IEnumerable<string> lines, Encoding? encoding = null)
        {
            encoding ??= new UTF8Encoding(false);

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = destinationPath + ".tmp";
            var backupPath = destinationPath + ".bak";

            lock (FileLock)
            {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, encoding))
                {
                    foreach (var line in lines)
                    {
                        writer.WriteLine(line);
                    }
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(destinationPath))
                {
                    try
                    {
                        File.Replace(tempPath, destinationPath, backupPath, ignoreMetadataErrors: true);
                        if (File.Exists(backupPath))
                        {
                            File.Delete(backupPath);
                        }
                    }
                    catch
                    {
                        File.Delete(destinationPath);
                        File.Move(tempPath, destinationPath);
                    }
                }
                else
                {
                    File.Move(tempPath, destinationPath);
                }
            }
        }
        #endregion
    }
}