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
            SELECT id, quiz_name, description, theme, questions_json, created_at, updated_at
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
            SELECT id, quiz_name, description, theme, questions_json, created_at, updated_at
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
        var quiz = ToResponse(id, request, now, now);

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
                INSERT INTO saved_quizzes (id, owner_hash, quiz_name, description, theme, questions_json, created_at, updated_at)
                VALUES ($id, $owner_hash, $quiz_name, $description, $theme, $questions_json, $created_at, $updated_at);
                """;
            AddQuizParameters(command, id, ownerHash, request, now, now);
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
                updated_at = $updated_at
            WHERE id = $id AND owner_hash = $owner_hash;
            """;
        AddQuizParameters(command, id, ownerHash, request, createdAt: null, DateTimeOffset.UtcNow);
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
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_saved_quizzes_owner_updated
                ON saved_quizzes (owner_hash, updated_at DESC);
            """;
        command.ExecuteNonQuery();
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
        foreach (var question in request.Questions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.Prompt))
                return "Every question needs a prompt.";
            if (question.Options is null ||
                question.Options.Count is < 2 or > 4 ||
                question.Options.Any(string.IsNullOrWhiteSpace) ||
                question.CorrectOptionIndex < 0 ||
                question.CorrectOptionIndex >= question.Options.Count ||
                question.TimeLimitSeconds is < 5 or > 60)
                return "Check the answer choices, correct answer, and timer on each question.";
        }
        return null;
    }

    private static SavedQuizResponse ReadQuiz(SqliteDataReader reader)
    {
        var createdAt = DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture);
        var updatedAt = DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture);
        var questions = JsonSerializer.Deserialize<List<QuizQuestionRequest>>(reader.GetString(4), JsonOptions)
            ?? throw new InvalidDataException("A saved quiz contains invalid question data.");
        return new SavedQuizResponse(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            questions,
            createdAt,
            updatedAt);
    }

    private static SavedQuizResponse ToResponse(string id, SaveQuizRequest request, DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
        new(id, request.QuizName.Trim(), request.Description?.Trim() ?? "", request.Theme, request.Questions, createdAt, updatedAt);

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
        command.Parameters.AddWithValue("$created_at", createdAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", updatedAt.ToString("O"));
    }

    private static string HashOwnerKey(string ownerKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerKey)));

    private static QuizLibraryResult Invalid(string message) =>
        new(QuizLibraryStatus.Invalid, null, message);
}
