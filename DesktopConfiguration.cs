using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Paradox;

internal sealed record DesktopConfiguration(Uri AppUri, string? Announcement);
internal sealed record DesktopConfigurationLoad(DesktopConfiguration Configuration, string? Warning);

internal static class DesktopConfigurationLoader
{
    private const string ConfigurationUrl = "https://paradox-lft5.onrender.com/desktop-config.json";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static Uri DefaultAppUri { get; } = new("https://paradox-lft5.onrender.com/");

    public static async Task<DesktopConfigurationLoad> LoadAsync()
    {
        try
        {
            using var response = await Client.GetAsync(ConfigurationUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var document = await response.Content.ReadFromJsonAsync<DesktopConfigurationDocument>();
            if (document is null ||
                !Uri.TryCreate(document.AppUrl, UriKind.Absolute, out var appUri) ||
                appUri.Scheme != Uri.UriSchemeHttps)
            {
                return UseDefault("The online desktop settings are invalid. Using the default Paradox website.");
            }

            return new(new DesktopConfiguration(appUri, document.Announcement), null);
        }
        catch (HttpRequestException exception)
        {
            return UseDefault($"Online desktop settings could not be loaded ({exception.Message}). Using the default website.");
        }
        catch (OperationCanceledException)
        {
            return UseDefault("Online desktop settings timed out. Using the default Paradox website.");
        }
        catch (JsonException)
        {
            return UseDefault("Online desktop settings could not be read. Using the default Paradox website.");
        }
    }

    private static DesktopConfigurationLoad UseDefault(string warning) =>
        new(new DesktopConfiguration(DefaultAppUri, null), warning);

}

internal sealed class DesktopConfigurationDocument
{
    public string? AppUrl { get; init; }
    public string? Announcement { get; init; }
}
