using System.Globalization;
using Converter.Models;
using Converter.Services;
using FluentAssertions;

namespace Converter.Tests;

public class ConverterViewModelTests
{
    [Fact]
    public void Constructor_SetsDefaultsWithoutRequestingRates()
    {
        Task<CbrResponse> ReturnTestRates(DateTime requestedDate)
        {
            CbrResponse rates = CreateRates(requestedDate);
            return Task.FromResult(rates);
        }

        var service = new RateServiceStub(ReturnTestRates);

        var viewModel = new ConverterViewModel(service);

        viewModel.FromCurrency.Should().Be("RUB");
        viewModel.ToCurrency.Should().Be("USD");
        viewModel.FromAmount.Should().Be("1");
        viewModel.ToAmount.Should().BeEmpty();
        viewModel.ActualRateDate.Should().BeNull();
        viewModel.AvailableCurrencies.Should().BeEmpty();
        service.RequestedDates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("RUB", "USD", "1000", "10")]
    [InlineData("USD", "EUR", "100", "80")]
    [InlineData("GBP", "RUB", "2", "300")]
    [InlineData("AUD", "RUB", "100", "250")]
    [InlineData("RUB", "AUD", "250", "100")]
    [InlineData("USD", "EUR", "3", "2.4")]
    [InlineData("RUB", "USD", "1", "0.01")]
    [InlineData("EUR", "EUR", "17", "17")]
    [InlineData("EUR", "USD", "0", "0")]
    public async Task FromAmount_ConvertsCurrency(
        string fromCurrency, string toCurrency, string input, string expected)
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.FromCurrency = fromCurrency;
        viewModel.ToCurrency = toCurrency;

        viewModel.FromAmount = input;

        decimal actualAmount = ParseAmount(viewModel.ToAmount);
        decimal expectedAmount = decimal.Parse(expected, CultureInfo.InvariantCulture);

