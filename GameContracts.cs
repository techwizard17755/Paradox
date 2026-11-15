using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Paradox;

/// <summary>Details for one quiz question.</summary>
public sealed record QuizQuestionRequest
{
    /// <summary>The question shown to players.</summary>
    [Required, StringLength(240, MinimumLength = 1)]
    public required string Prompt { get; init; }

    /// <summary>How players answer this question.</summary>
    public QuestionType Type { get; init; } = QuestionType.MultipleChoice;

    /// <summary>Selectable answers for multiple-choice and survey questions.</summary>
    public List<string> Options { get; init; } = [];

    /// <summary>Zero-based correct choice for multiple-choice questions.</summary>
    public int CorrectOptionIndex { get; init; }

    /// <summary>Accepted text answers for type-answer questions.</summary>
    public List<string> AcceptedAnswers { get; init; } = [];

    /// <summary>Optional alternative wording keyed by a creator-defined player profile.</summary>
    public Dictionary<string, string> ProfilePrompts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Settings for player capacity and team play.</summary>
    public ShowSettings ShowSettings { get; init; } = new();
}

/// <summary>Creator-controlled rules for a live show.</summary>
public sealed record ShowSettings
{
    /// <summary>Maximum number of players allowed in this room.</summary>
    [Range(1, 40)]
    public int PlayerLimit { get; init; } = 40;

    /// <summary>Number of automatically balanced teams; one means solo play.</summary>
    [Range(1, 8)]
    public int TeamCount { get; init; } = 1;

    /// <summary>Base points awarded for a correct scored answer.</summary>
    [Range(0, 5000)]
    public int BasePoints { get; init; } = 500;

    /// <summary>Maximum speed bonus added to a correct answer.</summary>
    [Range(0, 5000)]
    public int SpeedBonusPoints { get; init; } = 500;

    /// <summary>Optional creator-defined labels the host can assign to players.</summary>
    public List<string> ProfileLabels { get; init; } = [];

    /// <summary>Name of the optional show currency tracked by creator rules.</summary>
    [StringLength(20, MinimumLength = 1)]
    public string CurrencyName { get; init; } = "Coins";

    /// <summary>Safe event, condition, and action blocks for this show.</summary>
    public List<ShowRuleBlock> RuleBlocks { get; init; } = [];
}

/// <summary>A safe, creator-configured event/condition/action rule for a show.</summary>
public sealed record ShowRuleBlock
{
    /// <summary>Game event that triggers this block.</summary>
    public ShowRuleTrigger Trigger { get; init; }

    /// <summary>Optional check that must pass before the action runs.</summary>
    public ShowRuleCondition Condition { get; init; } = ShowRuleCondition.Always;

    /// <summary>Value used by the selected condition, such as a score or team name.</summary>
    [StringLength(40)]
    public string ConditionValue { get; init; } = "";

    /// <summary>Points or currency action to perform.</summary>
    public ShowRuleAction Action { get; init; }

    /// <summary>Amount added or removed by the action.</summary>
    [Range(1, 1000)]
    public int Amount { get; init; } = 1;
}

/// <summary>Supported game events for creator rule blocks.</summary>
public enum ShowRuleTrigger
{
    /// <summary>The player submits a correct answer.</summary>
    CorrectAnswer,
    /// <summary>The player submits an incorrect scored answer.</summary>
    IncorrectAnswer,
    /// <summary>The player submits any answer, including an unscored response.</summary>
    AnyAnswer,
    /// <summary>A player joins the lobby.</summary>
    PlayerJoined
}

/// <summary>Supported checks for creator rule blocks.</summary>
public enum ShowRuleCondition
{
    /// <summary>Always run when the selected event occurs.</summary>
    Always,
    /// <summary>Run when the player's score is at least the configured value.</summary>
    ScoreAtLeast,
    /// <summary>Run when the player's currency is at least the configured value.</summary>
    CurrencyAtLeast,
    /// <summary>Run when the player belongs to the selected team.</summary>
    TeamIs,
    /// <summary>Run when the player has the selected profile.</summary>
    ProfileIs,
    /// <summary>Run when the active question uses the selected format.</summary>
    QuestionTypeIs,
    /// <summary>Run when the number of joined players reaches the configured value.</summary>
    PlayerCountAtLeast,
    /// <summary>Run when the current round number reaches the configured value.</summary>
    RoundNumberAtLeast
}

/// <summary>Supported actions for creator rule blocks.</summary>
public enum ShowRuleAction
{
    /// <summary>Add currency to the triggering player's balance.</summary>
    AddCurrency,
    /// <summary>Remove currency from the triggering player's balance, not below zero.</summary>
    RemoveCurrency,
    /// <summary>Add points to the triggering player's score.</summary>
    AddPoints,
    /// <summary>Remove points from the triggering player's score, not below zero.</summary>
    RemovePoints,
    /// <summary>Add points to every player on the triggering player's team.</summary>
    AddTeamPoints
}

