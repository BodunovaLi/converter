namespace Converter.Models
{
    public class CbrCurrency
    {
        public string CharCode { get; set; } = "";
        public string Name { get; set; } = "";
        public int Nominal { get; set; }
        public decimal Value { get; set; }
    }
}
