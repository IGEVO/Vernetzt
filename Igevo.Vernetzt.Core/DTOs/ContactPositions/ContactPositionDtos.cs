using System.ComponentModel.DataAnnotations;

namespace Igevo.Vernetzt.Core.DTOs.ContactPositions;

public class ContactPositionDto
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Beschreibung darf maximal 1000 Zeichen lang sein")]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateContactPositionDto
{
    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Beschreibung darf maximal 1000 Zeichen lang sein")]
    public string? Description { get; set; }
}

public class UpdateContactPositionDto
{
    [Required(ErrorMessage = "Name ist erforderlich")]
    [StringLength(200, ErrorMessage = "Name darf maximal 200 Zeichen lang sein")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Beschreibung darf maximal 1000 Zeichen lang sein")]
    public string? Description { get; set; }
}
