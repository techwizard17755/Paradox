using System.Net;

namespace Paradox;

internal static class DesktopDownloadEndpoints
{
    private const string DownloadPath = "/downloads/Paradox-v1.0.0.exe";
    private const string ReleaseAssetUrl =
        "https://github.com/techwizard17755/Paradox/releases/download/v1.0.0/Paradox-v1.0.0.exe";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static WebApplication MapDesktopDownloadEndpoint(this WebApplication app)
    {
        app.MapMethods(DownloadPath, ["GET", "HEAD"], DownloadAsync)
            .WithName("DownloadParadoxForWindows")
            .WithSummary("Download Paradox for Windows")
            .WithDescription("Streams the Windows executable from the versioned release without redirecting the download request.");
        return app;
    }

    private static async Task<IResult> DownloadAsync(HttpContext context, ILogger<Program> logger)
    {
        using var request = new HttpRequestMessage(
            HttpMethods.IsHead(context.Request.Method) ? HttpMethod.Head : HttpMethod.Get,
            ReleaseAssetUrl);
        if (context.Request.Headers.TryGetValue("Range", out var range))
            request.Headers.TryAddWithoutValidation("Range", range.ToString());

        HttpResponseMessage upstream;
        try
        {
            upstream = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Could not retrieve the Paradox Windows release asset.");
            return TypedResults.Problem(
                "The Paradox download is temporarily unavailable. Please try again later.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        using (upstream)
        {
            if (upstream.StatusCode != HttpStatusCode.OK &&
                upstream.StatusCode != HttpStatusCode.PartialContent)
            {
                logger.LogError("Release asset request returned HTTP {StatusCode}.", (int)upstream.StatusCode);
                return TypedResults.Problem(
                    "The Paradox download is temporarily unavailable. Please try again later.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var response = context.Response;
            response.StatusCode = (int)upstream.StatusCode;
            response.ContentType = "application/octet-stream";
            response.ContentLength = upstream.Content.Headers.ContentLength;
            response.Headers.ContentDisposition = "attachment; filename=\"Paradox-v1.0.0.exe\"";
            response.Headers.CacheControl = "public, max-age=3600";
            if (upstream.Content.Headers.ContentRange is { } contentRange)
                response.Headers.ContentRange = contentRange.ToString();
            if (upstream.Headers.AcceptRanges.Count > 0)
                response.Headers.AcceptRanges = string.Join(", ", upstream.Headers.AcceptRanges);

            if (!HttpMethods.IsHead(context.Request.Method))
                await upstream.Content.CopyToAsync(response.Body, context.RequestAborted);
        }

        return TypedResults.Empty;
    }
}