internal static class ShowRuleValidation
{
    public static bool IsValid(ShowRuleBlock? rule, ShowSettings settings)
    {
        if (rule is null ||
            !Enum.IsDefined(rule.Trigger) ||
            !Enum.IsDefined(rule.Condition) ||
            !Enum.IsDefined(rule.Action) ||
            rule.Amount is < 1 or > 1000 ||
            rule.ConditionValue is null ||
            rule.ConditionValue.Length > 40)
            return false;

        return rule.Condition switch
        {
            ShowRuleCondition.Always => true,
            ShowRuleCondition.ScoreAtLeast or ShowRuleCondition.CurrencyAtLeast =>
                int.TryParse(rule.ConditionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var threshold) &&
                threshold is >= 0 and <= 1_000_000,
            ShowRuleCondition.PlayerCountAtLeast =>
                int.TryParse(rule.ConditionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var playerCount) &&
                playerCount is >= 1 and <= 40,
            ShowRuleCondition.RoundNumberAtLeast =>
                int.TryParse(rule.ConditionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var roundNumber) &&
                roundNumber is >= 1 and <= 30,
            ShowRuleCondition.TeamIs =>
                settings.TeamCount > 1 &&
                Enumerable.Range(1, settings.TeamCount)
                    .Any(team => string.Equals(rule.ConditionValue, $"Team {team}", StringComparison.Ordinal)),
            ShowRuleCondition.ProfileIs =>
                settings.ProfileLabels.Any(label =>
                    string.Equals(rule.ConditionValue.Trim(), label.Trim(), StringComparison.OrdinalIgnoreCase)),
            ShowRuleCondition.QuestionTypeIs =>
                Enum.GetNames<QuestionType>().Contains(rule.ConditionValue, StringComparer.Ordinal),
            _ => false
        };
    }
}

/// <summary>Payload for joining a game with a display name.</summary>
public sealed record JoinGameRequest
{
    /// <summary>Player name shown to the host and scoreboard.</summary>
    [Required, StringLength(20, MinimumLength = 1)]
    public required string Username { get; init; }
}

/// <summary>Host-assigned player profile for this show.</summary>
public sealed record AssignPlayerProfileRequest
{
    /// <summary>Configured profile label, or null to clear the assignment.</summary>
    [StringLength(30)]
    public string? ProfileLabel { get; init; }
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

/// <summary>Unique game PIN and host key returned when a game is created.</summary>
public sealed record GameCreatedResponse(string Pin, string HostKey, string QuizName);

/// <summary>Player credentials returned after a successful join.</summary>
public sealed record PlayerJoinedResponse(string PlayerId, string Pin, string Username, string? TeamName);

/// <summary>Public scoreboard entry.</summary>
public sealed record PlayerScoreResponse(
    string Username,
    int Score,
    int? LastAnswerPoints,
    bool? LastAnswerCorrect,
    string? TeamName,
    string? ProfileLabel,
    string? PlayerId,
    int Currency);

/// <summary>Combined score for an automatically balanced team.</summary>
public sealed record TeamScoreResponse(string TeamName, int Score, int PlayerCount, int Currency);

/// <summary>Current question details, without revealing accepted answers early.</summary>
public sealed record QuestionResponse(
    string Prompt,
    QuestionType Type,
    IReadOnlyList<string> Options,
    int TimeLimitSeconds,
    IReadOnlyList<string> AcceptedAnswers);

/// <summary>A named response to a survey question.</summary>
public sealed record SurveyResponse(string Username, int? OptionIndex, string? AnswerText);

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
    ShowSettings ShowSettings,
    bool IsPaused,
    int? PausedSecondsRemaining,
    bool MyDoublePointsUsed,
    bool MyDoublePointsArmed,
    IReadOnlyList<TeamScoreResponse> Teams);

/// <summary>How a quiz question is answered and scored.</summary>
public enum QuestionType
{
    /// <summary>Players select one choice; only the marked choice is correct.</summary>
    MultipleChoice,
    /// <summary>Players select one choice; responses are gathered without scoring.</summary>
    Survey,
    /// <summary>Players type a response; a matching accepted answer earns points.</summary>
    TypeAnswer,
    /// <summary>Players predict an outcome and earn points for the revealed result.</summary>
    Prediction,
    /// <summary>Players choose between two options without a right answer.</summary>
    WouldYouRather,
    /// <summary>Players submit short free-text story contributions without scoring.</summary>
    StoryPrompt
}

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
