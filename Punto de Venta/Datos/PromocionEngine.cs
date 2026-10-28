using Punto_de_Venta.Modelos;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Punto_de_Venta.Datos
{
    public class ResultadoPromocion
    {
        public int PromocionesAplicadasCount { get; set; }
        public decimal DescuentoTotal { get; set; }
        public List<string> MensajesPromocion { get; set; } = [];
    }

    public static class PromocionEngine
    {
        public static ResultadoPromocion Evaluar(IEnumerable<ItemCarrito> carrito, List<Promocion> promocionesActivas)
        {
            var resultado = new ResultadoPromocion();
            if (carrito == null || !carrito.Any() || promocionesActivas == null || promocionesActivas.Count == 0)
            {
                return resultado;
            }
            var promocionesVigentes = promocionesActivas.Where(p => p.EstaVigente()).ToList();
            foreach (var promo in promocionesVigentes)
            {
                switch (promo.Tipo)
                {
                    case TipoPromocion.Volumen:
                        var itemVolumen = carrito.FirstOrDefault(c => c.ProductoId == promo.ProductoIdPrincipal);
                        if (itemVolumen != null && itemVolumen.Cantidad >= promo.CantidadMinima && promo.CantidadMinima > 0)
                        {
                            int paquetes = (int)(itemVolumen.Cantidad / promo.CantidadMinima);
                            if (paquetes > 0)
                            {
                                decimal precioNormalPaquete = itemVolumen.Precio * promo.CantidadMinima;
                                decimal ahorroPorPaquete = precioNormalPaquete - promo.PrecioEspecial;

                                if (ahorroPorPaquete > 0)
                                {
                                    decimal ahorroTotal = ahorroPorPaquete * paquetes;
                                    resultado.DescuentoTotal += ahorroTotal;
                                    resultado.PromocionesAplicadasCount += paquetes;
                                    resultado.MensajesPromocion.Add($"{promo.Nombre} (-{ahorroTotal:C2})");
                                }
                            }
                        }
                        break;
                    case TipoPromocion.Combo:
                        var itemA = carrito.FirstOrDefault(c => c.ProductoId == promo.ProductoIdPrincipal);
                        var itemB = carrito.FirstOrDefault(c => c.ProductoId == promo.ProductoIdSecundario);

                        if (itemA != null && itemB != null)
                        {
                            int combosPosibles = (int)Math.Min(itemA.Cantidad, itemB.Cantidad);
                            if (combosPosibles > 0)
                            {
                                decimal precioNormalCombo = itemA.Precio + itemB.Precio;
                                decimal ahorroPorCombo = precioNormalCombo - promo.PrecioComboTotal;
                                if (ahorroPorCombo > 0)
                                {
                                    decimal ahorroTotal = ahorroPorCombo * combosPosibles;
                                    resultado.DescuentoTotal += ahorroTotal;
                                    resultado.PromocionesAplicadasCount += combosPosibles;
                                    resultado.MensajesPromocion.Add($"{promo.Nombre} (-{ahorroTotal:C2})");
                                }
                            }
                        }
                        break;
                    case TipoPromocion.PorcentajeSegundo:
                        var itemPar = carrito.FirstOrDefault(c => c.ProductoId == promo.ProductoIdPrincipal);
                        if (itemPar != null && itemPar.Cantidad >= 2)
                        {
                            int pares = (int)(itemPar.Cantidad / 2);
                            if (pares > 0)
                            {
                                decimal ahorroPorPar = itemPar.Precio * promo.PorcentajeDescuento;
                                decimal ahorroTotal = ahorroPorPar * pares;

                                resultado.DescuentoTotal += ahorroTotal;
                                resultado.PromocionesAplicadasCount += pares;
                                resultado.MensajesPromocion.Add($"{promo.Nombre} (-{ahorroTotal:C2})");
                            }
                        }
                        break;
                }
            }
            return resultado;
        }
    }
}