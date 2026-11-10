using System.Windows;

namespace Paradox;

public partial class App : Application
{
    /// <summary>Opens the desktop shell for the live Paradox website.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