        actualAmount.Should().Be(expectedAmount);
    }

    [Theory]
    [InlineData("RUB", "USD", "10", 1000)]
    [InlineData("USD", "EUR", "80", 100)]
    [InlineData("AUD", "RUB", "250", 100)]
    [InlineData("EUR", "USD", "0", 0)]
    public async Task ToAmount_ConvertsInReverse(
        string fromCurrency, string toCurrency, string input, int expected)
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.FromCurrency = fromCurrency;
        viewModel.ToCurrency = toCurrency;

        viewModel.ToAmount = input;

        decimal actualAmount = ParseAmount(viewModel.FromAmount);

        actualAmount.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("   ")]
    [InlineData("-")]
    [InlineData("9999999999999999999999999999999999999999")]
    public async Task InvalidAmount_ClearsOtherSideAndValidInputRestoresConversion(string invalidAmount)
    {
        var fromViewModel = await CreateLoadedViewModelAsync();
        var toViewModel = await CreateLoadedViewModelAsync();

        fromViewModel.FromAmount = invalidAmount;
        toViewModel.ToAmount = invalidAmount;

        fromViewModel.ToAmount.Should().BeEmpty();
        toViewModel.FromAmount.Should().BeEmpty();

        fromViewModel.FromAmount = "1000";
        toViewModel.ToAmount = "10";

        ParseAmount(fromViewModel.ToAmount).Should().Be(10m);
        ParseAmount(toViewModel.FromAmount).Should().Be(1000m);
    }

    [Fact]
    public async Task ChangingCurrency_AfterLeftInput_KeepsLeftAmount()
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.FromAmount = "001000";

        viewModel.ToCurrency = "EUR";

        viewModel.FromAmount.Should().Be("001000");
        ParseAmount(viewModel.ToAmount).Should().Be(8m);
    }

    [Fact]
    public async Task ChangingCurrency_AfterRightInput_KeepsRightAmount()
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.ToAmount = "0010";

        viewModel.FromCurrency = "AUD";

        viewModel.ToAmount.Should().Be("0010");
        ParseAmount(viewModel.FromAmount).Should().Be(400m);
    }

    [Fact]
    public async Task SwapCurrencies_SwapsCodesAndRecalculatesFromEnteredAmount()
    {
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.FromAmount = "10";

        viewModel.SwapCurrenciesCommand.Execute(null);

        viewModel.FromCurrency.Should().Be("USD");
        viewModel.ToCurrency.Should().Be("RUB");
        viewModel.FromAmount.Should().Be("10");
        ParseAmount(viewModel.ToAmount).Should().Be(1000m);
    }

    [Fact]
    public void CurrencyListCommands_KeepAtMostOneListOpenAndCloseItOnSelection()
    {
        var viewModel = new ConverterViewModel(new RateServiceStub(date => Task.FromResult(CreateRates(date))));
        viewModel.IsFromCurrencyListVisible.Should().BeFalse();
        viewModel.IsToCurrencyListVisible.Should().BeFalse();

        viewModel.ToggleFromCurrencyListCommand.Execute(null);
        viewModel.IsFromCurrencyListVisible.Should().BeTrue();
        viewModel.IsToCurrencyListVisible.Should().BeFalse();

        viewModel.ToggleToCurrencyListCommand.Execute(null);
        viewModel.IsFromCurrencyListVisible.Should().BeFalse();
        viewModel.IsToCurrencyListVisible.Should().BeTrue();

        viewModel.SelectToCurrencyCommand.Execute("AUD");

        viewModel.ToCurrency.Should().Be("AUD");
        viewModel.ToExtraCurrency.Should().Be("AUD");
        viewModel.IsToExtraCurrencySelected.Should().BeTrue();
        viewModel.OpenedCurrencyList.Should().Be(CurrencyListSide.None);
    }

    [Fact]
    public async Task LoadRates_UsesSelectedDateAndPublishesActualRates()
    {
        var selectedDate = new DateTime(2026, 1, 11);
        var rateDate = selectedDate.AddDays(-2);
        var service = new RateServiceStub(_ => Task.FromResult(CreateRates(rateDate)));

        var viewModel = new ConverterViewModel(service) { SelectedDate = selectedDate };

        var changes = new List<string?>();

        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await viewModel.LoadRatesAsync();

        service.RequestedDates.Should().ContainSingle().Which.Should().Be(selectedDate);
        viewModel.SelectedDate.Should().Be(selectedDate);
        viewModel.ActualRateDate.Should().Be(rateDate);

        viewModel.AvailableCurrencies.Select(currency => currency.CharCode)
            .Should().BeEquivalentTo(new[] { "RUB", "USD", "EUR", "GBP", "AUD" });

        var ruble = viewModel.AvailableCurrencies.Single(currency => currency.CharCode == "RUB");

        ruble.Nominal.Should().Be(1);
        ruble.Value.Should().Be(1m);

        changes.Should().Contain(new[]
        {
            nameof(ConverterViewModel.AvailableCurrencies),
            nameof(ConverterViewModel.ActualRateDate),
            nameof(ConverterViewModel.ToAmount)
        });

        ParseAmount(viewModel.ToAmount).Should().Be(0.01m);
        viewModel.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadRates_UsesAmountEnteredBeforeFirstLoadCompletes()
    {
        var pending = new TaskCompletionSource<CbrResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new ConverterViewModel(new RateServiceStub(_ => pending.Task));

        var loading = viewModel.LoadRatesAsync();
        viewModel.FromAmount = "1000";
        pending.SetResult(CreateRates(viewModel.SelectedDate));
        await loading;

        viewModel.FromAmount.Should().Be("1000");
        ParseAmount(viewModel.ToAmount).Should().Be(10m);
    }

    [Fact]
    public async Task LoadRates_ShowsErrorAndCanRecoverOnRetry()
    {
        var service = new RateServiceStub(date =>
        {
            if (date.Day == 1)
            {
                var error = new HttpRequestException("Service unavailable");
                return Task.FromException<CbrResponse>(error);
            }

            CbrResponse rates = CreateRates(date);
            return Task.FromResult(rates);
        });

        var viewModel = new ConverterViewModel(service) { SelectedDate = new DateTime(2026, 1, 1) };

        await viewModel.LoadRatesAsync();

        viewModel.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        viewModel.ActualRateDate.Should().BeNull();

        viewModel.SelectedDate = viewModel.SelectedDate.AddDays(1);
        await viewModel.LoadRatesAsync();

        viewModel.ErrorMessage.Should().BeEmpty();
        viewModel.ActualRateDate.Should().Be(viewModel.SelectedDate);
        ParseAmount(viewModel.ToAmount).Should().Be(0.01m);
    }

    [Fact]
    public async Task ReloadRates_AfterRightInput_KeepsAmountAndReplacesCurrencies()
    {
        var service = new RateServiceStub(date =>
        {
            var response = CreateRates(date);
            if (date.Day == 2)
            {
                response.Valute["USD"].Value = 120m;
                response.Valute.Remove("AUD");
            }

            return Task.FromResult(response);
        });
        var viewModel = new ConverterViewModel(service) { SelectedDate = new DateTime(2026, 1, 1) };
        await viewModel.LoadRatesAsync();
        viewModel.ToAmount = "10";

        viewModel.SelectedDate = viewModel.SelectedDate.AddDays(1);
        await viewModel.LoadRatesAsync();

        viewModel.ToAmount.Should().Be("10");
        ParseAmount(viewModel.FromAmount).Should().Be(1200m);
        viewModel.AvailableCurrencies.Select(currency => currency.CharCode)
            .Should().BeEquivalentTo(new[] { "RUB", "USD", "EUR", "GBP" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadRates_IgnoresOlderRequestThatCompletesLast(bool olderRequestFails)
    {
        var olderDate = new DateTime(2026, 1, 5);
        var newerDate = olderDate.AddDays(1);
        var olderResponse = new TaskCompletionSource<CbrResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newerResponse = CreateRates(newerDate);
        newerResponse.Valute["USD"].Value = 50m;
        var service = new RateServiceStub(
            date => date == olderDate ? olderResponse.Task : Task.FromResult(newerResponse));
        var viewModel = new ConverterViewModel(service) { SelectedDate = olderDate, FromAmount = "1000" };
        var olderLoad = viewModel.LoadRatesAsync();

        viewModel.SelectedDate = newerDate;
        await viewModel.LoadRatesAsync();

        if (olderRequestFails)
        {
            olderResponse.SetException(new HttpRequestException("Service unavailable"));
        }
        else
        {
            olderResponse.SetResult(CreateRates(olderDate));
        }

        await olderLoad;

        service.RequestedDates.Should().Equal(olderDate, newerDate);
        viewModel.ActualRateDate.Should().Be(newerDate);
        viewModel.ErrorMessage.Should().BeEmpty();
        ParseAmount(viewModel.ToAmount).Should().Be(20m);
    }

    private static async Task<ConverterViewModel> CreateLoadedViewModelAsync()
    {
        Task<CbrResponse> ReturnTestRates(DateTime requestedDate)
        {
            CbrResponse rates = CreateRates(requestedDate);
            return Task.FromResult(rates);
        }

        var service = new RateServiceStub(ReturnTestRates);
        var viewModel = new ConverterViewModel(service);

        await viewModel.LoadRatesAsync();
        return viewModel;
    }

    private static decimal ParseAmount(string amount) => decimal.Parse(amount, CultureInfo.CurrentCulture);

    private static CbrResponse CreateRates(DateTime date) => new()
    {
        Date = date,
        Valute = new()
        {
            ["USD"] = new CbrCurrency { CharCode = "USD", Nominal = 1, Value = 100m },
            ["EUR"] = new CbrCurrency { CharCode = "EUR", Nominal = 1, Value = 125m },
            ["GBP"] = new CbrCurrency { CharCode = "GBP", Nominal = 1, Value = 150m },
            ["AUD"] = new CbrCurrency { CharCode = "AUD", Nominal = 100, Value = 250m }
        }
    };

    private sealed class RateServiceStub : ICurrencyRateService
    {
        private readonly Func<DateTime, Task<CbrResponse>> _getRates;

        public RateServiceStub(Func<DateTime, Task<CbrResponse>> getRates)
        {
            _getRates = getRates;
        }

        public List<DateTime> RequestedDates { get; } = new List<DateTime>();

        public Task<CbrResponse> GetRatesAsync(DateTime date)
        {
            RequestedDates.Add(date);
            return _getRates(date);
        }
    }
}
