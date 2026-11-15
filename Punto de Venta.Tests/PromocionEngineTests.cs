using Punto_de_Venta.Datos;
using Punto_de_Venta.Modelos;
using Xunit;

namespace Punto_de_Venta.Tests
{
    public class PromocionEngineTests
    {
        [Fact]
        public void Evaluar_CarritoVacioOSinPromociones_RetornaCeroDescuento()
        {
            var res = PromocionEngine.Evaluar([], []);

            Assert.Equal(0, res.PromocionesAplicadasCount);
            Assert.Equal(0m, res.DescuentoTotal);
            Assert.Empty(res.MensajesPromocion);
        }

        [Fact]
        public void Evaluar_ComboVigente_AplicaDescuentoYConsumeUnidades()
        {
            var carrito = new List<ItemCarrito>
            {
                new() { ProductoId = 1, Nombre = "Refresco", Precio = 20m, Cantidad = 2 },
                new() { ProductoId = 2, Nombre = "Papas", Precio = 18m, Cantidad = 1 }
            };
            var promos = new List<Promocion>
            {
                new()
                {
                    Id = 1,
                    Nombre = "Combo Botana",
                    Tipo = TipoPromocion.Combo,
                    Activa = true,
                    ProductoIdPrincipal = 1,
                    ProductoIdSecundario = 2,
                    PrecioComboTotal = 30m
                }
            };

            var res = PromocionEngine.Evaluar(carrito, promos);

            Assert.Equal(1, res.PromocionesAplicadasCount);
            Assert.Equal(8m, res.DescuentoTotal); // (20 + 18) - 30 = 8
        }

        [Fact]
        public void Evaluar_Mayoreo_AplicaCuandoCumpleCantidadMinima()
        {
            var carrito = new List<ItemCarrito>
            {
                new() { ProductoId = 10, Nombre = "Jugo", Precio = 15m, Cantidad = 6 }
            };
            var promos = new List<Promocion>
            {
                new()
                {
                    Id = 2,
                    Nombre = "Mayoreo 6+",
                    Tipo = TipoPromocion.Mayoreo,
                    Activa = true,
                    ProductoIdPrincipal = 10,
                    CantidadMinima = 6,
                    PrecioEspecial = 12m
                }
            };

            var res = PromocionEngine.Evaluar(carrito, promos);

            Assert.Equal(1, res.PromocionesAplicadasCount);
            Assert.Equal(18m, res.DescuentoTotal); // (15 - 12) * 6 = 18
        }

        [Fact]
        public void Evaluar_Volumen_AplicaPorPaquetesCompletos()
        {
            var carrito = new List<ItemCarrito>
            {
                new() { ProductoId = 5, Nombre = "Galletas", Precio = 10m, Cantidad = 7 }
            };
            var promos = new List<Promocion>
            {
                new()
                {
                    Id = 3,
                    Nombre = "3x25",
                    Tipo = TipoPromocion.Volumen,
                    Activa = true,
                    ProductoIdPrincipal = 5,
                    CantidadMinima = 3,
                    PrecioEspecial = 25m
                }
            };

            var res = PromocionEngine.Evaluar(carrito, promos);

            // 7 unidades = 2 paquetes de 3 (ahorro 5 c/u = 10), 1 unidad suelta sin descuento
            Assert.Equal(1, res.PromocionesAplicadasCount);
            Assert.Equal(10m, res.DescuentoTotal);
        }

        [Fact]
        public void Evaluar_PorcentajeSegundo_AplicaDescuentoPorPares()
        {
            var carrito = new List<ItemCarrito>
            {
                new() { ProductoId = 8, Nombre = "Chocolate", Precio = 20m, Cantidad = 3 }
            };
            var promos = new List<Promocion>
            {
                new()
                {
                    Id = 4,
                    Nombre = "2do al 50%",
                    Tipo = TipoPromocion.PorcentajeSegundo,
                    Activa = true,
                    ProductoIdPrincipal = 8,
                    PorcentajeDescuento = 0.50m
                }
            };

            var res = PromocionEngine.Evaluar(carrito, promos);

            // 3 unidades = 1 par, descuento 20 * 0.50 = 10
            Assert.Equal(1, res.PromocionesAplicadasCount);
            Assert.Equal(10m, res.DescuentoTotal);
        }

        [Fact]
        public void Evaluar_PromocionInactivaOVencida_NoSeAplica()
        {
            var carrito = new List<ItemCarrito>
            {
                new() { ProductoId = 10, Nombre = "Jugo", Precio = 15m, Cantidad = 10 }
            };
            var promos = new List<Promocion>
            {
                new()
                {
                    Id = 5,
                    Nombre = "Inactiva",
                    Tipo = TipoPromocion.Mayoreo,
                    Activa = false,
                    ProductoIdPrincipal = 10,
                    CantidadMinima = 2,
                    PrecioEspecial = 10m
                },
                new()
                {
                    Id = 6,
                    Nombre = "Vencida",
                    Tipo = TipoPromocion.Mayoreo,
                    Activa = true,
                    FechaFin = DateTime.Today.AddDays(-1),
                    ProductoIdPrincipal = 10,
                    CantidadMinima = 2,
                    PrecioEspecial = 10m
                }
            };

            var res = PromocionEngine.Evaluar(carrito, promos);

            Assert.Equal(0, res.PromocionesAplicadasCount);
            Assert.Equal(0m, res.DescuentoTotal);
        }

        [Fact]
        public void Evaluar_VariasPromocionesMismoProducto_EligeLaDeMayorAhorro()
        {
            var carrito = new List<ItemCarrito>
            {
                new() { ProductoId = 20, Nombre = "Leche", Precio = 25m, Cantidad = 4 }
            };
            var promos = new List<Promocion>
            {
                new()
                {
                    Id = 7,
                    Nombre = "Mayoreo leve",
                    Tipo = TipoPromocion.Mayoreo,
                    Activa = true,
                    ProductoIdPrincipal = 20,
                    CantidadMinima = 4,
                    PrecioEspecial = 23m // ahorro = 8
                },
                new()
                {
                    Id = 8,
                    Nombre = "2do al 50%",
                    Tipo = TipoPromocion.PorcentajeSegundo,
                    Activa = true,
                    ProductoIdPrincipal = 20,
                    PorcentajeDescuento = 0.50m // 2 pares * 12.50 = 25
                }
            };

            var res = PromocionEngine.Evaluar(carrito, promos);

            Assert.Equal(1, res.PromocionesAplicadasCount);
            Assert.Equal(25m, res.DescuentoTotal);
        }
    }
}

