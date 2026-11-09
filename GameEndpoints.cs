using Microsoft.AspNetCore.Http.HttpResults;

namespace Paradox;

internal static class GameEndpoints
{
    public static WebApplication MapParadoxEndpoints(this WebApplication app)
    {
        app.MapPost("/api/games", IResult (CreateGameRequest body, IGameService games, CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            var (response, error) = games.Create(body);
            return response is null
                ? TypedResults.Problem(error, statusCode: StatusCodes.Status400BadRequest, title: "Quiz could not be created")
                : TypedResults.Created($"/api/games/{response.Pin}", response);
        })
        .WithName("CreateGame")
        .WithSummary("Create a quiz game")
        .WithDescription("Creates a quiz and returns its unique join PIN and private host key.")
        .Produces<GameCreatedResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/games/{pin}", IResult (
            string pin,
            HttpRequest request,
            IGameService games,
            CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            var hostKey = request.Headers["X-Host-Key"].FirstOrDefault();
            var playerId = request.Headers["X-Player-Id"].FirstOrDefault();
            var snapshot = games.GetSnapshot(pin, hostKey, playerId, out var unauthorized);
            if (unauthorized)
                return TypedResults.Problem("Host access could not be verified.", statusCode: StatusCodes.Status401Unauthorized);
            return snapshot is null
                ? TypedResults.Problem("That game PIN was not found.", statusCode: StatusCodes.Status404NotFound)
                : TypedResults.Ok(snapshot);
        })
        .WithName("GetGame")
        .WithSummary("Get the current game state")
        .WithDescription("Returns the lobby, question, reveal, or final scoreboard for a game.")
        .Produces<GameSnapshotResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/games/{pin}/players", IResult (
            string pin,
            JoinGameRequest body,
            IGameService games,
            CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            var result = games.Join(pin, body.Username);
            if (result.Response is not null)
                return TypedResults.Ok(result.Response);
            var statusCode = result.Status switch
            {
                GameActionStatus.NotFound => StatusCodes.Status404NotFound,
                GameActionStatus.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return TypedResults.Problem(result.Error, statusCode: statusCode, title: "Could not join game");
        })
        .WithName("JoinGame")
        .WithSummary("Join a game")
        .WithDescription("Adds a player to a game's lobby and returns a private player identifier.")
        .Produces<PlayerJoinedResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/games/{pin}/start", IResult (
            string pin,
            HttpRequest request,
            IGameService games,
            CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            return ToHttpResult(games.Start(pin, request.Headers["X-Host-Key"].FirstOrDefault() ?? string.Empty));
        })
        .WithName("StartGame")
        .WithSummary("Start a hosted quiz")
        .WithDescription("Starts the first question after at least one player has joined.")
        .Produces<GameSnapshotResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/games/{pin}/answers", IResult (
            string pin,
            SubmitAnswerRequest body,
            HttpRequest request,
            IGameService games,
            CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            var playerId = request.Headers["X-Player-Id"].FirstOrDefault() ?? string.Empty;
            return ToHttpResult(games.SubmitAnswer(pin, playerId, body.OptionIndex));
        })
        .WithName("SubmitAnswer")
        .WithSummary("Submit an answer")
        .WithDescription("Accepts one answer per player while the current question is open.")
        .Produces<GameSnapshotResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/games/{pin}/next", IResult (
            string pin,
            HttpRequest request,
            IGameService games,
            CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            return ToHttpResult(games.Next(pin, request.Headers["X-Host-Key"].FirstOrDefault() ?? string.Empty));
        })
        .WithName("NextQuestion")
        .WithSummary("Continue to the next question")
        .WithDescription("Advances after the current question's answer reveal, or finishes the game.")
        .Produces<GameSnapshotResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static IResult ToHttpResult(GameActionResult result) =>
        result.Status switch
        {
            GameActionStatus.Success => TypedResults.Ok(result.Snapshot),
            GameActionStatus.NotFound => TypedResults.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),
            GameActionStatus.Unauthorized => TypedResults.Problem(result.Error, statusCode: StatusCodes.Status401Unauthorized),
            GameActionStatus.Conflict => TypedResults.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest)
        };
}
