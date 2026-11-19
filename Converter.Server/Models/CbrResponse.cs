namespace Converter.Server.Models
{
    public class CbrResponse
    {
        public DateTime Date { get; set; }

        public Dictionary<string, CbrCurrency> Valute { get; set; } = new();
    }
}
