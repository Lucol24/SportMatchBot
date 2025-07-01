using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SportMatchBot.Services;

/// <summary>
/// Service for loading and retrieving localized strings based on language files.
/// </summary>
public class LocalizationService
{
    private readonly string _localizationPath;
    private readonly ILogger<LocalizationService> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private JsonDocument? _localizedMessages;
    private Dictionary<string, string> _localizationCache;

    /// <summary>
    /// Initializes the LocalizationService by fetching the necessary paths and default language from the provided configuration.
    /// Sets up the localization cache and logs the initialization process.
    /// </summary>
    /// <param name="configuration">The application configuration, which provides paths and default language settings.</param>
    /// <param name="logger">The logger used to log events and errors related to the localization service.</param>
    /// <exception cref="ArgumentNullException">Thrown if either the configuration or logger is null.</exception>
    public LocalizationService(IConfiguration configuration, ILogger<LocalizationService> logger)
    {
        // Fetch paths and default language from configuration
        _localizationPath = configuration["Paths:DataDirectory"] ?? throw new ArgumentNullException(nameof(_localizationPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localizationCache = new Dictionary<string, string>(); // Initialize the cache

        var languageCode = configuration["Language"] ?? throw new ArgumentNullException("Language");

        LoadLocalization(languageCode);

        // Log the initialization process
        _logger.LogInformation($"LocalizationService initialized. Using language folder: {_localizationPath}");
    }

    #region Public Methods

    /// <summary>
    /// Loads the localization data from a JSON file based on the specified language code. 
    /// Initializes the `_localizedMessages` with the content of the file.
    /// </summary>
    /// <param name="languageCode">The language code (e.g., "en", "it") to load the corresponding localization file.</param>
    /// <exception cref="FileNotFoundException">Thrown if the specified localization file cannot be found.</exception>
    /// <exception cref="Exception">Thrown if any error occurs while reading or parsing the localization file.</exception>
    private void LoadLocalization(string languageCode)
    {
        var filePath = Path.Combine(_localizationPath, "LocalizationFiles", $"{languageCode}.json");

        if (!File.Exists(filePath))
        {
            _logger.LogError($"Localization file for {languageCode} not found at {filePath}.");
            throw new FileNotFoundException($"Localization file for {languageCode} not found.");
        }

        try
        {
            _fileLock.Wait();

            var jsonContent = File.ReadAllText(filePath);
            _localizedMessages = JsonDocument.Parse(jsonContent);
            _logger.LogInformation($"Successfully loaded localization file: {filePath}");

            _localizationCache.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error while loading localization file: {filePath}");
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Retrieves the localized string corresponding to the provided key, caching the result on first use.
    /// If the key represents a nested value (e.g., "soccer.emoji"), it will traverse the object structure.
    /// </summary>
    /// <param name="key">The localization key to retrieve the string for.</param>
    /// <returns>The localized string corresponding to the provided key, or the key itself if not found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if localization data has not been loaded.</exception>
    public string GetLocalizedString(string key)
    {
        if (_localizationCache.TryGetValue(key, out var cachedValue))
        {
            return cachedValue;
        }

        if (_localizedMessages == null)
        {
            throw new InvalidOperationException("Localization data is not loaded.");
        }

        var root = _localizedMessages.RootElement;

        // Check if the key has a '.' to determine if it's a nested key
        if (key.Contains('.'))
        {
            var keys = key.Split('.');  // Split key to handle nested properties

            if (keys.Length == 2 && root.TryGetProperty(keys[0], out var sport) && sport.TryGetProperty(keys[1], out var value))
            {
                var result = value.GetString() ?? key;
                _localizationCache[key] = result;  // Cache the value
                return result;
            }
        }
        else
        {
            // If it's a flat key (not nested), try to get the value directly
            if (root.TryGetProperty(key, out var value))
            {
                var result = value.GetString() ?? key;
                _localizationCache[key] = result;  // Cache the value
                return result;
            }
        }
        _logger.LogWarning($"Localization key '{key}' not found. Returning the key itself.");
        return key;
    }
    
    #endregion
}
