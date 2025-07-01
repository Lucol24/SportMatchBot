using SportMatchBot.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging; 
using System; 
using SportMatchBot.Enums; 

namespace SportMatchBot.Services;

/// <summary>
/// Manages the transient conversation state for each chat.
/// </summary>
public class StateService
{
    private readonly ConcurrentDictionary<long, MatchState> _chatStates = new();

    private readonly ILogger<StateService> _logger;

    /// <summary>
    /// Initializes a new instance of the StateService class.
    /// </summary>
    /// <param name="logger">The logger instance to be injected.</param>
    /// <exception cref="ArgumentNullException">Thrown if the logger is null.</exception>
    public StateService(ILogger<StateService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("StateService initialized.");
    }

    #region Public Methods

    /// <summary>
    /// Retrieves the state for a specific chat, or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="chatId">The chat ID for which the state should be retrieved or created.</param>
    /// <returns>The MatchState instance for the specified chat.</returns>
    public MatchState GetOrCreateState(long chatId)
    {
        // If the 'chatId' key exists, it returns its value.
        // If it doesn't exist, it creates a new MatchState using the provided lambda function,
        // adds it to the dictionary, and then returns it.
        var state = _chatStates.GetOrAdd(chatId, id =>
        {
            _logger.LogDebug($"Creating new state for chat {id}.");
            return new MatchState { ChatId = id, Stage = MatchStage.Start }; // Initialize with chatId and an initial stage
        });

        _logger.LogTrace($"State accessed for chat {chatId}. Current stage: {state.Stage.ToString() ?? "None"}");
        return state;
    }

    /// <summary>
    /// Removes the state for a specific chat.
    /// </summary>
    /// <param name="chatId">The chat ID for which the state should be cleared.</param>
    public void ClearState(long chatId)
    {
        // It attempts to remove the key. Returns true if removal was successful, false otherwise.
        if (_chatStates.TryRemove(chatId, out var removedState))
        {
            _logger.LogInformation($"State cleared for chat {chatId}. Final stage was: {removedState?.Stage.ToString() ?? "None"}");
        }
        else
        {
            // This debug/trace log is useful to understand why a ClearState
            // might not have removed anything (e.g., state was already removed or never existed).
            _logger.LogDebug($"Attempted to clear state for chat {chatId}, but no state was found.");
        }
    }

    #endregion
}