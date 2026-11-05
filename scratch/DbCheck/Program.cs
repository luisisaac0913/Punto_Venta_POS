using System;
using System.IO;
using Microsoft.Data.Sqlite;

string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
string dbPath = Path.Combine(appData, "POS_DataStorage", "pos_data.db");

if (!File.Exists(dbPath))
{
    Console.WriteLine($"Database not found at {dbPath}");
    return;
}

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

Console.WriteLine("=== PROMOCIONES REGISTRADAS ===");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT * FROM Promociones";
    using var r = cmd.ExecuteReader();
    int count = 0;
    while (r.Read())
    {
        count++;
        Console.WriteLine($"ID: {r["Id"]} | Nombre: {r["Nombre"]} | Tipo: {r["Tipo"]} | Activa: {r["Activa"]} | P1: {r["ProductoIdPrincipal"]} | Cant: {r["CantidadMinima"]} | Esp: {r["PrecioEspecial"]} | P2: {r["ProductoIdSecundario"]} | Combo: {r["PrecioComboTotal"]} | Desc: {r["PorcentajeDescuento"]} | Inicio: {r["FechaInicio"]} | Fin: {r["FechaFin"]}");
    }
    if (count == 0) Console.WriteLine("(No hay promociones registradas en la base de datos)");
}

Console.WriteLine("\n=== PRODUCTOS REGISTRADOS ===");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT Id, Codigo, Nombre, Precio, Stock FROM Productos LIMIT 15";
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        Console.WriteLine($"Id: {r["Id"]} | Codigo: '{r["Codigo"]}' | Nombre: '{r["Nombre"]}' | Precio: {r["Precio"]}");
    }
}
