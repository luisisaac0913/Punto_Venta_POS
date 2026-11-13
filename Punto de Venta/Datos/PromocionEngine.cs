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
        private class EvaluacionItem
        {
            public decimal CantidadDisponible { get; set; }
            public ItemCarrito Item { get; set; } = null!;
        }

        private class CandidatoPromo
        {
            public Promocion Promo { get; set; } = null!;
            public decimal Ahorro { get; set; }
            public int CantidadAplicada { get; set; }
            public string Mensaje { get; set; } = string.Empty;
        }

        public static ResultadoPromocion Evaluar(IEnumerable<ItemCarrito> carrito, List<Promocion> promocionesActivas)
        {
            if (carrito == null || promocionesActivas == null || promocionesActivas.Count == 0)
            {
                return new ResultadoPromocion();
            }

            var combos = new List<Promocion>();
            var promosPorProducto = new Dictionary<int, List<Promocion>>();

            foreach (var promo in promocionesActivas)
            {
                if (!promo.EstaVigente()) continue;

                if (promo.Tipo == TipoPromocion.Combo)
                {
                    combos.Add(promo);
                }
                else
                {
                    if (!promosPorProducto.TryGetValue(promo.ProductoIdPrincipal, out var lista))
                    {
                        lista = [];
                        promosPorProducto[promo.ProductoIdPrincipal] = lista;
                    }
                    lista.Add(promo);
                }
            }

            return EvaluarIndexado(carrito, promosPorProducto, combos);
        }

        public static ResultadoPromocion EvaluarIndexado(
            IEnumerable<ItemCarrito> carrito,
            IReadOnlyDictionary<int, List<Promocion>> promosPorProducto,
            IReadOnlyList<Promocion> combos)
        {
            var resultado = new ResultadoPromocion();
            if (carrito == null || !carrito.Any())
            {
                return resultado;
            }

            bool hayCombos = combos != null && combos.Count > 0;
            bool hayIndividuales = promosPorProducto != null && promosPorProducto.Count > 0;
            if (!hayCombos && !hayIndividuales)
            {
                return resultado;
            }

            var estadoCarrito = carrito
                .GroupBy(c => c.ProductoId)
                .ToDictionary(
                    g => g.Key,
                    g => new EvaluacionItem { Item = g.First(), CantidadDisponible = g.Sum(x => x.Cantidad) }
                );

            if (hayCombos)
            {
                foreach (var promo in combos!)
                {
                    if (!promo.EstaVigente()) continue;

                    if (estadoCarrito.TryGetValue(promo.ProductoIdPrincipal, out var itemA) &&
                        estadoCarrito.TryGetValue(promo.ProductoIdSecundario, out var itemB) &&
                        itemA.CantidadDisponible > 0 && itemB.CantidadDisponible > 0)
                    {
                        int combosPosibles = (int)Math.Min(itemA.CantidadDisponible, itemB.CantidadDisponible);
                        if (combosPosibles > 0)
                        {
                            decimal precioNormalCombo = itemA.Item.Precio + itemB.Item.Precio;
                            decimal ahorroPorCombo = precioNormalCombo - promo.PrecioComboTotal;
                            if (ahorroPorCombo > 0)
                            {
                                decimal ahorroTotal = ahorroPorCombo * combosPosibles;
                                resultado.DescuentoTotal += ahorroTotal;
                                resultado.PromocionesAplicadasCount += 1;
                                resultado.MensajesPromocion.Add($"{promo.Nombre} (-{ahorroTotal:C2})");

                                itemA.CantidadDisponible -= combosPosibles;
                                itemB.CantidadDisponible -= combosPosibles;
                            }
                        }
                    }
                }
            }

            if (hayIndividuales)
            {
                foreach (var kvp in estadoCarrito)
                {
                    var estado = kvp.Value;
                    if (estado.CantidadDisponible <= 0) continue;

                    if (!promosPorProducto!.TryGetValue(kvp.Key, out var promosIndividuales) || promosIndividuales.Count == 0)
                    {
                        continue;
                    }

                    CandidatoPromo? mejorCandidato = null;
                    decimal subtotalDisponible = estado.Item.Precio * estado.CantidadDisponible;

                    foreach (var promo in promosIndividuales)
                    {
                        if (!promo.EstaVigente()) continue;

                        switch (promo.Tipo)
                        {
                            case TipoPromocion.Mayoreo:
                                if (promo.CantidadMinima > 0 && estado.CantidadDisponible >= promo.CantidadMinima)
                                {
                                    decimal ahorroPorUnidad = estado.Item.Precio - promo.PrecioEspecial;
                                    if (ahorroPorUnidad > 0)
                                    {
                                        decimal ahorroTotal = Math.Min(subtotalDisponible, ahorroPorUnidad * estado.CantidadDisponible);
                                        if (mejorCandidato == null || ahorroTotal > mejorCandidato.Ahorro)
                                        {
                                            mejorCandidato = new CandidatoPromo
                                            {
                                                Promo = promo,
                                                Ahorro = ahorroTotal,
                                                CantidadAplicada = 1,
                                                Mensaje = $"{promo.Nombre} (-{ahorroTotal:C2})"
                                            };
                                        }
                                    }
                                }
                                break;

                            case TipoPromocion.Volumen:
                                if (promo.CantidadMinima > 0 && estado.CantidadDisponible >= promo.CantidadMinima)
                                {
                                    int paquetes = (int)(estado.CantidadDisponible / promo.CantidadMinima);
                                    if (paquetes > 0)
                                    {
                                        decimal precioNormalPaquete = estado.Item.Precio * promo.CantidadMinima;
                                        decimal ahorroPorPaquete = precioNormalPaquete - promo.PrecioEspecial;
                                        if (ahorroPorPaquete > 0)
                                        {
                                            decimal ahorroTotal = Math.Min(subtotalDisponible, ahorroPorPaquete * paquetes);
                                            if (mejorCandidato == null || ahorroTotal > mejorCandidato.Ahorro)
                                            {
                                                mejorCandidato = new CandidatoPromo
                                                {
                                                    Promo = promo,
                                                    Ahorro = ahorroTotal,
                                                    CantidadAplicada = 1,
                                                    Mensaje = $"{promo.Nombre} (-{ahorroTotal:C2})"
                                                };
                                            }
                                        }
                                    }
                                }
                                break;

                            case TipoPromocion.PorcentajeSegundo:
                                if (estado.CantidadDisponible >= 2)
                                {
                                    int pares = (int)(estado.CantidadDisponible / 2);
                                    if (pares > 0)
                                    {
                                        decimal ahorroPorPar = estado.Item.Precio * promo.PorcentajeDescuento;
                                        decimal ahorroTotal = Math.Min(subtotalDisponible, ahorroPorPar * pares);
                                        if (ahorroTotal > 0 && (mejorCandidato == null || ahorroTotal > mejorCandidato.Ahorro))
                                        {
                                            mejorCandidato = new CandidatoPromo
                                            {
                                                Promo = promo,
                                                Ahorro = ahorroTotal,
                                                CantidadAplicada = 1,
                                                Mensaje = $"{promo.Nombre} (-{ahorroTotal:C2})"
                                            };
                                        }
                                    }
                                }
                                break;
                        }
                    }

                    if (mejorCandidato != null && mejorCandidato.Ahorro > 0)
                    {
                        resultado.DescuentoTotal += mejorCandidato.Ahorro;
                        resultado.PromocionesAplicadasCount += mejorCandidato.CantidadAplicada;
                        resultado.MensajesPromocion.Add(mejorCandidato.Mensaje);
                    }
                }
            }

            decimal totalBruto = carrito.Sum(c => c.Subtotal);
            if (resultado.DescuentoTotal > totalBruto)
            {
                resultado.DescuentoTotal = totalBruto;
            }

            return resultado;
        }
    }
}