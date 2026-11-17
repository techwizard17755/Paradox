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
            Choice("Which ancient people built the pyramids at Giza?", "History", ["Ancient Egyptians", "Ancient Greeks", "Romans", "Vikings"], 0),
            Choice("What is the largest ocean on Earth?", "Geography", ["Pacific Ocean", "Atlantic Ocean", "Indian Ocean", "Arctic Ocean"], 0),
            Choice("Which gas do plants absorb from the air during photosynthesis?", "Science", ["Carbon dioxide", "Oxygen", "Nitrogen", "Hydrogen"], 0),
            Typed("What do we call molten rock after it reaches Earth's surface?", "Earth science", ["Lava"]),
            Choice("What is the capital of Japan?", "Geography", ["Tokyo", "Kyoto", "Osaka", "Hiroshima"], 0),
            Choice("Which planet is known as the Red Planet?", "Space", ["Mars", "Venus", "Jupiter", "Mercury"], 0),
            Typed("What is the longest river in South America?", "Geography", ["Amazon", "Amazon River", "The Amazon", "The Amazon River"]),
            Choice("Which language is primarily spoken in Brazil?", "Language", ["Portuguese", "Spanish", "French", "Italian"], 0),
            Typed("What is the name of the galaxy that contains our solar system?", "Space", ["Milky Way", "The Milky Way"]),
            Choice("Which is Earth's largest layer by volume?", "Earth science", ["Mantle", "Crust", "Outer core", "Inner core"], 0),
            Typed("How many sides does a hexagon have?", "Math", ["6", "six"]),
            Typed("What is the next prime number after 7?", "Math", ["11", "eleven"]),
            Typed("What is 12 multiplied by 12?", "Math", ["144", "one hundred forty four", "one hundred forty-four"]),
            Typed("Which organ pumps blood around the human body?", "Biology", ["Heart", "The heart"]),
            Typed("Which river flows through Paris?", "Geography", ["Seine", "Seine River", "The Seine", "The Seine River"]),
            Typed("What is the SI unit of magnetic flux?", "Physics", ["Weber", "The weber"]),
            Choice("The Royal Road from Susa to Sardis was built under which empire?", "History", ["Achaemenid Persian Empire", "Roman Empire", "Mongol Empire", "Byzantine Empire"], 0),
            Typed("What enzyme unwinds the DNA double helix during replication?", "Biology", ["Helicase"]),
            Typed("What is the name for the Moon's closest point to Earth in its orbit?", "Space", ["Perigee"]),
            Choice("Which element has atomic number 74?", "Chemistry", ["Tungsten", "Tantalum", "Osmium", "Rhenium"], 0),
            Typed("If you have 3 apples and take away 2, how many apples do you have?", "Riddles", ["2", "two"]),
            Typed("What number comes next in the pattern 2, 4, 6, 8, ...?", "Riddles", ["10", "ten"]),
            Typed("I have a face and two hands but no arms or legs. What am I?", "Riddles", ["Clock", "A clock"]),
            Typed("A farmer had 17 sheep. All but 9 ran away. How many sheep were left?", "Riddles", ["9", "nine"]),
            Typed("I have a head and a tail but no body. What am I?", "Riddles", ["Coin", "A coin"]),
            Choice("Which weighs more: a kilogram of feathers or a kilogram of steel?", "Riddles", ["They weigh the same", "The feathers", "The steel", "It depends on the size"], 0),
            Typed("What has to be broken before you can use it?", "Riddles", ["Egg", "An egg"]),
            Typed("What appears once in a minute, twice in a moment, but never in a thousand years?", "Riddles", ["The letter M", "M", "Letter M"]),
            Typed("I have cities, rivers, and forests, but no houses, water, or trees. What am I?", "Riddles", ["Map", "A map"]),
            Typed("What has a neck but no head?", "Riddles", ["Bottle", "A bottle"]),
            Typed("What gets wetter the more it dries?", "Riddles", ["Towel", "A towel"]),
            Typed("What can travel around the world while staying in one corner?", "Riddles", ["Stamp", "A stamp", "Postage stamp", "A postage stamp"]),
            Choice("Which physicist is associated with the uncertainty principle?", "Physics", ["Werner Heisenberg", "Niels Bohr", "Erwin Schrödinger", "Max Planck"], 0),
            Typed("What scale is used to rank a mineral's resistance to scratching?", "Earth science", ["Mohs scale", "The Mohs scale"]),
            Choice("Which tectonic plate is the largest by area?", "Earth science", ["Pacific Plate", "African Plate", "North American Plate", "Eurasian Plate"], 0),
            Typed("What is the chemical name of baking soda?", "Chemistry", ["Sodium bicarbonate", "Sodium hydrogen carbonate"]),
            Choice("Which planet rotates so slowly that its sidereal day is longer than its year?", "Space", ["Venus", "Mercury", "Mars", "Jupiter"], 0),
            Typed("What word describes animals that are most active at dawn and dusk?", "Biology", ["Crepuscular"]),
            Typed("What is the direct change from a solid to a gas called?", "Chemistry", ["Sublimation"]),
            Choice("Which 1783 treaty formally recognized the independence of the United States?", "History", ["Treaty of Paris", "Treaty of Ghent", "Treaty of Versailles", "Treaty of Jay"], 0),
            Choice("Which pharaoh's tomb was discovered by Howard Carter in 1922?", "History", ["Tutankhamun", "Ramses II", "Akhenaten", "Khufu"], 0),
            Typed("How many edges does a regular dodecahedron have?", "Math", ["30", "thirty"]),
            Choice("Polaris is part of which constellation?", "Space", ["Ursa Minor", "Ursa Major", "Cassiopeia", "Cepheus"], 0),
            Typed("What is the third most abundant gas in Earth's atmosphere?", "Earth science", ["Argon"]),
            Typed("What is the most common isotope of hydrogen called?", "Chemistry", ["Protium"]),
            Choice("Which brain structure is most associated with balance and coordination?", "Biology", ["Cerebellum", "Amygdala", "Hippocampus", "Medulla"], 0),
            Typed("Which naturally occurring element has atomic number 92?", "Chemistry", ["Uranium"]),
            Typed("What is the capital of Mongolia?", "Geography", ["Ulaanbaatar", "Ulan Bator"]),
            Typed("What is the study of flags called?", "Language", ["Vexillology"]),
            Typed("What is the boundary between the illuminated and dark halves of a planet called?", "Space", ["Terminator", "The terminator"]),
            Typed("How many months have at least 28 days?", "Riddles", ["12", "twelve", "all 12", "all twelve"]),
            Typed("What has four legs in the morning, two at noon, and three in the evening?", "Riddles", ["A human", "Human", "A person", "Person"]),
            Typed("If two's company and three's a crowd, what are four and five?", "Riddles", ["9", "nine"]),
            Typed("A bag has 3 red and 4 blue balls. Two are drawn without replacement. What is the probability they have the same color?", "Riddles", ["3/7", "three sevenths", "three-sevenths"]),
            Typed("Exactly one bottle is poisoned. With 10 binary test strips and one testing round, what is the maximum number of bottles you can distinguish?", "Riddles", ["1024", "1,024", "one thousand twenty four", "one thousand twenty-four"]),
            Choice("Use 1, 3, 4, and 6 exactly once with arithmetic operations and parentheses. Which expression equals 24?", "Riddles", ["6 ÷ (1 − 3/4)", "(6 − 4) × (3 + 1)", "(6 + 4) × (3 − 1)", "6 × (4 − 3) × 1"], 0),
            Typed("What is the minimum number of moves to solve the Tower of Hanoi with three disks?", "Riddles", ["7", "seven"]),
            Choice("You face two doors, one safe and one dangerous, guarded by one truth-teller and one liar. What single question guarantees you find the safe door?", "Riddles", ["Ask either guard which door the other would say is safe, then choose the opposite.", "Ask either guard if they are the truth-teller, then choose their door.", "Ask both guards which door is safe and choose the door named twice.", "Ask one guard whether the left door is safe and follow the answer."], 0),
            Typed("What is the minimum number of balance-scale weighings needed to find one heavier coin among 8 coins?", "Riddles", ["2", "two"]),
            Typed("What is the minimum number of races needed to identify the three fastest horses among 25, if only five can race at once and there is no stopwatch?", "Riddles", ["7", "seven"])
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
            Description = "Race around the board, tackle challenging mixed trivia and logic riddles for teens and adults, and uncover the secret code before the villain escapes.",
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
        int correctOptionIndex)
    {
        var optionOrder = Enumerable.Range(0, options.Count).ToArray();
        for (var index = optionOrder.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (optionOrder[index], optionOrder[swapIndex]) = (optionOrder[swapIndex], optionOrder[index]);
        }

        return new()
        {
            Prompt = prompt,
            Subject = subject,
            Type = QuestionType.MultipleChoice,
            Options = optionOrder.Select(index => options[index]).ToList(),
            CorrectOptionIndex = Array.IndexOf(optionOrder, correctOptionIndex)
        };
    }

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
