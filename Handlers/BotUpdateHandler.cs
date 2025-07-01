using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Telegram.Bot.Exceptions;

using SportMatchBot.Services;
using SportMatchBot.Models;
using SportMatchBot.Enums;

namespace SportMatchBot.Handlers;

/// <summary>
/// Handles incoming updates from Telegram Bot, including messages and callback queries. 
/// This class is responsible for processing user interactions, including handling commands, 
/// managing match stages, and sending appropriate responses based on user input.
/// </summary>
/// <remarks>
/// The <see cref="BotUpdateHandler"/> class listens for updates (e.g., callback queries and messages) from the Telegram Bot API.
/// It processes commands like <em>/start</em>, <em>/help</em>, <em>/match</em>, <em>/info</em>, and <em>/undo</em>, and manages the flow of an ongoing match.
/// The handler also localizes messages and commands to support multiple languages, ensuring a smooth user experience.
/// </remarks>
public class BotUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly MatchLogService _matchLogService;
    private readonly TeamService _teamService;
    private readonly StateService _stateService;
    private readonly LoggerService _messageLogger;
    private readonly LocalizationService _localizationService;
    private readonly ILogger<BotUpdateHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BotUpdateHandler"/> class, which is responsible for handling updates from the Telegram bot.
    /// </summary>
    /// <param name="botClient">The Telegram bot client for sending and receiving messages.</param>
    /// <param name="matchLogService">The service responsible for logging match data.</param>
    /// <param name="teamService">The service responsible for managing team-related data.</param>
    /// <param name="stateService">The service responsible for tracking the state of ongoing matches.</param>
    /// <param name="messageLogger">The service for logging messages sent by the bot.</param>
    /// <param name="localizationService">The service for handling localization and providing translated strings.</param>
    /// <param name="logger">The logger instance used to log events and errors for this handler.</param>
    /// <remarks>
    /// This constructor ensures that all dependencies are provided and initializes the necessary services for handling bot updates.
    /// It also logs the initialization of the handler for tracking purposes.
    /// </remarks>
    public BotUpdateHandler(
        ITelegramBotClient botClient,
        MatchLogService matchLogService,
        TeamService teamService,
        StateService stateService,
        LoggerService messageLogger,
        LocalizationService localizationService,
        ILogger<BotUpdateHandler> logger)
    {
        _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
        _matchLogService = matchLogService ?? throw new ArgumentNullException(nameof(matchLogService));
        _teamService = teamService ?? throw new ArgumentNullException(nameof(teamService));
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        _messageLogger = messageLogger ?? throw new ArgumentNullException(nameof(messageLogger));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("BotUpdateHandler initialized.");
    }

    #region Public Methods

    /// <summary>
    /// Handles incoming updates (messages and callback queries) from the user. Depending on the type of update, the method processes the request by calling appropriate handlers.
    /// </summary>
    /// <param name="update">The update object containing the message or callback query sent by the user.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <remarks>
    /// This method performs the following tasks:
    /// - Handles callback queries by calling specific methods based on the query data.
    /// - Handles messages by checking the content and responding to specific commands such as <em>/start</em>, <em>/help</em>, <em>/info</em>, <em>/match</em>, and <em>/undo</em>.
    /// - Fetches the list of available sports for the <em>/match</em> command, presents them to the user as buttons, and responds accordingly.
    /// - If the message is unrecognized, it either sends an unexpected input message or a generic error message depending on the current match stage.
    /// - Uses the localization service to provide localized responses based on the current language setting.
    /// </remarks>
    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            try
            {
                // Answer the callback query to acknowledge reception
                await _botClient.AnswerCallbackQuery(update.CallbackQuery.Id, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to answer callback query {update.CallbackQuery.Id}");
            }

            // Handle the callback query
            await HandleCallbackQueryAsync(update.CallbackQuery, cancellationToken);
            return;
        }

        if (update.Type != UpdateType.Message || update.Message?.Text == null)
        {
            return; // Ignore if message is null or not of type 'Message'
        }

        var message = update.Message;
        var messageText = message.Text.Trim();
        var chatId = message.Chat.Id;
        var from = message.From;

        // Log the message details
        var username = from?.Username ?? "unknown";
        var firstName = from?.FirstName ?? "unknown";
        var lastName = from?.LastName ?? "unknown";
        var userId = from?.Id.ToString() ?? "unknown";
        var date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        await _messageLogger.LogMessageAsync(username, firstName, lastName, userId, date, messageText);

        _logger.LogInformation($"Received message '{messageText}' from user {username} ({userId}) in chat {chatId}");

        try
        {
            // Notify that the bot is typing in response to the message
            await _botClient.SendChatAction(chatId, ChatAction.Typing, cancellationToken: cancellationToken);

            // Handle command "/start"
            if (messageText.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
            {
                string welcomeMessage = _localizationService.GetLocalizedString("start");
                await _botClient.SendMessage(chatId, welcomeMessage, cancellationToken: cancellationToken);
            }
            // Handle command "/help"
            else if (messageText.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
            {
                string helpMessage = _localizationService.GetLocalizedString("help");
                await _botClient.SendMessage(chatId, helpMessage, cancellationToken: cancellationToken);
            }
            // Handle command "/info"
            else if (messageText.StartsWith("/info", StringComparison.OrdinalIgnoreCase))
            {
                // Retrieve all the unique sports in the tournament
                var sports = _teamService.GetAllSports();

                // Create a list of buttons with localized sport names and emojis
                var buttons = sports
                    .Select(sport =>
                    {
                        // Fetch the localized sport name and emoji from the localization service
                        var localizedSportName = _localizationService.GetLocalizedString(sport + ".name");
                        var sportEmoji = _localizationService.GetLocalizedString(sport + ".emoji");

                        // Combine emoji and localized sport name for the button text
                        return InlineKeyboardButton.WithCallbackData($"{sportEmoji} {localizedSportName}", $"info_{sport}");
                    })
                    .ToArray();

                // Create an inline keyboard with the buttons
                var keyboard = new InlineKeyboardMarkup(buttons.Select(b => new[] { b }));

                // Get the localized message for asking the user to choose a sport
                string chooseSportMessage = _localizationService.GetLocalizedString("chooseSportInfo");

                // Send the message with the inline keyboard
                await _botClient.SendMessage(chatId, chooseSportMessage, replyMarkup: keyboard, cancellationToken: cancellationToken);
            }
            // Handle command "/match"
            else if (messageText.StartsWith("/match", StringComparison.OrdinalIgnoreCase))
            {
                // Clear any previous state related to this chat
                _stateService.ClearState(chatId);
                var state = _stateService.GetOrCreateState(chatId);
                state.Stage = MatchStage.Sport;

                // Retrieve all available sports dynamically
                var sports = _teamService.GetAllSports(); // Fetch all sports dynamically

                // Create a list of inline buttons for each sport with emoji and localized names
                var buttons = sports
                    .Select(sport =>
                    {
                        var localizedSport = _localizationService.GetLocalizedString(sport + ".name");
                        var sportEmoji = _localizationService.GetLocalizedString(sport + ".emoji");
                        return InlineKeyboardButton.WithCallbackData($"{sportEmoji} {localizedSport}", $"sport_{sport}");
                    })
                    .ToArray();

                var keyboard = new InlineKeyboardMarkup(buttons.Select(b => new[] { b }));

                // Get the localized message for asking the user to choose a sport
                string msg = _localizationService.GetLocalizedString("selectSportMatch");
                await _botClient.SendMessage(chatId, msg, replyMarkup: keyboard, cancellationToken: cancellationToken);
            }
            // Handle command "/undo"
            else if (messageText.StartsWith("/undo", StringComparison.OrdinalIgnoreCase))
            {
                await _matchLogService.UndoLastMatchAsync(chatId, cancellationToken);
            }
            // Handle unexpected input
            else
            {
                var state = _stateService.GetOrCreateState(chatId);
                if (state.Stage != MatchStage.Start)
                {
                    // Localize the "unexpectedInput" message and format it with the current stage
                    string unexpectedInputMessage = _localizationService.GetLocalizedString("unexpectedInput");
                    string formattedMessage = string.Format(unexpectedInputMessage, state.Stage.ToString());
                    await _botClient.SendMessage(chatId, formattedMessage, cancellationToken: cancellationToken);
                }
                else
                {
                    string unknownCommandMessage = _localizationService.GetLocalizedString("unknownCommand");
                    await _botClient.SendMessage(chatId, unknownCommandMessage, cancellationToken: cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling message '{messageText}' from {chatId}");
            string responseErrorMsg = _localizationService.GetLocalizedString("generalError");
            await _botClient.SendMessage(chatId, responseErrorMsg, cancellationToken: cancellationToken);
            _stateService.ClearState(chatId);
        }
    }

    #endregion

    #region Private Methods
    
    /// <summary>
    /// Processes the completion of the scorer selection phase for a team, confirming the selected scorers and handling the transition
    /// to the next team or the match confirmation stage. It also ensures that the match progresses to the next logical step depending
    /// on whether there are scorers for both teams.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to be edited with the updated match information.</param>
    /// <param name="state">The current state of the match, including the teams, scores, and scorers.</param>
    /// <param name="cancellationToken">A token used to cancel the operation if needed.</param>
    /// <remarks>
    /// This method:
    /// - Validates the current team's scorers and updates the match state.
    /// - Localizes messages for confirming the scorers and proceeding with the next steps.
    /// - Sends a confirmation message or proceeds to the next team depending on whether scorers have been selected for the current team.
    /// - Handles the case where no scorers are selected and moves to the match confirmation stage.
    /// </remarks>
    private async Task ProcessCompletedScorersStepAsync(long chatId, int messageId, MatchState state, CancellationToken cancellationToken)
    {
        string currentTeamDone = state.ScoringTeam ?? throw new InvalidOperationException("ScoringTeam cannot be null in ProcessCompletedScorersStepAsync.");
        List<string> scorersEntered = (currentTeamDone == state.Team1) ? state.ScorersTeam1 : state.ScorersTeam2;
        string currentTeamDisplay = currentTeamDone;

        // Localize the "Scorers confirmed" message
        string confirmationMessage = _localizationService.GetLocalizedString("scorersConfirmed");

        string message = $"{confirmationMessage} {currentTeamDisplay}.\n";

        if (scorersEntered.Any())
        {
            message += $"{_localizationService.GetLocalizedString("scorersSoFar")}: ({string.Join(", ", scorersEntered)})";
        }

        if (currentTeamDone == state.Team1)
        {
            state.ScoringTeam = state.Team2;
            string team2Display = state.Team2 ?? _localizationService.GetLocalizedString("unknownTeam");

            if ((state.Score2 ?? 0) > 0)
            {
                // Localize message for team 2 scorer selection
                string selectTeam2ScorersMessage = _localizationService.GetLocalizedString("selectTeam2Scorers");
                message += $"\n\n{selectTeam2ScorersMessage} {team2Display}:";
                await SendScorerSelectionMessageAsync(chatId, messageId, state, cancellationToken, message);
            }
            else
            {
                state.Stage = MatchStage.Confirmation;
                await SendConfirmationMessageAsync(chatId, messageId, state, cancellationToken);
            }
        }
        else
        {
            state.Stage = MatchStage.Confirmation;
            await SendConfirmationMessageAsync(chatId, messageId, state, cancellationToken);
        }
    }

    /// <summary>
    /// Sends a message to the user to select scorers for the current match. This method also handles incomplete or invalid states,
    /// and provides localized messages for various scenarios such as no scorers available, no players found, or invalid data.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to be edited with the scorer selection details.</param>
    /// <param name="state">The current state of the match, including teams, scores, and scorers.</param>
    /// <param name="cancellationToken">A token used to cancel the operation if needed.</param>
    /// <param name="precedingMessage">An optional preceding message that will be included in the message body.</param>
    /// <remarks>
    /// This method:
    /// - Verifies if the state contains all the necessary information (e.g., teams and scores).
    /// - Handles scenarios where no scorers are available, displaying an appropriate error message if needed.
    /// - If the team has players, it generates a selection list for choosing scorers and localizes all relevant messages.
    /// - If there are no players or if a scoring error occurs, it provides localized warnings or error messages.
    /// </remarks>
    private async Task SendScorerSelectionMessageAsync(long chatId, int messageId, MatchState state, CancellationToken cancellationToken, string? precedingMessage = null)
    {
        if (state.ScoringTeam == null || !state.Score1.HasValue || !state.Score2.HasValue)
        {
            _logger.LogError($"SendScorerSelectionMessageAsync called with incomplete state for chat {chatId}. Stage: {state.Stage.ToString() ?? "None"}");

            // Localize error message for incomplete state
            string errorMessage = _localizationService.GetLocalizedString("internalErrorIncompleteState");
            await _botClient.EditMessageText(chatId, messageId, errorMessage, cancellationToken: cancellationToken);
            _stateService.ClearState(chatId);
            return;
        }

        string teamName = state.ScoringTeam;
        int goalsForThisTeam = teamName == state.Team1 ? state.Score1.Value : state.Score2.Value;
        List<string> scorersForThisTeam = (teamName == state.Team1) ? state.ScorersTeam1 : state.ScorersTeam2;
        string teamNameDisplay = teamName;

        _logger.LogDebug($"Sending scorer selection message for {teamName} ({scorersForThisTeam.Count}/{goalsForThisTeam}) to {chatId}.");

        string baseMessage = precedingMessage != null ? $"{precedingMessage}\n\n" : "";

        if (goalsForThisTeam == 0)
        {
            _logger.LogError($"SendScorerSelectionMessageAsync called for team {teamName} with 0 goals. This indicates a logic error.");

            // Localize error message for 0 goals
            baseMessage += _localizationService.GetLocalizedString("internalErrorZeroGoals");
            await _botClient.EditMessageText(chatId, messageId, baseMessage, cancellationToken: cancellationToken);
            return;
        }

        var players = _teamService.GetPlayersByTeam(teamName);
        var keyboardButtons = new List<List<InlineKeyboardButton>>();

        if (players == null || !players.Any())
        {
            _logger.LogWarning($"No players found for team {teamName} when trying to select scorers.");

            // Localize warning for no players found
            baseMessage += _localizationService.GetLocalizedString("noPlayersForTeam");
        }

        var currentRow = new List<InlineKeyboardButton>();
        if (players == null)
        {
            _logger.LogWarning($"No players found for team {teamName}.");
            return;
        }

        foreach (var player in players)
        {
            var buttonText = $"👤 {player}";
            var callbackData = $"scorer_{player}";

            if (System.Text.Encoding.UTF8.GetBytes(callbackData).Length > 64)
            {
                _logger.LogWarning($"Player name '{player}' is too long for callback data ({System.Text.Encoding.UTF8.GetBytes(callbackData).Length} bytes). Skipping player.");
                continue;
            }

            currentRow.Add(InlineKeyboardButton.WithCallbackData(buttonText, callbackData));

            if (currentRow.Count == 2)
            {
                keyboardButtons.Add(new List<InlineKeyboardButton>(currentRow));
                currentRow.Clear();
            }
        }
        if (currentRow.Any()) keyboardButtons.Add(currentRow);

        // Add "Skip scorer" button with localization
        var skipButton = InlineKeyboardButton.WithCallbackData(_localizationService.GetLocalizedString("skipScorer"), "scorer_skip!");
        keyboardButtons.Add(new List<InlineKeyboardButton> { skipButton });

        // Localize the selection prompt
        string selectionPrompt = _localizationService.GetLocalizedString("selectScorerPrompt");
        selectionPrompt = string.Format(selectionPrompt, scorersForThisTeam.Count + 1, goalsForThisTeam, teamNameDisplay);
        baseMessage += selectionPrompt;

        if (scorersForThisTeam.Any())
        {
            baseMessage += $"\n\n{_localizationService.GetLocalizedString("soFar")} ({string.Join(", ", scorersForThisTeam)})";
        }

        await _botClient.EditMessageText(chatId, messageId, baseMessage,
            replyMarkup: new InlineKeyboardMarkup(keyboardButtons), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends a confirmation message to the user after a match has been registered, including team names, scores, and scorers if applicable.
    /// Displays the match details and provides options to confirm or cancel.
    /// </summary>
    /// <param name="chatId">The chat ID where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to edit with confirmation details.</param>
    /// <param name="state">The current state of the match, including teams, scores, and scorers.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Localizes the sport name, emoji, team names, and score details for the match.
    /// - Handles cases where scorers are disabled by displaying a message indicating that scorers are not enabled.
    /// - Displays the scorers for the match if scorers are enabled and available.
    /// - Includes buttons for confirming or canceling the match registration.
    /// </remarks>
    private async Task SendConfirmationMessageAsync(long chatId, int messageId, MatchState state, CancellationToken cancellationToken)
    {
        _logger.LogDebug($"Sending confirmation message to {chatId}.");

        // Localize sport name and emoji
        string sport = state.Sport ?? _teamService.GetTeamSport(state.Team1 ?? "Unknown Sport");
        string emoji = _localizationService.GetLocalizedString($"{sport.ToLower()}.emoji");

        // Localize team names
        string team1Display = state.Team1 ?? _localizationService.GetLocalizedString("unknownTeam");
        string team2Display = state.Team2 ?? _localizationService.GetLocalizedString("unknownTeam");

        // Localize score display
        string score1Display = state.Score1?.ToString() ?? "?";
        string score2Display = state.Score2?.ToString() ?? "?";

        // Localize the confirmation buttons
        var confirmButton = _localizationService.GetLocalizedString("confirm");
        var cancelButton = _localizationService.GetLocalizedString("cancel");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] {
                InlineKeyboardButton.WithCallbackData($"✅ {confirmButton}", "confirm"),
                InlineKeyboardButton.WithCallbackData($"❌ {cancelButton}", "cancel")
            }
        });

        // Check if the scorers are enabled for this sport
        if ((state.Team1 == null || !_teamService.IsScorersEnabled(state.Team1)) &&
            (state.Team2 == null || !_teamService.IsScorersEnabled(state.Team2)))
        {
            // If the scorers are disabled, send the scorersDisabled message instead
            string scorersDisabledMessage = _localizationService.GetLocalizedString("scorersDisabled");

            string summary =
                $"✅ {_localizationService.GetLocalizedString("matchConfirmation")} ✅\n\n" +
                $"{emoji} {_localizationService.GetLocalizedString(sport + ".name")}\n" +
                $"🆚 {team1Display} {score1Display} - {score2Display} {team2Display}\n\n" +
                $"🎯 {scorersDisabledMessage}";

            await _botClient.EditMessageText(chatId, messageId, summary, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        else
        {
            // Handle scorer selection for teams that have scorers enabled
            var sb = new System.Text.StringBuilder();
            if (state.ScorersTeam1.Any())
                sb.AppendLine($"{team1Display}: {string.Join(", ", state.ScorersTeam1)}");
            if (state.ScorersTeam2.Any())
                sb.AppendLine($"{team2Display}: {string.Join(", ", state.ScorersTeam2)}");

            string matchScorersSection = sb.Length > 0
                ? sb.ToString().TrimEnd()
                : _localizationService.GetLocalizedString("noScorersFound");

            // Construct the match confirmation message
            string summary =
                $"✅ {_localizationService.GetLocalizedString("matchConfirmation")} ✅\n\n" +
                $"{emoji} {_localizationService.GetLocalizedString(sport + ".name")}\n" +
                $"🆚 {team1Display} {score1Display} - {score2Display} {team2Display}\n\n" +
                $"🎯 {_localizationService.GetLocalizedString("matchScorers")}\n" +
                matchScorersSection;

            // Send the final message
            await _botClient.EditMessageText(chatId, messageId, summary, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }

        state.Stage = MatchStage.Confirmation;
    }

    #endregion

    #region Private Handlers

    /// <summary>
    /// Handles incoming callback queries from the user. Based on the query data, it processes the request by calling appropriate methods for each match stage.
    /// </summary>
    /// <param name="callbackQuery">The callback query received from the user.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method performs the following tasks:
    /// - Retrieves necessary data from the callback query, such as the chat ID, message ID, and query data.
    /// - Validates the callback query data and sends a localized error message if the data is invalid.
    /// - Determines the match stage and processes the callback accordingly, such as selecting a sport, team, score, or scorer.
    /// - Handles the confirmation and cancellation of matches.
    /// - If any exception occurs during processing, it logs the error and responds with a general error message.
    /// </remarks>
    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message?.Chat?.Id;
        var messageId = callbackQuery.Message?.MessageId;
        var data = callbackQuery.Data;

        if (!chatId.HasValue || !messageId.HasValue || data == null)
        {
            // Log the warning with detailed information
            _logger.LogWarning($"Received invalid callback query: chatId={chatId}, messageId={messageId}, data={data}");

            // Retrieve the localized error message for invalid callback query data
            string errorMessage = _localizationService.GetLocalizedString("invalidCallbackData");

            // Respond to the user with the localized error message
            await _botClient.AnswerCallbackQuery(callbackQuery.Id, errorMessage, showAlert: true, cancellationToken: cancellationToken);
            return;
        }

        var state = _stateService.GetOrCreateState(chatId.Value);
        _logger.LogInformation($"Received callback query with data '{data}' from chat {chatId.Value}. Current stage: {state.Stage.ToString() ?? "None"}");


        try
        {
            if (data.StartsWith("sport_") && state.Stage == MatchStage.Sport)
            {
                await HandleSportSelection(chatId.Value, messageId.Value, state, data, cancellationToken);
            }
            else if (data.StartsWith("team1_") && state.Stage == MatchStage.Team1)
            {
                await HandleTeam1Selection(chatId.Value, messageId.Value, state, data, cancellationToken);
            }
            else if (data.StartsWith("team2_") && state.Stage == MatchStage.Team2 && state.Team1 != null)
            {
                await HandleTeam2Selection(chatId.Value, messageId.Value, state, data, cancellationToken);
            }
            else if (data.StartsWith("score1_") && state.Stage == MatchStage.Score1 && state.Team1 != null && state.Team2 != null)
            {
                await HandleScoreInput(chatId.Value, messageId.Value, state, data, callbackQuery, "score1", cancellationToken);
            }
            else if (data.StartsWith("score2_") && state.Stage == MatchStage.Score2 && state.Team1 != null && state.Team2 != null)
            {
                await HandleScoreInput(chatId.Value, messageId.Value, state, data, callbackQuery, "score2", cancellationToken);
            }
            else if (data.StartsWith("scorer_") && state.Stage == MatchStage.Scorers && state.ScoringTeam != null && state.Score1.HasValue && state.Score2.HasValue)
            {
                await HandleScorerSelection(chatId.Value, messageId.Value, state, data, callbackQuery, cancellationToken);
            }
            else if (data.StartsWith("info_"))
            {
                await HandleSportInfoRequest(chatId.Value, messageId.Value, state, data, cancellationToken);
            }
            else if (data == "confirm" && state.Stage == MatchStage.Confirmation)
            {
                await HandleMatchConfirmation(chatId.Value, messageId.Value, state, cancellationToken);
            }
            else if (data == "cancel")
            {
                await HandleMatchCancellation(chatId.Value, messageId.Value, cancellationToken);
            }
            else
            {
                await HandleUnexpectedInput(chatId.Value, messageId.Value, state.Stage, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error handling callback query '{data}' from {chatId.Value}");
            await HandleErrorMessage(chatId.Value, messageId.Value, "⚠️ An error occurred while processing your request.", cancellationToken);
        }
    }

    /// <summary>
    /// Handles the sport selection during the match registration process. This method updates the match state with the selected sport, transitions to the "Team1" stage,
    /// and prompts the user to select Team 1. It also generates a keyboard with the available teams for the selected sport.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to be edited with the updated information.</param>
    /// <param name="state">The current state of the match, including the selected sport and stage.</param>
    /// <param name="data">The callback data representing the selected sport.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Sets the selected sport and updates the match state to the "Team1" stage.
    /// - Checks if there are any teams available for the selected sport and handles the case where no teams are found.
    /// - Sends a localized message with the sport name and emoji, along with a list of available teams for Team 1 selection.
    /// </remarks>
    private async Task HandleSportSelection(long chatId, int messageId, MatchState state, string data, CancellationToken cancellationToken)
    {
        state.Sport = data.Split('_')[1];
        state.Stage = MatchStage.Team1;

        // Localize sport name and emoji
        string localizedSportName = _localizationService.GetLocalizedString(state.Sport + ".name");
        string sportEmoji = _localizationService.GetLocalizedString(state.Sport + ".emoji");

        var teams = _teamService.GetTeamsBySport(state.Sport);
        if (teams == null || teams.Count == 0)
        {
            string noTeamsMessage = _localizationService.GetLocalizedString("noTeamsFound");
            await _botClient.EditMessageText(chatId, messageId, $"{noTeamsMessage} {localizedSportName}.", cancellationToken: cancellationToken);
            _stateService.ClearState(chatId);
            return;
        }

        // Create the buttons with localized names and emojis
        var keyboard = teams.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"🛡️ {t.Name}", $"team1_{t.Name}") }).ToArray();
        string selectionMessage = _localizationService.GetLocalizedString("selectTeam1Message");
        await _botClient.EditMessageText(chatId, messageId, $"{sportEmoji} {localizedSportName}\n\n{selectionMessage}", replyMarkup: new InlineKeyboardMarkup(keyboard), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Handles the selection of Team 1 in the match registration process. This method sets the selected Team 1, updates the match state to the Team 2 selection phase, 
    /// and sends a message prompting the user to select Team 2. It also creates the corresponding keyboard for selecting Team 2 from the available teams.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to be edited with the updated information.</param>
    /// <param name="state">The current state of the match, including the teams, scores, and scorers.</param>
    /// <param name="data">The callback data representing the selected Team 1.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Sets the selected Team 1 in the match state and transitions to the "Team2" stage.
    /// - If no available teams are left for Team 2, sends a localized message indicating that no second team is available and clears the state.
    /// - Creates a list of buttons for selecting Team 2, with localized names and emojis, and displays it to the user.
    /// </remarks>
    private async Task HandleTeam1Selection(long chatId, int messageId, MatchState state, string data, CancellationToken cancellationToken)
    {
        state.Team1 = data.Split('_')[1];
        state.Stage = MatchStage.Team2;

        // Localize the team selection message
        var teams = _teamService.GetTeamsBySport(state.Sport).Where(t => t.Name != state.Team1).ToList();
        if (teams.Count == 0)
        {
            string noSecondTeamMessage = _localizationService.GetLocalizedString("noSecondTeamAvailable");
            await _botClient.EditMessageText(chatId, messageId, noSecondTeamMessage, cancellationToken: cancellationToken);
            _stateService.ClearState(chatId);
            return;
        }

        // Create the buttons for selecting team 2 with localized names and emojis
        var keyboard = teams.Select(t => new[] { InlineKeyboardButton.WithCallbackData($"🛡️ {t.Name}", $"team2_{t.Name}") }).ToArray();
        string selectTeam2Message = _localizationService.GetLocalizedString("selectTeam2Message");
        string team1 = _localizationService.GetLocalizedString("team1");
        await _botClient.EditMessageText(chatId, messageId, $"✅ {team1}: {state.Team1}\n\n{selectTeam2Message}", replyMarkup: new InlineKeyboardMarkup(keyboard), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Handles the selection of Team 2 in the match registration process. This method sets the selected Team 2, updates the match state to the score input phase 
    /// for Team 1, and sends a message prompting the user to enter the score for Team 1. It also generates the corresponding keyboard for score input.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to be edited with the updated information.</param>
    /// <param name="state">The current state of the match, including the teams, scores, and scorers.</param>
    /// <param name="data">The callback data representing the selected Team 2.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Sets the selected Team 2 in the match state and transitions to the "Score1" stage.
    /// - Sends a localized message prompting the user to enter the score for Team 1, with the current score displayed.
    /// - Creates a score input keyboard for entering the score for Team 1 and displays it with the message.
    /// </remarks>
    private async Task HandleTeam2Selection(long chatId, int messageId, MatchState state, string data, CancellationToken cancellationToken)
    {
        state.Team2 = data.Split('_')[1];
        state.Stage = MatchStage.Score1;
        state.ScoringTeam = state.Team1;

        // Localize the score prompt message
        var keyboardButtons = CreateScoreKeyboard("score1");
        string scoreMessage = _localizationService.GetLocalizedString("scorePromptMessage");
        string team1 = _localizationService.GetLocalizedString("team1");
        string team2 = _localizationService.GetLocalizedString("team2");
        string messageText = $"{scoreMessage}\n{team1}: {state.Team1} {state.Score1}\n{team2}: {state.Team2}\n\n🥅 {_localizationService.GetLocalizedString("enterScoreFor")} {state.Team1}:";
        await _botClient.EditMessageText(chatId, messageId, messageText, replyMarkup: new InlineKeyboardMarkup(keyboardButtons), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Handles the input for score selection during the match registration process. It processes the "done", "delete", or individual score input actions 
    /// for either Team 1 or Team 2 and updates the match state accordingly. Depending on the progress of score input, it transitions between score input stages, 
    /// scorer selection, or match confirmation.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to be edited with the updated score information.</param>
    /// <param name="state">The current state of the match, including the teams, scores, and scorers.</param>
    /// <param name="data">The callback data representing the score action (done, delete, or individual score input).</param>
    /// <param name="callbackQuery">The callback query associated with the score action.</param>
    /// <param name="scoreType">Indicates whether the score being updated is for "score1" (Team 1) or "score2" (Team 2).</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Handles "done" actions to finalize the score for a team and transition to the next team or scorer selection.
    /// - Handles "delete" actions to remove the last digit of the score.
    /// - Handles individual score inputs, updating the score for the current team and ensuring the correct number of digits is entered.
    /// - Sends a localized message for each action (score input, score deletion, invalid score, etc.).
    /// - If a team’s score input is complete, it transitions to the next phase (scorer selection or match confirmation).
    /// </remarks>
    private async Task HandleScoreInput(long chatId, int messageId, MatchState state, string data, CallbackQuery callbackQuery, string scoreType, CancellationToken cancellationToken)
    {
        try
        {
            int? currentScore = scoreType == "score1" ? state.Score1 : state.Score2;

            // Handle "done" button
            if (data == $"{scoreType}_done")
            {
                // If the score is not set, initialize it to 0 for the current team
                currentScore ??= 0;

                if (scoreType == "score1")
                {
                    // Ensure that Team 1 has a score before moving to Team 2
                    if (state.Score1 == null)
                    {
                        state.Score1 = 0;  // Set Team 1's score to 0 if it's not set
                    }

                    state.Stage = MatchStage.Score2;

                    // Update the message for score2 input
                    var keyboardButtons = CreateScoreKeyboard("score2");

                    // Fetch localized message for score input
                    string messageText = $"{_localizationService.GetLocalizedString("scorePromptMessage")}\n" +
                                        $"{_localizationService.GetLocalizedString("team1")} {state.Team1}: {state.Score1}\n" +
                                        $"{_localizationService.GetLocalizedString("team2")} {state.Team2}:\n\n" +
                                        $"{_localizationService.GetLocalizedString("enterScoreFor")} {state.Team2}:";
                    await _botClient.EditMessageText(chatId, messageId, messageText, replyMarkup: new InlineKeyboardMarkup(keyboardButtons), cancellationToken: cancellationToken);
                }
                else if (scoreType == "score2")
                {
                    // Ensure that Team 2 has a score before moving to next stage
                    state.Score2 ??= 0;  // Set Team 2's score to 0 if it's not set

                    state.Stage = MatchStage.Scorers;

                    // Check if the scorers are enabled for the team
                    if (state.ScoringTeam != null && _teamService.IsScorersEnabled(state.ScoringTeam)) // Check if scorers are enabled for the current team
                    {
                        if (state.Score1 > 0) // ToBe checked
                        {
                            await SendScorerSelectionMessageAsync(chatId, messageId, state, cancellationToken);
                        }
                        else if (state.Score2 > 0)
                        {
                            state.ScoringTeam = state.Team2;
                            await SendScorerSelectionMessageAsync(chatId, messageId, state, cancellationToken);
                        }
                        else
                        {
                            state.Stage = MatchStage.Confirmation;
                            await SendConfirmationMessageAsync(chatId, messageId, state, cancellationToken);
                            return;
                        }
                    }
                    else
                    {
                        // Ensure both scores are set to 0 if not set
                        state.Score1 ??= 0;
                        state.Score2 ??= 0;

                        state.Stage = MatchStage.Confirmation;
                        await SendConfirmationMessageAsync(chatId, messageId, state, cancellationToken);
                        return;
                    }
                }
                else
                {
                    // Otherwise, proceed to the scorers phase for other sports
                    state.Stage = MatchStage.Scorers;
                    await SendScorerSelectionMessageAsync(chatId, messageId, state, cancellationToken);
                    return;
                }
            }
            // Handle "delete" button (removes last digit)
            else if (data == $"{scoreType}_del")
            {
                if (currentScore != null && currentScore > 0)
                {
                    currentScore /= 10;  // Remove last digit

                    // Update the state based on the score type
                    if (scoreType == "score1") state.Score1 = currentScore;
                    else if (scoreType == "score2") state.Score2 = currentScore;

                    var keyboardButtons = CreateScoreKeyboard(scoreType);
                    string messageText = $"{_localizationService.GetLocalizedString("scorePromptMessage")}\n" +
                                        $"{_localizationService.GetLocalizedString("team1")} {state.Team1}: {state.Score1}\n" +
                                        $"{_localizationService.GetLocalizedString("team2")} {state.Team2}: {state.Score2}\n\n" +
                                        $"{_localizationService.GetLocalizedString("enterScoreFor")} {state.Team1}:";
                    if (scoreType == "score2") messageText = $"{_localizationService.GetLocalizedString("scorePromptMessage")}\n" +
                                                            $"{_localizationService.GetLocalizedString("team1")} {state.Team1}: {state.Score1}\n" +
                                                            $"{_localizationService.GetLocalizedString("team2")} {state.Team2}: {state.Score2}\n\n" +
                                                            $"{_localizationService.GetLocalizedString("enterScoreFor")} {state.Team2}:";

                    await _botClient.EditMessageText(chatId, messageId, messageText,
                        replyMarkup: new InlineKeyboardMarkup(keyboardButtons), cancellationToken: cancellationToken);
                }
            }
            // Otherwise, handle adding a score
            else
            {
                int score = ParseScoreData(data);
                if (score >= 0)
                {
                    // Update score
                    if (currentScore == 0 || currentScore == null)
                    {
                        if (scoreType == "score1") state.Score1 = score;
                        else if (scoreType == "score2") state.Score2 = score;
                    }
                    else
                    {
                        if (scoreType == "score1") state.Score1 = (state.Score1 * 10) + score;
                        else if (scoreType == "score2") state.Score2 = (state.Score2 * 10) + score;
                    }

                    // Update keyboard
                    var keyboardButtons = CreateScoreKeyboard(scoreType);
                    string messageText = $"{_localizationService.GetLocalizedString("scorePromptMessage")}\n" +
                                        $"{_localizationService.GetLocalizedString("team1")} {state.Team1}: {state.Score1}\n" +
                                        $"{_localizationService.GetLocalizedString("team2")} {state.Team2}: {state.Score2}\n\n" +
                                        $"{_localizationService.GetLocalizedString("enterScoreFor")} {state.Team1}:";
                    if (scoreType == "score2") messageText = $"{_localizationService.GetLocalizedString("scorePromptMessage")}\n" +
                                                            $"{_localizationService.GetLocalizedString("team1")} {state.Team1}: {state.Score1}\n" +
                                                            $"{_localizationService.GetLocalizedString("team2")} {state.Team2}: {state.Score2}\n\n" +
                                                            $"{_localizationService.GetLocalizedString("enterScoreFor")} {state.Team2}:";

                    await _botClient.EditMessageText(chatId, messageId, messageText,
                        replyMarkup: new InlineKeyboardMarkup(keyboardButtons), cancellationToken: cancellationToken);
                }
                else
                {
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, _localizationService.GetLocalizedString("invalidScore"), showAlert: true, cancellationToken: cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error parsing {scoreType} data: {data}");
            await _botClient.AnswerCallbackQuery(callbackQuery.Id, _localizationService.GetLocalizedString("generalError"), showAlert: true, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Handles the selection of a scorer for the match. This method processes the input for adding a scorer or skipping the scorer selection,
    /// and transitions to the next phase of the match, either completing the scorer selection or moving to the next team or match confirmation.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to be edited with the updated scorer information.</param>
    /// <param name="state">The current state of the match, including the teams, scores, and scorers.</param>
    /// <param name="data">The callback data representing the selected scorer or the skip action.</param>
    /// <param name="callbackQuery">The callback query associated with the selected scorer or skip action.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Retrieves the scorer name from the callback data and updates the list of scorers for the current team.
    /// - If the "skip" option is selected, it adds a "Guest" scorer and proceeds to the next team or confirms the match if no more scorers are needed.
    /// - If a scorer is selected, it adds the scorer to the list and checks if the required number of scorers for the team has been reached.
    /// - If the team already has the required number of scorers, it proceeds to the next step, which could be the match confirmation.
    /// - Sends localized messages for adding a scorer, skipping a scorer, or when the scorers are already completed.
    /// </remarks>
    private async Task HandleScorerSelection(long chatId, int messageId, MatchState state, string data, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var scorerName = data.Substring("scorer_".Length);
        List<string> currentTeamScorers = state.ScoringTeam == state.Team1 ? state.ScorersTeam1 : state.ScorersTeam2;

        if (state.Score1 == null || state.Score2 == null)
        {
            throw new InvalidOperationException("Score1 or Score2 cannot be null when updating scorers.");
        }

        int goalsForCurrentTeam = state.ScoringTeam == state.Team1 ? state.Score1.Value : state.Score2.Value;

        if (scorerName == "skip!")
        {
            currentTeamScorers.Add("Guest");

            // Localized message for skipping scorer
            string skipMessage = _localizationService.GetLocalizedString("scorerSkip");
            await _botClient.AnswerCallbackQuery(callbackQuery.Id, skipMessage, cancellationToken: cancellationToken);

            if (currentTeamScorers.Count == goalsForCurrentTeam)
            {
                await ProcessCompletedScorersStepAsync(chatId, messageId, state, cancellationToken);
            }
            else
            {
                await SendScorerSelectionMessageAsync(chatId, messageId, state, cancellationToken);
            }
        }
        else
        {
            if (currentTeamScorers.Count < goalsForCurrentTeam)
            {
                currentTeamScorers.Add(scorerName);

                // Localized message for adding scorer
                string scorerAddedMessage = _localizationService.GetLocalizedString("scorerAdded");
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, $"{scorerAddedMessage} {scorerName}!", cancellationToken: cancellationToken);

                if (currentTeamScorers.Count == goalsForCurrentTeam)
                {
                    await ProcessCompletedScorersStepAsync(chatId, messageId, state, cancellationToken);
                }
                else
                {
                    await SendScorerSelectionMessageAsync(chatId, messageId, state, cancellationToken);
                }
            }
            else
            {
                // Localize the message for when scorers are already completed and insert the team name
                string scorersCompletedMessage = _localizationService.GetLocalizedString("scorersAlreadyCompleted");
                string formattedMessage = string.Format(scorersCompletedMessage, state.ScoringTeam);

                // Send the localized message
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, formattedMessage, showAlert: true, cancellationToken: cancellationToken);

                // Proceed with the completed scorers step
                await ProcessCompletedScorersStepAsync(chatId, messageId, state, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Handles the request for information about a specific sport, including the standings and top scorers, if applicable.
    /// This method checks whether scorers are enabled for the selected sport and sends the relevant information to the user.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to be edited with the sport information.</param>
    /// <param name="state">The current state of the match, including the selected sport.</param>
    /// <param name="data">The callback data containing the selected sport information.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Extracts the sport from the callback data.
    /// - Retrieves and localizes the sport name and emoji.
    /// - Fetches the standings for the requested sport.
    /// - Checks if any teams for the selected sport have scorers enabled.
    /// - If scorers are enabled, it includes the top scorers in the message, otherwise, it informs the user that scorers are disabled.
    /// - Sends a message to the user with the localized sport information and the standings.
    /// </remarks>
    private async Task HandleSportInfoRequest(long chatId, int messageId, MatchState state, string data, CancellationToken cancellationToken)
    {
        // Extract the sport from the callback data
        var sport = data.Substring("info_".Length);

        // Log the request for the specific sport
        _logger.LogInformation($"Requested info for sport: {sport}");

        // Localize the sport name and emoji
        string localizedSportName = _localizationService.GetLocalizedString($"{sport.ToLower()}.name");
        string sportEmoji = _localizationService.GetLocalizedString($"{sport.ToLower()}.emoji");

        // Get the standings for the requested sport
        var standingsMessage = await _matchLogService.GetStandingsAsync(sport);

        // Retrieve teams that belong to the selected sport
        var teamsInSport = _teamService.GetTeamsBySport(sport);

        // Check if any teams have scorers enabled
        bool scorersEnabled = false;
        foreach (var team in teamsInSport)
        {
            if (_teamService.IsScorersEnabled(team.Name)) // Use team name for checking if scorers are enabled
            {
                scorersEnabled = true;
                break; // No need to continue checking if we already found a team with enabled scorers
            }
        }

        string messageText = $"{sportEmoji} {localizedSportName}\n\n📈 {_localizationService.GetLocalizedString("standings")}\n{standingsMessage}";

        string topScorersTitle = _localizationService.GetLocalizedString("topScorers");

        if (scorersEnabled)
        {
            // If scorers are enabled, get the top scorers and include them in the message
            string scorersMessage = await _matchLogService.GetTopScorersAsync(sport);
            messageText += $"\n\n🎯 {topScorersTitle}\n{scorersMessage}";
        }
        else
        {
            // If scorers are not enabled, add a message indicating that scorers are not available
            string scorersNotDisplayed = _localizationService.GetLocalizedString("scorersDisabled");
            messageText += $"\n\n🎯 {topScorersTitle}\n{scorersNotDisplayed}";
        }

        // Send the message to the user
        await _botClient.EditMessageText(chatId, messageId, messageText, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Handles the confirmation of a match, ensuring the match data is valid and saving it to the log. 
    /// It also sends a confirmation message with the match details, including team names, scores, standings, and scorers (if applicable).
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the confirmation message will be sent.</param>
    /// <param name="messageId">The ID of the message to edit with the confirmation details.</param>
    /// <param name="state">The current state of the match, including the teams, scores, and scorers.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Validates that the match data (teams and scores) is complete before proceeding.
    /// - Localizes the sport name, emoji, team names, and scores.
    /// - Retrieves and displays the updated standings for the sport.
    /// - Adds a list of scorers to the confirmation message if available.
    /// - Sends a final confirmation message to the user.
    /// - Handles errors by providing a localized error message if any issues arise during confirmation.
    /// </remarks>
    private async Task HandleMatchConfirmation(long chatId, int messageId, MatchState state, CancellationToken cancellationToken)
    {
        _logger.LogDebug($"Sending confirmation message to {chatId}.");

        // Localize the sport name and emoji
        string sportDisplay = state.Sport ?? _teamService.GetTeamSport(state.Team1 ?? "Unknown Sport");
        string sportName = _localizationService.GetLocalizedString($"{sportDisplay.ToLower()}.name");
        string emoji = _localizationService.GetLocalizedString($"{sportDisplay.ToLower()}.emoji");

        // Validate the teams and scores
        if (string.IsNullOrEmpty(state.Team1) || string.IsNullOrEmpty(state.Team2) || !state.Score1.HasValue || !state.Score2.HasValue)
        {
            _logger.LogError($"Invalid match data for confirmation. Teams or scores are missing.");

            // Localize the error message for invalid match data
            string invalidMatchDataMessage = _localizationService.GetLocalizedString("invalidMatchData");
            await _botClient.EditMessageText(chatId, messageId, invalidMatchDataMessage, cancellationToken: cancellationToken);
            _stateService.ClearState(chatId);
            return;
        }

        try
        {
            // Save the match to the log
            await _matchLogService.SaveMatchToLogAsync(chatId, sportDisplay, state.Team1, state.Team2, state.Score1.Value, state.Score2.Value, state.ScorersTeam1, state.ScorersTeam2);

            // Retrieve the updated standings
            var standingsLines = await _matchLogService.GetStandingsAsync(sportDisplay);

            // Prepare message details
            string team1Display = state.Team1 ?? _localizationService.GetLocalizedString("unknownTeam");
            string team2Display = state.Team2 ?? _localizationService.GetLocalizedString("unknownTeam");

            // Localize the "Match Saved" message
            string matchSavedMessage = _localizationService.GetLocalizedString("matchSaved");

            // Build the confirmation message including standings and scores
            string finalMessage = $"{matchSavedMessage}\n\n" +
                                $"{emoji} {sportName}\n" +
                                $"🆚 {team1Display} {state.Score1.Value} - {state.Score2.Value} {team2Display}\n\n" +
                                $"📈 {_localizationService.GetLocalizedString("updatedStandings")}\n" +
                                string.Join('\n', standingsLines) + "\n\n";

            // Add the scorers list to the message
            if (state.ScorersTeam1.Any() || state.ScorersTeam2.Any())
            {
                string team1Scorers = state.ScorersTeam1.Any() ? $"{team1Display}: {string.Join(", ", state.ScorersTeam1)}" : string.Empty;
                string team2Scorers = state.ScorersTeam2.Any() ? $"{team2Display}: {string.Join(", ", state.ScorersTeam2)}" : string.Empty;

                // Append scorers to the final message
                finalMessage += $"🎯 {_localizationService.GetLocalizedString("matchScorers")}\n";
                if (team1Scorers != null)
                    finalMessage += team1Scorers + "\n";
                if (team2Scorers != null)
                    finalMessage += team2Scorers;
            }

            // Send the final confirmation message
            await _botClient.EditMessageText(chatId, messageId, finalMessage, cancellationToken: cancellationToken);
            _stateService.ClearState(chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while confirming the match.");

            // Localize the error message for any exception during the match confirmation
            string generalErrorMessage = _localizationService.GetLocalizedString("generalError");
            await _botClient.EditMessageText(chatId, messageId, generalErrorMessage, cancellationToken: cancellationToken);
            _stateService.ClearState(chatId);
        }
    }

    /// <summary>
    /// Handles the cancellation of a match registration by the user. It sends a localized cancellation message to the user and clears the match state.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to edit with the cancellation confirmation.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Logs the cancellation of the match registration.
    /// - Retrieves and sends a localized message informing the user that the match registration has been cancelled.
    /// - Clears the match state using the state service to reset the match process for the user.
    /// </remarks>
    private async Task HandleMatchCancellation(long chatId, int messageId, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Match registration cancelled by user in chat {chatId}.");

        // Localize the cancellation message
        string cancelMessage = _localizationService.GetLocalizedString("matchCancelled");

        await _botClient.EditMessageText(chatId, messageId, cancelMessage, cancellationToken: cancellationToken);
        _stateService.ClearState(chatId);
    }
    
    /// <summary>
    /// Handles unexpected input from the user during the match registration process. It retrieves and formats a localized message
    /// indicating that the input is not expected for the current stage of the match.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to edit with the error details.</param>
    /// <param name="stage">The current stage of the match process that the user is in.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Retrieves the localized "unexpectedInput" message, formats it with the current stage of the match, and sends it to the user.
    /// - Handles cases where the user provides input that is not valid for the current stage in the match registration process.
    /// </remarks>
    private async Task HandleUnexpectedInput(long chatId, int messageId, MatchStage stage, CancellationToken cancellationToken)
    {
        // Localize the "unexpectedInput" message and format it with the current stage
        string unexpectedMessage = _localizationService.GetLocalizedString("unexpectedInput");
        string formattedMessage = string.Format(unexpectedMessage, stage.ToString());

        // Send the formatted message to the user
        await _botClient.EditMessageText(chatId, messageId, formattedMessage, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sends a localized error message to the user in the specified chat. This method retrieves the localized version of the provided error message and updates the existing message.
    /// </summary>
    /// <param name="chatId">The unique identifier of the chat where the message will be sent.</param>
    /// <param name="messageId">The ID of the message to edit with the error details.</param>
    /// <param name="message">The key for the localized error message that will be sent to the user.</param>
    /// <param name="cancellationToken">The cancellation token used to manage cancellation of the operation.</param>
    /// <remarks>
    /// This method:
    /// - Retrieves the localized error message using the provided key from the localization service.
    /// - Updates the message in the chat with the localized error message.
    /// </remarks>
    private async Task HandleErrorMessage(long chatId, int messageId, string message, CancellationToken cancellationToken)
    {
        // Localize the error message
        string localizedErrorMessage = _localizationService.GetLocalizedString(message);

        await _botClient.EditMessageText(chatId, messageId, localizedErrorMessage, cancellationToken: cancellationToken);
    }
    
    /// <summary>
    /// Handles errors that occur during the polling process of the Telegram Bot.
    /// Logs the error message to the console in red text for visibility.
    /// </summary>
    /// <param name="botClient">The instance of the bot client used to handle updates.</param>
    /// <param name="exception">The exception thrown during polling.</param>
    /// <param name="cancellationToken">The cancellation token to allow stopping the task.</param>
    /// <returns>A completed task indicating the handling of the polling error.</returns>
    public static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Polling Error: {exception}");
        Console.ResetColor();

        return Task.CompletedTask;
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Creates a score selection keyboard with number buttons (1-9), a 'Done' button, a '0' button for score input, and a 'Delete' button.
    /// The keyboard is divided into rows of 3 buttons, allowing the user to input scores for a match.
    /// </summary>
    /// <param name="stage">The stage of the match (e.g., "score1", "score2") which determines the callback data for the buttons.</param>
    /// <returns>A list of lists of <see cref="InlineKeyboardButton"/> representing the score selection keyboard layout.</returns>
    /// <remarks>
    /// This method dynamically generates a keyboard based on the provided stage. It:
    /// - Adds buttons for scores 1-9 in rows of 3 buttons each.
    /// - Adds additional buttons for 'Done' (to finalize the score), '0' (for zero score), and 'Delete' (to remove the last digit).
    /// </remarks>
    private static List<List<InlineKeyboardButton>> CreateScoreKeyboard(string stage)
    {
        var keyboardButtons = new List<List<InlineKeyboardButton>>();

        // Create number buttons for score selection (1-9)
        var row = new List<InlineKeyboardButton>();

        for (int i = 1; i <= 9; i++)
        {
            row.Add(InlineKeyboardButton.WithCallbackData(i.ToString(), $"{stage}_{i}"));

            // After every 3 buttons, start a new row
            if (i % 3 == 0)
            {
                keyboardButtons.Add([.. row]);
                row.Clear();  // Clear the row to start a new one for the next set of buttons
            }
        }

        // Add 'Done' button to finish the score input, the '0' button for score input and the 'Delete' button to remove the last entered digit
        keyboardButtons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("🗸", $"{stage}_done"),
            InlineKeyboardButton.WithCallbackData("0", $"{stage}_0"),
            InlineKeyboardButton.WithCallbackData("⌫", $"{stage}_del")
        });

        return keyboardButtons;
    }

    /// <summary>
    /// Parses the score value from the callback data (e.g., "score1_1" → 1).
    /// </summary>
    /// <param name="data">The callback data string representing the score (e.g., "score1_1").</param>
    /// <returns>
    /// The parsed score as an integer. If the data format is invalid, returns -1 to indicate an error.
    /// </returns>
    /// <remarks>
    /// This method:
    /// - Splits the input string based on the underscore character (`_`).
    /// - Attempts to parse the second part of the string (e.g., "1" in "score1_1") as an integer.
    /// - If parsing succeeds, the method returns the score value.
    /// - If the data format is incorrect or the parsing fails, the method returns `-1` to indicate an error.
    /// </remarks>
    private static int ParseScoreData(string data)
    {
        // Extract the score value from the data (e.g., "score1_1" => "1")
        var parts = data.Split('_');

        // Ensure the data has the expected format (e.g., "score1_1")
        if (parts.Length == 2 && int.TryParse(parts[1], out int score))
        {
            return score;  // Return the parsed score
        }

        // If the score is invalid, return -1 to indicate an error
        return -1;
    }


    #endregion
}
