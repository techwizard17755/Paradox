using System.Windows;

namespace Paradox;

public partial class App : Application
{
    private WebApplication? _server;

    /// <summary>Starts the local game server before opening the desktop window.</summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _server = await DesktopHost.StartAsync(e.Args);
            var window = new MainWindow(_server);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Paradox could not start its local game server.\n\n{exception.Message}",
                "Paradox could not start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Stops the local game server when the desktop window closes.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _server?.StopAsync().GetAwaiter().GetResult();
        _server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
