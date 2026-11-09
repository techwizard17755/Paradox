using System.ComponentModel.DataAnnotations;

namespace Paradox;

/// <summary>Details for one multiple-choice quiz question.</summary>
public sealed record QuizQuestionRequest
{
    /// <summary>The question shown to players.</summary>
    [Required, StringLength(240, MinimumLength = 1)]
    public required string Prompt { get; init; }

    /// <summary>Two to four possible answers, in display order.</summary>
    [Required, MinLength(2), MaxLength(4)]
    public required List<string> Options { get; init; }

    /// <summary>Zero-based index of the correct answer.</summary>
    [Range(0, 3)]
    public required int CorrectOptionIndex { get; init; }

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
    /// <summary>Zero-based index of the selected answer.</summary>
    [Range(0, 3)]
    public required int OptionIndex { get; init; }
}

/// <summary>Unique game PIN and host key returned when a game is created.</summary>
public sealed record GameCreatedResponse(string Pin, string HostKey, string QuizName);

/// <summary>Player credentials returned after a successful join.</summary>
public sealed record PlayerJoinedResponse(string PlayerId, string Pin, string Username);

/// <summary>Public scoreboard entry.</summary>
public sealed record PlayerScoreResponse(string Username, int Score, int? LastAnswerPoints);

/// <summary>Current question choices, without revealing the correct answer early.</summary>
public sealed record QuestionResponse(string Prompt, IReadOnlyList<string> Options, int TimeLimitSeconds);

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
    int? MyAnswerIndex);

/// <summary>Game lifecycle state.</summary>
public enum GameStatus
{
    /// <summary>Players may join while the host prepares to start.</summary>
    Lobby,
    /// <summary>A timed question is accepting answers.</summary>
    Question,
    /// <summary>The answer is revealed and the host may continue.</summary>
    Reveal,
    /// <summary>All questions have been completed.</summary>
    Finished
}
