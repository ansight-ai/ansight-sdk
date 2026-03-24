namespace Ansight.TestHarness;

public partial class NavigationTestPage : ContentPage
{
    public NavigationTestPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Runtime.ScreenViewed(nameof(NavigationTestPage));
    }

    private async void OnPopClicked(object? sender, EventArgs e)
    {
        if (Navigation == null)
        {
            return;
        }

        Runtime.Event("Pop NavigationTestPage", CustomAnsightConfiguration.CustomEventChannelId);
        await Navigation.PopAsync();
    }
}
