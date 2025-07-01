using Microsoft.Extensions.Logging; 
using System; 
using System.IO; 
using System.Threading; 
using System.Threading.Tasks; 

namespace SportMatchBot.Services;

/// <summary>
/// This service is responsible for logging messages to a specified log file.
/// </summary>
public class LoggerService
{
    private readonly string _logFilePath;
    private readonly ILogger<LoggerService> _internalLogger; 
    private readonly SemaphoreSlim _fileLock = new(1, 1); 
    
    /// <summary>
    /// Initializes a new instance of the <see cref="LoggerService"/> class, setting up the log file and directory.
    /// It ensures that the log directory exists and logs any issues that occur during the setup process.
    /// </summary>
    /// <param name="logFilePath">The file path where the log file should be saved.</param>
    /// <param name="internalLogger">An instance of <see cref="ILogger"/> for logging internal errors and information.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logFilePath"/> or <paramref name="internalLogger"/> is null.</exception>
    public LoggerService(string logFilePath, ILogger<LoggerService> internalLogger)
    {
        _logFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath));
        _internalLogger = internalLogger ?? throw new ArgumentNullException(nameof(internalLogger));

        try
        {
            // Ensure directory exists
            var logDirectory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
                _internalLogger.LogInformation($"Created log directory: {logDirectory}");
            }
            _internalLogger.LogInformation($"LoggerService initialized. Log file: {_logFilePath}");
        }
        catch (Exception ex)
        {
            _internalLogger.LogError(ex, $"Failed to initialize LoggerService or create directory for {_logFilePath}");
        }
    }

    #region Public Methods

    /// <summary>
    /// Asynchronously logs a message from a user, including their username, name, user ID, and message text, 
    /// while ensuring thread-safety for file writing.
    /// </summary>
    /// <param name="username">The username of the user sending the message.</param>
    /// <param name="firstName">The first name of the user.</param>
    /// <param name="lastName">The last name of the user.</param>
    /// <param name="userId">The unique ID of the user.</param>
    /// <param name="date">The timestamp of when the message was sent.</param>
    /// <param name="messageText">The content of the message sent by the user.</param>
    /// <exception cref="IOException">Thrown when there's an error while writing to the log file.</exception>
    /// <exception cref="Exception">Catches any other unexpected errors during the logging process.</exception>
    public async Task LogMessageAsync(string username, string firstName, string lastName, string userId, string date, string messageText)
    {
        var logEntry = $"{date} [User: {username} ({firstName} {lastName}, ID:{userId})] Message: {messageText}";

        await _fileLock.WaitAsync();
        try
        {
            await File.AppendAllLinesAsync(_logFilePath, new[] { logEntry });
            _internalLogger.LogTrace($"Logged message to file: {logEntry}");
        }
        catch (IOException ex)
        {
            _internalLogger.LogError(ex, $"Failed to write log entry to {_logFilePath}");
        }
        catch (Exception ex)
        {
            _internalLogger.LogError(ex, $"An unexpected error occurred while logging message to {_logFilePath}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    #endregion
}
