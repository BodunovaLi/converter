using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Windows.Input;
using Converter.Services;
using Converter.Models;

namespace Converter
{
    class ConverterViewModel: INotifyPropertyChanged
    {
        public List<CbrCurrency> AvailableCurrencies { get; private set; } = new();
        private int _loadVersion;
        private readonly ICurrencyRateService _rateService;
        private readonly Dictionary<string, decimal> _rates = new();
        public event PropertyChangedEventHandler? PropertyChanged;
        public ICommand SelectFromCurrencyCommand { get; }
        public ICommand SelectToCurrencyCommand { get; }
        public ICommand SwapCurrenciesCommand { get; }
        public ICommand ToggleFromCurrencyListCommand { get; }
        public ICommand ToggleToCurrencyListCommand { get; }
        private bool _isRecalculating;
        private bool _isFromAmountSource = true;
        public string FromExtraCurrency { get; private set; } = "GBP";
        private DateTime _selectedDate = DateTime.Today;
        private string _fromCurrency = "RUB";
        private string _toCurrency = "USD";
        private string _fromAmount = "1";
        private string _toAmount = "";
        private DateTime? _actualRateDate;
        private string _errorMessage = "";

        public string ToExtraCurrency { get; private set; } = "GBP";

        public bool IsToExtraCurrencySelected
        {
            get
            {
                return ToCurrency == ToExtraCurrency;
            }
        }

        public bool IsFromExtraCurrencySelected
        {
            get
            {
                return FromCurrency == FromExtraCurrency;
            }
        }
        public string ErrorMessage
        {
            get
            {
                return _errorMessage;
            }
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }

        public DateTime? ActualRateDate
        {
            get
            {
                return _actualRateDate;
            }
            set
            {
                if (_actualRateDate != value)
                {
                    _actualRateDate = value;
                    OnPropertyChanged(nameof(ActualRateDate));
                }
            }
        }
        public DateTime SelectedDate { 
            get 
            {
                return _selectedDate;
            }

            set {
                if (_selectedDate != value)
                {
                    _selectedDate = value;
                    OnPropertyChanged(nameof(SelectedDate));
                    
                }
            } 
        }
        private CurrencyListSide _openedCurrencyList = CurrencyListSide.None;

        public CurrencyListSide OpenedCurrencyList
        {
            get
            {
                return _openedCurrencyList;
            }
            set
            {
                if (_openedCurrencyList != value)
                {
                    _openedCurrencyList = value;
                    OnPropertyChanged(nameof(OpenedCurrencyList));
                    OnPropertyChanged(nameof(IsFromCurrencyListVisible));
                    OnPropertyChanged(nameof(IsToCurrencyListVisible));
                }
            }
        }

        public string FromCurrency { 
            get 
            {
                return _fromCurrency;
            } 
            set 
            {
                if (_fromCurrency != value) 
                {
                    _fromCurrency = value;

                    if (value != "RUB" && value != "USD" && value != "EUR")
                    {
                        FromExtraCurrency = value;
                        OnPropertyChanged(nameof(FromExtraCurrency));
                    }

                    OnPropertyChanged(nameof(FromCurrency));
                    OnPropertyChanged(nameof(IsFromExtraCurrencySelected));
                    Recalculate();
                }
            } 
        }
        public string ToCurrency
        {
            get
            {
                return _toCurrency;
            }
            set
            {
                if (_toCurrency != value)
                {
                    _toCurrency = value;

                    if (value != "RUB" && value != "USD" && value != "EUR")
                    {
                        ToExtraCurrency = value;
                        OnPropertyChanged(nameof(ToExtraCurrency));
                    }

                    OnPropertyChanged(nameof(ToCurrency));
                    OnPropertyChanged(nameof(IsToExtraCurrencySelected));

                    Recalculate();
                }
            }
        }
        public string FromAmount
        {
            get
            {
                return _fromAmount;
            }
            set
            {
                if (!_isRecalculating)
                {
                    _isFromAmountSource = true;
                }

                if (_fromAmount != value)
                {
                    _fromAmount = value;
                    OnPropertyChanged(nameof(FromAmount));
                    RecalculateFromAmount();
                }
            }
        }
        public string ToAmount
        {
            get
            {
                return _toAmount;
            }
            set
            {
                if (!_isRecalculating)
                {
                    _isFromAmountSource = false;
                }

                if (_toAmount != value)
                {
                    _toAmount = value;
                    OnPropertyChanged(nameof(ToAmount));
                    RecalculateToAmount();
                }
            }
        }

        public bool IsFromCurrencyListVisible
        {
            get
            {
                return OpenedCurrencyList == CurrencyListSide.From;
            }
        }

        public bool IsToCurrencyListVisible
        {
            get
            {
                return OpenedCurrencyList == CurrencyListSide.To;
            }
        }

