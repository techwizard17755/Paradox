using System.ComponentModel.DataAnnotations;

namespace Paradox;

/// <summary>Content and appearance settings used when saving a quiz.</summary>
public sealed record SaveQuizRequest
{
    /// <summary>Name displayed in the quiz library and game.</summary>
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

/// <summary>A saved quiz available to its creator.</summary>
public sealed record SavedQuizResponse(
    string Id,
    string QuizName,
    string Description,
    string Theme,
    IReadOnlyList<QuizQuestionRequest> Questions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A newly saved quiz and its private management key.</summary>
public sealed record SavedQuizCreatedResponse(SavedQuizResponse Quiz, string OwnerKey);

/// <summary>One of the ready-to-use quiz appearance themes.</summary>
public sealed record QuizThemeResponse(string Id, string Name, string AccentColor);

internal static class QuizThemes
{
    private static readonly QuizThemeResponse[] AllThemes =
    [
        new("violet", "Dreamscape", "#A98BFF"),
        new("ocean", "Ocean", "#66E4D1"),
        new("citrus", "Citrus", "#D3FF72"),
        new("coral", "Coral", "#FF887C"),
        new("sunset", "Sunset", "#FFB36B")
    ];

    public static bool IsSupported(string? theme) =>
        AllThemes.Any(candidate => string.Equals(candidate.Id, theme, StringComparison.Ordinal));

    public static IReadOnlyList<QuizThemeResponse> List() => AllThemes;
}
