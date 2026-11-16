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

    /// <summary>Whether accepted type answers award their configured point values instead of speed points.</summary>
    public bool RarestAnswerScoring { get; init; }

    /// <summary>Points awarded for each accepted answer, in the same order as AcceptedAnswers.</summary>
    public List<int> AnswerPoints { get; init; } = [];

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

    /// <summary>Title displayed above the board.</summary>
    [StringLength(48)]
    public string BoardTitle { get; init; } = "The Showdown";

    /// <summary>Visual arrangement used to display the board spaces.</summary>
    public BoardLayout BoardLayout { get; init; } = BoardLayout.Track;

    /// <summary>Color treatment used to display the board.</summary>
    public BoardStyle BoardStyle { get; init; } = BoardStyle.Aurora;

    /// <summary>Optional secret-phrase puzzle and timed answer-smash configuration.</summary>
    public PhrasePuzzleRequest? PhrasePuzzle { get; init; }

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

/// <summary>Phrase puzzle settings used by a hosted show.</summary>
public sealed record PhrasePuzzleRequest
{
    /// <summary>Phrase players reveal by earning cards.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Phrase { get; init; }

    /// <summary>Whether to use timed Smash the Answer rounds after the board-lap threshold or quiz questions.</summary>
    public bool SmashEnabled { get; init; } = true;

    /// <summary>Completed board laps required before Smash the Answer begins.</summary>
    [Range(1, 10)]
    public int LapsBeforeSmash { get; init; } = 2;

    /// <summary>Timed rounds and accepted responses used by Smash the Answer.</summary>
    [Required, MinLength(1), MaxLength(20)]
    public required List<SmashRoundRequest> SmashRounds { get; init; }
}

/// <summary>A timed prompt and the responses accepted in one Smash the Answer round.</summary>
public sealed record SmashRoundRequest
{
    /// <summary>Prompt shown to every player during the 60-second round.</summary>
    [Required, StringLength(160, MinimumLength = 1)]
    public required string Prompt { get; init; }

    /// <summary>Unique accepted responses for the prompt, ignoring capitalization.</summary>
    [Required, MinLength(1), MaxLength(100)]
    public required List<string> AcceptedAnswers { get; init; }
}

/// <summary>Answers submitted by one player during an active Smash the Answer round.</summary>
public sealed record SubmitSmashAnswersRequest
{
    /// <summary>Full current answer list; duplicate or incorrect responses do not score.</summary>
    [Required, MaxLength(100)]
    public required List<string> Answers { get; init; }
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

    /// <summary>Optional display label shown on the board.</summary>
    [StringLength(40)]
    public string Label { get; init; } = "";

    /// <summary>Optional compressed raster image represented as a data URL.</summary>
    [StringLength(80000)]
    public string ImageDataUrl { get; init; } = "";

    /// <summary>Optional key for a built-in board illustration.</summary>
    [StringLength(24)]
    public string ImageKey { get; init; } = "";
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
    string Subject,
    IReadOnlyList<int>? AnswerPoints = null,
    bool RarestAnswerScoring = false);

/// <summary>A named response to a survey question.</summary>
public sealed record SurveyResponse(string Username, int? OptionIndex, string? AnswerText);

/// <summary>A player's public position on the digital board.</summary>
public sealed record PlayerBoardPositionResponse(string Username, int Position);

/// <summary>Public phrase progress for one game participant.</summary>
public sealed record PhrasePlayerResponse(
    string Username,
    string MaskedPhrase,
    int CardsEarned,
    int BoardLapsCompleted,
    int SmashCorrectAnswers);

/// <summary>A letter card awarded to one player.</summary>
public sealed record CardAwardResponse(int Sequence, string Username, string Letter, string Reason);

/// <summary>Current timed Smash the Answer round, with only the requesting player's entries.</summary>
public sealed record SmashRoundResponse(
    int RoundNumber,
    string Prompt,
    DateTimeOffset EndsAtUtc,
    IReadOnlyList<string> MyAnswers,
    int MyCorrectCount);

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
    string? WinnerUsername = null,
    string BoardTitle = "The Showdown",
    BoardLayout BoardLayout = BoardLayout.Track,
    BoardStyle BoardStyle = BoardStyle.Aurora,
    int RollSequence = 0,
    bool PhrasePuzzleEnabled = false,
    IReadOnlyList<PhrasePlayerResponse>? PhrasePlayers = null,
    IReadOnlyList<CardAwardResponse>? CardAwards = null,
    SmashRoundResponse? SmashRound = null,
    DateTimeOffset? NextCardBonusAtUtc = null);

internal static class BoardArtworkLibrary
{
    private static readonly HashSet<string> SupportedKeys = new(StringComparer.Ordinal)
    {
        "comet", "crown", "crystal", "dice", "flame", "planet", "rocket", "treasure"
    };

    public static bool IsSupported(string? key) =>
        string.IsNullOrEmpty(key) || SupportedKeys.Contains(key);
}

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

/// <summary>Visual arrangement of the board spaces.</summary>
public enum BoardLayout
{
    /// <summary>A winding track that follows the order of the spaces.</summary>
    Track,
    /// <summary>A serpentine grid that winds like a classic snakes-and-ladders board.</summary>
    SnakesAndLadders
}

/// <summary>Visual color treatment for a digital board.</summary>
public enum BoardStyle
{
    /// <summary>Violet and teal light over a dark board.</summary>
    Aurora,
    /// <summary>Bright candy colors with playful tile highlights.</summary>
    Candy,
    /// <summary>High-contrast arcade colors and glowing borders.</summary>
    Arcade
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
    /// <summary>Players are submitting answers in a timed Smash the Answer round.</summary>
    SmashAnswer,
    /// <summary>All questions have been completed.</summary>
    Finished
}

