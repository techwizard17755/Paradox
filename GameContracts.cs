using System.ComponentModel.DataAnnotations;

namespace Paradox;

/// <summary>Details for one quiz question.</summary>
public sealed record QuizQuestionRequest
{
    /// <summary>The question shown to players.</summary>
    [Required, StringLength(240, MinimumLength = 1)]
    public required string Prompt { get; init; }

    /// <summary>How players answer this question.</summary>
    public QuestionType Type { get; init; } = QuestionType.MultipleChoice;

    /// <summary>Optional subject used by a quiz wheel or a board question tile.</summary>
    [StringLength(40)]
    public string Subject { get; init; } = "General";

    /// <summary>Selectable answers for multiple-choice and survey questions.</summary>
    public List<string> Options { get; init; } = [];

    /// <summary>Zero-based correct choice for multiple-choice questions.</summary>
    public int CorrectOptionIndex { get; init; }

    /// <summary>Accepted text answers for type-answer questions.</summary>
    public List<string> AcceptedAnswers { get; init; } = [];

    /// <summary>Time allowed for answers, between 5 and 60 seconds.</summary>
    [Range(5, 60)]
    public int TimeLimitSeconds { get; init; } = 20;
}

/// <summary>Payload for creating a quiz game.</summary>
public sealed record CreateGameRequest
{
    /// <summary>Name displayed in the lobby and game.</summary>
    [Required, StringLength(80, MinimumLength = 1)]
    public required string QuizName { get; init; }

    /// <summary>Optional introduction displayed with the quiz.</summary>
    [StringLength(280)]
    public string Description { get; init; } = "";

    /// <summary>Color theme identifier selected from the supported quiz themes.</summary>
    [Required, StringLength(20)]
    public string Theme { get; init; } = "violet";

    /// <summary>Overall show format.</summary>
    public ShowMode Mode { get; init; } = ShowMode.Quiz;

    /// <summary>Whether a Custom show includes a turn-based board.</summary>
    public bool BoardEnabled { get; init; }

    /// <summary>Whether the host chooses a question subject using the wheel.</summary>
    public bool SubjectWheelEnabled { get; init; }

    /// <summary>Ordered board spaces, beginning with a Start tile.</summary>
    public List<BoardTileRequest> BoardTiles { get; init; } = [];

    /// <summary>Questions to play in order.</summary>
    [Required, MinLength(1), MaxLength(30)]
    public required List<QuizQuestionRequest> Questions { get; init; }
}

/// <summary>Payload for joining a game with a display name.</summary>
public sealed record JoinGameRequest
{
    /// <summary>Player name shown to the host and scoreboard.</summary>
    [Required, StringLength(20, MinimumLength = 1)]
    public required string Username { get; init; }
}

/// <summary>Payload for submitting an answer to the current question.</summary>
public sealed record SubmitAnswerRequest
{
    /// <summary>Zero-based index of the selected answer, when answering a choice question.</summary>
    [Range(0, 3)]
    public int? OptionIndex { get; init; }

    /// <summary>Free-text response, when answering a survey or type-answer question.</summary>
    [StringLength(100)]
    public string? AnswerText { get; init; }
}

/// <summary>One editable space on a digital board.</summary>
public sealed record BoardTileRequest
{
    /// <summary>Action to resolve when a player lands on this space.</summary>
    public BoardTileAction Action { get; init; } = BoardTileAction.Question;

    /// <summary>Points or spaces applied by a numeric tile action.</summary>
    [Range(0, 1000)]
    public int Amount { get; init; } = 100;

    /// <summary>Optional subject constraint for question tiles.</summary>
    [StringLength(40)]
    public string Subject { get; init; } = "";

    /// <summary>Message shown when a player lands on a custom tile.</summary>
    [StringLength(160)]
    public string Prompt { get; init; } = "";
}

/// <summary>Optional subject selected by the host; omit it to spin randomly.</summary>
public sealed record SpinWheelRequest
{
    /// <summary>Subject to choose manually, or null to spin randomly.</summary>
    [StringLength(40)]
    public string? Subject { get; init; }
}

/// <summary>Unique game PIN and host key returned when a game is created.</summary>
public sealed record GameCreatedResponse(string Pin, string HostKey, string QuizName);

/// <summary>Player credentials returned after a successful join.</summary>
public sealed record PlayerJoinedResponse(string PlayerId, string Pin, string Username);

/// <summary>Public scoreboard entry.</summary>
public sealed record PlayerScoreResponse(string Username, int Score, int? LastAnswerPoints);

/// <summary>Current question details, without revealing accepted answers early.</summary>
public sealed record QuestionResponse(
    string Prompt,
    QuestionType Type,
    IReadOnlyList<string> Options,
    int TimeLimitSeconds,
    IReadOnlyList<string> AcceptedAnswers,
    string Subject);

/// <summary>A named response to a survey question.</summary>
public sealed record SurveyResponse(string Username, int? OptionIndex, string? AnswerText);

/// <summary>A player's public position on the digital board.</summary>
public sealed record PlayerBoardPositionResponse(string Username, int Position);

