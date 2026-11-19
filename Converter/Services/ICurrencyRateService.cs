using Converter.Models;

namespace Converter.Services
{
    public interface ICurrencyRateService
    {
        Task<CbrResponse> GetRatesAsync(DateTime date);
    }
}
