using System.Collections.Generic;
using SportMatchBot.Enums;

namespace SportMatchBot.Models;

/// <summary>
/// Represents the transient state of an ongoing match for a specific chat.
/// </summary>
public class MatchState
{
    /// <summary>
    /// The chat ID that this match state belongs to.
    /// </summary>
    public long ChatId { get; set; }

    /// <summary>
    /// The current stage of the match registration (e.g., "sport", "team1", "score1", "scorers", "confirmation").
    /// </summary>
    public MatchStage Stage { get; set; }

    /// <summary>
    /// The sport selected for the match.
    /// </summary>
    public string Sport { get; set; } = string.Empty;

    /// <summary>
    /// The name of the first team selected.
    /// </summary>
    public string? Team1 { get; set; }

    /// <summary>
    /// The name of the second team selected.
    /// </summary>
    public string? Team2 { get; set; }

    /// <summary>
    /// The score of the first team.
    /// </summary>
    public int? Score1 { get; set; }

    /// <summary>
    /// The score of the second team.
    /// </summary>
    public int? Score2 { get; set; }

    /// <summary>
    /// The team for which the scorers are currently being selected.
    /// </summary>
    public string? ScoringTeam { get; set; }

    /// <summary>
    /// The list of scorers for the first team.
    /// </summary>
    public List<string> ScorersTeam1 { get; set; } = new List<string>();

    /// <summary>
    /// The list of scorers for the second team.
    /// </summary>
    public List<string> ScorersTeam2 { get; set; } = new List<string>();

    /// <summary>
    /// The date and time when the match state was created.
    /// </summary>
    public DateTime Timestamp { get; set; }
}
