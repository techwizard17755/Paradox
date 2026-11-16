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
            Choice("Which 1648 peace settlement ended the Thirty Years' War?", "History", ["Peace of Westphalia", "Treaty of Utrecht", "Treaty of Tordesillas", "Congress of Vienna"], 0),
            Typed("Which sea has no coastline and is bounded by currents in the North Atlantic?", "Geography", ["Sargasso Sea", "The Sargasso Sea"]),
            Choice("What is the SI unit for catalytic activity?", "Science", ["Katal", "Weber", "Siemens", "Gray"], 0),
            Choice("The Mohorovičić discontinuity marks the boundary between which layers?", "Earth science", ["Crust and mantle", "Mantle and outer core", "Outer and inner core", "Lithosphere and atmosphere"], 0),
            Typed("Which country is completely surrounded by South Africa?", "Geography", ["Lesotho"]),
            Typed("What is the name for the point in an orbit farthest from the Sun?", "Space", ["Aphelion"]),
            Choice("Which strait links the Black Sea to the Sea of Marmara?", "Geography", ["Bosporus", "Dardanelles", "Kerch Strait", "Strait of Gibraltar"], 0),
            Choice("Hungarian, Finnish, and Estonian belong to which language family?", "Language", ["Uralic", "Slavic", "Romance", "Germanic"], 0),
            Typed("What is the deepest known point in Earth's oceans called?", "Earth science", ["Challenger Deep"]),
            Choice("Which discontinuity separates Earth's liquid outer core from its solid inner core?", "Earth science", ["Lehmann discontinuity", "Mohorovičić discontinuity", "Gutenberg discontinuity", "Conrad discontinuity"], 0),
            Typed("What is the sum of the interior angles of a 20-sided polygon, in degrees?", "Math", ["3240", "3,240"]),
            Choice("1729 is divisible by the sum of its digits. What is this kind of number called?", "Math", ["A Harshad number", "A Mersenne number", "A perfect number", "A Fibonacci number"], 0),
            Typed("What decimal number is hexadecimal 2D?", "Computing", ["45", "forty five", "forty-five"]),
            Choice("Which vessels carry oxygen-rich blood from the lungs to the heart?", "Biology", ["Pulmonary veins", "Pulmonary arteries", "Aorta", "Venae cavae"], 0),
            Typed("Which European river flows through or along ten countries?", "Geography", ["Danube", "The Danube"]),
            Typed("What is the SI unit of magnetic flux?", "Physics", ["Weber", "The weber"]),
            Choice("The Royal Road from Susa to Sardis was built under which empire?", "History", ["Achaemenid Persian Empire", "Roman Empire", "Mongol Empire", "Byzantine Empire"], 0),
            Typed("What enzyme unwinds the DNA double helix during replication?", "Biology", ["Helicase"]),
            Typed("What is the name for the Moon's closest point to Earth in its orbit?", "Space", ["Perigee"]),
            Choice("Which element has atomic number 74?", "Chemistry", ["Tungsten", "Tantalum", "Osmium", "Rhenium"], 0),
            Choice("Two uneven-burning ropes each take exactly one hour to burn. How can you measure 45 minutes?", "Riddles", ["Light one rope at both ends and the other at one end; when the first goes out, light the second rope's other end.", "Light both ropes at one end and stop when one is halfway burned.", "Light one rope at both ends, then light the second rope when it goes out.", "Light both ropes at both ends and wait for one to go out."], 0),
            Typed("A two-digit number has digits that sum to 9. Reversing its digits makes it 27 larger. What is the original number?", "Riddles", ["36", "thirty-six", "thirty six"]),
            Typed("A lock code has clues: 682 (one right and well placed), 614 (one right but misplaced), 206 (two right but misplaced), 738 (none right), 780 (one right but misplaced). What is the code?", "Riddles", ["042", "42", "zero four two"]),
            Typed("Four people take 1, 2, 7, and 10 minutes to cross a bridge. Only two can cross at once, and they need one shared lamp. What is the fastest total time?", "Riddles", ["17", "17 minutes", "seventeen", "seventeen minutes"]),
            Typed("A fair die is rolled until its first 6. What is the probability that the first 6 appears on an odd-numbered roll?", "Riddles", ["6/11", "six elevenths"]),
            Choice("With two eggs and a 100-floor building, what is the fewest drops needed in the worst case to find the highest safe floor?", "Riddles", ["14", "10", "13", "15"], 0),
            Typed("A clock takes 5 seconds to strike 6 times. At the same pace, how many seconds does it take to strike 12 times?", "Riddles", ["11", "11 seconds", "eleven", "eleven seconds"]),
            Typed("One of 100 closed doors is toggled on every pass that is a multiple of its number. How many doors are open after 100 passes?", "Riddles", ["10", "ten"]),
            Typed("What is the smallest positive integer with exactly 12 positive divisors?", "Riddles", ["60", "sixty"]),
            Typed("How many times do the hour and minute hands overlap in 24 hours?", "Riddles", ["22", "twenty two", "twenty-two"]),
            Choice("Three switches control one old-fashioned bulb in another room. You may enter the room once. What lets you identify the correct switch?", "Riddles", ["Use the bulb's warmth as well as whether it is lit.", "Switch each one on and off quickly, then listen.", "Turn all three on and compare their shadows.", "There is no way to tell with one visit."], 0),
            Typed("What is the next term in this look-and-say sequence: 1, 11, 21, 1211, 111221?", "Riddles", ["312211", "3,12211"]),
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
            Typed("In the Monty Hall problem, what is the probability of winning by switching doors after a host reveals a goat?", "Riddles", ["2/3", "two thirds", "two-thirds"]),
            Choice("An 8×8 chessboard has two opposite corners removed. Can the remaining board be tiled with 31 dominoes, each covering two adjacent squares?", "Riddles", ["No; the removed corners are the same color, leaving unequal numbers of the two colors.", "Yes; each domino can cover one square of each color.", "Yes; rotate the board and place dominoes along the edges.", "No; 31 dominoes always cover an odd number of squares."], 0),
            Typed("At a gathering, everyone shakes hands with everyone else exactly once. There are 45 handshakes. How many people are there?", "Riddles", ["10", "ten"]),
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
