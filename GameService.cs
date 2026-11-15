using System.Globalization;
using System.Security.Cryptography;

namespace Paradox;

internal interface IGameService
{
    (GameCreatedResponse? Response, string? Error) Create(CreateGameRequest request);
    PlayerJoinResult Join(string pin, string username);
    GameSnapshotResponse? GetSnapshot(string pin, string? hostKey, string? playerId, out bool unauthorized);
    GameActionResult Start(string pin, string hostKey);
    GameActionResult Pause(string pin, string hostKey);
    GameActionResult Resume(string pin, string hostKey);
    GameActionResult Reveal(string pin, string hostKey);
    GameActionResult Skip(string pin, string hostKey);
    GameActionResult KickPlayer(string pin, string hostKey, string playerId);
    GameActionResult AssignProfile(string pin, string hostKey, string playerId, string? profileLabel);
    GameActionResult SubmitAnswer(string pin, string playerId, int? optionIndex, string? answerText);
    GameActionResult ArmDoublePoints(string pin, string playerId);
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
        if (request.ShowSettings is null ||
            request.ShowSettings.PlayerLimit is < 1 or > 40 ||
            request.ShowSettings.TeamCount is < 1 or > 8 ||
            request.ShowSettings.TeamCount > request.ShowSettings.PlayerLimit ||
            request.ShowSettings.BasePoints is < 0 or > 5000 ||
            request.ShowSettings.SpeedBonusPoints is < 0 or > 5000)
            return (null, "Set a player limit from 1 to 40 and a team count from 1 to 8, no greater than the player limit.");
        if (request.ShowSettings.ProfileLabels is null ||
            request.ShowSettings.ProfileLabels.Count > 4 ||
            request.ShowSettings.ProfileLabels.Any(label => string.IsNullOrWhiteSpace(label) || label.Trim().Length > 30) ||
            request.ShowSettings.ProfileLabels.Select(label => label.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.ShowSettings.ProfileLabels.Count)
            return (null, "Add up to four unique profile labels, each 30 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.ShowSettings.CurrencyName) ||
            request.ShowSettings.CurrencyName.Trim().Length > 20 ||
            request.ShowSettings.RuleBlocks is null ||
            request.ShowSettings.RuleBlocks.Count > 20 ||
            request.ShowSettings.RuleBlocks.Any(rule => !ShowRuleValidation.IsValid(rule, request.ShowSettings)))
            return (null, "Choose a currency name and up to 20 valid show-program blocks.");

        if (request.Questions is null || request.Questions.Count == 0 || request.Questions.Count > 30)
            return (null, "A quiz needs between 1 and 30 questions.");

        foreach (var question in request.Questions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.Prompt))
                return (null, "Every question needs a prompt.");
            if (question.ProfilePrompts is null ||
                question.ProfilePrompts.Count > request.ShowSettings.ProfileLabels.Count ||
                question.ProfilePrompts.Keys.Select(label => label.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != question.ProfilePrompts.Count ||
                question.ProfilePrompts.Any(variant =>
                    string.IsNullOrWhiteSpace(variant.Value) ||
                    variant.Value.Trim().Length > 240 ||
                    !request.ShowSettings.ProfileLabels.Any(label =>
                        string.Equals(label.Trim(), variant.Key.Trim(), StringComparison.OrdinalIgnoreCase))))
                return (null, "Each profile-specific question must use a configured profile and contain 1 to 240 characters.");
            if (!Enum.IsDefined(question.Type))
                return (null, "Choose a supported question type.");
            if (question.Type is QuestionType.MultipleChoice or QuestionType.Survey or QuestionType.Prediction or QuestionType.WouldYouRather)
            {
                if (question.Options is null || question.Options.Count is < 2 or > 4)
                    return (null, "Multiple-choice and survey questions need between 2 and 4 choices.");
                if (question.Options.Any(string.IsNullOrWhiteSpace))
                    return (null, "Answer choices cannot be blank.");
                if (question.Type == QuestionType.WouldYouRather && question.Options.Count != 2)
                    return (null, "Would-you-rather questions need exactly two choices.");
                if (question.Type is (QuestionType.MultipleChoice or QuestionType.Prediction) &&
                    (question.CorrectOptionIndex < 0 || question.CorrectOptionIndex >= question.Options.Count))
                    return (null, "Choose the outcome that scores as correct.");
            }
            else if (question.Type is QuestionType.TypeAnswer or QuestionType.StoryPrompt)
            {
                if (question.Type == QuestionType.TypeAnswer &&
                    (question.AcceptedAnswers is null || question.AcceptedAnswers.Count is < 1 or > 10))
                    return (null, "Type-answer questions need between 1 and 10 accepted answers.");
                if (question.Type == QuestionType.TypeAnswer &&
                    question.AcceptedAnswers.Any(answer => string.IsNullOrWhiteSpace(answer) || answer.Trim().Length > 100))
                    return (null, "Accepted answers must contain between 1 and 100 characters.");
                if (question.Type == QuestionType.StoryPrompt && question.AcceptedAnswers is { Count: > 0 })
                    return (null, "Story prompts do not use accepted answers.");
            }
            if (question.TimeLimitSeconds is < 5 or > 60)
                return (null, "Question timers must be between 5 and 60 seconds.");
        }

