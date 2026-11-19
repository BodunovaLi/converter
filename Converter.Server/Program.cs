using Converter.Server.Models;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapGet("/rates", async (
    DateTime? date,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache) =>
{
    DateTime requestedDate = (date ?? DateTime.Today).Date;

    var client = httpClientFactory.CreateClient();

    string cacheKey = $"rates:{requestedDate:yyyy-MM-dd}";

    CbrResponse? cachedData = cache.Get<CbrResponse>(cacheKey);

    if (cachedData is not null)
    {
        return Results.Ok(cachedData);
    }

    try
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            string url = "https://www.cbr-xml-daily.ru/archive/" +
                 $"{requestedDate.Year}/{requestedDate.Month:00}/{requestedDate.Day:00}/daily_json.js";

            using var response = await client.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                requestedDate = requestedDate.AddDays(-1);

                await Task.Delay(2100);

                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return Results.Problem(
                    "Источник курсов вернул ошибку.",
                    statusCode: 502);
            }

            string json = await response.Content.ReadAsStringAsync();

            CbrResponse? data = JsonSerializer.Deserialize<CbrResponse>(json);

            if (data is null ||
                data.Date == default ||
                data.Valute is null ||
                data.Valute.Count == 0)
            {
                return Results.Problem(
                    "Источник вернул пустые или неполные данные курсов.",
                    statusCode: 502);
            }

            foreach (var currency in data.Valute.Values)
            {
                if (currency is null ||
                    string.IsNullOrWhiteSpace(currency.CharCode) ||
                    string.IsNullOrWhiteSpace(currency.Name) ||
                    currency.Nominal <= 0 ||
                    currency.Value <= 0)
                {
                    return Results.Problem(
                        "Источник вернул некорректные данные валюты.",
                        statusCode: 502);
                }
            }

            cache.Set(cacheKey, data, TimeSpan.FromMinutes(10));

            return Results.Ok(data);
        }

        return Results.NotFound("Курсы не найдены за проверенный период.");
    }
    catch (HttpRequestException)
    {
        return Results.Problem(
            "Не удалось подключиться к источнику курсов.",
            statusCode: 502);
    }
    catch (TaskCanceledException)
    {
        return Results.Problem(
            "Источник курсов не ответил вовремя. Попробуйте позже.",
            statusCode: 504);
    }
    catch (JsonException)
    {
        return Results.Problem(
            "Источник вернул некорректные данные курсов.",
            statusCode: 502);
    }

});

app.Run();
