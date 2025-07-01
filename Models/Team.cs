using System.Collections.Generic;
using System.Text.Json.Serialization; // Necessary for [JsonPropertyName]

namespace SportMatchBot.Models;

/// <summary>
/// Represents a team in the bot, containing details such as name, sport, players, and whether scorers are enabled.
/// This model is used for deserializing team data from a JSON file.
/// </summary>
public class Team
{
    /// <summary>
    /// Gets or sets the name of the team.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = ""; // Initializes to avoid null

    /// <summary>
    /// Gets or sets the sport associated with the team.
    /// </summary>
    [JsonPropertyName("sport")]
    public string Sport { get; set; } = ""; // Initializes to avoid null

    /// <summary>
    /// Gets or sets the list of players in the team.
    /// </summary>
    [JsonPropertyName("players")]
    public List<string> Players { get; set; } = new List<string>(); // Initializes to avoid null

    /// <summary>
    /// Gets or sets a flag indicating whether scorers are enabled for this team.
    /// </summary>
    [JsonPropertyName("enableScorers")]
    public bool EnableScorers { get; set; } = false; // Default is false, meaning scorers are not enabled by default
}
