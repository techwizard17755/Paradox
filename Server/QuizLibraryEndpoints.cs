using Microsoft.AspNetCore.Http.HttpResults;

namespace Paradox;

internal static class QuizLibraryEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/library");
        group.MapGet("/themes", () => TypedResults.Ok(QuizThemes.List()))
            .WithName("ListQuizThemes")
            .WithSummary("List available quiz themes")
            .Produces<IReadOnlyList<QuizThemeResponse>>();

        group.MapGet("/", async Task<Results<Ok<IReadOnlyList<SavedQuizResponse>>, ProblemHttpResult>> (
            HttpRequest request,
            IQuizLibraryService library,
            CancellationToken cancellationToken) =>
        {
            var ownerKey = request.Headers["X-Quiz-Key"].FirstOrDefault();
            if (!IsValidOwnerKey(ownerKey))
                return TypedResults.Problem("A valid quiz library key is required.", statusCode: StatusCodes.Status401Unauthorized);
            var quizzes = await library.ListAsync(ownerKey!, cancellationToken);
            return TypedResults.Ok(quizzes);
        })
        .WithName("ListSavedQuizzes")
        .WithSummary("List the caller's saved quizzes")
        .WithDescription("Requires the private X-Quiz-Key returned when the first quiz was saved.")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/", async Task<IResult> (
            SaveQuizRequest body,
            HttpRequest request,
            IQuizLibraryService library,
            CancellationToken cancellationToken) =>
        {
            var ownerKey = request.Headers["X-Quiz-Key"].FirstOrDefault();
            var createdOwnerKey = string.IsNullOrWhiteSpace(ownerKey);
            if (!createdOwnerKey && !IsValidOwnerKey(ownerKey))
                return TypedResults.Problem("The quiz library key is invalid.", statusCode: StatusCodes.Status401Unauthorized);
            if (createdOwnerKey)
                ownerKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

            var result = await library.CreateAsync(body, ownerKey!, cancellationToken);
            if (result.Quiz is null)
                return ToProblem(result);
            var response = new SavedQuizCreatedResponse(result.Quiz, createdOwnerKey ? ownerKey! : "");
            return TypedResults.Created($"/api/library/{result.Quiz.Id}", response);
        })
        .WithName("SaveQuiz")
        .WithSummary("Save a quiz to the caller's library")
        .WithDescription("Creates a private quiz library on first use and returns its management key once.")
        .Produces<SavedQuizCreatedResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/{id}", async Task<IResult> (
            string id,
            HttpRequest request,
            IQuizLibraryService library,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadOwnerKey(request, out var ownerKey))
                return Unauthorized();
            var quiz = await library.GetAsync(id, ownerKey, cancellationToken);
            return quiz is null
                ? TypedResults.Problem("The saved quiz was not found.", statusCode: StatusCodes.Status404NotFound)
                : TypedResults.Ok(quiz);
        })
        .WithName("GetSavedQuiz")
        .WithSummary("Get one of the caller's saved quizzes")
        .Produces<SavedQuizResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id}", async Task<IResult> (
            string id,
            SaveQuizRequest body,
            HttpRequest request,
            IQuizLibraryService library,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadOwnerKey(request, out var ownerKey))
                return Unauthorized();
            return ToHttpResult(await library.UpdateAsync(id, body, ownerKey, cancellationToken));
        })
        .WithName("UpdateSavedQuiz")
        .WithSummary("Update a saved quiz")
        .Produces<SavedQuizResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}", async Task<IResult> (
            string id,
            HttpRequest request,
            IQuizLibraryService library,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadOwnerKey(request, out var ownerKey))
                return Unauthorized();
            return await library.DeleteAsync(id, ownerKey, cancellationToken)
                ? TypedResults.NoContent()
                : TypedResults.Problem("The saved quiz was not found.", statusCode: StatusCodes.Status404NotFound);
        })
        .WithName("DeleteSavedQuiz")
        .WithSummary("Delete a saved quiz")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResult ToHttpResult(QuizLibraryResult result) =>
        result.Status switch
        {
            QuizLibraryStatus.Success => TypedResults.Ok(result.Quiz),
            QuizLibraryStatus.NotFound => TypedResults.Problem("The saved quiz was not found.", statusCode: StatusCodes.Status404NotFound),
            QuizLibraryStatus.LimitReached => TypedResults.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest)
        };

    private static ProblemHttpResult ToProblem(QuizLibraryResult result) => result.Status switch
    {
        QuizLibraryStatus.LimitReached => TypedResults.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
        _ => TypedResults.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest)
    };

    private static bool TryReadOwnerKey(HttpRequest request, out string ownerKey)
    {
        ownerKey = request.Headers["X-Quiz-Key"].FirstOrDefault() ?? "";
        return IsValidOwnerKey(ownerKey);
    }

    private static bool IsValidOwnerKey(string? ownerKey) => ownerKey is { Length: 64 } &&
        ownerKey.All(Uri.IsHexDigit);

    private static ProblemHttpResult Unauthorized() =>
        TypedResults.Problem("A valid quiz library key is required.", statusCode: StatusCodes.Status401Unauthorized);
}
