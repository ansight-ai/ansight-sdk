namespace Ansight.TestHarness;

public partial class NavigationTestPage : ContentPage
{
    public NavigationTestPage()
    {
        InitializeComponent();
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
