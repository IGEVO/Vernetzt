namespace Igevo.Vernetzt.Server.Configuration;

/// <summary>
/// Runtime settings for WebAuthn/passkey operations.
/// </summary>
public sealed class PasskeyOptions
{
    public const string SectionName = "Auth:Passkeys";

    /// <summary>
    /// Relying party id (domain). If empty, request host will be used.
    /// </summary>
    public string? RelyingPartyId { get; set; }

    /// <summary>
    /// Relying party display name.
    /// </summary>
    public string RelyingPartyName { get; set; } = "Vernetzt";

    /// <summary>
    /// Allowed origins for WebAuthn ceremonies.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>
    /// Challenge timeout used for attestation/assertion options.
    /// </summary>
    public int ChallengeTimeoutSeconds { get; set; } = 300;
}
