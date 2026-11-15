using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Paradox;

internal interface IQuizLibraryService
{
    Task<IReadOnlyList<SavedQuizResponse>> ListAsync(string ownerKey, CancellationToken cancellationToken);
    Task<SavedQuizResponse?> GetAsync(string id, string ownerKey, CancellationToken cancellationToken);
    Task<QuizLibraryResult> CreateAsync(SaveQuizRequest request, string ownerKey, CancellationToken cancellationToken);
    Task<QuizLibraryResult> UpdateAsync(string id, SaveQuizRequest request, string ownerKey, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string id, string ownerKey, CancellationToken cancellationToken);
}

internal enum QuizLibraryStatus
{
    Success,
    NotFound,
    Invalid,
    LimitReached
}

internal sealed record QuizLibraryResult(QuizLibraryStatus Status, SavedQuizResponse? Quiz, string? Error);

internal sealed class QuizLibraryService : IQuizLibraryService
{
    private const int MaximumQuizzesPerOwner = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public QuizLibraryService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "paradox.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
        InitializeDatabase();
    }

    public async Task<IReadOnlyList<SavedQuizResponse>> ListAsync(string ownerKey, CancellationToken cancellationToken)
    {
        var ownerHash = HashOwnerKey(ownerKey);
        var quizzes = new List<SavedQuizResponse>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, quiz_name, description, theme, questions_json, show_settings_json, created_at, updated_at
            FROM saved_quizzes
            WHERE owner_hash = $owner_hash
            ORDER BY updated_at DESC;
            """;
        command.Parameters.AddWithValue("$owner_hash", ownerHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            quizzes.Add(ReadQuiz(reader));
        return quizzes;
    }

    public async Task<SavedQuizResponse?> GetAsync(string id, string ownerKey, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, quiz_name, description, theme, questions_json, show_settings_json, created_at, updated_at
            FROM saved_quizzes
            WHERE id = $id AND owner_hash = $owner_hash;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$owner_hash", HashOwnerKey(ownerKey));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadQuiz(reader) : null;
    }

    public async Task<QuizLibraryResult> CreateAsync(
        SaveQuizRequest request,
        string ownerKey,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null)
            return Invalid(validationError);

        var ownerHash = HashOwnerKey(ownerKey);
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var normalizedRequest = Normalize(request);
        var quiz = ToResponse(id, normalizedRequest, now, now);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM saved_quizzes WHERE owner_hash = $owner_hash;";
            countCommand.Parameters.AddWithValue("$owner_hash", ownerHash);
            var count = (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (count >= MaximumQuizzesPerOwner)
                return new(QuizLibraryStatus.LimitReached, null, $"A library can contain up to {MaximumQuizzesPerOwner} quizzes.");
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO saved_quizzes (id, owner_hash, quiz_name, description, theme, questions_json, show_settings_json, created_at, updated_at)
                VALUES ($id, $owner_hash, $quiz_name, $description, $theme, $questions_json, $show_settings_json, $created_at, $updated_at);
                """;
            AddQuizParameters(command, id, ownerHash, normalizedRequest, now, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(QuizLibraryStatus.Success, quiz, null);
    }

    public async Task<QuizLibraryResult> UpdateAsync(
        string id,
        SaveQuizRequest request,
        string ownerKey,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null)
            return Invalid(validationError);

        var ownerHash = HashOwnerKey(ownerKey);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE saved_quizzes
            SET quiz_name = $quiz_name,
                description = $description,
                theme = $theme,
                questions_json = $questions_json,
                show_settings_json = $show_settings_json,
                updated_at = $updated_at
            WHERE id = $id AND owner_hash = $owner_hash;
            """;
        AddQuizParameters(command, id, ownerHash, Normalize(request), createdAt: null, DateTimeOffset.UtcNow);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed == 0)
            return new(QuizLibraryStatus.NotFound, null, null);

        var quiz = await GetAsync(id, ownerKey, cancellationToken);
        return quiz is null
            ? new(QuizLibraryStatus.NotFound, null, null)
            : new(QuizLibraryStatus.Success, quiz, null);
    }

    public async Task<bool> DeleteAsync(string id, string ownerKey, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM saved_quizzes WHERE id = $id AND owner_hash = $owner_hash;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$owner_hash", HashOwnerKey(ownerKey));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS saved_quizzes (
                id TEXT NOT NULL PRIMARY KEY,
                owner_hash TEXT NOT NULL,
                quiz_name TEXT NOT NULL,
                description TEXT NOT NULL,
                theme TEXT NOT NULL,
                questions_json TEXT NOT NULL,
                show_settings_json TEXT NOT NULL DEFAULT '{}',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_saved_quizzes_owner_updated
                ON saved_quizzes (owner_hash, updated_at DESC);
            """;
        command.ExecuteNonQuery();
        using var migration = connection.CreateCommand();
        migration.CommandText = "PRAGMA table_info(saved_quizzes);";
        using var reader = migration.ExecuteReader();
        var hasShowSettings = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), "show_settings_json", StringComparison.Ordinal))
                hasShowSettings = true;
        }
        reader.Close();
        if (!hasShowSettings)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE saved_quizzes ADD COLUMN show_settings_json TEXT NOT NULL DEFAULT '{}';";
            alter.ExecuteNonQuery();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string? Validate(SaveQuizRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.QuizName))
            return "Add a name for your quiz.";
        if (request.Questions is null || request.Questions.Count is < 1 or > 30)
            return "A quiz needs between 1 and 30 questions.";
        if (request.Description?.Length > 280)
            return "The quiz description must be 280 characters or fewer.";
        if (!QuizThemes.IsSupported(request.Theme))
            return "Choose one of the available quiz themes.";
        if (request.ShowSettings is null ||
            request.ShowSettings.PlayerLimit is < 1 or > 40 ||
            request.ShowSettings.TeamCount is < 1 or > 8 ||
            request.ShowSettings.TeamCount > request.ShowSettings.PlayerLimit ||
            request.ShowSettings.BasePoints is < 0 or > 5000 ||
            request.ShowSettings.SpeedBonusPoints is < 0 or > 5000)
            return "Set a player limit from 1 to 40 and a team count from 1 to 8, no greater than the player limit.";
        if (request.ShowSettings.ProfileLabels is null ||
            request.ShowSettings.ProfileLabels.Count > 4 ||
            request.ShowSettings.ProfileLabels.Any(label => string.IsNullOrWhiteSpace(label) || label.Trim().Length > 30) ||
            request.ShowSettings.ProfileLabels.Select(label => label.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.ShowSettings.ProfileLabels.Count)
            return "Add up to four unique profile labels, each 30 characters or fewer.";
        if (string.IsNullOrWhiteSpace(request.ShowSettings.CurrencyName) ||
            request.ShowSettings.CurrencyName.Trim().Length > 20 ||
            request.ShowSettings.RuleBlocks is null ||
            request.ShowSettings.RuleBlocks.Count > 20 ||
            request.ShowSettings.RuleBlocks.Any(rule => !ShowRuleValidation.IsValid(rule, request.ShowSettings)))
            return "Choose a currency name and up to 20 valid show-program blocks.";
        foreach (var question in request.Questions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.Prompt))
                return "Every question needs a prompt.";
            if (question.ProfilePrompts is null ||
                question.ProfilePrompts.Count > request.ShowSettings.ProfileLabels.Count ||
                question.ProfilePrompts.Keys.Select(label => label.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != question.ProfilePrompts.Count ||
                question.ProfilePrompts.Any(variant =>
                    string.IsNullOrWhiteSpace(variant.Value) ||
                    variant.Value.Trim().Length > 240 ||
                    !request.ShowSettings.ProfileLabels.Any(label =>
                        string.Equals(label.Trim(), variant.Key.Trim(), StringComparison.OrdinalIgnoreCase))))
                return "Each profile-specific question must use a configured profile and contain 1 to 240 characters.";
            if (!Enum.IsDefined(question.Type) || question.TimeLimitSeconds is < 5 or > 60)
                return "Check the question type and timer on each question.";
            if (question.Type is QuestionType.MultipleChoice or QuestionType.Survey or QuestionType.Prediction or QuestionType.WouldYouRather)
            {
                if (question.Options is null ||
                    question.Options.Count is < 2 or > 4 ||
                    question.Options.Any(string.IsNullOrWhiteSpace) ||
                    (question.Type == QuestionType.WouldYouRather && question.Options.Count != 2) ||
                    (question.Type is (QuestionType.MultipleChoice or QuestionType.Prediction) &&
                     (question.CorrectOptionIndex < 0 || question.CorrectOptionIndex >= question.Options.Count)))
                    return "Check the answer choices and outcome on each question.";
            }
            else if (question.Type == QuestionType.TypeAnswer &&
                     (question.AcceptedAnswers is null ||
                     question.AcceptedAnswers.Count is < 1 or > 10 ||
                     question.AcceptedAnswers.Any(answer => string.IsNullOrWhiteSpace(answer) || answer.Trim().Length > 100)))
            {
                return "Type-answer questions need between 1 and 10 accepted answers, each 100 characters or fewer.";
            }
            else if (question.Type == QuestionType.StoryPrompt && question.AcceptedAnswers is { Count: > 0 })
            {
                return "Story prompts do not use accepted answers.";
            }
        }
        return null;
    }

    private static SaveQuizRequest Normalize(SaveQuizRequest request)
    {
        var labels = request.ShowSettings.ProfileLabels.Select(label => label.Trim()).ToList();
        return request with
        {
            QuizName = request.QuizName.Trim(),
            Description = request.Description?.Trim() ?? "",
            ShowSettings = request.ShowSettings with
            {
                ProfileLabels = labels,
                CurrencyName = request.ShowSettings.CurrencyName.Trim(),
                RuleBlocks = request.ShowSettings.RuleBlocks
                    .Select(rule => rule with { ConditionValue = rule.ConditionValue?.Trim() ?? "" })
                    .ToList()
            },
            Questions = request.Questions.Select(question => question with
            {
                Prompt = question.Prompt.Trim(),
                ProfilePrompts = question.ProfilePrompts
                    .Where(variant => !string.IsNullOrWhiteSpace(variant.Value))
                    .ToDictionary(
                        variant => labels.First(label =>
                            string.Equals(label, variant.Key.Trim(), StringComparison.OrdinalIgnoreCase)),
                        variant => variant.Value.Trim(),
                        StringComparer.OrdinalIgnoreCase)
            }).ToList()
        };
    }

    private static SavedQuizResponse ReadQuiz(SqliteDataReader reader)
    {
        var createdAt = DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture);
        var updatedAt = DateTimeOffset.Parse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture);
        var questions = JsonSerializer.Deserialize<List<QuizQuestionRequest>>(reader.GetString(4), JsonOptions)
            ?? throw new InvalidDataException("A saved quiz contains invalid question data.");
        var showSettings = JsonSerializer.Deserialize<ShowSettings>(reader.GetString(5), JsonOptions)
            ?? new ShowSettings();
        return new SavedQuizResponse(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            questions,
            showSettings,
            createdAt,
            updatedAt);
    }

    private static SavedQuizResponse ToResponse(string id, SaveQuizRequest request, DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
        new(id, request.QuizName.Trim(), request.Description?.Trim() ?? "", request.Theme, request.Questions, request.ShowSettings, createdAt, updatedAt);

    private static void AddQuizParameters(
        SqliteCommand command,
        string id,
        string ownerHash,
        SaveQuizRequest request,
        DateTimeOffset? createdAt,
        DateTimeOffset updatedAt)
    {
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$owner_hash", ownerHash);
        command.Parameters.AddWithValue("$quiz_name", request.QuizName.Trim());
        command.Parameters.AddWithValue("$description", request.Description?.Trim() ?? "");
        command.Parameters.AddWithValue("$theme", request.Theme);
        command.Parameters.AddWithValue("$questions_json", JsonSerializer.Serialize(request.Questions, JsonOptions));
        command.Parameters.AddWithValue("$show_settings_json", JsonSerializer.Serialize(request.ShowSettings, JsonOptions));
        command.Parameters.AddWithValue("$created_at", createdAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", updatedAt.ToString("O"));
    }

    private static string HashOwnerKey(string ownerKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerKey)));

    private static QuizLibraryResult Invalid(string message) =>
        new(QuizLibraryStatus.Invalid, null, message);
}
