using Microsoft.Data.Sqlite;
using Punto_de_Venta.Modelos;
using Punto_de_Venta.Servicios;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Punto_de_Venta.Datos
{
    public static class LibretaDB
    {
        public static List<NotaContacto> CargarTodos()
        {
            var lista = new List<NotaContacto>();
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Titulo, Telefono, Contenido, FechaModificacion, EsFijado FROM NotasContactos ORDER BY FechaModificacion DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                string titulo = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                string telefono = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                string contenido = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                string fechaStr = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                bool fijado = !reader.IsDBNull(5) && reader.GetInt32(5) == 1;

                if (!DateTime.TryParse(fechaStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
                {
                    fecha = DateTime.Now;
                }

                lista.Add(new NotaContacto
                {
                    Id = id,
                    Titulo = titulo,
                    Telefono = telefono,
                    Contenido = contenido,
                    FechaModificacion = fecha,
                    EsFijado = fijado
                });
            }

            const string defaultTituloEs = "Notas Rapidas y Contactos";
            const string defaultTituloEn = "Quick Notes and Contacts";
            string tituloBienvenida = IdiomaService.Obtener("NotebookWelcomeTitle", defaultTituloEs);
            string contenidoBienvenida = IdiomaService.Obtener(
                "NotebookWelcomeContent",
                "Bienvenido a tu libreta.\n\nEste espacio es libre para anotar lo que necesites para tu negocio:\n- Contactos de proveedores y repartidores\n- Pedidos especiales de clientes\n- Servicios de mantenimiento o emergencia\n- Recordatorios y notas del dia\n\nPuedes agregar nuevos registros con el boton '+ Nueva Nota' o buscar cualquier dato desde la barra superior.");

            if (lista.Count == 0)
            {
                var bienvenida = new NotaContacto
                {
                    Titulo = tituloBienvenida,
                    Telefono = "",
                    Contenido = contenidoBienvenida,
                    FechaModificacion = DateTime.Now,
                    EsFijado = true
                };
                Guardar(bienvenida);
                lista.Add(bienvenida);
            }
            else
            {
                foreach (var nota in lista)
                {
                    if ((nota.Titulo == defaultTituloEs || nota.Titulo == defaultTituloEn) &&
                        (nota.Contenido.StartsWith("Bienvenido a tu libreta.") || nota.Contenido.StartsWith("Welcome to your notebook.")))
                    {
                        nota.Titulo = tituloBienvenida;
                        nota.Contenido = contenidoBienvenida;
                    }
                }
            }

            return lista;
        }

        public static int Guardar(NotaContacto nota)
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            string fechaIso = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            nota.FechaModificacion = DateTime.Now;

            if (nota.Id <= 0)
            {
                cmd.CommandText = @"
                    INSERT INTO NotasContactos (Titulo, Telefono, Contenido, FechaModificacion, EsFijado)
                    VALUES (@titulo, @telefono, @contenido, @fecha, @fijado);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@titulo", nota.Titulo ?? string.Empty);
                cmd.Parameters.AddWithValue("@telefono", (object?)nota.Telefono ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@contenido", (object?)nota.Contenido ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fecha", fechaIso);
                cmd.Parameters.AddWithValue("@fijado", nota.EsFijado ? 1 : 0);

                object? result = cmd.ExecuteScalar();
                if (result != null && int.TryParse(result.ToString(), out int nuevoId))
                {
                    nota.Id = nuevoId;
                }
            }
            else
            {
                cmd.CommandText = @"
                    UPDATE NotasContactos
                    SET Titulo = @titulo,
                        Telefono = @telefono,
                        Contenido = @contenido,
                        FechaModificacion = @fecha,
                        EsFijado = @fijado
                    WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", nota.Id);
                cmd.Parameters.AddWithValue("@titulo", nota.Titulo ?? string.Empty);
                cmd.Parameters.AddWithValue("@telefono", (object?)nota.Telefono ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@contenido", (object?)nota.Contenido ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fecha", fechaIso);
                cmd.Parameters.AddWithValue("@fijado", nota.EsFijado ? 1 : 0);
                cmd.ExecuteNonQuery();
            }

            return nota.Id;
        }

        public static void Eliminar(int id)
        {
            if (id <= 0) return;
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM NotasContactos WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }
}