        public ConverterViewModel(ICurrencyRateService rateService)
        {
            _rateService = rateService;

            SelectFromCurrencyCommand = new Command<string>(currency =>
            {
                FromCurrency = currency;
                OpenedCurrencyList = CurrencyListSide.None;
            });

            SelectToCurrencyCommand = new Command<string>(currency =>
            {
                ToCurrency = currency;
                OpenedCurrencyList = CurrencyListSide.None;
            });

            ToggleFromCurrencyListCommand = new Command(() =>
            {
                if (OpenedCurrencyList == CurrencyListSide.From)
                {
                    OpenedCurrencyList = CurrencyListSide.None;
                }
                else
                {
                    OpenedCurrencyList = CurrencyListSide.From;
                }
            });

            ToggleToCurrencyListCommand = new Command(() =>
            {
                if (OpenedCurrencyList == CurrencyListSide.To)
                {
                    OpenedCurrencyList = CurrencyListSide.None;
                }
                else
                {
                    OpenedCurrencyList = CurrencyListSide.To;
                }
            });

            SwapCurrenciesCommand = new Command(SwapCurrencies);


        }


        private void OnPropertyChanged(string propertyName) { 
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

        private void SwapCurrencies()
        {
            _isRecalculating = true;

            try
            {
                string temp = ToCurrency;
                ToCurrency = FromCurrency;
                FromCurrency = temp;
            }
            finally
            {
                _isRecalculating = false;
            }

            Recalculate();

        }

        private decimal GetAmount(decimal amount, string fromCurrency, string toCurrency)
        {
            return amount * _rates[fromCurrency] / _rates[toCurrency];
        }

        private void RecalculateFromAmount()
        {


            if (_isRecalculating)
            {
                return;
            }

            _isRecalculating = true;

            try
            {
                if (ErrorMessage == "Сумма слишком большая для пересчёта." ||
                    ErrorMessage == "Для выбранной пары валют нет курсов.")
                {
                    ErrorMessage = "";
                }

                if (!_rates.ContainsKey(FromCurrency) ||
                    !_rates.ContainsKey(ToCurrency))
                {
                    ToAmount = "";
                    ErrorMessage = "Для выбранной пары валют нет курсов.";
                    return;
                }

                if (!decimal.TryParse(FromAmount, out decimal amount))
                {
                    ToAmount = "";
                    return;
                }

                decimal result = GetAmount(amount, FromCurrency, ToCurrency);
                ToAmount = result.ToString();
            }
            catch (OverflowException)
            {
                ToAmount = "";
                ErrorMessage = "Сумма слишком большая для пересчёта.";
            }


            finally 
            { 
                _isRecalculating = false;
            }
        }

        private void RecalculateToAmount()
        {

            if (_isRecalculating)
            {
                return;
            }

            _isRecalculating = true;

            try
            {
                if (ErrorMessage == "Сумма слишком большая для пересчёта." ||
                    ErrorMessage == "Для выбранной пары валют нет курсов.")
                {
                    ErrorMessage = "";
                }

                if (!_rates.ContainsKey(FromCurrency) ||
                    !_rates.ContainsKey(ToCurrency))
                {
                    FromAmount = "";
                    ErrorMessage = "Для выбранной пары валют нет курсов.";
                    return;
                }

                if (!decimal.TryParse(ToAmount, out decimal amount))
                {
                    FromAmount = "";
                    return;
                }

                decimal result = GetAmount(amount, ToCurrency, FromCurrency);
                FromAmount = result.ToString();
            }
            catch (OverflowException)
            {
                FromAmount = "";
                ErrorMessage = "Сумма слишком большая для пересчёта.";
            }

            finally
            {
                _isRecalculating = false;
            }

        }

        private void Recalculate()
        {
            if (_isFromAmountSource)
            {
                RecalculateFromAmount();
            }
            else
            {
                RecalculateToAmount();
            }
        }

        public async Task LoadRatesAsync()
        {
            int currentVersion = ++_loadVersion;
            DateTime requestedDate = SelectedDate;

            ErrorMessage = "";

            try
            {
                var result = await _rateService.GetRatesAsync(requestedDate);

                if (currentVersion != _loadVersion)
                {
                    return;
                }

                _rates.Clear();
                _rates["RUB"] = 1m;

                AvailableCurrencies = new List<CbrCurrency>();

                AvailableCurrencies.Add(new CbrCurrency
                {
                    CharCode = "RUB",
                    Name = "Российский рубль",
                    Nominal = 1,
                    Value = 1m
                });

                foreach (var currency in result.Valute.Values)
                {
                    _rates[currency.CharCode] = currency.Value / currency.Nominal;
                    AvailableCurrencies.Add(currency);
                }

                OnPropertyChanged(nameof(AvailableCurrencies));

                ActualRateDate = result.Date;

                Recalculate();
            }
            catch (Exception)
            {
                if (currentVersion == _loadVersion)
                {
                    ErrorMessage = "Не удалось загрузить курсы. Проверьте подключение к серверу.";
                }
            }

        }
    }
}
