namespace Igevo.Vernetzt.Core.Interfaces.Services;

/// <summary>
/// Service for managing application settings stored in the database.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// Gets a setting value by key. Returns null if not found.
    /// </summary>
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a setting value. Creates or updates the key.
    /// </summary>
    Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple settings by keys.
    /// </summary>
    Task<Dictionary<string, string>> GetValuesAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets multiple settings at once.
    /// </summary>
    Task SetValuesAsync(Dictionary<string, string> settings, CancellationToken cancellationToken = default);
}
