using System.Text.Json.Serialization;

namespace Paradox;

/// <summary>Registers the persistent Paradox application services.</summary>
public static class ParadoxServices
{
    /// <summary>Registers game and quiz-library services using the given data directory.</summary>
    public static IServiceCollection AddParadoxServices(
        this IServiceCollection services,
        string dataDirectory)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler(options => { });
        services.AddValidation();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddSingleton<IGameService, GameService>();
        services.AddSingleton<IQuizLibraryService>(_ => new QuizLibraryService(dataDirectory));
        return services;
    }

    /// <summary>Maps the live game API.</summary>
    public static WebApplication MapParadoxGameEndpoints(this WebApplication app)
    {
        app.MapParadoxEndpoints();
        return app;
    }

    /// <summary>Maps the authenticated saved-quiz library API.</summary>
    public static WebApplication MapQuizLibraryEndpoints(this WebApplication app)
    {
        QuizLibraryEndpoints.Map(app);
        return app;
    }
}
