# Paradox

Paradox is a browser-based game-show website. There is no Windows desktop app. Run the website locally or deploy it to Render, then hosts and players use the same site in their browsers.

## Run locally

Install the .NET 10 SDK, then run:

```powershell
dotnet run --project Server\Paradox.Server.csproj
```

Open the local address printed by the server, usually `http://localhost:10000`.

## Deploy the website

Deploy this repository as a Render Blueprint using `render.yaml`. Render builds the ASP.NET Core website and serves it over HTTPS. See Render's current service and storage limits before relying on its free tier.

## Use Paradox

- Open **Join a game** and enter the host's six-digit PIN.
- Open **Create** to make a quiz, digital board game, or Custom show.
- Share the PIN with players so they can join from their own browsers.
- **My quizzes** stores saved quizzes in the current browser on the current device. It does not sync across devices or browsers.

## Repository file guide

### Website and game logic

| File | Purpose |
|---|---|
| `GameContracts.cs` | Game, question, board, request, response, and validation types shared with the server. |
| `GameEndpoints.cs` | HTTP endpoints for creating, joining, hosting, and playing live games. |
| `GameService.cs` | Authoritative in-memory game state, turn flow, scoring, wheel, and board actions. |
| `wwwroot\index.html` | Browser page shell and root-relative CSS/JavaScript links. |
| `wwwroot\app.js` | Browser application: navigation, quiz builder, board/wheel studios, host/player views, and API calls. |
| `wwwroot\styles.css` | Website layout, themes, responsive styling, and animations. |

### ASP.NET Core server

| File | Purpose |
|---|---|
| `Server\Paradox.Server.csproj` | Server project definition and shared source/static-file inclusion. |
| `Server\Program.cs` | Server startup, hosting configuration, middleware, API routes, and page fallbacks. |
| `Server\GlobalUsings.cs` | Common namespaces used throughout the server project. |
| `Server\ParadoxServices.cs` | Registers the game and quiz-library services and maps their endpoint groups. |
| `Server\QuizLibraryContracts.cs` | Request and response types for saved quizzes and themes. |
| `Server\QuizLibraryEndpoints.cs` | HTTP endpoints for listing, saving, editing, and deleting quizzes. |
| `Server\QuizLibraryService.cs` | SQLite-backed saved-quiz storage, validation, and settings serialization. |
| `Server\Dockerfile` | Builds and runs the website container used by Render. |

### Deployment, testing, and repository settings

| File | Purpose |
|---|---|
| `render.yaml` | Render Blueprint configuration for deploying the web service. |
| `Paradox.http` | Example HTTP requests for manually exercising the game API. |
| `.dockerignore` | Excludes local and generated files from the Docker build context. |
| `.gitignore` | Excludes build output, local data, and environment files from Git. |
| `README.md` | Local run, deployment, usage instructions, and this file guide. |
