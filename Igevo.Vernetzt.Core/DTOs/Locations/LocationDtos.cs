using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.DTOs.Locations;

/// <summary>
/// Centrally maintained location ("Standort") with usage information.
/// </summary>
public class LocationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }

    /// <summary>Number of clubs, departments and groups using this location.</summary>
    public int UsageCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Short reference to a location, used on clubs, departments and groups.
/// </summary>
public class LocationSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Create or update a location.
/// </summary>
public class SaveLocationDto
{
    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Beschreibung darf maximal 1000 Zeichen lang sein")]
    public string? Description { get; set; }

    [StringLength(200, ErrorMessage = "Straße darf maximal 200 Zeichen lang sein")]
    public string? Street { get; set; }

    [StringLength(20, ErrorMessage = "PLZ darf maximal 20 Zeichen lang sein")]
    public string? PostalCode { get; set; }

    [StringLength(100, ErrorMessage = "Ort darf maximal 100 Zeichen lang sein")]
    public string? City { get; set; }
}
