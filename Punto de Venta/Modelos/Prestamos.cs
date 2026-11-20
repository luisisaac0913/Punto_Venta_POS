using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace Punto_de_Venta.Modelos
{
    public class Deudor : INotifyPropertyChanged
    {
        private string _nombre = string.Empty;
        private string _concepto = string.Empty;
        private decimal _montoOriginal;
        private decimal _totalAbonado;
        private decimal _limiteCredito;
        private bool _tieneInteres;
        private decimal _porcentajeInteres;
        private decimal _interesAcumulado;
        private string _tipoInteres = "PorcentajeSaldo";
        private string _frecuenciaInteres = "Unico";
        private DateTime _fechaInicioInteres = DateTime.Now;
        public double PorcentajeAbonado => TotalConInteres > 0
    ? (double)Math.Min(100m, Math.Round((TotalAbonado / TotalConInteres) * 100m, 1))
    : 100.0;
        public int Id { get; set; }
        public string Nombre
        {
            get => _nombre;
            set
            {
                if (_nombre != value)
                {
                    _nombre = value;
                    OnPropertyChanged();
                }
            }
        }
        public string Telefono { get; set; } = string.Empty;
        public string Concepto
        {
            get => _concepto;
            set
            {
                if (_concepto != value)
                {
                    _concepto = value;
                    OnPropertyChanged();
                }
            }
        }
        public decimal MontoOriginal
        {
            get => _montoOriginal;
            set
            {
                if (_montoOriginal != value)
                {
                    _montoOriginal = value;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public decimal TotalAbonado
        {
            get => _totalAbonado;
            set
            {
                if (_totalAbonado != value)
                {
                    _totalAbonado = value;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public bool TieneInteres
        {
            get => _tieneInteres;
            set
            {
                if (_tieneInteres != value)
                {
                    _tieneInteres = value;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public decimal PorcentajeInteres
        {
            get => _porcentajeInteres;
            set
            {
                if (_porcentajeInteres != value)
                {
                    _porcentajeInteres = value;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public decimal InteresAcumulado
        {
            get => _interesAcumulado;
            set
            {
                if (_interesAcumulado != value)
                {
                    _interesAcumulado = value;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public string TipoInteres
        {
            get => _tipoInteres;
            set
            {
                var normalizado = string.IsNullOrWhiteSpace(value) ? "PorcentajeSaldo" : value;
                if (_tipoInteres != normalizado)
                {
                    _tipoInteres = normalizado;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public string FrecuenciaInteres
        {
            get => _frecuenciaInteres;
            set
            {
                var normalizado = string.IsNullOrWhiteSpace(value) ? "Unico" : value;
                if (_frecuenciaInteres != normalizado)
                {
                    _frecuenciaInteres = normalizado;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public DateTime FechaInicioInteres
        {
            get => _fechaInicioInteres;
            set
            {
                if (_fechaInicioInteres != value)
                {
                    _fechaInicioInteres = value;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public int PeriodosInteresTranscurridos
        {
            get
            {
                if (!TieneInteres || PorcentajeInteres <= 0m) return 0;
                DateTime inicio = FechaInicioInteres == default ? DateTime.Now : FechaInicioInteres;
                DateTime ahora = DateTime.Now;

                if (string.Equals(FrecuenciaInteres, "Diario", StringComparison.OrdinalIgnoreCase))
                {
                    return Math.Max(0, (int)Math.Floor((ahora - inicio).TotalDays));
                }
                if (string.Equals(FrecuenciaInteres, "Semanal", StringComparison.OrdinalIgnoreCase))
                {
                    return Math.Max(0, (int)Math.Floor((ahora - inicio).TotalDays / 7.0));
                }
                if (string.Equals(FrecuenciaInteres, "Mensual", StringComparison.OrdinalIgnoreCase))
                {
                    int meses = ((ahora.Year - inicio.Year) * 12) + ahora.Month - inicio.Month;
                    if (ahora < inicio.AddMonths(meses)) meses--;
                    return Math.Max(0, meses);
                }
                return 1;
            }
        }
        public decimal InteresPorPeriodo
        {
            get
            {
                if (!TieneInteres || PorcentajeInteres <= 0m) return 0m;
                if (string.Equals(TipoInteres, "MontoFijo", StringComparison.OrdinalIgnoreCase))
                {
                    return Math.Round(PorcentajeInteres, 2);
                }
                if (string.Equals(TipoInteres, "PorcentajeOriginal", StringComparison.OrdinalIgnoreCase))
                {
                    return Math.Round(MontoOriginal * (PorcentajeInteres / 100m), 2);
                }
                decimal baseSaldo = Math.Max(0m, MontoOriginal - TotalAbonado);
                return Math.Round(baseSaldo * (PorcentajeInteres / 100m), 2);
            }
        }
        public decimal InteresCalculado => !TieneInteres
            ? 0m
            : Math.Round(InteresAcumulado + (InteresPorPeriodo * PeriodosInteresTranscurridos), 2);

        public void ConsolidarInteresYReiniciarPeriodo(DateTime? ahora = null)
        {
            if (TieneInteres && PorcentajeInteres > 0m)
            {
                InteresAcumulado = Math.Round(InteresAcumulado + (InteresPorPeriodo * PeriodosInteresTranscurridos), 2);
            }
            FechaInicioInteres = ahora ?? DateTime.Now;
        }
        public decimal TotalConInteres => MontoOriginal + InteresCalculado;
        public decimal SaldoRestante => Math.Max(0, TotalConInteres - TotalAbonado);
        public decimal SaldoPendiente
        {
            get => SaldoRestante;
            set => MontoOriginal = TotalAbonado + value;
        }
        public decimal LimiteCredito
        {
            get => _limiteCredito;
            set
            {
                var normalizado = Math.Max(0m, value);
                if (_limiteCredito != normalizado)
                {
                    _limiteCredito = normalizado;
                    OnPropertyChanged();
                    NotifyBalanceState();
                }
            }
        }
        public bool TieneLimiteCredito => LimiteCredito > 0m;
        public decimal CreditoDisponible => TieneLimiteCredito ? Math.Max(0m, LimiteCredito - SaldoRestante) : 0m;
        public bool ExcedeLimiteCredito => TieneLimiteCredito && SaldoRestante > LimiteCredito;
        public bool ExcederiaLimite(decimal nuevoCargo) => TieneLimiteCredito && (SaldoRestante + Math.Max(0m, nuevoCargo)) > LimiteCredito;
        public bool EstaSaldada => SaldoRestante <= 0;
        public bool EstaAbonada => TotalAbonado > 0 && SaldoRestante > 0;
        public bool SinAbonos => TotalAbonado == 0 && SaldoRestante > 0;

        public List<Abono> Abonos { get; set; } = [];
        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyBalanceState()
        {
            OnPropertyChanged(nameof(PeriodosInteresTranscurridos));
            OnPropertyChanged(nameof(InteresPorPeriodo));
            OnPropertyChanged(nameof(InteresCalculado));
            OnPropertyChanged(nameof(TotalConInteres));
            OnPropertyChanged(nameof(PorcentajeAbonado));
            OnPropertyChanged(nameof(SaldoRestante));
            OnPropertyChanged(nameof(SaldoPendiente));
            OnPropertyChanged(nameof(TieneLimiteCredito));
            OnPropertyChanged(nameof(CreditoDisponible));
            OnPropertyChanged(nameof(ExcedeLimiteCredito));
            OnPropertyChanged(nameof(EstaSaldada));
            OnPropertyChanged(nameof(EstaAbonada));
            OnPropertyChanged(nameof(SinAbonos));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    public class Abono
    {
        public int Id { get; set; }
        public int DeudorId { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;
        public decimal Monto { get; set; }                
    }
}
