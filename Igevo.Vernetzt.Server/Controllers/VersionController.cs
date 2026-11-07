using Igevo.Vernetzt.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Controller for application version and build information.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class VersionController : ControllerBase
{
    /// <summary>
    /// Gets application version and build information.
    /// </summary>
    /// <returns>Version information including name, version, git hash, and environment.</returns>
    /// <response code="200">Returns the version information.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(VersionResponse), StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var buildInfo = AppBuildInfo.FromAssembly(typeof(Program).Assembly, "Igevo.Vernetzt");

        return Ok(new VersionResponse
        {
            Name = buildInfo.Name,
            Version = buildInfo.Version,
            GitHash = buildInfo.GitHash,
            DisplayVersion = buildInfo.DisplayVersion,
            UserDisplayVersion = buildInfo.UserDisplayVersion,
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
        });
    }
}

/// <summary>
/// Response model for version information.
/// </summary>
public class VersionResponse
{
    /// <summary>
    /// Application name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Semantic version number.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Git commit hash.
    /// </summary>
    public string GitHash { get; init; } = string.Empty;

    /// <summary>
    /// Display version (version + git hash).
    /// </summary>
    public string DisplayVersion { get; init; } = string.Empty;

    /// <summary>
    /// User-friendly display version (version + git hash in parentheses).
    /// </summary>
    public string UserDisplayVersion { get; init; } = string.Empty;

    /// <summary>
    /// Current runtime environment.
    /// </summary>
    public string Environment { get; init; } = string.Empty;
}
