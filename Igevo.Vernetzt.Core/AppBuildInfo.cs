using System.Reflection;

namespace Igevo.Vernetzt.Core;

/// <summary>
/// Provides build information about the application.
/// </summary>
public class AppBuildInfo
{
    /// <summary>
    /// Application name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Semantic version number (e.g., "1.0.0").
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Git commit hash (short or full).
    /// </summary>
    public string GitHash { get; init; } = string.Empty;

    /// <summary>
    /// Display version combining version and git hash (e.g., "1.0.0+abc123").
    /// Used for technical/semver-compatible representation.
    /// </summary>
    public string DisplayVersion => string.IsNullOrEmpty(GitHash) || GitHash == "local"
        ? Version
        : $"{Version}+{GitHash[..Math.Min(7, GitHash.Length)]}";

    /// <summary>
    /// User-friendly display version with git hash in parentheses (e.g., "1.0.0 (abc1234)").
    /// Intended for display in the WebApp UI.
    /// </summary>
    public string UserDisplayVersion => string.IsNullOrEmpty(GitHash) || GitHash == "local"
        ? Version
        : $"{Version} ({GitHash[..Math.Min(7, GitHash.Length)]})";

    /// <summary>
    /// Creates AppBuildInfo from assembly metadata.
    /// </summary>
    /// <param name="assembly">The assembly to extract information from.</param>
    /// <param name="defaultName">Default name if assembly name is not available.</param>
    /// <returns>AppBuildInfo instance with extracted metadata.</returns>
    public static AppBuildInfo FromAssembly(Assembly assembly, string defaultName = "Unknown")
    {
        var version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;

        // Parse informational version (format: "1.0.0+githash" or "1.0.0")
        var parts = informationalVersion.Split('+');
        var semVer = parts[0];
        var gitHash = parts.Length > 1 ? parts[1] : Environment.GetEnvironmentVariable("VERNETZT_GIT_HASH") ?? "local";

        return new AppBuildInfo
        {
            Name = assembly.GetName().Name ?? defaultName,
            Version = semVer,
            GitHash = gitHash
        };
    }
}
