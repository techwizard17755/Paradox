# Paradox

Paradox is a Windows desktop quiz-hosting app with a separate cross-platform server for public Internet hosting. The quiz UI runs in the native Windows app or in a browser. Players join using the six-digit PIN and player website.

## Run the Windows desktop app

Double-click `dist\win-x64\Paradox.exe` after publishing, or run `Publish-Paradox.ps1` to build it. The self-contained executable includes .NET; Windows users do not need the .NET SDK.

The Microsoft Edge WebView2 Runtime is also required to display the app window. It is already present on most Windows 10 and Windows 11 computers. If it is missing, Paradox offers a link to Microsoft's official installer.

The Windows app hosts games on your computer. Its **Copy player link** is for players on your local Wi-Fi; it is not a public Internet address.

## Host games over the Internet

The cloud server lets a host and players join from different networks. Deploy this project's `render.yaml` blueprint from Render's dashboard. Render builds `Server/Dockerfile`, provides HTTPS, and mounts persistent storage at `/var/data`. The blueprint uses a paid Starter web service because quiz libraries need a persistent disk; check Render's current pricing before deployment.

1. Put the project in a GitHub repository you control and push the source.
2. In Render, choose **New → Blueprint**, connect that repository, and approve the `render.yaml` configuration.
3. Wait for the first deploy, then open the HTTPS URL Render assigns to the app.
4. Create and save quizzes on that URL. Share the same HTTPS website and each game's PIN with players.

No cloud password or private key belongs in this project. Saved quiz libraries are private to a random key stored in that browser's local storage; clearing browser site data loses access to that library. There are no email accounts yet.

The server stores quizzes in SQLite under `PARADOX_DATA_DIR`. The Render disk is mounted at `/var/data`, so quizzes survive app restarts and redeploys. The blueprint is intentionally a single server instance: live game rooms are currently in server memory and do not yet synchronize across multiple instances. A server restart ends active games, but saved quizzes remain available.

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
- **My quizzes:** Save, edit, delete, and host quizzes again from the same browser.
- **Host:** Share the PIN (and the public website if deployed), start the quiz, reveal answers when the timer ends, and advance through the scoreboard.

Games are stored in memory and disappear when the server restarts. Saved quizzes persist in SQLite. Public hosting is configured for one server instance; scaling to multiple instances requires shared live-game storage and coordination.

Quiz libraries are protected by a random owner key, which the app stores locally in the browser. The service does not yet include registered accounts, password recovery, or quiz-library synchronization across browsers/devices.
