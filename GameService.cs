using System.Security.Cryptography;

namespace Paradox;

internal interface IGameService
{
    (GameCreatedResponse? Response, string? Error) Create(CreateGameRequest request);
    PlayerJoinResult Join(string pin, string username);
    GameSnapshotResponse? GetSnapshot(string pin, string? hostKey, string? playerId, out bool unauthorized);
    GameActionResult Start(string pin, string hostKey);
    GameActionResult SubmitAnswer(string pin, string playerId, int? optionIndex, string? answerText);
    GameActionResult Next(string pin, string hostKey);
}

internal enum GameActionStatus
{
    Success,
    NotFound,
    Unauthorized,
    Conflict,
    BadRequest
}

internal sealed record GameActionResult(GameActionStatus Status, GameSnapshotResponse? Snapshot, string? Error);
internal sealed record PlayerJoinResult(GameActionStatus Status, PlayerJoinedResponse? Response, string? Error);

internal sealed class GameService : IGameService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Game> _games = new(StringComparer.Ordinal);

    public (GameCreatedResponse? Response, string? Error) Create(CreateGameRequest request)
    {
        var title = request.QuizName?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(title))
            return (null, "Add a name for your quiz.");
        if (!QuizThemes.IsSupported(request.Theme))
            return (null, "Choose one of the available quiz themes.");
        if (description.Length > 280)
            return (null, "The quiz description must be 280 characters or fewer.");

        if (request.Questions is null || request.Questions.Count == 0 || request.Questions.Count > 30)
            return (null, "A quiz needs between 1 and 30 questions.");

        foreach (var question in request.Questions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.Prompt))
                return (null, "Every question needs a prompt.");
            if (!Enum.IsDefined(question.Type))
                return (null, "Choose a supported question type.");
            if (question.Type is QuestionType.MultipleChoice or QuestionType.Survey)
            {
                if (question.Options is null || question.Options.Count is < 2 or > 4)
                    return (null, "Multiple-choice and survey questions need between 2 and 4 choices.");
                if (question.Options.Any(string.IsNullOrWhiteSpace))
                    return (null, "Answer choices cannot be blank.");
                if (question.Type == QuestionType.MultipleChoice &&
                    (question.CorrectOptionIndex < 0 || question.CorrectOptionIndex >= question.Options.Count))
                    return (null, "Choose a correct answer for every multiple-choice question.");
            }
            else
            {
                if (question.AcceptedAnswers is null || question.AcceptedAnswers.Count is < 1 or > 10)
                    return (null, "Type-answer questions need between 1 and 10 accepted answers.");
                if (question.AcceptedAnswers.Any(answer => string.IsNullOrWhiteSpace(answer) || answer.Trim().Length > 100))
                    return (null, "Accepted answers must contain between 1 and 100 characters.");
            }
            if (question.TimeLimitSeconds is < 5 or > 60)
                return (null, "Question timers must be between 5 and 60 seconds.");
        }

        var normalizedQuestions = request.Questions
            .Select(question => question with
            {
                Prompt = question.Prompt.Trim(),
                Options = question.Type is QuestionType.MultipleChoice or QuestionType.Survey
                    ? question.Options.Select(option => option.Trim()).ToList()
                    : [],
                AcceptedAnswers = question.Type == QuestionType.TypeAnswer
                    ? question.AcceptedAnswers
                    .Select(answer => answer.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    : []
            })
            .ToArray();

        lock (_gate)
        {
            string pin;
            do
            {
                pin = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            } while (_games.ContainsKey(pin));

            var hostKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            _games.Add(pin, new Game(pin, hostKey, title, description, request.Theme, normalizedQuestions));
            return (new GameCreatedResponse(pin, hostKey, title), null);
        }
    }

    public PlayerJoinResult Join(string pin, string username)
    {
        lock (_gate)
        {
            if (!_games.TryGetValue(NormalizePin(pin), out var game))
                return new(GameActionStatus.NotFound, null, "That game PIN was not found. Check it and try again.");
            if (game.Status != GameStatus.Lobby)
                return new(GameActionStatus.Conflict, null, "This game has already started.");

            var name = username.Trim();
            if (name.Length is < 1 or > 20)
                return new(GameActionStatus.BadRequest, null, "Your name must be between 1 and 20 characters.");
            if (game.Players.Values.Any(player => string.Equals(player.Username, name, StringComparison.OrdinalIgnoreCase)))
                return new(GameActionStatus.Conflict, null, "That name is already in this game. Try another one.");

            var playerId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            game.Players.Add(playerId, new Player(playerId, name));
            game.LeaderboardDirty = true;
            return new(GameActionStatus.Success, new PlayerJoinedResponse(playerId, game.Pin, name), null);
        }
    }

    public GameSnapshotResponse? GetSnapshot(string pin, string? hostKey, string? playerId, out bool unauthorized)
    {
        lock (_gate)
        {
            unauthorized = false;
            if (!_games.TryGetValue(NormalizePin(pin), out var game))
                return null;

            var isHost = !string.IsNullOrEmpty(hostKey);
            if (isHost && !FixedTimeEquals(game.HostKey, hostKey!))
            {
                unauthorized = true;
                return null;
            }

            AdvanceExpiredQuestion(game);
            return Snapshot(game, playerId, isHost);
        }
    }

    public GameActionResult Start(string pin, string hostKey)
    {
        lock (_gate)
        {
            if (!TryGetHostGame(pin, hostKey, out var game, out var error))
                return error!;
            if (game.Status != GameStatus.Lobby)
                return Conflict("This game has already started.");
            if (game.Players.Count == 0)
                return Conflict("Wait for at least one player to join before starting.");

            game.CurrentQuestionIndex = 0;
            BeginQuestion(game);
            return Success(game);
        }
    }

    public GameActionResult SubmitAnswer(string pin, string playerId, int? optionIndex, string? answerText)
    {
        lock (_gate)
        {
            if (!_games.TryGetValue(NormalizePin(pin), out var game))
                return NotFound();
            if (!game.Players.TryGetValue(playerId, out var player))
                return new(GameActionStatus.Unauthorized, null, "Your player session could not be found.");

            AdvanceExpiredQuestion(game);
            if (game.Status != GameStatus.Question)
                return Conflict("This question is no longer accepting answers.");

            var question = game.Questions[game.CurrentQuestionIndex];
            if (question.Type is QuestionType.MultipleChoice or QuestionType.Survey)
            {
                if (optionIndex is null || optionIndex < 0 || optionIndex >= question.Options.Count || answerText is not null)
                    return new(GameActionStatus.BadRequest, null, "Choose one of the available answer choices.");
            }
            else
            {
                if (optionIndex is not null || string.IsNullOrWhiteSpace(answerText) || answerText.Trim().Length > 100)
                    return new(GameActionStatus.BadRequest, null, "Enter an answer between 1 and 100 characters.");
                answerText = answerText.Trim();
            }
            if (player.Answers.ContainsKey(game.CurrentQuestionIndex))
                return Conflict("You have already answered this question.");

            player.Answers[game.CurrentQuestionIndex] = new PlayerAnswer(optionIndex, answerText);
            var isCorrect = question.Type switch
            {
                QuestionType.Survey => false,
                QuestionType.MultipleChoice => optionIndex == question.CorrectOptionIndex,
                QuestionType.TypeAnswer => question.AcceptedAnswers.Any(accepted =>
                    string.Equals(accepted, answerText, StringComparison.OrdinalIgnoreCase)),
                _ => false
            };
            if (isCorrect)
            {
                var remaining = Math.Max(0, (game.EndsAtUtc!.Value - DateTimeOffset.UtcNow).TotalSeconds);
                player.LastAnswerPoints = 500 + (int)Math.Round(500 * remaining / question.TimeLimitSeconds);
                player.Score += player.LastAnswerPoints.Value;
            }
            else
            {
                player.LastAnswerPoints = 0;
            }

            game.LeaderboardDirty = true;
            return Success(game, playerId);
        }
    }

    public GameActionResult Next(string pin, string hostKey)
    {
        lock (_gate)
        {
            if (!TryGetHostGame(pin, hostKey, out var game, out var error))
                return error!;

            AdvanceExpiredQuestion(game);
            if (game.Status != GameStatus.Reveal)
                return Conflict("Wait for the question timer to finish before continuing.");

            if (game.CurrentQuestionIndex + 1 >= game.Questions.Count)
            {
                game.Status = GameStatus.Finished;
                game.EndsAtUtc = null;
                return Success(game);
            }

            game.CurrentQuestionIndex++;
            BeginQuestion(game);
            return Success(game);
        }
    }

    private bool TryGetHostGame(string pin, string hostKey, out Game game, out GameActionResult? error)
    {
        if (!_games.TryGetValue(NormalizePin(pin), out game!))
        {
            error = NotFound();
            return false;
        }
        if (!FixedTimeEquals(game.HostKey, hostKey))
        {
            error = new(GameActionStatus.Unauthorized, null, "Host access could not be verified.");
            return false;
        }
        error = null;
        return true;
    }

    private static void BeginQuestion(Game game)
    {
        game.Status = GameStatus.Question;
        game.EndsAtUtc = DateTimeOffset.UtcNow.AddSeconds(game.Questions[game.CurrentQuestionIndex].TimeLimitSeconds);
        foreach (var player in game.Players.Values)
            player.LastAnswerPoints = null;
        game.LeaderboardDirty = true;
    }

    private static void AdvanceExpiredQuestion(Game game)
    {
        if (game.Status == GameStatus.Question && game.EndsAtUtc <= DateTimeOffset.UtcNow)
        {
            game.Status = GameStatus.Reveal;
            game.EndsAtUtc = null;
        }
    }

    private static GameSnapshotResponse Snapshot(Game game, string? playerId, bool isHost = false)
    {
        var question = game.CurrentQuestionIndex >= 0
            ? game.Questions[game.CurrentQuestionIndex]
            : null;
        if (game.LeaderboardDirty)
        {
            game.Leaderboard = game.Players.Values
                .OrderByDescending(player => player.Score)
                .ThenBy(player => player.Username, StringComparer.OrdinalIgnoreCase)
                .Select(player => new PlayerScoreResponse(player.Username, player.Score, player.LastAnswerPoints))
                .ToArray();
            game.LeaderboardDirty = false;
        }
        PlayerAnswer? myAnswer = playerId is not null &&
                                 game.Players.TryGetValue(playerId, out var player) &&
                                 game.CurrentQuestionIndex >= 0 &&
                                 player.Answers.TryGetValue(game.CurrentQuestionIndex, out var answer)
            ? answer
            : null;
        var surveyResponses = isHost &&
                          question?.Type == QuestionType.Survey &&
                          game.Status is GameStatus.Reveal or GameStatus.Finished
            ? game.Players.Values
                .Where(player => player.Answers.ContainsKey(game.CurrentQuestionIndex))
                .Select(player =>
                {
                    var response = player.Answers[game.CurrentQuestionIndex];
                    return new SurveyResponse(player.Username, response.OptionIndex, response.AnswerText);
                })
                .ToArray()
            : null;

        return new GameSnapshotResponse(
            game.Pin,
            game.QuizName,
            game.Description,
            game.Theme,
            game.Status,
            game.Players.Count,
            game.Leaderboard,
            question is null ? null : new QuestionResponse(
                question.Prompt,
                question.Type,
                question.Options,
                question.TimeLimitSeconds,
                question.Type == QuestionType.TypeAnswer &&
                    (game.Status is GameStatus.Reveal or GameStatus.Finished)
                    ? question.AcceptedAnswers
                    : []),
            game.CurrentQuestionIndex + 1,
            game.Questions.Count,
            game.EndsAtUtc,
            (game.Status is GameStatus.Reveal or GameStatus.Finished) &&
                question?.Type == QuestionType.MultipleChoice ? question.CorrectOptionIndex : null,
            myAnswer?.OptionIndex,
            myAnswer?.AnswerText,
            surveyResponses);
    }

    private static GameActionResult Success(Game game, string? playerId = null) =>
        new(GameActionStatus.Success, Snapshot(game, playerId), null);

    private static GameActionResult NotFound() =>
        new(GameActionStatus.NotFound, null, "That game PIN was not found.");

    private static GameActionResult Conflict(string message) =>
        new(GameActionStatus.Conflict, null, message);

    private static string NormalizePin(string pin) => pin.Trim();

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private sealed class Game(
        string pin,
        string hostKey,
        string quizName,
        string description,
        string theme,
        IReadOnlyList<QuizQuestionRequest> questions)
    {
        public string Pin { get; } = pin;
        public string HostKey { get; } = hostKey;
        public string QuizName { get; } = quizName;
        public string Description { get; } = description;
        public string Theme { get; } = theme;
        public IReadOnlyList<QuizQuestionRequest> Questions { get; } = questions;
        public Dictionary<string, Player> Players { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<PlayerScoreResponse> Leaderboard { get; set; } = [];
        public bool LeaderboardDirty { get; set; } = true;
        public GameStatus Status { get; set; } = GameStatus.Lobby;
        public int CurrentQuestionIndex { get; set; } = -1;
        public DateTimeOffset? EndsAtUtc { get; set; }
    }

    private sealed class Player(string id, string username)
    {
        public string Id { get; } = id;
        public string Username { get; } = username;
        public int Score { get; set; }
        public int? LastAnswerPoints { get; set; }
        public Dictionary<int, PlayerAnswer> Answers { get; } = [];
    }

    private sealed record PlayerAnswer(int? OptionIndex, string? AnswerText);
}
