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
            var resultado = new ResultadoPromocion();
            if (carrito == null || !carrito.Any() || promocionesActivas == null || promocionesActivas.Count == 0)
            {
                return resultado;
            }

            var promocionesVigentes = promocionesActivas.Where(p => p.EstaVigente()).ToList();
            if (promocionesVigentes.Count == 0)
            {
                return resultado;
            }

            var estadoCarrito = carrito
                .GroupBy(c => c.ProductoId)
                .ToDictionary(
                    g => g.Key,
                    g => new EvaluacionItem { Item = g.First(), CantidadDisponible = g.Sum(x => x.Cantidad) }
                );

            var combos = promocionesVigentes.Where(p => p.Tipo == TipoPromocion.Combo).ToList();
            foreach (var promo in combos)
            {
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

            foreach (var kvp in estadoCarrito)
            {
                var estado = kvp.Value;
                if (estado.CantidadDisponible <= 0) continue;

                var promosIndividuales = promocionesVigentes
                    .Where(p => p.ProductoIdPrincipal == kvp.Key && p.Tipo != TipoPromocion.Combo)
                    .ToList();

                if (promosIndividuales.Count == 0) continue;

                CandidatoPromo? mejorCandidato = null;

                foreach (var promo in promosIndividuales)
                {
                    switch (promo.Tipo)
                    {
                        case TipoPromocion.Mayoreo:
                            if (promo.CantidadMinima > 0 && estado.Item.Cantidad >= promo.CantidadMinima)
                            {
                                decimal ahorroPorUnidad = estado.Item.Precio - promo.PrecioEspecial;
                                if (ahorroPorUnidad > 0)
                                {
                                    decimal ahorroTotal = Math.Min(estado.Item.Subtotal, ahorroPorUnidad * estado.CantidadDisponible);
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
                                        decimal ahorroTotal = Math.Min(estado.Item.Subtotal, ahorroPorPaquete * paquetes);
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
                                    decimal ahorroTotal = Math.Min(estado.Item.Subtotal, ahorroPorPar * pares);
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

            decimal totalBruto = carrito.Sum(c => c.Subtotal);
            if (resultado.DescuentoTotal > totalBruto)
            {
                resultado.DescuentoTotal = totalBruto;
            }

            return resultado;
        }
    }
}