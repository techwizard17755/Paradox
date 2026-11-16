using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Paradox;

/// <summary>Private template response returned only after invite-key verification.</summary>
public sealed record FamilyShowTemplateResponse(CreateGameRequest Show);

internal static class FamilyShowTemplateEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/private/family-show", IResult (
            HttpRequest request,
            HttpResponse response,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            _ = cancellationToken;
            response.Headers.CacheControl = "no-store";
            var expectedKey = configuration["PARADOX_FAMILY_SHOW_KEY"];
            var phrase = configuration["PARADOX_FAMILY_SHOW_PHRASE"];
            var suppliedKey = request.Headers["X-Private-Template-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || string.IsNullOrEmpty(phrase) ||
                !FixedTimeEquals(expectedKey, suppliedKey ?? string.Empty))
                return TypedResults.NotFound();

            var show = CreateFamilyShow(phrase);
            if (PhrasePuzzleValidator.Validate(show.PhrasePuzzle) is not null)
            {
                loggerFactory.CreateLogger("FamilyShowTemplate")
                    .LogError("The private family show phrase configuration is invalid.");
                return TypedResults.Problem(
                    "The private family show is not correctly configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return TypedResults.Ok(new FamilyShowTemplateResponse(show));
        })
        .WithName("GetPrivateFamilyShowTemplate")
        .WithSummary("Load the private family show template")
        .WithDescription("Returns the family game template only when its Render-configured private invite key is supplied.")
        .Produces<FamilyShowTemplateResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static CreateGameRequest CreateFamilyShow(string phrase)
    {
        var questions = new List<QuizQuestionRequest>
        {
            Choice("Which ocean is the largest on Earth?", "Geography", ["Pacific", "Atlantic", "Indian", "Arctic"], 0),
            Choice("Which organ pumps blood around the body?", "Science", ["Heart", "Lung", "Stomach", "Kidney"], 0),
            Typed("How many sides does a hexagon have?", "Math", ["6", "six"]),
            Typed("What gets wetter the more it dries?", "Riddles", ["A towel", "Towel"]),
            Choice("Which continent is home to the Sahara Desert?", "Geography", ["Africa", "Asia", "Europe", "South America"], 0),
            Typed("What is the chemical formula for water?", "Science", ["H2O", "H₂O"]),
            Choice("Which planet is famous for its bright rings?", "Space", ["Saturn", "Mars", "Venus", "Mercury"], 0),
            Typed("What has keys but no locks, and space but no room?", "Riddles", ["A keyboard", "Keyboard"]),
            Choice("Which animal is a mammal that lays eggs?", "Nature", ["Platypus", "Dolphin", "Koala", "Giraffe"], 0),
            Typed("What is 9 multiplied by 8?", "Math", ["72", "seventy two", "seventy-two"]),
            Choice("Which direction is opposite north-east?", "Geography", ["South-west", "South-east", "North-west", "East"], 0),
            Typed("What is frozen water called?", "Science", ["Ice"]),
            Choice("Which animal carries its young in a pouch?", "Nature", ["Kangaroo", "Fox", "Penguin", "Seal"], 0),
            Typed("What has a head and a tail but no body?", "Riddles", ["A coin", "Coin"]),
            Choice("Which is the closest star to Earth?", "Space", ["The Sun", "Sirius", "Polaris", "Vega"], 0),
            Typed("How many minutes are in two hours?", "Math", ["120", "one hundred twenty", "one hundred and twenty"]),
            Choice("What do bees collect from flowers to make honey?", "Nature", ["Nectar", "Sand", "Dew", "Seeds"], 0),
            Typed("What has a neck but no head?", "Riddles", ["A bottle", "Bottle"])
        };
        var rounds = new List<SmashRoundRequest>
        {
            new()
            {
                Prompt = "Name as many fruits as you can!",
                AcceptedAnswers = ["Apple", "Banana", "Orange", "Pear", "Peach", "Plum", "Grape", "Mango", "Lemon", "Lime", "Cherry", "Strawberry", "Raspberry", "Blueberry", "Blackberry", "Watermelon", "Cantaloupe", "Pineapple", "Kiwi", "Papaya", "Apricot", "Fig", "Guava", "Coconut", "Pomegranate", "Cranberry", "Tangerine", "Grapefruit", "Nectarine", "Dragon fruit"]
            },
            new()
            {
                Prompt = "Name as many countries as you can!",
                AcceptedAnswers = ["Canada", "Mexico", "Brazil", "Argentina", "Chile", "Peru", "United States", "United Kingdom", "France", "Germany", "Spain", "Italy", "Portugal", "Norway", "Sweden", "Finland", "Poland", "Greece", "Egypt", "Kenya", "Nigeria", "South Africa", "India", "China", "Japan", "Thailand", "Australia", "New Zealand", "Indonesia", "South Korea"]
            },
            new()
            {
                Prompt = "Name as many animals as you can!",
                AcceptedAnswers = ["Cat", "Dog", "Horse", "Cow", "Sheep", "Goat", "Pig", "Rabbit", "Fox", "Wolf", "Bear", "Deer", "Elephant", "Giraffe", "Zebra", "Lion", "Tiger", "Panda", "Koala", "Kangaroo", "Penguin", "Eagle", "Owl", "Dolphin", "Whale", "Shark", "Turtle", "Frog", "Snake", "Butterfly"]
            }
        };
        var tiles = Enumerable.Range(0, 40).Select(index => index switch
        {
            0 => new BoardTileRequest { Action = BoardTileAction.Start, Label = "Start" },
            5 or 17 or 27 or 35 => new BoardTileRequest
            {
                Action = BoardTileAction.MoveForward,
                Amount = index is 17 or 35 ? 3 : 2,
                Label = "Shortcut! Move ahead",
                ImageKey = "rocket"
            },
            9 or 20 or 31 => new BoardTileRequest
            {
                Action = BoardTileAction.SkipTurn,
                Label = "A trap! Miss a turn",
                ImageKey = "flame"
            },
            13 or 24 or 37 => new BoardTileRequest
            {
                Action = BoardTileAction.MoveBackward,
                Amount = index == 24 ? 3 : 2,
                Label = "Villain's trick! Go back",
                ImageKey = "comet"
            },
            8 or 29 => new BoardTileRequest
            {
                Action = BoardTileAction.Bonus,
                Amount = 150,
                Label = "Find a supply cache",
                ImageKey = "treasure"
            },
            _ => new BoardTileRequest { Action = BoardTileAction.Question, Label = "Solve a clue" }
        }).ToList();

        return new CreateGameRequest
        {
            QuizName = "The Puzzle Solvers: Stop the Villain!",
            Description = "Race around the board, solve family-friendly clues, and uncover the secret code before the villain escapes.",
            Theme = "violet",
            Mode = ShowMode.Board,
            BoardTiles = tiles,
            BoardTitle = "Race to Stop the Villain",
            BoardLayout = BoardLayout.SnakesAndLadders,
            BoardStyle = BoardStyle.Arcade,
            PhrasePuzzle = new PhrasePuzzleRequest
            {
                Phrase = phrase,
                SmashEnabled = true,
                LapsBeforeSmash = 2,
                SmashRounds = rounds
            },
            Questions = questions
        };
    }

    private static QuizQuestionRequest Choice(
        string prompt,
        string subject,
        List<string> options,
        int correctOptionIndex) =>
        new()
        {
            Prompt = prompt,
            Subject = subject,
            Type = QuestionType.MultipleChoice,
            Options = options,
            CorrectOptionIndex = correctOptionIndex
        };

    private static QuizQuestionRequest Typed(string prompt, string subject, List<string> acceptedAnswers) =>
        new()
        {
            Prompt = prompt,
            Subject = subject,
            Type = QuestionType.TypeAnswer,
            AcceptedAnswers = acceptedAnswers
        };

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
