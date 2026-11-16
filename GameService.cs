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
    GameActionResult SpinWheel(string pin, string hostKey, string? subject);
    GameActionResult RollBoard(string pin, string playerId);
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
        if (!Enum.IsDefined(request.Mode))
            return (null, "Choose a supported show mode.");

        if (request.Questions is null || request.Questions.Count == 0 || request.Questions.Count > 30)
            return (null, "A quiz needs between 1 and 30 questions.");

        foreach (var question in request.Questions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.Prompt))
                return (null, "Every question needs a prompt.");
            if (!Enum.IsDefined(question.Type))
                return (null, "Choose a supported question type.");
            if ((question.Subject?.Trim().Length ?? 0) > 40)
                return (null, "Question subjects must be 40 characters or fewer.");
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

        var configurationError = ShowConfigurationValidator.Validate(
            request.Mode,
            request.BoardEnabled,
            request.SubjectWheelEnabled,
            request.BoardTiles,
            request.Questions,
            request.BoardTitle,
            request.BoardLayout,
            request.BoardStyle);
        if (configurationError is not null)
            return (null, configurationError);

        var normalizedQuestions = request.Questions
            .Select(question => question with
            {
                Prompt = question.Prompt.Trim(),
                Subject = string.IsNullOrWhiteSpace(question.Subject) ? "General" : question.Subject.Trim(),
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
        var boardTiles = ShowConfigurationValidator.HasBoard(request.Mode, request.BoardEnabled)
            ? request.BoardTiles.Select(tile => tile with
            {
                Subject = tile.Subject?.Trim() ?? "",
                Prompt = tile.Prompt?.Trim() ?? "",
                Label = tile.Label?.Trim() ?? ""
            }).ToArray()
            : [];
        var hasBoard = ShowConfigurationValidator.HasBoard(request.Mode, request.BoardEnabled);
        var hasWheel = ShowConfigurationValidator.HasSubjectWheel(request.Mode, request.SubjectWheelEnabled);

        lock (_gate)
        {
            string pin;
            do
            {
                pin = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            } while (_games.ContainsKey(pin));

            var hostKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            _games.Add(pin, new Game(
                pin,
                hostKey,
                title,
                description,
                request.Theme,
                request.Mode,
                hasBoard,
                hasWheel,
                normalizedQuestions,
                boardTiles,
                request.BoardTitle.Trim(),
                request.BoardLayout,
                request.BoardStyle));
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
            game.PlayerOrder.Add(playerId);
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

            if (game.BoardEnabled)
            {
                game.CurrentTurnIndex = 0;
                game.CurrentPlayerId = game.PlayerOrder[0];
                game.Status = GameStatus.BoardTurn;
                game.TileMessage = "The board is ready. Roll to begin your turn.";
                ClearRoundPoints(game);
            }
            else if (game.SubjectWheelEnabled)
            {
                game.CurrentQuestionIndex = -1;
                game.Status = GameStatus.Wheel;
                game.TileMessage = null;
                ClearRoundPoints(game);
            }
            else
            {
                game.CurrentQuestionIndex = 0;
                BeginQuestion(game);
            }
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
            if (game.BoardEnabled && !string.Equals(game.CurrentPlayerId, playerId, StringComparison.Ordinal))
                return Conflict("It is not your turn to answer this board question.");

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
            if (player.Answers.ContainsKey(game.CurrentRoundNumber))
                return Conflict("You have already answered this question.");

            player.Answers[game.CurrentRoundNumber] = new PlayerAnswer(optionIndex, answerText);
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
            if (game.BoardEnabled)
            {
                game.Status = GameStatus.Reveal;
                game.EndsAtUtc = null;
            }
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
            if (game.BoardEnabled)
            {
                if (game.Status is not (GameStatus.Reveal or GameStatus.BoardAction))
                    return Conflict("Wait for the board action to finish before continuing.");
                if (game.FinishAfterTurn)
                {
                    FinishBoardGame(game);
                    return Success(game);
                }
                AdvanceToNextBoardTurn(game);
                return Success(game);
            }
            if (game.Status != GameStatus.Reveal)
                return Conflict("Wait for the question timer to finish before continuing.");

            if (game.SubjectWheelEnabled)
            {
                if (game.UnusedQuestionIndexes.Count == 0)
                {
                    game.Status = GameStatus.Finished;
                    game.EndsAtUtc = null;
                    return Success(game);
                }
                BeginWheel(game, isBoardTurn: false, subjectConstraint: null);
                return Success(game);
            }
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

    public GameActionResult SpinWheel(string pin, string hostKey, string? subject)
    {
        lock (_gate)
        {
            if (!TryGetHostGame(pin, hostKey, out var game, out var error))
                return error!;
            if (game.Status != GameStatus.Wheel || !game.SubjectWheelEnabled)
                return Conflict("The subject wheel is not waiting for a spin.");

            var availableSubjects = AvailableSubjects(game);
            if (availableSubjects.Count == 0)
                return Conflict("There are no question subjects left on the wheel.");

            string chosenSubject;
            if (string.IsNullOrWhiteSpace(subject))
            {
                chosenSubject = availableSubjects[RandomNumberGenerator.GetInt32(availableSubjects.Count)];
            }
            else
            {
                chosenSubject = availableSubjects.FirstOrDefault(candidate =>
                    string.Equals(candidate, subject.Trim(), StringComparison.OrdinalIgnoreCase))!;
                if (chosenSubject is null)
                    return Conflict("Choose a subject currently shown on the wheel.");
            }

            var questions = game.Questions
                .Select((question, index) => (question, index))
                .Where(entry =>
                    string.Equals(entry.question.Subject, chosenSubject, StringComparison.OrdinalIgnoreCase) &&
                    (game.WheelForBoardTurn || game.UnusedQuestionIndexes.Contains(entry.index)))
                .ToArray();
            if (questions.Length == 0)
                return Conflict("That subject has no questions available.");

            var selected = questions[RandomNumberGenerator.GetInt32(questions.Length)];
            if (!game.WheelForBoardTurn)
                game.UnusedQuestionIndexes.Remove(selected.index);
            game.CurrentQuestionIndex = selected.index;
            game.ActiveSubject = chosenSubject;
            game.TileMessage = $"The wheel landed on {chosenSubject}.";
            BeginQuestion(game);
            return Success(game);
        }
    }

    public GameActionResult RollBoard(string pin, string playerId)
    {
        lock (_gate)
        {
            if (!_games.TryGetValue(NormalizePin(pin), out var game))
                return NotFound();
            if (!game.Players.TryGetValue(playerId, out var player))
                return new(GameActionStatus.Unauthorized, null, "Your player session could not be found.");
            if (!game.BoardEnabled || game.Status != GameStatus.BoardTurn)
                return Conflict("The board is not waiting for a roll.");
            if (!string.Equals(game.CurrentPlayerId, playerId, StringComparison.Ordinal))
                return Conflict("It is not your turn to roll.");

            var roll = RandomNumberGenerator.GetInt32(1, 7);
            game.LastRoll = roll;
            player.Position = Math.Min(game.BoardTiles.Count - 1, player.Position + roll);
            game.TileMessage = $"{player.Username} rolled {roll}.";
            ResolveBoardTile(game, player, 0);
            return Success(game, playerId);
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

    private static IReadOnlyList<string> AvailableSubjects(Game game)
    {
        return game.Questions
            .Select((question, index) => (question, index))
            .Where(entry =>
                (game.WheelForBoardTurn || game.UnusedQuestionIndexes.Contains(entry.index)) &&
                (string.IsNullOrWhiteSpace(game.WheelSubjectConstraint) ||
                 string.Equals(entry.question.Subject, game.WheelSubjectConstraint, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.question.Subject)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(subject => subject, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void BeginWheel(Game game, bool isBoardTurn, string? subjectConstraint)
    {
        game.Status = GameStatus.Wheel;
        game.EndsAtUtc = null;
        game.CurrentQuestionIndex = -1;
        game.ActiveSubject = null;
        game.WheelForBoardTurn = isBoardTurn;
        game.WheelSubjectConstraint = subjectConstraint;
        game.TileMessage = isBoardTurn ? "A question tile is waiting for the subject wheel." : null;
    }

    private static void ResolveBoardTile(Game game, Player player, int depth)
    {
        if (depth > game.BoardTiles.Count)
        {
            if (player.Position == game.BoardTiles.Count - 1)
            {
                FinishBoardGame(game);
                return;
            }
            game.TileMessage = "A movement chain was stopped to avoid a loop.";
            AdvanceToNextBoardTurn(game);
            return;
        }

        var tile = game.BoardTiles[player.Position];
        switch (tile.Action)
        {
            case BoardTileAction.Question:
                game.FinishAfterTurn = player.Position == game.BoardTiles.Count - 1;
                if (game.SubjectWheelEnabled)
                {
                    BeginWheel(game, isBoardTurn: true, tile.Subject);
                    return;
                }
                var matchingQuestions = game.Questions
                    .Select((question, index) => (question, index))
                    .Where(entry => string.IsNullOrWhiteSpace(tile.Subject) ||
                        string.Equals(entry.question.Subject, tile.Subject, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var selectedQuestion = matchingQuestions[RandomNumberGenerator.GetInt32(matchingQuestions.Length)];
                game.CurrentQuestionIndex = selectedQuestion.index;
                game.ActiveSubject = selectedQuestion.question.Subject;
                game.TileMessage = $"{player.Username} landed on a question tile.";
                BeginQuestion(game);
                return;

            case BoardTileAction.Bonus:
                player.Score += tile.Amount;
                game.LeaderboardDirty = true;
                CompleteBoardTile(game, player, $"{player.Username} gained {tile.Amount} points.");
                return;

            case BoardTileAction.Penalty:
                player.Score = Math.Max(0, player.Score - tile.Amount);
                game.LeaderboardDirty = true;
                CompleteBoardTile(game, player, $"{player.Username} lost {tile.Amount} points.");
                return;

            case BoardTileAction.MoveForward:
                player.Position = Math.Min(game.BoardTiles.Count - 1, player.Position + tile.Amount);
                game.TileMessage = $"{player.Username} moved forward {tile.Amount} spaces.";
                ResolveBoardTile(game, player, depth + 1);
                return;

            case BoardTileAction.MoveBackward:
                player.Position = Math.Max(0, player.Position - tile.Amount);
                game.TileMessage = $"{player.Username} moved backward {tile.Amount} spaces.";
                ResolveBoardTile(game, player, depth + 1);
                return;

            case BoardTileAction.SkipTurn:
                player.SkipNextTurn = true;
                CompleteBoardTile(game, player, $"{player.Username} will skip their next turn.");
                return;

            case BoardTileAction.Custom:
                game.Status = GameStatus.BoardAction;
                game.EndsAtUtc = null;
                game.FinishAfterTurn = player.Position == game.BoardTiles.Count - 1;
                game.TileMessage = tile.Prompt;
                return;

            case BoardTileAction.Start:
                CompleteBoardTile(game, player, $"{player.Username} returned to Start.");
                return;
        }
    }

    private static void CompleteBoardTile(Game game, Player player, string message)
    {
        game.TileMessage = message;
        if (player.Position == game.BoardTiles.Count - 1)
        {
            FinishBoardGame(game);
            return;
        }
        AdvanceToNextBoardTurn(game);
    }

    private static void AdvanceToNextBoardTurn(Game game)
    {
        var previousMessage = game.TileMessage;
        var skippedPlayers = new List<string>();
        for (var attempt = 0; attempt < game.PlayerOrder.Count; attempt++)
        {
            game.CurrentTurnIndex = (game.CurrentTurnIndex + 1) % game.PlayerOrder.Count;
            var playerId = game.PlayerOrder[game.CurrentTurnIndex];
            var player = game.Players[playerId];
            if (player.SkipNextTurn)
            {
                player.SkipNextTurn = false;
                skippedPlayers.Add(player.Username);
                continue;
            }

            game.CurrentPlayerId = playerId;
            game.Status = GameStatus.BoardTurn;
            game.EndsAtUtc = null;
            game.CurrentQuestionIndex = -1;
            game.ActiveSubject = null;
            game.WheelForBoardTurn = false;
            game.WheelSubjectConstraint = null;
            game.FinishAfterTurn = false;
            game.TileMessage = string.Join(" ", new[]
            {
                previousMessage,
                skippedPlayers.Count > 0 ? $"{string.Join(", ", skippedPlayers)} skipped a turn." : null,
                $"It is {player.Username}'s turn."
            }.Where(message => !string.IsNullOrWhiteSpace(message)));
            return;
        }

        game.CurrentTurnIndex = 0;
        game.CurrentPlayerId = game.PlayerOrder[0];
        game.Status = GameStatus.BoardTurn;
        game.EndsAtUtc = null;
        game.CurrentQuestionIndex = -1;
        game.ActiveSubject = null;
        game.WheelForBoardTurn = false;
        game.WheelSubjectConstraint = null;
        game.TileMessage = "Everyone's skip has been used. It is " +
            game.Players[game.CurrentPlayerId].Username + "'s turn.";
    }

    private static void FinishBoardGame(Game game)
    {
        game.Status = GameStatus.Finished;
        game.EndsAtUtc = null;
        game.FinishAfterTurn = false;
        game.WinnerUsername = game.CurrentPlayerId is not null &&
            game.Players.TryGetValue(game.CurrentPlayerId, out var winner)
            ? winner.Username
            : null;
        if (game.WinnerUsername is not null)
            game.TileMessage = $"{game.WinnerUsername} reached the finish!";
    }

    private static void ClearRoundPoints(Game game)
    {
        foreach (var player in game.Players.Values)
            player.LastAnswerPoints = null;
        game.LeaderboardDirty = true;
    }

    private static void BeginQuestion(Game game)
    {
        game.Status = GameStatus.Question;
        game.CurrentRoundNumber++;
        game.EndsAtUtc = DateTimeOffset.UtcNow.AddSeconds(game.Questions[game.CurrentQuestionIndex].TimeLimitSeconds);
        ClearRoundPoints(game);
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
                                 player.Answers.TryGetValue(game.CurrentRoundNumber, out var answer)
            ? answer
            : null;
        var surveyResponses = isHost &&
                          question?.Type == QuestionType.Survey &&
                          game.Status is GameStatus.Reveal or GameStatus.Finished
            ? game.Players.Values
                .Where(player => player.Answers.ContainsKey(game.CurrentRoundNumber))
                .Select(player =>
                {
                    var response = player.Answers[game.CurrentRoundNumber];
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
                    : [],
                question.Subject),
            game.CurrentRoundNumber,
            game.Questions.Count,
            game.EndsAtUtc,
            (game.Status is GameStatus.Reveal or GameStatus.Finished) &&
                question?.Type == QuestionType.MultipleChoice ? question.CorrectOptionIndex : null,
            myAnswer?.OptionIndex,
            myAnswer?.AnswerText,
            surveyResponses,
            game.Mode,
            game.BoardEnabled,
            game.SubjectWheelEnabled,
            game.Status == GameStatus.Wheel ? AvailableSubjects(game) : [],
            game.ActiveSubject,
            game.CurrentPlayerId is not null && game.Players.TryGetValue(game.CurrentPlayerId, out var activePlayer)
                ? activePlayer.Username
                : null,
            game.LastRoll,
            game.BoardTiles,
            game.BoardEnabled
                ? game.PlayerOrder.Select(id => game.Players[id])
                    .Select(boardPlayer => new PlayerBoardPositionResponse(boardPlayer.Username, boardPlayer.Position))
                    .ToArray()
                : [],
            game.TileMessage,
            game.WinnerUsername,
            game.BoardTitle,
            game.BoardLayout,
            game.BoardStyle);
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
        ShowMode mode,
        bool boardEnabled,
        bool subjectWheelEnabled,
        IReadOnlyList<QuizQuestionRequest> questions,
        IReadOnlyList<BoardTileRequest> boardTiles,
        string boardTitle,
        BoardLayout boardLayout,
        BoardStyle boardStyle)
    {
        public string Pin { get; } = pin;
        public string HostKey { get; } = hostKey;
        public string QuizName { get; } = quizName;
        public string Description { get; } = description;
        public string Theme { get; } = theme;
        public string BoardTitle { get; } = boardTitle;
        public BoardLayout BoardLayout { get; } = boardLayout;
        public BoardStyle BoardStyle { get; } = boardStyle;
        public ShowMode Mode { get; } = mode;
        public bool BoardEnabled { get; } = boardEnabled;
        public bool SubjectWheelEnabled { get; } = subjectWheelEnabled;
        public IReadOnlyList<QuizQuestionRequest> Questions { get; } = questions;
        public IReadOnlyList<BoardTileRequest> BoardTiles { get; } = boardTiles;
        public Dictionary<string, Player> Players { get; } = new(StringComparer.Ordinal);
        public List<string> PlayerOrder { get; } = [];
        public List<int> UnusedQuestionIndexes { get; } = Enumerable.Range(0, questions.Count).ToList();
        public IReadOnlyList<PlayerScoreResponse> Leaderboard { get; set; } = [];
        public bool LeaderboardDirty { get; set; } = true;
        public GameStatus Status { get; set; } = GameStatus.Lobby;
        public int CurrentQuestionIndex { get; set; } = -1;
        public int CurrentRoundNumber { get; set; }
        public int CurrentTurnIndex { get; set; }
        public string? CurrentPlayerId { get; set; }
        public string? ActiveSubject { get; set; }
        public bool WheelForBoardTurn { get; set; }
        public string? WheelSubjectConstraint { get; set; }
        public int? LastRoll { get; set; }
        public bool FinishAfterTurn { get; set; }
        public string? TileMessage { get; set; }
        public string? WinnerUsername { get; set; }
        public DateTimeOffset? EndsAtUtc { get; set; }
    }

    private sealed class Player(string id, string username)
    {
        public string Id { get; } = id;
        public string Username { get; } = username;
        public int Score { get; set; }
        public int? LastAnswerPoints { get; set; }
        public int Position { get; set; }
        public bool SkipNextTurn { get; set; }
        public Dictionary<int, PlayerAnswer> Answers { get; } = [];
    }

    private sealed record PlayerAnswer(int? OptionIndex, string? AnswerText);
}
