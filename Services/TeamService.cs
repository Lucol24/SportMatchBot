using SportMatchBot.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic; 
using System.Linq;
using System.IO; 
using System.Text.Json;
using System.Threading;

namespace SportMatchBot.Services;

/// <summary>
/// Service for managing team data, including teams, players, and scoring configuration.
/// </summary>
public class TeamService
{
    private readonly string _teamsPath;
    private readonly ILogger<TeamService> _logger;
    private Dictionary<string, Team> _teamsData = new();
    private Dictionary<string, List<string>> _playersData = new();
    private readonly SemaphoreSlim _dataLock = new(1, 1);

    #region Public Methods

    /// <summary>
    /// Initializes the TeamService, loads team and player data from the specified JSON path, and sets up logging.
    /// </summary>
    /// <param name="teamsJsonPath">Path to the JSON file containing team data.</param>
    /// <param name="logger">Logger instance for logging service activities.</param>
    public TeamService(string teamsJsonPath, ILogger<TeamService> logger)
    {
        _teamsPath = teamsJsonPath ?? throw new ArgumentNullException(nameof(teamsJsonPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("TeamService initializing...");

        LoadTeamsData();

        _logger.LogInformation("TeamService initialized.");
    }

    /// <summary>
    /// Loads the team data from the specified JSON file.
    /// This method handles errors related to file reading and deserialization, and initializes the data structures accordingly.
    /// </summary>
    private void LoadTeamsData()
    {
        _logger.LogInformation($"Attempting to load teams data from {_teamsPath}");

        // Initialize empty dictionaries as fallback in case of errors or missing/empty files
        _teamsData = new Dictionary<string, Team>();
        _playersData = new Dictionary<string, List<string>>();
        List<Team>? teamList; // Temporary list for deserialization

        if (File.Exists(_teamsPath))
        {
            try
            {
                var content = File.ReadAllText(_teamsPath);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    // Deserialize JSON content into a list of teams
                    var options = new JsonSerializerOptions
                    {
                        AllowTrailingCommas = true, // Allow trailing commas
                        ReadCommentHandling = JsonCommentHandling.Skip // Ignore comments
                    };
                    teamList = JsonSerializer.Deserialize<List<Team>>(content, options);

                    if (teamList == null)
                    {
                        _logger.LogWarning($"Deserialization returned null for {_teamsPath}. Initializing empty data.");
                        teamList = []; // Ensure the list isn't null for the subsequent foreach
                    }
                }
                else
                {
                    _logger.LogInformation($"Teams file {_teamsPath} is empty. Initializing empty data.");
                    teamList = []; // Initialize an empty list if the content is empty
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, $"Failed to deserialize teams data from {_teamsPath}. Initializing empty data.");
                teamList = []; // Initialize empty list in case of JSON deserialization error
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, $"Failed to read teams file {_teamsPath}. Initializing empty data.");
                teamList = []; // Initialize empty list in case of file IO error
            }
            catch (Exception ex) // Catch any other unexpected errors
            {
                _logger.LogError(ex, $"An unexpected error occurred while loading teams from {_teamsPath}. Initializing empty data.");
                teamList = []; // Initialize empty list
            }
        }
        else
        {
            _logger.LogWarning($"Teams file {_teamsPath} not found. Initializing empty data.");
            teamList = []; // Initialize empty list if the file doesn't exist
        }

        // Populate the internal dictionaries from the deserialized data (or from an empty list)
        _dataLock.Wait();
        try
        {
            foreach (var item in teamList)
            {
                // Basic validation of data read from the file
                if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Sport))
                {
                    _logger.LogWarning($"Skipping team with invalid data from file: Name='{item.Name}', Sport='{item.Sport}'");
                    continue; // Skip malformed entry
                }

                // Add the team to the dictionary (_teamsData)
                var team = new Team { Name = item.Name, Sport = item.Sport, EnableScorers = item.EnableScorers };
                if (!_teamsData.TryAdd(item.Name, team))
                {
                    _logger.LogWarning($"Duplicate team name found in {_teamsPath}: '{item.Name}'. Skipping duplicate entry.");
                    continue; // Skip if the team name is a duplicate
                }

                // Add the list of players to the dictionary (_playersData)
                _playersData.TryAdd(item.Name, item.Players ?? new List<string>());
            }
            _logger.LogInformation($"Successfully processed {teamList.Count} entries. Loaded {_teamsData.Count} unique teams.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing loaded team data.");
            // Optionally clear dictionaries if population fails halfway
            _teamsData.Clear();
            _playersData.Clear();
            _logger.LogWarning("Cleared team data due to processing error.");
        }
        finally
        {
            _dataLock.Release();
        }
    }

    /// <summary>
    /// Checks if the specified team has scorers enabled based on the <em>EnableScorers</em> property in the team's data.
    /// </summary>
    /// <param name="teamName">The name of the team to check.</param>
    /// <returns>A boolean indicating whether the scorers are enabled for the specified team.</returns>
    /// <exception cref="ArgumentException">Thrown if the team name is null or whitespace.</exception>
    public bool IsScorersEnabled(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName)) throw new ArgumentException("Team name cannot be null or whitespace.", nameof(teamName));

        _dataLock.Wait(); // Acquire lock synchronously for read
        try
        {
            if (_teamsData.TryGetValue(teamName, out var team))
            {
                return team.EnableScorers;
            }
            _logger.LogWarning($"Team '{teamName}' not found in data. Cannot determine if scorers are enabled.");
            return false; // Return false if the team is not found
        }
        finally
        {
            _dataLock.Release(); // Release lock
        }
    }

    /// <summary>
    /// Retrieves all teams that participate in a specific sport.
    /// </summary>
    /// <param name="sport">The sport for which to retrieve the teams.</param>
    /// <returns>A list of teams that participate in the specified sport.</returns>
    /// <exception cref="ArgumentException">Thrown if the sport name is null or whitespace.</exception>
    public List<Team> GetTeamsBySport(string sport)
    {
        if (string.IsNullOrWhiteSpace(sport)) throw new ArgumentException("Sport name cannot be null or whitespace.", nameof(sport));
        _logger.LogDebug($"Getting teams for sport: {sport}");

        _dataLock.Wait(); // Acquire lock synchronously for read
        try
        {
            return _teamsData.Values.Where(t => t.Sport.Equals(sport, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        finally
        {
            _dataLock.Release(); // Release lock
        }
    }

    /// <summary>
    /// Retrieves the sport associated with a specific team.
    /// </summary>
    /// <param name="teamName">The name of the team for which to retrieve the sport.</param>
    /// <returns>The sport of the specified team, or "Unknown Sport" if the team is not found.</returns>
    /// <exception cref="ArgumentException">Thrown if the team name is null or whitespace.</exception>
    public string GetTeamSport(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName)) throw new ArgumentException("Team name cannot be null or whitespace.", nameof(teamName));
        _logger.LogDebug($"Getting sport for team: {teamName}");

        _dataLock.Wait(); // Acquire lock synchronously for read
        try
        {
            if (_teamsData.TryGetValue(teamName, out var team))
            {
                return team.Sport;
            }
            _logger.LogWarning($"Team '{teamName}' not found in data. Cannot determine sport.");
            return "Unknown Sport"; // Default or throw
        }
        finally
        {
            _dataLock.Release(); // Release lock
        }
    }

    /// <summary>
    /// Retrieves the list of players for a specific team.
    /// </summary>
    /// <param name="teamName">The name of the team for which to retrieve players.</param>
    /// <returns>A list of player names for the specified team, or an empty list if no players are found.</returns>
    /// <exception cref="ArgumentException">Thrown if the team name is null or whitespace.</exception>
    public List<string> GetPlayersByTeam(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName)) throw new ArgumentException("Team name cannot be null or whitespace.", nameof(teamName));
        _logger.LogDebug($"Getting players for team: {teamName}");

        _dataLock.Wait(); // Acquire lock synchronously for read
        try
        {
            // Adapt this based on how players are stored
            if (_playersData.TryGetValue(teamName, out var players))
            {
                return players;
            }
            _logger.LogWarning($"Players not found for team '{teamName}'. Returning empty list.");
            return new List<string>(); // Return empty list if team or players not found
        }
        finally
        {
            _dataLock.Release(); // Release lock
        }
    }

    /// <summary>
    /// Retrieves all unique sports in the tournament by checking each team's sport.
    /// </summary>
    /// <returns>A list of distinct sports in the tournament (case-insensitive).</returns>
    public List<string> GetAllSports()
    {
        _logger.LogDebug("Getting all sports in the tournament.");

        _dataLock.Wait();
        try
        {
            // Get all unique sports by using LINQ
            return _teamsData.Values
                .Select(t => t.Sport.ToLower())
                .Distinct()
                .ToList();
        }
        finally
        {
            _dataLock.Release();
        }
    }

    #endregion
}