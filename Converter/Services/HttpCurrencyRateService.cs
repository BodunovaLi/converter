using Converter.Models;
using System.Net.Http.Json;

namespace Converter.Services
{
    public class HttpCurrencyRateService : ICurrencyRateService
    {
        private readonly HttpClient _client = new HttpClient();
        public async Task<CbrResponse> GetRatesAsync(DateTime date)
        {
            string url = "https://localhost:7009/rates" +
             $"?date={date.Year}-{date.Month:00}-{date.Day:00}";

            CbrResponse? result = await _client.GetFromJsonAsync<CbrResponse>(url);

            if (result is null)
            {
                throw new InvalidOperationException("Сервер не вернул курсы валют.");
            }

            return result;
        }
    }
}
