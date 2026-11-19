namespace Converter
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();

            BindingContext = new ConverterViewModel(new Converter.Services.HttpCurrencyRateService());
        }

        private async void MainPage_Loaded(object? sender, EventArgs e)
        {
            if (BindingContext is ConverterViewModel viewModel)
            {
                await viewModel.LoadRatesAsync();
            }
        }

        private async void RateDatePicker_DateSelected(object? sender, DateChangedEventArgs e)
        {
            if (BindingContext is ConverterViewModel viewModel)
            {
                await viewModel.LoadRatesAsync();
            }
        }
    }
}
