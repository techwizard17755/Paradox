using System.Text.Json.Serialization;
using Paradox;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 128 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddParadoxServices(
    Environment.GetEnvironmentVariable("PARADOX_DATA_DIR") ??
    Path.Combine(AppContext.BaseDirectory, "data"));
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }));
app.MapOpenApi();
app.MapParadoxGameEndpoints();
app.MapQuizLibraryEndpoints();
app.MapDesktopDownloadEndpoint();
app.Run();
