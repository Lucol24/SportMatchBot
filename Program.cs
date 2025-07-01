using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

using SportMatchBot.Services;
using SportMatchBot.Handlers;

class Program
{
    static async Task Main(string[] args)
    {
        // Build the .NET Host
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                // Add configuration sources (e.g., appsettings.json, environment variables)
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
                // Optional: Add user secrets for sensitive info (bot token) during development
                // config.AddUserSecrets<Program>();
            })
            .ConfigureLogging(loggingBuilder =>
            {
                // Clear default providers if desired, add the console logger
                loggingBuilder.ClearProviders();
                loggingBuilder.AddConsole();
                // Add more providers if needed (e.g., file logger, Debug, Azure App Services)

                // Configure log levels
                loggingBuilder.SetMinimumLevel(LogLevel.Information); // Default minimum log level for the application
                loggingBuilder.AddFilter("Microsoft", LogLevel.Warning); // Filter out verbose logs from Microsoft libraries
                // loggingBuilder.AddFilter("SportMatchBot", LogLevel.Debug); // Enable more detailed logs for your namespaces
            })
            .ConfigureServices((hostContext, services) =>
            {
                var configuration = hostContext.Configuration;

                // Get file paths from configuration or fallback to hardcoded paths
                var projectDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
                // Ensure the Data directory exists
                if (!Directory.Exists(projectDirectory))
                {
                    Directory.CreateDirectory(projectDirectory);
                }

                var logPath = configuration["Paths:LogFile"] ?? Path.Combine(projectDirectory, "log.txt");
                var teamsPath = configuration["Paths:TeamsFile"] ?? Path.Combine(projectDirectory, "teams.json");
                var matchesJsonPath = configuration["Paths:MatchesFile"] ?? Path.Combine(projectDirectory, "matches.json");

                // Get the bot token from configuration
                var botToken = configuration["BotToken"];
                if (string.IsNullOrEmpty(botToken))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("CRITICAL ERROR: BotToken not found in configuration. Set it in appsettings.json or environment variables.");
                    Console.ResetColor();
                    // Throw an exception to block startup if the token is missing
                    throw new InvalidOperationException("BotToken configuration is missing.");
                }

                // Register services with the DI container

                // TeamService needs the path to the teams.json file and the logger
                services.AddSingleton<TeamService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<TeamService>>();
                    return new TeamService(teamsPath, logger);
                });

                // Register LocalizationService
                services.AddSingleton<LocalizationService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<LocalizationService>>();
                    return new LocalizationService(configuration, logger); 
                });

                // Register MatchLogService 
                services.AddSingleton<MatchLogService>(sp =>
                {
                    var teamService = sp.GetRequiredService<TeamService>();
                    var logger = sp.GetRequiredService<ILogger<MatchLogService>>();
                    var botClient = sp.GetRequiredService<ITelegramBotClient>();
                    var localizationService = sp.GetRequiredService<LocalizationService>();
                    return new MatchLogService(Path.Combine(projectDirectory, "matches.json"), teamService, logger, botClient, localizationService);
                });

                // Register StateService with logger
                services.AddSingleton<StateService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<StateService>>();
                    return new StateService(logger);
                });

                // Register LoggerService
                services.AddSingleton<LoggerService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<LoggerService>>(); // Logger for LoggerService itself
                    return new LoggerService(logPath, logger);
                });

                // Register TelegramBotClient as a singleton
                services.AddSingleton<ITelegramBotClient>(sp =>
                {
                    var client = new TelegramBotClient(botToken);
                    // Optional: configure the client here if needed (e.g., timeout)
                    return client;
                });

                // Register the BotUpdateHandler (used for handling updates from the bot)
                services.AddScoped<BotUpdateHandler>();

                // Register the HostedService to manage the bot polling
                services.AddHostedService<PollingWorker>();
            })
            .Build(); // Build the host

        // Get the logger from the host to log startup messages
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        // Retrieve bot client and send an initial start message (optional)
        try
        {
            var botClient = host.Services.GetRequiredService<ITelegramBotClient>();
            var me = await botClient.GetMe();
            logger.LogInformation($"@{me.Username} is running...");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get bot info or bot token might be invalid.");
            // Continue anyway, polling errors will be handled elsewhere
        }

        // Add suggested bot commands
        var botClientService = host.Services.GetRequiredService<ITelegramBotClient>();
        await botClientService.SetMyCommands(new Telegram.Bot.Types.BotCommand[] 
        {
            new Telegram.Bot.Types.BotCommand { Command = "/start", Description = "Start using the bot" },
            new Telegram.Bot.Types.BotCommand { Command = "/help", Description = "Show list of commands" },
            new Telegram.Bot.Types.BotCommand { Command = "/info", Description = "View statistics" },
            new Telegram.Bot.Types.BotCommand { Command = "/match", Description = "Record a new match" },
            new Telegram.Bot.Types.BotCommand { Command = "/undo", Description = "Undo the last recorded match" }
        });

        // Run the host. This will block the thread until the host is stopped (e.g., Ctrl+C)
        await host.RunAsync();

        logger.LogInformation("Host terminated.");
    }

    // --- IHostedService for executing the bot polling in the background ---
    public class PollingWorker : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IServiceProvider _serviceProvider; // To create scopes for handling updates
        private readonly ILogger<PollingWorker> _logger;

        public PollingWorker(ITelegramBotClient botClient, IServiceProvider serviceProvider, ILogger<PollingWorker> logger)
        {
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ExecuteAsync is the method called when the host starts this background service
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PollingWorker starting bot client...");

            // Configure receiver options (e.g., allowed update types)
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // Leave empty to receive all update types, or specify specific ones
            };

            // Start receiving updates from the bot
            _botClient.StartReceiving(
                // updateHandler: Delegate that handles each update
                updateHandler: HandleUpdateAsyncScoped,
                // pollingErrorHandler: Handles errors in the polling loop itself
                errorHandler: BotUpdateHandler.HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken // Pass the cancellation token to stop cleanly
            );

            _logger.LogInformation("Telegram Bot Client polling started. Press Ctrl+C to stop.");

            // Keep the service running until the stop token is triggered
            await Task.Delay(Timeout.Infinite, stoppingToken);

            _logger.LogInformation("PollingWorker stopping bot client...");
        }

        // This delegate is called by the Telegram.Bot library for each incoming update.
        // It creates a scope and resolves the BotUpdateHandler to process the update.
        private async Task HandleUpdateAsyncScoped(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope(); // Create a new scope for each update
            try
            {
                var handler = scope.ServiceProvider.GetRequiredService<BotUpdateHandler>();
                await handler.HandleUpdateAsync(update, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"Update {update.Id} processing cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An unhandled exception occurred while processing update {update.Id}");
                // HandlePollingErrorAsync (above) catches errors in the polling loop
            }
        }
    }
}
