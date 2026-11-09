# Paradox

Paradox is a Windows desktop quiz-hosting app with a separate cross-platform server for public Internet hosting. The quiz UI runs in the native Windows app or in a browser. Players join using the six-digit PIN and player website.

## Run the Windows desktop app

Double-click `dist\win-x64\Paradox.exe` after publishing, or run `Publish-Paradox.ps1` to build it. The self-contained executable includes .NET; Windows users do not need the .NET SDK.

The Microsoft Edge WebView2 Runtime is also required to display the app window. It is already present on most Windows 10 and Windows 11 computers. If it is missing, Paradox offers a link to Microsoft's official installer.

The Windows app hosts games on your computer. Its **Copy player link** is for players on your local Wi-Fi; it is not a public Internet address.

## Host games over the Internet for free

The cloud server lets a host and players join from different networks. Deploy this project's `render.yaml` blueprint from Render's dashboard. Render builds `Server/Dockerfile` and provides HTTPS on its Free web-service plan.

1. Put the project in a GitHub repository you control and push the source.
2. In Render, choose **New → Blueprint**, connect that repository, and approve the `render.yaml` configuration.
3. Wait for the first deploy, then open the HTTPS URL Render assigns to the app.
4. Create and save quizzes on that URL. Share the same HTTPS website and each game's PIN with players.

There is no sign-in. On a public website, My Quizzes saves quiz content in that browser's local storage on the current device. It is not uploaded to the server or synced to other devices, browsers, or browser profiles. Clearing that site's browser data loses the library.

Render Free services spin down after 15 minutes without traffic and can take about a minute to start again. Free services have an ephemeral filesystem; the server's SQLite data under `/tmp/paradox-data` is temporary and is not used as the public website's quiz library. Live game rooms are held in server memory, so a spin-down, restart, or redeploy ends active games. While players and the host are actively using a game, their regular polling keeps the service active. Render Free has a shared 750 instance-hour monthly allowance per workspace; check Render's current limits before relying on it for a large audience.

## Build the Windows executable

On Windows with the .NET 10 SDK installed, run:

```powershell
.\Publish-Paradox.ps1
```

The self-contained Windows x64 app is written to `dist\win-x64\Paradox.exe`.

## Run the server locally

```powershell
dotnet run --project Server\Paradox.Server.csproj
```

Open the printed local HTTP address to use the browser-hosted quiz site.

## Play

- **Join a game:** Enter the host's six-digit PIN, choose a nickname, and answer each timed question.
- **Create:** Add an introduction, choose among five color themes, add up to 30 questions, mark correct answers, and select each timer.
- **My quizzes:** On the public website, save, edit, delete, and host quizzes from the same browser and device without an account.
- **Host:** Share the PIN (and the public website if deployed), start the quiz, reveal answers when the timer ends, and advance through the scoreboard.

Games are stored in memory and disappear when the server restarts. On the public website, quiz libraries are saved in the creator's browser; the server-side SQLite library is temporary in the free cloud configuration. Public hosting is configured for one server instance; scaling to multiple instances requires shared live-game storage and coordination.

The service does not include registered accounts, password recovery, or quiz-library synchronization across browsers/devices.