internal static class PhrasePuzzleValidator
{
    public static string? Validate(PhrasePuzzleRequest? puzzle)
    {
        if (puzzle is null)
            return null;
        if (string.IsNullOrWhiteSpace(puzzle.Phrase) ||
            puzzle.Phrase.Length > 100 ||
            puzzle.Phrase[0] == ' ' ||
            puzzle.Phrase[^1] == ' ' ||
            puzzle.Phrase.Any(character => !char.IsAsciiLetter(character) && character != ' ') ||
            puzzle.Phrase.Contains("  ", StringComparison.Ordinal))
            return "The secret phrase must contain only letters and single spaces, with no leading or trailing spaces.";
        if (puzzle.LapsBeforeSmash is < 1 or > 10)
            return "Choose between 1 and 10 board laps before Smash the Answer.";
        if (puzzle.SmashRounds is null || puzzle.SmashRounds.Count is < 1 or > 20)
            return "Add between 1 and 20 Smash the Answer rounds.";
        foreach (var round in puzzle.SmashRounds)
        {
            if (round is null || string.IsNullOrWhiteSpace(round.Prompt) || round.Prompt.Trim().Length > 160)
                return "Every Smash the Answer round needs a prompt of 160 characters or fewer.";
            if (round.AcceptedAnswers is null || round.AcceptedAnswers.Count is < 1 or > 100 ||
                round.AcceptedAnswers.Any(answer => string.IsNullOrWhiteSpace(answer) || answer.Trim().Length > 80))
                return "Every Smash the Answer round needs between 1 and 100 accepted answers, each 80 characters or fewer.";
            if (round.AcceptedAnswers.Select(answer => answer.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != round.AcceptedAnswers.Count)
                return "Smash the Answer responses must be unique, ignoring capitalization.";
        }
        return null;
    }
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
        IReadOnlyList<QuizQuestionRequest>? questions,
        string? boardTitle,
        BoardLayout boardLayout,
        BoardStyle boardStyle)
    {
        if (!Enum.IsDefined(mode))
            return "Choose a supported show mode.";
        if (mode == ShowMode.Board && subjectWheelEnabled)
            return "The subject wheel can be used in Quiz or Custom mode.";

        if (!HasBoard(mode, boardEnabled))
            return null;
        if (string.IsNullOrWhiteSpace(boardTitle) || boardTitle.Trim().Length > 48)
            return "Add a board title with no more than 48 characters.";
        if (!Enum.IsDefined(boardLayout) || !Enum.IsDefined(boardStyle))
            return "Choose a supported board layout and style.";

        if (boardTiles is null || boardTiles.Count is < 3 or > 40)
            return "A digital board needs between 3 and 40 tiles, including Start.";
        if (boardTiles[0] is null || boardTiles[0].Action != BoardTileAction.Start)
            return "The first board tile must be Start.";

        var imageDataLength = 0;
        for (var index = 0; index < boardTiles.Count; index++)
        {
            var tile = boardTiles[index];
            if (tile is null || !Enum.IsDefined(tile.Action))
                return "Choose a supported action for every board tile.";
            if ((index == 0) != (tile.Action == BoardTileAction.Start))
                return "Start can only be used on the first board tile.";
            if ((tile.Subject?.Trim().Length ?? 0) > 40 ||
                (tile.Prompt?.Trim().Length ?? 0) > 160 ||
                (tile.Label?.Trim().Length ?? 0) > 40)
                return "Board tile labels, subjects, and prompts are too long.";
            if (!IsValidImageDataUrl(tile.ImageDataUrl))
                return "Board images must be compressed PNG, JPEG, or WebP images.";
            if (!BoardArtworkLibrary.IsSupported(tile.ImageKey) ||
                !string.IsNullOrEmpty(tile.ImageKey) && !string.IsNullOrEmpty(tile.ImageDataUrl))
                return "Choose one supported built-in illustration or upload a board image.";
            imageDataLength += tile.ImageDataUrl?.Length ?? 0;
            if (imageDataLength > 400_000)
                return "Board images are too large. Remove some images or use smaller pictures.";
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

    private static bool IsValidImageDataUrl(string? imageDataUrl)
    {
        if (string.IsNullOrEmpty(imageDataUrl))
            return true;
        if (imageDataUrl.Length > 80_000)
            return false;
        var separator = imageDataUrl.IndexOf(',');
        if (separator < 0)
            return false;
        var mediaType = imageDataUrl[..separator];
        if (mediaType is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64"))
            return false;
        var encoded = imageDataUrl[(separator + 1)..];
        if (encoded.Length == 0)
            return false;
        var buffer = new byte[encoded.Length];
        return Convert.TryFromBase64String(encoded, buffer, out var bytesWritten) &&
            bytesWritten <= 60_000 &&
            HasImageSignature(mediaType, buffer.AsSpan(0, bytesWritten));
    }

    private static bool HasImageSignature(string mediaType, ReadOnlySpan<byte> bytes) =>
        mediaType switch
        {
            "data:image/png;base64" => bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "data:image/jpeg;base64" => bytes.StartsWith(new byte[] { 255, 216, 255 }),
            "data:image/webp;base64" => bytes.Length >= 12 &&
                bytes[..4].SequenceEqual("RIFF"u8) &&
                bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
}
