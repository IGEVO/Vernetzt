using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Database-backed application settings service using the AppSetting entity.
/// </summary>
public class AppSettingsService : IAppSettingsService
{
    private readonly VernetztDbContext _dbContext;
    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(VernetztDbContext dbContext, ILogger<AppSettingsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _dbContext.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        return setting?.Value;
    }

    public async Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var setting = await _dbContext.AppSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting == null)
        {
            setting = new AppSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("App setting '{Key}' updated", key);
    }

    public async Task<Dictionary<string, string>> GetValuesAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        var settings = await _dbContext.AppSettings
            .AsNoTracking()
            .Where(s => keyList.Contains(s.Key))
            .ToListAsync(cancellationToken);

        return settings.ToDictionary(s => s.Key, s => s.Value);
    }

    public async Task SetValuesAsync(Dictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        var keys = settings.Keys.ToList();
        var existing = await _dbContext.AppSettings
            .Where(s => keys.Contains(s.Key))
            .ToListAsync(cancellationToken);

        foreach (var (key, value) in settings)
        {
            var setting = existing.FirstOrDefault(s => s.Key == key);
            if (setting == null)
            {
                setting = new AppSetting
                {
                    Id = Guid.NewGuid(),
                    Key = key,
                    Value = value,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.AppSettings.Add(setting);
            }
            else
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Updated {Count} app settings", settings.Count);
    }
}