        var normalizedQuestions = request.Questions
            .Select(question => question with
            {
                Prompt = question.Prompt.Trim(),
                    ProfilePrompts = question.ProfilePrompts
                        .Where(variant => !string.IsNullOrWhiteSpace(variant.Value))
                        .ToDictionary(
                            variant => request.ShowSettings.ProfileLabels.First(label =>
                                string.Equals(label.Trim(), variant.Key.Trim(), StringComparison.OrdinalIgnoreCase)),
                            variant => variant.Value.Trim(),
                            StringComparer.OrdinalIgnoreCase),
                Options = question.Type is QuestionType.MultipleChoice or QuestionType.Survey or QuestionType.Prediction or QuestionType.WouldYouRather
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
        var normalizedShowSettings = request.ShowSettings with
        {
            ProfileLabels = request.ShowSettings.ProfileLabels.Select(label => label.Trim()).ToList(),
            CurrencyName = request.ShowSettings.CurrencyName.Trim(),
            RuleBlocks = request.ShowSettings.RuleBlocks
                .Select(rule => rule with { ConditionValue = rule.ConditionValue?.Trim() ?? "" })
                .ToList()
        };

        lock (_gate)
        {
            string pin;
            do
            {
                pin = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            } while (_games.ContainsKey(pin));

            var hostKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            _games.Add(pin, new Game(pin, hostKey, title, description, request.Theme, normalizedQuestions, normalizedShowSettings));
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
            if (game.Players.Count >= game.ShowSettings.PlayerLimit)
                return new(GameActionStatus.Conflict, null, "This show has reached its player limit.");

            var name = username.Trim();
            if (name.Length is < 1 or > 20)
                return new(GameActionStatus.BadRequest, null, "Your name must be between 1 and 20 characters.");
            if (game.Players.Values.Any(player => string.Equals(player.Username, name, StringComparison.OrdinalIgnoreCase)))
                return new(GameActionStatus.Conflict, null, "That name is already in this game. Try another one.");

            var playerId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var teamName = GetSmallestTeam(game);
            var player = new Player(playerId, name, teamName);
            game.Players.Add(playerId, player);
            ApplyRuleBlocks(game, player, ShowRuleTrigger.PlayerJoined);
            game.LeaderboardDirty = true;
            return new(GameActionStatus.Success, new PlayerJoinedResponse(playerId, game.Pin, name, teamName), null);
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
            if (!isHost && !string.IsNullOrEmpty(playerId) && !game.Players.ContainsKey(playerId))
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
            return Success(game, isHost: true);
        }
    }

    public GameActionResult Pause(string pin, string hostKey)
    {
        lock (_gate)
        {
            if (!TryGetHostGame(pin, hostKey, out var game, out var error))
                return error!;
            AdvanceExpiredQuestion(game);
            if (game.Status != GameStatus.Question || game.IsPaused || game.EndsAtUtc is null)
                return Conflict("Only an active question can be paused.");

            game.PausedRemaining = game.EndsAtUtc.Value - DateTimeOffset.UtcNow;
            game.IsPaused = true;
            game.EndsAtUtc = null;
            return Success(game, isHost: true);
        }
    }

    public GameActionResult Resume(string pin, string hostKey)
    {
        lock (_gate)
        {
            if (!TryGetHostGame(pin, hostKey, out var game, out var error))
                return error!;
            if (game.Status != GameStatus.Question || !game.IsPaused)
                return Conflict("This question is not paused.");

            game.EndsAtUtc = DateTimeOffset.UtcNow + (game.PausedRemaining ?? TimeSpan.Zero);
            game.PausedRemaining = null;
            game.IsPaused = false;
            return Success(game, isHost: true);
        }
    }

    public GameActionResult Reveal(string pin, string hostKey)
    {
        lock (_gate)
        {
            if (!TryGetHostGame(pin, hostKey, out var game, out var error))
                return error!;
            if (game.Status != GameStatus.Question)
                return Conflict("There is no active question to reveal.");

            game.IsPaused = false;
            game.PausedRemaining = null;
            game.EndsAtUtc = null;
            game.Status = GameStatus.Reveal;
            return Success(game, isHost: true);
        }
    }

    public GameActionResult Skip(string pin, string hostKey)
    {
        lock (_gate)
        {
            if (!TryGetHostGame(pin, hostKey, out var game, out var error))
                return error!;
            if (game.Status is not (GameStatus.Question or GameStatus.Reveal))
                return Conflict("There is no active question to skip.");

            AdvanceToNextQuestion(game);
            return Success(game, isHost: true);
        }
    }

    public GameActionResult KickPlayer(string pin, string hostKey, string playerId)
    {
        lock (_gate)
        {
            if (!TryGetHostGame(pin, hostKey, out var game, out var error))
                return error!;
            if (game.Status == GameStatus.Finished)
                return Conflict("The show has already finished.");
            if (!game.Players.Remove(playerId))
                return NotFound();

            game.LeaderboardDirty = true;
            return Success(game, isHost: true);
        }
    }

    public GameActionResult AssignProfile(string pin, string hostKey, string playerId, string? profileLabel)
    {
        lock (_gate)
        {
            if (!TryGetHostGame(pin, hostKey, out var game, out var error))
                return error!;
            if (game.Status != GameStatus.Lobby)
                return Conflict("Player profiles can only be changed in the lobby.");
            if (!game.Players.TryGetValue(playerId, out var player))
                return NotFound();

            var normalizedLabel = string.IsNullOrWhiteSpace(profileLabel) ? null : profileLabel.Trim();
            if (normalizedLabel is not null &&
                !game.ShowSettings.ProfileLabels.Contains(normalizedLabel, StringComparer.OrdinalIgnoreCase))
                return new(GameActionStatus.BadRequest, null, "Choose one of this show's profile labels.");

            player.ProfileLabel = normalizedLabel;
            game.LeaderboardDirty = true;
            return Success(game, isHost: true);
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
            if (game.Status != GameStatus.Question || game.IsPaused)
                return Conflict("This question is no longer accepting answers.");

            var question = game.Questions[game.CurrentQuestionIndex];
            if (question.Type is QuestionType.MultipleChoice or QuestionType.Survey or QuestionType.Prediction or QuestionType.WouldYouRather)
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
            var doublePointsArmed = player.DoublePointsArmed;
            player.DoublePointsArmed = false;
            var isCorrect = question.Type switch
            {
                QuestionType.Survey or QuestionType.WouldYouRather or QuestionType.StoryPrompt => false,
                QuestionType.MultipleChoice or QuestionType.Prediction => optionIndex == question.CorrectOptionIndex,
                QuestionType.TypeAnswer => question.AcceptedAnswers.Any(accepted =>
                    string.Equals(accepted, answerText, StringComparison.OrdinalIgnoreCase)),
                _ => false
            };
            if (isCorrect)
            {
                var remaining = Math.Max(0, (game.EndsAtUtc!.Value - DateTimeOffset.UtcNow).TotalSeconds);
                player.LastAnswerPoints = game.ShowSettings.BasePoints +
                    (int)Math.Round(game.ShowSettings.SpeedBonusPoints * remaining / question.TimeLimitSeconds);
                if (doublePointsArmed)
                    player.LastAnswerPoints *= 2;
                player.Score += player.LastAnswerPoints.Value;
            }
            else
            {
                player.LastAnswerPoints = 0;
            }
            player.LastAnswerCorrect = IsScoredQuestion(question.Type) ? isCorrect : null;

            ApplyRuleBlocks(
                game,
                player,
                !IsScoredQuestion(question.Type)
                    ? ShowRuleTrigger.AnyAnswer
                    : isCorrect ? ShowRuleTrigger.CorrectAnswer : ShowRuleTrigger.IncorrectAnswer,
                question);
            game.LeaderboardDirty = true;
            return Success(game, playerId);
        }
    }

    public GameActionResult ArmDoublePoints(string pin, string playerId)
    {
        lock (_gate)
        {
            if (!_games.TryGetValue(NormalizePin(pin), out var game))
                return NotFound();
            if (!game.Players.TryGetValue(playerId, out var player))
                return new(GameActionStatus.Unauthorized, null, "Your player session could not be found.");
            AdvanceExpiredQuestion(game);
            if (game.Status != GameStatus.Question || game.IsPaused)
                return Conflict("Double points can only be armed during an active question.");
            if (!IsScoredQuestion(game.Questions[game.CurrentQuestionIndex].Type) ||
                player.Answers.ContainsKey(game.CurrentQuestionIndex))
                return Conflict("Double points can only be armed before answering a scored question.");
            if (player.DoublePointsUsed)
                return Conflict("You have already used double points in this show.");

            player.DoublePointsUsed = true;
            player.DoublePointsArmed = true;
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

            AdvanceToNextQuestion(game);
            return Success(game, isHost: true);
        }
    }

    private static void AdvanceToNextQuestion(Game game)
    {
        game.IsPaused = false;
        game.PausedRemaining = null;
        game.EndsAtUtc = null;
        if (game.CurrentQuestionIndex + 1 >= game.Questions.Count)
        {
            game.Status = GameStatus.Finished;
            return;
        }

        game.CurrentQuestionIndex++;
        BeginQuestion(game);
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
        foreach (var player in game.Players.Values)
            player.LastAnswerCorrect = null;
        foreach (var player in game.Players.Values)
            player.DoublePointsArmed = false;
        game.LeaderboardDirty = true;
    }

    private static void AdvanceExpiredQuestion(Game game)
    {
        if (game.Status == GameStatus.Question && !game.IsPaused && game.EndsAtUtc <= DateTimeOffset.UtcNow)
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
                .Select(player => new PlayerScoreResponse(
                    player.Username,
                    player.Score,
                    player.LastAnswerPoints,
                    player.LastAnswerCorrect,
                    player.TeamName,
                    player.ProfileLabel,
                    player.Id,
                    player.Currency))
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
                          question?.Type is (QuestionType.Survey or QuestionType.WouldYouRather or QuestionType.StoryPrompt) &&
                          game.Status is (GameStatus.Reveal or GameStatus.Finished)
            ? game.Players.Values
                .Where(player => player.Answers.ContainsKey(game.CurrentQuestionIndex))
                .Select(player =>
                {
                    var response = player.Answers[game.CurrentQuestionIndex];
                    return new SurveyResponse(player.Username, response.OptionIndex, response.AnswerText);
                })
                .ToArray()
            : null;

        var profilePrompt = question?.Prompt;
        if (playerId is not null &&
            question is not null &&
            game.Players.TryGetValue(playerId, out var profilePlayer) &&
            profilePlayer.ProfileLabel is not null &&
            question.ProfilePrompts.TryGetValue(profilePlayer.ProfileLabel, out var alternatePrompt))
            profilePrompt = alternatePrompt;

        return new GameSnapshotResponse(
            game.Pin,
            game.QuizName,
            game.Description,
            game.Theme,
            game.Status,
            game.Players.Count,
            isHost
                ? game.Leaderboard
                : game.Leaderboard
                    .Select(player => player with { PlayerId = null, ProfileLabel = null })
                    .ToArray(),
            question is null ? null : new QuestionResponse(
                profilePrompt!,
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
            question?.Type is (QuestionType.MultipleChoice or QuestionType.Prediction) ? question.CorrectOptionIndex : null,
            myAnswer?.OptionIndex,
            myAnswer?.AnswerText,
            surveyResponses,
            game.ShowSettings,
            game.IsPaused,
            game.IsPaused ? Math.Max(0, (int)Math.Ceiling((game.PausedRemaining ?? TimeSpan.Zero).TotalSeconds)) : null,
            playerId is not null &&
                game.Players.TryGetValue(playerId, out var powerUpPlayer) &&
                powerUpPlayer.DoublePointsUsed,
            playerId is not null &&
                game.Players.TryGetValue(playerId, out var armedPlayer) &&
                armedPlayer.DoublePointsArmed,
            game.ShowSettings.TeamCount > 1
                ? game.Players.Values
                    .GroupBy(player => player.TeamName!)
                    .Select(team => new TeamScoreResponse(team.Key, team.Sum(player => player.Score), team.Count(), team.Sum(player => player.Currency)))
                    .OrderByDescending(team => team.Score)
                    .ThenBy(team => team.TeamName, StringComparer.Ordinal)
                    .ToArray()
                : []);
    }

    private static string? GetSmallestTeam(Game game)
    {
        if (game.ShowSettings.TeamCount == 1)
            return null;

        return Enumerable.Range(1, game.ShowSettings.TeamCount)
            .Select(index => $"Team {index}")
            .OrderBy(teamName => game.Players.Values.Count(player => player.TeamName == teamName))
            .ThenBy(teamName => teamName, StringComparer.Ordinal)
            .First();
    }

    private static void ApplyRuleBlocks(
        Game game,
        Player player,
        ShowRuleTrigger trigger,
        QuizQuestionRequest? question = null)
    {
        foreach (var rule in game.ShowSettings.RuleBlocks.Where(rule =>
            (rule.Trigger == trigger ||
             (rule.Trigger == ShowRuleTrigger.AnyAnswer &&
              trigger is ShowRuleTrigger.CorrectAnswer or ShowRuleTrigger.IncorrectAnswer)) &&
            RuleConditionMatches(game, player, rule, question)))
        {
            switch (rule.Action)
            {
                case ShowRuleAction.AddCurrency:
                    player.Currency = Math.Min(1_000_000, player.Currency + rule.Amount);
                    break;
                case ShowRuleAction.RemoveCurrency:
                    player.Currency = Math.Max(0, player.Currency - rule.Amount);
                    break;
                case ShowRuleAction.AddPoints:
                    player.Score = Math.Min(1_000_000, player.Score + rule.Amount);
                    break;
                case ShowRuleAction.RemovePoints:
                    player.Score = Math.Max(0, player.Score - rule.Amount);
                    break;
                case ShowRuleAction.AddTeamPoints:
                    foreach (var teammate in game.Players.Values.Where(teammate =>
                        player.TeamName is null
                            ? teammate.Id == player.Id
                            : string.Equals(teammate.TeamName, player.TeamName, StringComparison.Ordinal)))
                        teammate.Score = Math.Min(1_000_000, teammate.Score + rule.Amount);
                    break;
            }
        }
        game.LeaderboardDirty = true;
    }

    private static bool RuleConditionMatches(
        Game game,
        Player player,
        ShowRuleBlock rule,
        QuizQuestionRequest? question) =>
        rule.Condition switch
        {
            ShowRuleCondition.Always => true,
            ShowRuleCondition.ScoreAtLeast =>
                int.TryParse(rule.ConditionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var score) &&
                player.Score >= score,
            ShowRuleCondition.CurrencyAtLeast =>
                int.TryParse(rule.ConditionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var currency) &&
                player.Currency >= currency,
            ShowRuleCondition.PlayerCountAtLeast =>
                int.TryParse(rule.ConditionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var playerCount) &&
                game.Players.Count >= playerCount,
            ShowRuleCondition.RoundNumberAtLeast =>
                int.TryParse(rule.ConditionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var roundNumber) &&
                question is not null &&
                game.CurrentQuestionIndex + 1 >= roundNumber,
            ShowRuleCondition.TeamIs =>
                string.Equals(player.TeamName, rule.ConditionValue, StringComparison.Ordinal),
            ShowRuleCondition.ProfileIs =>
                string.Equals(player.ProfileLabel, rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            ShowRuleCondition.QuestionTypeIs =>
                question is not null &&
                string.Equals(question.Type.ToString(), rule.ConditionValue, StringComparison.Ordinal),
            _ => false
        };

    private static bool IsScoredQuestion(QuestionType type) =>
        type is QuestionType.MultipleChoice or QuestionType.Prediction or QuestionType.TypeAnswer;

    private static GameActionResult Success(Game game, string? playerId = null, bool isHost = false) =>
        new(GameActionStatus.Success, Snapshot(game, playerId, isHost), null);

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
        IReadOnlyList<QuizQuestionRequest> questions,
        ShowSettings showSettings)
    {
        public string Pin { get; } = pin;
        public string HostKey { get; } = hostKey;
        public string QuizName { get; } = quizName;
        public string Description { get; } = description;
        public string Theme { get; } = theme;
        public IReadOnlyList<QuizQuestionRequest> Questions { get; } = questions;
        public ShowSettings ShowSettings { get; } = showSettings;
        public Dictionary<string, Player> Players { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<PlayerScoreResponse> Leaderboard { get; set; } = [];
        public bool LeaderboardDirty { get; set; } = true;
        public GameStatus Status { get; set; } = GameStatus.Lobby;
        public int CurrentQuestionIndex { get; set; } = -1;
        public DateTimeOffset? EndsAtUtc { get; set; }
        public TimeSpan? PausedRemaining { get; set; }
        public bool IsPaused { get; set; }
    }

    private sealed class Player(string id, string username, string? teamName)
    {
        public string Id { get; } = id;
        public string Username { get; } = username;
        public string? TeamName { get; } = teamName;
        public string? ProfileLabel { get; set; }
        public int Currency { get; set; }
        public int Score { get; set; }
        public int? LastAnswerPoints { get; set; }
        public bool? LastAnswerCorrect { get; set; }
        public bool DoublePointsUsed { get; set; }
        public bool DoublePointsArmed { get; set; }
        public Dictionary<int, PlayerAnswer> Answers { get; } = [];
    }

    private sealed record PlayerAnswer(int? OptionIndex, string? AnswerText);
}
