using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServerRateResponse = Converter.Server.Models.CbrResponse;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Converter.Tests;

public class RatesEndpointTests
{
    [Fact]
    public async Task GetRates_RequestsSelectedArchiveDateAndReturnsRates()
    {
        var actualDate = new DateTime(2026, 1, 8);
        var source = new ArchiveStub(_ => RatesResponse(actualDate, 100m));

        await using var server = CreateServer(source);
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/rates?date=2026-01-09");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rates = await response.Content.ReadFromJsonAsync<ServerRateResponse>();
        rates.Should().NotBeNull();
        rates!.Date.Should().Be(actualDate);
        rates.Valute.Should().ContainKey("USD").WhoseValue.Value.Should().Be(100m);
        source.Requests.Should().Equal("https://www.cbr-xml-daily.ru/archive/2026/01/09/daily_json.js");
    }

    [Fact]
    public async Task GetRates_CachesRepeatedDatesButFetchesOtherDates()
    {
        var firstDate = new DateTime(2026, 1, 9);
        var secondDate = firstDate.AddDays(1);
        var source = new ArchiveStub(uri => uri.AbsolutePath.Contains("/01/09/")
            ? RatesResponse(firstDate, 100m)
            : RatesResponse(secondDate, 120m));
        await using var server = CreateServer(source);
        using var client = server.CreateClient();

        var first = await client.GetFromJsonAsync<ServerRateResponse>("/rates?date=2026-01-09");
        var repeated = await client.GetFromJsonAsync<ServerRateResponse>("/rates?date=2026-01-09T12:30:00");
        var second = await client.GetFromJsonAsync<ServerRateResponse>("/rates?date=2026-01-10");

        first.Should().NotBeNull();
        repeated.Should().BeEquivalentTo(first);
        second.Should().NotBeNull();
        second!.Date.Should().Be(secondDate);
        second.Valute["USD"].Value.Should().Be(120m);
        source.Requests.Should().Equal(
            "https://www.cbr-xml-daily.ru/archive/2026/01/09/daily_json.js",
            "https://www.cbr-xml-daily.ru/archive/2026/01/10/daily_json.js");
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026-02-30")]
    public async Task GetRates_RejectsInvalidDatesWithoutContactingSource(string date)
    {
        var source = new ArchiveStub(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await using var server = CreateServer(source);
        using var client = server.CreateClient();

        using var response = await client.GetAsync($"/rates?date={date}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        source.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GetRates_WhenSourceFails_ReturnsBadGatewayWithoutCaching(HttpStatusCode upstreamStatus)
    {
        var source = new ArchiveStub(_ => new HttpResponseMessage(upstreamStatus));
        await using var server = CreateServer(source);
        using var client = server.CreateClient();

        using var first = await client.GetAsync("/rates?date=2026-01-09");
        using var repeated = await client.GetAsync("/rates?date=2026-01-09");

        first.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        repeated.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        source.Requests.Should().Equal(
            "https://www.cbr-xml-daily.ru/archive/2026/01/09/daily_json.js",
            "https://www.cbr-xml-daily.ru/archive/2026/01/09/daily_json.js");
    }

    [Theory]
    [InlineData(false, HttpStatusCode.BadGateway,
    "Не удалось подключиться к источнику курсов.")]
    [InlineData(true, HttpStatusCode.GatewayTimeout,
    "Источник курсов не ответил вовремя. Попробуйте позже.")]
    public async Task GetRates_WhenRequestThrows_ReturnsExpectedError(
    bool timeout,
    HttpStatusCode expectedStatus,
    string expectedMessage)
    {
        HttpResponseMessage FailRequest()
        {
            if (timeout)
            {
                throw new TaskCanceledException("Test timeout");
            }

            throw new HttpRequestException("Test connection failure");
        }

        await CheckFailureAndRecoveryAsync(
            FailRequest,
            expectedStatus,
            expectedMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    public async Task GetRates_WhenJsonIsInvalid_ReturnsBadGateway(string json)
    {
        await CheckFailureAndRecoveryAsync(
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    json,
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
            HttpStatusCode.BadGateway,
            "Источник вернул некорректные данные курсов.");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"Date":"2026-01-09"}""")]
    [InlineData("""{"Date":"2026-01-09","Valute":null}""")]
    [InlineData("""{"Date":"2026-01-09","Valute":{}}""")]
    [InlineData("""
    {
        "Valute": {
            "USD": {
                "CharCode": "USD",
                "Name": "Доллар США",
                "Nominal": 1,
                "Value": 100
            }
        }
    }
    """)]
    public async Task GetRates_WhenDataIsIncomplete_ReturnsBadGateway(string json)
    {
        await CheckFailureAndRecoveryAsync(
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    json,
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
            HttpStatusCode.BadGateway,
            "Источник вернул пустые или неполные данные курсов.");
    }

    [Theory]
    [InlineData(null, "Доллар США", 1, 100)]
    [InlineData("", "Доллар США", 1, 100)]
    [InlineData("   ", "Доллар США", 1, 100)]
    [InlineData("USD", null, 1, 100)]
    [InlineData("USD", "", 1, 100)]
    [InlineData("USD", "   ", 1, 100)]
    [InlineData("USD", "Доллар США", 0, 100)]
    [InlineData("USD", "Доллар США", -1, 100)]
    [InlineData("USD", "Доллар США", 1, 0)]
    [InlineData("USD", "Доллар США", 1, -100)]
    public async Task GetRates_WhenCurrencyIsInvalid_ReturnsBadGateway(
    string? code,
    string? name,
    int nominal,
    int value)
    {
        await CheckFailureAndRecoveryAsync(
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Date = new DateTime(2026, 1, 9),
                    Valute = new
                    {
                        USD = new
                        {
                            CharCode = code,
                            Name = name,
                            Nominal = nominal,
                            Value = value
                        }
                    }
                }, options: new JsonSerializerOptions())
            },
            HttpStatusCode.BadGateway,
            "Источник вернул некорректные данные валюты.");
    }

    [Fact]
    public async Task GetRates_WhenCurrencyIsNull_ReturnsBadGateway()
    {
        await CheckFailureAndRecoveryAsync(
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"Date":"2026-01-09","Valute":{"USD":null}}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
            HttpStatusCode.BadGateway,
            "Источник вернул некорректные данные валюты.");
    }

    private static async Task CheckFailureAndRecoveryAsync(
    Func<HttpResponseMessage> createFailure,
    HttpStatusCode expectedStatus,
    string expectedMessage)
    {
        int requestCount = 0;
        var rateDate = new DateTime(2026, 1, 9);

        var source = new ArchiveStub(_ =>
        {
            requestCount++;

            if (requestCount <= 2)
            {
                return createFailure();
            }

            return RatesResponse(rateDate, 100m);
        });

        await using var server = CreateServer(source);
        using var client = server.CreateClient();

        const string url = "/rates?date=2026-01-09";

        // Первые два запроса получают ошибку.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.GetAsync(url);

            response.StatusCode.Should().Be(expectedStatus);
            response.Content.Headers.ContentType?.MediaType
                .Should().Be("application/problem+json");

            var problem = await response.Content
                .ReadFromJsonAsync<ProblemDetails>();

            problem.Should().NotBeNull();
            problem!.Status.Should().Be((int)expectedStatus);
            problem.Detail.Should().Be(expectedMessage);
        }

        // Повторное обращение к источнику подтверждает,
        // что ошибка не попала в кэш.
        source.Requests.Should().HaveCount(2);

        // После восстановления источника запрос успешен.
        using var recovered = await client.GetAsync(url);

        recovered.StatusCode.Should().Be(HttpStatusCode.OK);

        var rates = await recovered.Content
            .ReadFromJsonAsync<ServerRateResponse>();

        rates.Should().NotBeNull();
        rates!.Date.Should().Be(rateDate);
        rates.Valute["USD"].Value.Should().Be(100m);

        // Успешный ответ уже должен браться из кэша.
        using var cached = await client.GetAsync(url);

        cached.StatusCode.Should().Be(HttpStatusCode.OK);

        var cachedRates = await cached.Content
            .ReadFromJsonAsync<ServerRateResponse>();

        cachedRates.Should().BeEquivalentTo(rates);
        source.Requests.Should().HaveCount(3);
    }

    private static WebApplicationFactory<ServerRateResponse> CreateServer(ArchiveStub source) =>
        new WebApplicationFactory<ServerRateResponse>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(source);
            }));

    private static HttpResponseMessage RatesResponse(DateTime date, decimal dollarRate) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            Date = date,
            Valute = new { USD = new { CharCode = "USD", Name = "US Dollar", Nominal = 1, Value = dollarRate } }
        }, options: new System.Text.Json.JsonSerializerOptions())
    };

    private sealed class ArchiveStub(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Requests { get; } = new();

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri.AbsoluteUri);
            return Task.FromResult(respond(uri));
        }
    }
}
