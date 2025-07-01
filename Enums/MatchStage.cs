namespace SportMatchBot.Enums;

/// <summary>
/// Enum representing the different stages of a match in the bot workflow.
/// </summary>
public enum MatchStage
{
    /// <summary>
    /// The initial stage where the match is yet to begin.
    /// </summary>
    Start,

    /// <summary>
    /// Stage for selecting the sport for the match.
    /// </summary>
    Sport,

    /// <summary>
    /// Stage for selecting the first team.
    /// </summary>
    Team1,

    /// <summary>
    /// Stage for selecting the second team.
    /// </summary>
    Team2,

    /// <summary>
    /// Stage for inputting the score of the first team.
    /// </summary>
    Score1,

    /// <summary>
    /// Stage for inputting the score of the second team.
    /// </summary>
    Score2,

    /// <summary>
    /// Stage for selecting the scorers for the teams.
    /// </summary>
    Scorers,

    /// <summary>
    /// Final confirmation stage before the match is saved.
    /// </summary>
    Confirmation
}