/// <summary>Current game state visible to a host or player.</summary>
public sealed record GameSnapshotResponse(
    string Pin,
    string QuizName,
    string Description,
    string Theme,
    GameStatus Status,
    int PlayerCount,
    IReadOnlyList<PlayerScoreResponse> Players,
    QuestionResponse? CurrentQuestion,
    int QuestionNumber,
    int QuestionCount,
    DateTimeOffset? EndsAtUtc,
    int? CorrectOptionIndex,
    int? MyAnswerIndex,
    string? MyAnswerText,
    IReadOnlyList<SurveyResponse>? SurveyResponses,
    ShowMode Mode = ShowMode.Quiz,
    bool BoardEnabled = false,
    bool SubjectWheelEnabled = false,
    IReadOnlyList<string>? AvailableSubjects = null,
    string? ActiveSubject = null,
    string? ActivePlayer = null,
    int? LastRoll = null,
    IReadOnlyList<BoardTileRequest>? BoardTiles = null,
    IReadOnlyList<PlayerBoardPositionResponse>? BoardPositions = null,
    string? TileMessage = null,
    string? WinnerUsername = null);

/// <summary>Show creation mode.</summary>
public enum ShowMode
{
    /// <summary>A question game, optionally using a host-controlled subject wheel.</summary>
    Quiz,
    /// <summary>A turn-based board game with configurable tile actions.</summary>
    Board,
    /// <summary>An advanced show that can combine board and quiz mechanics.</summary>
    Custom
}

/// <summary>Action performed on a digital board tile.</summary>
public enum BoardTileAction
{
    /// <summary>Starting space; only valid as the first tile.</summary>
    Start,
    /// <summary>Answer a question, optionally constrained to a subject.</summary>
    Question,
    /// <summary>Gain points.</summary>
    Bonus,
    /// <summary>Lose points.</summary>
    Penalty,
    /// <summary>Move forward the configured number of spaces.</summary>
    MoveForward,
    /// <summary>Move backward the configured number of spaces.</summary>
    MoveBackward,
    /// <summary>Skip the player's next turn.</summary>
    SkipTurn,
    /// <summary>Show a custom host prompt before the next turn.</summary>
    Custom
}

/// <summary>How a quiz question is answered and scored.</summary>
public enum QuestionType
{
    /// <summary>Players select one choice; only the marked choice is correct.</summary>
    MultipleChoice,
    /// <summary>Players select one choice; responses are gathered without scoring.</summary>
    Survey,
    /// <summary>Players type a response; a matching accepted answer earns points.</summary>
    TypeAnswer
}

/// <summary>Game lifecycle state.</summary>
public enum GameStatus
{
    /// <summary>Players may join while the host prepares to start.</summary>
    Lobby,
    /// <summary>A timed question is accepting answers.</summary>
    Question,
    /// <summary>The host is selecting a subject for the next question.</summary>
    Wheel,
    /// <summary>A player may roll to take their board turn.</summary>
    BoardTurn,
    /// <summary>A custom board prompt is waiting for the host to continue.</summary>
    BoardAction,
    /// <summary>The answer is revealed and the host may continue.</summary>
    Reveal,
    /// <summary>All questions have been completed.</summary>
    Finished
}

internal static class ShowConfigurationValidator
{
    public static bool HasBoard(ShowMode mode, bool boardEnabled) =>
        mode == ShowMode.Board || mode == ShowMode.Custom && boardEnabled;

    public static bool HasSubjectWheel(ShowMode mode, bool subjectWheelEnabled) =>
        mode != ShowMode.Board && subjectWheelEnabled;

    public static string? Validate(
        ShowMode mode,
        bool boardEnabled,
        bool subjectWheelEnabled,
        IReadOnlyList<BoardTileRequest>? boardTiles,
        IReadOnlyList<QuizQuestionRequest>? questions)
    {
        if (!Enum.IsDefined(mode))
            return "Choose a supported show mode.";
        if (mode == ShowMode.Board && subjectWheelEnabled)
            return "The subject wheel can be used in Quiz or Custom mode.";

        if (!HasBoard(mode, boardEnabled))
            return null;

        if (boardTiles is null || boardTiles.Count is < 3 or > 40)
            return "A digital board needs between 3 and 40 tiles, including Start.";
        if (boardTiles[0] is null || boardTiles[0].Action != BoardTileAction.Start)
            return "The first board tile must be Start.";

        for (var index = 0; index < boardTiles.Count; index++)
        {
            var tile = boardTiles[index];
            if (tile is null || !Enum.IsDefined(tile.Action))
                return "Choose a supported action for every board tile.";
            if ((index == 0) != (tile.Action == BoardTileAction.Start))
                return "Start can only be used on the first board tile.";
            if ((tile.Subject?.Trim().Length ?? 0) > 40 || (tile.Prompt?.Trim().Length ?? 0) > 160)
                return "Board tile subjects and prompts are too long.";
            if ((tile.Action is BoardTileAction.Bonus or BoardTileAction.Penalty or
                BoardTileAction.MoveForward or BoardTileAction.MoveBackward) &&
                (tile.Amount is < 1 or > 1000))
                return "Point and movement actions must use a value from 1 to 1,000.";
            if (tile.Action == BoardTileAction.Custom && string.IsNullOrWhiteSpace(tile.Prompt))
                return "Add a prompt to every custom board tile.";
            if (tile.Action == BoardTileAction.Question &&
                !string.IsNullOrWhiteSpace(tile.Subject) &&
                (questions is null || !questions.Any(question => question is not null &&
                    string.Equals(string.IsNullOrWhiteSpace(question.Subject) ? "General" : question.Subject.Trim(),
                        tile.Subject.Trim(),
                        StringComparison.OrdinalIgnoreCase))))
                return $"Add a question for the “{tile.Subject.Trim()}” board tile.";
        }

        return null;
    }
}
