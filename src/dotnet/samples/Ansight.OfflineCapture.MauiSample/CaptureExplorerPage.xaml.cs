using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Ansight.OfflineCapture.MauiSample;

public partial class CaptureExplorerPage : ContentPage
{
    private readonly OfflineCaptureHarness captureHarness = OfflineCaptureHarness.Shared;

    public CaptureExplorerPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void HandleRefreshClicked(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    private async void HandleExportClicked(object? sender, EventArgs e)
    {
        try
        {
            await captureHarness.ExportAsync(password: null);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export", ex.Message, "OK");
        }
    }

    private async void HandleShareClicked(object? sender, EventArgs e)
    {
        var exportPath = captureHarness.LastExportPath;
        if (string.IsNullOrWhiteSpace(exportPath) || !File.Exists(exportPath))
        {
            await DisplayAlert("Share", "Export a ZIP first.", "OK");
            return;
        }

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Ansight offline capture",
            File = new ShareFile(exportPath)
        });
    }

    private async Task RefreshAsync()
    {
        var summary = await captureHarness.GetSummaryAsync();
        var files = await captureHarness.GetCurrentSessionFilesAsync();
        SummaryLabel.Text = summary.Session is null
            ? summary.CaptureRoot
            : $"{summary.Session.SessionId}  |  {files.Count} files  |  {FormatBytes(summary.TotalBytes)}";
        FilesCollectionView.ItemsSource = files;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes} B";
    }
}
