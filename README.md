# SportMatchBot

**SportMatchBot** is a C# Telegram bot that helps you track friendly sports tournaments. It supports multiple sports and languages, logs match results, shows standings and top scorers, and lets you undo the last entry.

---

## 📌 Project Overview

SportMatchBot lets users register match results via Telegram commands, view up-to-date standings and top scorers, and manage matches in a conversational flow. It’s built using the [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) library and follows a modular architecture with handlers, services, and models.

---

## 🎯 Features

- **User Interaction & Commands**  
  - **/start** – Show a localized welcome message.  
  - **/help** – Display a list of available commands and basic instructions.  
  - **/match** – Begin registering a new match:  
    1. Select sport.  
    2. Select Team 1 and Team 2.  
    3. Enter scores for each team.  
    4. (Optional) Select scorers if enabled.  
    5. Confirm or cancel the match.  
  - **/info** – View current standings and top scorers for a selected sport.  
  - **/undo** – Remove the most recently registered match in this chat.  
  - **Language selection** – Choose your preferred language (supported: en, it, fr, de, es).  

- **Localization**  
  All user-facing text is stored in per-language JSON files (e.g. `en.json`) in `Data/LocalizationFiles` and loaded at runtime.

- **Team Configuration**  
  You must upload a valid `teams.json` file into `Data/teams.json`. You can follow the structure provided in `Data/teamsExample.json` as an example to define teams, players, and scorer settings.

- **State Management**  
  Transient match state (current stage, selected teams, scores, scorers) is managed in memory per chat via a thread-safe service.

- **Match Logging & Standings**  
  Completed matches are saved in `Data/matches.json`. Standings (points, goal differences, games played) and top scorers are computed on demand.

---

## 📁 Project Structure

```plaintext
C:.
│   appsettings.json
│   Program.cs
│   SportMatchBot.csproj
│
├───bin
│   └───Debug
├───Data
│   │   matches.json
│   │   teams.json
│   │   teamsExample.json
│   │
│   └───LocalizationFiles
│           de.json
│           en.json
│           es.json
│           fr.json
│           it.json
│
├───Enums
│       MatchStage.cs
│
├───Handlers
│       BotUpdateHandler.cs
│
├───Models
│       MatchState.cs
│       Team.cs
│
└───Services
        LocalizationService.cs
        LoggerService.cs
        MatchLogService.cs
        StateService.cs
        TeamService.cs
```

---

## 🚀 How to Run

1. **Prerequisites**  
   - .NET 9 SDK or later.  
   - A Telegram Bot token (create via @BotFather).  

2. **Clone & Restore**  
   ```bash
   git clone https://your-repo-url/SportMatchBot.git
   cd SportMatchBot
   dotnet restore
   ```

3. **Configure**  
   - Open `appsettings.json` and set your `BotToken` and `Language` (`en`, `it`, `fr`, `de`, `es`).  
   - Place your `teams.json` in `Data/teams.json` (see `teamsExample.json` for format).  
   - Ensure your chosen language file is in `Data/LocalizationFiles`.

4. **Run the Bot**  
   ```bash
   dotnet run
   ```

5. **Interact**  
   In Telegram, send `/start` then `/help` to see available commands.

---

## 🛠️ Extending the Bot

- **Add a new sport**:  
  SportMatchBot dynamically retrieves available sports, teams, and players from your `teams.json`. To add a new sport, simply create at least one team entry using that sport in `Data/teams.json`.  

- **Add or update teams/players**:  
  To create a new team or modify players for an existing team, edit the `teams.json` file in `Data` following the example in `teamsExample.json`.  

- **Add a new language**:  
  1. Copy `Data/LocalizationFiles/en.json` to `Data/LocalizationFiles/{lang}.json`.  
  2. Translate the values.  
  3. Update `appsettings.json` with `"Language": "{lang}"`.
