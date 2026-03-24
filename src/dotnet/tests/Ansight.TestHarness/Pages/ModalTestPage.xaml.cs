namespace Ansight.TestHarness;

public partial class ModalTestPage : ContentPage
{
    public ModalTestPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Runtime.ScreenViewed(nameof(ModalTestPage), "presentation=modal");
    }

    private async void OnDismissClicked(object? sender, EventArgs e)
    {
        if (Navigation == null)
        {
            return;
        }

        Runtime.Event("Pop ModalTestPage", CustomAnsightConfiguration.CustomEventChannelId);
        await Navigation.PopModalAsync();
    }
}
