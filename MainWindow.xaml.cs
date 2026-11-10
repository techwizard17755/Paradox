using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Paradox;

/// <summary>Desktop shell for the Paradox quiz experience.</summary>
public partial class MainWindow : Window
{
    private const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    private Uri _appUri = DesktopConfigurationLoader.DefaultAppUri;
    private string? _configurationWarning;
    private string? _announcement;
    private bool _hasLoadedPage;

    /// <summary>Creates the app window for the live Paradox website.</summary>
    public MainWindow()
    {
        InitializeComponent();
        StatusText.Text = "Connecting to the live Paradox website…";
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Paradox",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
            Browser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
            Browser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            Browser.Source = _appUri;
            await LoadRemoteConfigurationAsync();
        }
        catch (Exception exception)
        {
            ShowBrowserStartupError(exception);
        }
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var destination) ||
            (destination.Scheme == Uri.UriSchemeHttps &&
             string.Equals(destination.Host, _appUri.Host, StringComparison.OrdinalIgnoreCase) &&
             destination.Port == _appUri.Port))
            return;

        e.Cancel = true;
    }

    private void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            StatusText.Text = $"Paradox could not load ({e.WebErrorStatus}). Check your Internet connection and refresh.";
            return;
        }

        _hasLoadedPage = true;
        UpdateConnectionStatus();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Checking for the latest Paradox updates…";
        await LoadRemoteConfigurationAsync();
        Browser.CoreWebView2.Reload();
    }

    private async Task LoadRemoteConfigurationAsync()
    {
        var result = await DesktopConfigurationLoader.LoadAsync();
        _appUri = result.Configuration.AppUri;
        _configurationWarning = result.Warning;
        _announcement = result.Configuration.Announcement;
        if (_hasLoadedPage)
            UpdateConnectionStatus();
        if (Browser.Source?.GetLeftPart(UriPartial.Path) != _appUri.GetLeftPart(UriPartial.Path))
            Browser.Source = _appUri;
    }

    private void UpdateConnectionStatus()
    {
        var notices = new[] { _configurationWarning, _announcement }
            .Where(notice => !string.IsNullOrWhiteSpace(notice));
        StatusText.Text = string.Join("  ·  ", notices);
        if (string.IsNullOrWhiteSpace(StatusText.Text))
            StatusText.Text = "Connected · App updates load from the live website";
    }

    private void CopyJoinLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_appUri.GetLeftPart(UriPartial.Path).TrimEnd('/'));
            StatusText.Text = "Invite link copied · Share it with the game PIN";
        }
        catch (System.Runtime.InteropServices.ExternalException exception)
        {
            MessageBox.Show(
                $"Windows couldn't copy the player link.\n\n{exception.Message}",
                "Couldn't copy link",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowBrowserStartupError(Exception exception)
    {
        var result = MessageBox.Show(
            $"Paradox needs the Microsoft Edge WebView2 Runtime to show its app window.\n\n{exception.Message}\n\nOpen Microsoft's official download page now?",
            "One-time setup needed",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(RuntimeDownloadUrl) { UseShellExecute = true });

        Application.Current.Shutdown(1);
    }
}
