using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.DTOs.EmailRelay;

/// <summary>
/// Request for creating a new email relay token.
/// </summary>
public class CreateEmailRelayTokenRequest
{
    /// <summary>
    /// List of recipient contact assignment IDs (preferred).
    /// </summary>
    public List<Guid> RecipientAssignmentIds { get; set; } = new();

    /// <summary>
    /// List of recipient user IDs (compatibility fallback).
    /// </summary>
    public List<Guid> RecipientUserIds { get; set; } = new();

    /// <summary>
    /// Optional subject hint for display in the UI (e.g., "Einladung Jahreshauptversammlung").
    /// </summary>
    [StringLength(200, ErrorMessage = "Betreffhinweis darf maximal 200 Zeichen lang sein")]
    public string? SubjectHint { get; set; }
}
