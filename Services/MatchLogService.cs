using System.Text.Json;
using SportMatchBot.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace SportMatchBot.Services;

/// <summary>
/// Service responsible for logging match data, retrieving match logs, and interacting with various dependencies 
/// such as the bot client, team service, and localization service.
/// </summary>
public class MatchLogService
{
    private readonly string _logPath;
    private readonly TeamService _teamService;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<MatchLogService> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly LocalizationService _localizationService;

    private Dictionary<string, int> _scorers = new();
    private List<MatchState> _matches = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchLogService"/> class.
    /// This service manages the logging of match data, including saving matches to a log file, 
    /// retrieving match data, and interacting with various dependencies like the bot client, 
    /// team service, and localization service.
    /// </summary>
    /// <param name="logPath">The path to the match log file where match data will be stored.</param>
    /// <param name="teamService">The service responsible for managing teams and their players.</param>
    /// <param name="logger">The logger used for logging error and informational messages.</param>
    /// <param name="botClient">The Telegram bot client used for interacting with the bot.</param>
    /// <param name="localizationService">The service used for retrieving localized messages.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if any of the required parameters are null (teamService, logger, botClient, localizationService).
    /// </exception>
    public MatchLogService(string logPath, TeamService teamService, ILogger<MatchLogService> logger, ITelegramBotClient botClient, LocalizationService localizationService)
    {
        _logPath = logPath;
        _teamService = teamService ?? throw new ArgumentNullException(nameof(teamService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        // Create the log file if it doesn't exist
        if (!File.Exists(_logPath))
            File.WriteAllText(_logPath, "[]");
    }

    #region Public Methods

    /// <summary>
    /// Asynchronously saves a match to the log for a specific chat. The match includes teams, scores, and scorers.
    /// If a match with the same chat ID already exists, it appends the new match to the existing log.
    /// </summary>
    /// <param name="chatId">The ID of the chat where the match is being logged.</param>
    /// <param name="sport">The sport of the match (e.g., Soccer, Basketball).</param>
    /// <param name="team1">The name of the first team.</param>
    /// <param name="team2">The name of the second team.</param>
    /// <param name="score1">The score of the first team.</param>
    /// <param name="score2">The score of the second team.</param>
    /// <param name="scorers1">The list of scorers for the first team.</param>
    /// <param name="scorers2">The list of scorers for the second team.</param>
    /// <returns>
    /// A task representing the asynchronous operation of saving the match data.
    /// </returns>
    /// <exception cref="IOException">
    /// Thrown if an error occurs while reading or writing the match log file.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown if the program does not have permission to write to the log file.
    /// </exception>
    public async Task SaveMatchToLogAsync(long chatId, string sport, string team1, string team2, int score1, int score2, List<string> scorers1, List<string> scorers2)
    {
        var matchLog = await LoadMatchLogAsync();

        var match = new MatchState
        {
            ChatId = chatId,
            Sport = sport,
            Team1 = team1,
            Team2 = team2,
            Score1 = score1,
            Score2 = score2,
            ScorersTeam1 = scorers1,
            ScorersTeam2 = scorers2,
            Timestamp = DateTime.UtcNow,
        };

        if (!matchLog.ContainsKey(chatId.ToString()))
        {
            matchLog[chatId.ToString()] = new List<MatchState>();
        }

        matchLog[chatId.ToString()].Add(match);

        // Save the match data to the log file
        await SaveMatchLogAsync(matchLog);
    }

    /// <summary>
    /// Retrieves the top scorers for a given sport. If no scorers are enabled or found, returns appropriate localized messages.
    /// </summary>
    /// <param name="sport">The name of the sport for which to retrieve the top scorers. If null, the method will fetch all sports.</param>
    /// <returns>
    /// A string representing either the top scorers for the sport, a message indicating no scorers are available, 
    /// or a message saying scorers are disabled for the sport (based on the <em>EnableScorers</em> property in the teams.json file).
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the sport is not recognized or the scorers cannot be fetched.</exception>
    /// <remarks>
    /// This method:
    /// - Checks if the <em>EnableScorers</em> property is enabled for the teams in the given sport.
    /// - If no teams have scorers enabled, it will return a message stating that scorers are disabled.
    /// - If scorers are enabled, it will retrieve and return the top 5 scorers for the sport.
    /// - If no scorers are found, it will return a message stating that no scorers were found for the sport.
    /// </remarks>
    public async Task<string> GetTopScorersAsync(string? sport = null)
    {
        LoadData(); // Load the data if not already loaded
        await _lock.WaitAsync();
        try
        {
            // If sport is provided, proceed to check if it has scorers enabled
            if (!string.IsNullOrWhiteSpace(sport))
            {
                // Get teams for the sport
                var teams = _teamService.GetTeamsBySport(sport);

                // Check if any teams have the scorers enabled
                var teamsWithScorersEnabled = teams.Where(t => _teamService.IsScorersEnabled(t.Name)).ToList();

                // If no teams have scorers enabled, return the "scorersDisabled" message early
                if (teamsWithScorersEnabled.Count == 0)
                {
                    return _localizationService.GetLocalizedString("scorersDisabled");
                }

                // If scorers are enabled, process and gather the top scorers
                var scorerGoals = new Dictionary<string, int>();

                // Iterate through the matches and count the goals for each scorer
                foreach (var match in _matches.Where(m => m.Sport.Equals(sport, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var scorer in match.ScorersTeam1 ?? Enumerable.Empty<string>())
                    {
                        if (!string.IsNullOrWhiteSpace(scorer) && !scorer.Equals("Guest", StringComparison.OrdinalIgnoreCase))
                            scorerGoals[scorer] = scorerGoals.GetValueOrDefault(scorer) + 1;
                    }

                    foreach (var scorer in match.ScorersTeam2 ?? Enumerable.Empty<string>())
                    {
                        if (!string.IsNullOrWhiteSpace(scorer) && !scorer.Equals("Guest", StringComparison.OrdinalIgnoreCase))
                            scorerGoals[scorer] = scorerGoals.GetValueOrDefault(scorer) + 1;
                    }
                }

                // If no scorers are found, return the "no scorers found" message
                if (scorerGoals.Count == 0)
                    return _localizationService.GetLocalizedString("noScorersFound");

                // Get the top 5 scorers
                var top5 = scorerGoals
                    .OrderByDescending(s => s.Value)
                    .ThenBy(s => s.Key)
                    .Take(5)
                    .Select(s => $"{s.Key}: {s.Value} {_localizationService.GetLocalizedString("goals")}");

                return string.Join("\n", top5);
            }
            else
            {
                // If the sport is not recognized or does not have scorers, return the appropriate message
                return _localizationService.GetLocalizedString("scorersNotDisplayed");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Asynchronously saves the provided match log data to the specified file path.
    /// Serializes the match log dictionary into a JSON format and writes it to the file.
    /// </summary>
    /// <param name="matchLog">
    /// A dictionary where each key is a chat ID and each value is a list of match states to be saved.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous save operation. The operation is completed when the data is successfully written to the file.
    /// </returns>
    /// <exception cref="IOException">
    /// Thrown if an I/O error occurs while writing the file.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown if the program does not have permission to write to the file or directory.
    /// </exception>
    public async Task<string> GetStandingsAsync(string? sport = null)
    {
        LoadData();
        await _lock.WaitAsync();
        try
        {
            var matches = string.IsNullOrWhiteSpace(sport)
                ? _matches
                : _matches.Where(m => m.Sport.Equals(sport, StringComparison.OrdinalIgnoreCase));

            var standings = matches
                .SelectMany(m => new[] {
                    new { Team = m.Team1, GoalsFor = m.Score1, GoalsAgainst = m.Score2, Result = GetResult(m.Score1, m.Score2) },
                    new { Team = m.Team2, GoalsFor = m.Score2, GoalsAgainst = m.Score1, Result = GetResult(m.Score2, m.Score1) }
                })
                .GroupBy(e => e.Team)
                .Select(g => new
                {
                    Team = g.Key,
                    GamesPlayed = g.Count(),
                    GoalsFor = g.Sum(x => x.GoalsFor),
                    GoalsAgainst = g.Sum(x => x.GoalsAgainst),
                    GoalDifference = g.Sum(x => x.GoalsFor) - g.Sum(x => x.GoalsAgainst),
                    Score = g.Sum(x => x.Result) // 3 win, 1 draw, 0 loss
                })
                .OrderByDescending(e => e.Score)
                .ThenByDescending(e => e.GoalDifference)
                .ThenByDescending(e => e.GoalsFor)
                .ThenBy(e => e.Team);

            var lines = standings.Select(s =>
            {
                // Localize the team sport and retrieve corresponding labels
                var teamSport = s.Team != null ? _teamService.GetTeamSport(s.Team) : "Unknown";

                // Localize goals and goal difference labels based on sport
                string goalsLabel = teamSport == "Soccer" ? _localizationService.GetLocalizedString("goals") : _localizationService.GetLocalizedString("pointsScored");
                string gdLabel = teamSport == "Soccer" ? _localizationService.GetLocalizedString("gd") : _localizationService.GetLocalizedString("diffPoints");

                // Return the formatted standings line
                return $"{s.Team}: {s.Score} {_localizationService.GetLocalizedString("pts")}, {goalsLabel}: {s.GoalsFor}, {gdLabel}: {s.GoalDifference}, {_localizationService.GetLocalizedString("gamesPlayed")}: {s.GamesPlayed}";
            });

            // Return standings or a message if no standings are available
            return lines.Any() ? string.Join("\n", lines) : _localizationService.GetLocalizedString("noStandingsAvailable");
        }
        finally
        {
            _lock.Release();
        }
    }


    #endregion

    #region Private Methods
    /// <summary>
    /// Add a match log entry to the match log file asynchronously.
    /// </summary>
    private async Task SaveMatchLogAsync(Dictionary<string, List<MatchState>> matchLog)
    {
        await _lock.WaitAsync();
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(matchLog, options);
            await File.WriteAllTextAsync(_logPath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Asynchronously loads the match log from the specified file path and deserializes it into a dictionary of match states.
    /// This method reads the match log file, attempts to deserialize it into a dictionary where each key is a chat ID and each value is a list of match states.
    /// </summary>
    /// <returns>
    /// A dictionary containing chat IDs as keys and lists of match states as values.
    /// If the file is empty, not found, or fails to deserialize, an empty dictionary is returned.
    /// </returns>
    private async Task<Dictionary<string, List<MatchState>>> LoadMatchLogAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_logPath))
            {
                var json = await File.ReadAllTextAsync(_logPath);

                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        return JsonSerializer.Deserialize<Dictionary<string, List<MatchState>>>(json) ?? new Dictionary<string, List<MatchState>>();
                    }
                    catch (JsonException jex)
                    {
                        _logger.LogError($"JSON deserialization failed: {jex.Message}");
                    }
                }
                else
                {
                    _logger.LogWarning("Match log file is empty.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to load match log: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }

        return new Dictionary<string, List<MatchState>>();
    }

    /// <summary>
    /// Loads match data from the log, including scorers and match details.
    /// This method fetches the match log asynchronously, extracts scorers from both teams, and stores them in a dictionary.
    /// </summary>
    private void LoadData()
    {
        _matches = LoadMatchLogAsync().Result.Values.SelectMany(x => x).ToList();
        _scorers = new Dictionary<string, int>();

        foreach (var match in _matches)
        {
            foreach (var scorer in match.ScorersTeam1 ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(scorer))
                    _scorers[scorer] = _scorers.GetValueOrDefault(scorer) + 1;
            }

            foreach (var scorer in match.ScorersTeam2 ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(scorer))
                    _scorers[scorer] = _scorers.GetValueOrDefault(scorer) + 1;
            }
        }
    }

    /// <summary>
    /// Undoes the most recent match recorded by the user in the chat. 
    /// Removes the last match from the log and sends a confirmation message to the user.
    /// </summary>
    /// <param name="chatId">The ID of the chat where the match should be undone.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>Returns a task that represents the asynchronous operation.</returns>
    public async Task UndoLastMatchAsync(long chatId, CancellationToken cancellationToken)
    {
        var matchLog = await LoadMatchLogAsync();

        foreach (var entry in matchLog)
        {
            var myMatches = entry.Value
                .Where(m => m.ChatId == chatId)
                .OrderByDescending(m => m.Timestamp)
                .ToList();

            if (myMatches.Any())
            {
                var matchToRemove = myMatches.First();
                entry.Value.Remove(matchToRemove);

                await SaveMatchLogAsync(matchLog);
                await _botClient.SendMessage(chatId, _localizationService.GetLocalizedString("matchUndone"), cancellationToken: cancellationToken);
                return;
            }
        }

        await _botClient.SendMessage(chatId, _localizationService.GetLocalizedString("noMatchToUndo"), cancellationToken: cancellationToken);
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Returns match points: 3 for a win, 1 for a draw, or 0 for a loss.
    /// </summary>
    /// <param name="goalsFor">Goals scored by this team.</param>
    /// <param name="goalsAgainst">Goals scored by the opponent.</param>
    /// <returns>Points awarded based on goals comparison.</returns>
    private static int GetResult(int? goalsFor, int? goalsAgainst)
    {
        if (goalsFor > goalsAgainst) return 3;
        if (goalsFor == goalsAgainst) return 1;
        return 0;
    }

    #endregion
}