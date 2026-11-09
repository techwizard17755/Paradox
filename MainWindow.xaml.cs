using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Paradox;

/// <summary>Desktop shell for the Paradox quiz experience.</summary>
public partial class MainWindow : Window
{
    private const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    private readonly WebApplication _server;
    private readonly string _localAppUrl;
    private readonly string _playerUrl;

    /// <summary>Creates the app window for a running local game server.</summary>
    public MainWindow(WebApplication server)
    {
        InitializeComponent();
        _server = server;
        var boundPort = new Uri(_server.Urls.Single()).Port;
        _localAppUrl = $"http://127.0.0.1:{boundPort}";
        _playerUrl = GetPlayerUrl(boundPort);
        StatusText.Text = $"Game room ready  ·  Players on your Wi-Fi: {_playerUrl}";
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
            Browser.Source = new Uri(_localAppUrl);
        }
        catch (Exception exception)
        {
            ShowBrowserStartupError(exception);
        }
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var destination) ||
            (destination.Scheme == Uri.UriSchemeHttp && destination.Host is "127.0.0.1" or "localhost"))
            return;

        e.Cancel = true;
    }

    private void CopyJoinLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_playerUrl);
            StatusText.Text = $"Player link copied  ·  {_playerUrl}  ·  Game PIN is shown on your host screen";
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

        _server.StopAsync().GetAwaiter().GetResult();
        Application.Current.Shutdown(1);
    }

    private static string GetPlayerUrl(int port)
    {
        var address = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                              network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .FirstOrDefault(candidate =>
                candidate.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(candidate));

        return address is null ? $"http://127.0.0.1:{port}" : $"http://{address}:{port}";
    }
}
