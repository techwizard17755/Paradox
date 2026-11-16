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
        .WithSummary("Create a hosted game show")
        .WithDescription("Creates a quiz, subject-wheel show, digital board game, or Custom show and returns its join PIN and private host key.")
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
        .WithDescription("Returns the lobby, subject wheel, board turn, question, reveal, or final scoreboard for a game.")
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
        .WithSummary("Start a hosted game show")
        .WithDescription("Starts the first question or board turn after at least one player has joined.")
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
            return ToHttpResult(games.SubmitAnswer(pin, playerId, body.OptionIndex, body.AnswerText));
        })
        .WithName("SubmitAnswer")
        .WithSummary("Submit an answer")
        .WithDescription("Accepts one answer per player while the current question is open.")
        .Produces<GameSnapshotResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/games/{pin}/wheel/spin", IResult (
            string pin,
            SpinWheelRequest body,
            HttpRequest request,
            IGameService games,
            CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            return ToHttpResult(games.SpinWheel(
                pin,
                request.Headers["X-Host-Key"].FirstOrDefault() ?? string.Empty,
                body.Subject));
        })
        .WithName("SpinSubjectWheel")
        .WithSummary("Choose the next question subject")
        .WithDescription("The host may select a subject manually or leave it empty to spin randomly.")
        .Produces<GameSnapshotResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/games/{pin}/board/roll", IResult (
            string pin,
            HttpRequest request,
            IGameService games,
            CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            return ToHttpResult(games.RollBoard(
                pin,
                request.Headers["X-Player-Id"].FirstOrDefault() ?? string.Empty));
        })
        .WithName("RollBoard")
        .WithSummary("Roll for a board-game turn")
        .WithDescription("The active player rolls a six-sided die; the server moves their piece and resolves the landed tile.")
        .Produces<GameSnapshotResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/games/{pin}/smash/answers", IResult (
            string pin,
            SubmitSmashAnswersRequest body,
            HttpRequest request,
            IGameService games,
            CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            return ToHttpResult(games.SubmitSmashAnswers(
                pin,
                request.Headers["X-Player-Id"].FirstOrDefault() ?? string.Empty,
                body.Answers));
        })
        .WithName("SubmitSmashAnswers")
        .WithSummary("Submit answers during Smash the Answer")
        .WithDescription("Replaces the player's current answer list during the 60-second round; accepted responses are scored when the host advances.")
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
