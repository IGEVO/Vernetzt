using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.DTOs.Clubs;
using Igevo.Vernetzt.Core.DTOs.Departments;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using FluentValidation;

namespace Igevo.Vernetzt.Infrastructure.Validators;

/// <summary>
/// Validator for CreateDepartmentDto with business rule validation.
/// </summary>
public class CreateDepartmentDtoValidator : AbstractValidator<CreateDepartmentDto>
{
    private readonly IClubRepository _clubRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public CreateDepartmentDtoValidator(
        IClubRepository clubRepository,
        IDepartmentRepository departmentRepository)
    {
        _clubRepository = clubRepository;
        _departmentRepository = departmentRepository;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name ist erforderlich")
            .MaximumLength(200).WithMessage("Name darf maximal 200 Zeichen lang sein");

        RuleFor(x => x.ShortName)
            .MaximumLength(Club.MaxShortNameLength).WithMessage("Abkürzung darf maximal 20 Zeichen lang sein");

        RuleFor(x => x.ClubId)
            .NotEmpty().WithMessage("Verein ist erforderlich")
            .MustAsync(BeValidClubIdAsync).WithMessage("Der angegebene Verein existiert nicht");

        RuleFor(x => x.Description)
            .MaximumLength(4000).WithMessage("Beschreibung darf maximal 4000 Zeichen lang sein");

        // Groups hang below a top-level department of the same club (one level only).
        RuleFor(x => x)
            .MustAsync(HaveValidParentDepartmentAsync)
            .When(x => x.ParentDepartmentId.HasValue)
            .WithMessage("Eine Gruppe kann nur unterhalb einer Abteilung desselben Vereins angelegt werden");

        // Names are unique among siblings: departments within the club, groups within their department.
        RuleFor(x => x)
            .MustAsync(BeUniqueNameAmongSiblingsAsync)
            .WithMessage(x => x.ParentDepartmentId.HasValue
                ? "Eine Gruppe mit diesem Namen existiert bereits in dieser Abteilung"
                : "Eine Abteilung mit diesem Namen existiert bereits in diesem Verein");

        RuleFor(x => x.Street)
            .MaximumLength(200).WithMessage("Straße darf maximal 200 Zeichen lang sein");

        RuleFor(x => x.PostalCode)
            .MaximumLength(10).WithMessage("Postleitzahl darf maximal 10 Zeichen lang sein")
            .Matches(@"^\d{5}$")
            .When(x => !string.IsNullOrEmpty(x.PostalCode))
            .WithMessage("Postleitzahl muss aus 5 Ziffern bestehen");

        RuleFor(x => x.City)
            .MaximumLength(100).WithMessage("Stadt darf maximal 100 Zeichen lang sein");

        RuleFor(x => x.Website)
            .MaximumLength(500).WithMessage("Website-URL darf maximal 500 Zeichen lang sein")
            .Must(BeValidUrlOrEmpty).WithMessage("Ungültige URL")
            .When(x => !string.IsNullOrEmpty(x.Website));

        RuleFor(x => x.Email)
            .MaximumLength(200).WithMessage("E-Mail darf maximal 200 Zeichen lang sein")
            .EmailAddress().WithMessage("Ungültige E-Mail-Adresse")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.SocialMediaLinks).ValidateSocialMediaLinks();
    }

    private async Task<bool> BeValidClubIdAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var club = await _clubRepository.GetByIdAsync(clubId, cancellationToken);
        return club != null;
    }

    private async Task<bool> HaveValidParentDepartmentAsync(CreateDepartmentDto dto, CancellationToken cancellationToken)
    {
        var parent = await _departmentRepository.GetByIdAsync(dto.ParentDepartmentId!.Value, cancellationToken);
        return parent != null && parent.ClubId == dto.ClubId && !parent.IsGroup;
    }

    private async Task<bool> BeUniqueNameAmongSiblingsAsync(CreateDepartmentDto dto, CancellationToken cancellationToken)
    {
        var existingDepartments = await _departmentRepository.GetByClubIdAsync(dto.ClubId, cancellationToken);
        return !existingDepartments.Any(d =>
            d.ParentDepartmentId == dto.ParentDepartmentId &&
            d.Name.Equals(dto.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool BeValidUrlOrEmpty(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
               && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
}

/// <summary>
/// Validator for UpdateDepartmentDto with business rule validation.
/// </summary>
public class UpdateDepartmentDtoValidator : AbstractValidator<UpdateDepartmentDto>
{
    public UpdateDepartmentDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name ist erforderlich")
            .MaximumLength(200).WithMessage("Name darf maximal 200 Zeichen lang sein");

        RuleFor(x => x.ShortName)
            .MaximumLength(Club.MaxShortNameLength).WithMessage("Abkürzung darf maximal 20 Zeichen lang sein");

        RuleFor(x => x.Description)
            .MaximumLength(4000).WithMessage("Beschreibung darf maximal 4000 Zeichen lang sein");

        RuleFor(x => x.Street)
            .MaximumLength(200).WithMessage("Straße darf maximal 200 Zeichen lang sein");

        RuleFor(x => x.PostalCode)
            .MaximumLength(10).WithMessage("Postleitzahl darf maximal 10 Zeichen lang sein")
            .Matches(@"^\d{5}$")
            .When(x => !string.IsNullOrEmpty(x.PostalCode))
            .WithMessage("Postleitzahl muss aus 5 Ziffern bestehen");

        RuleFor(x => x.City)
            .MaximumLength(100).WithMessage("Stadt darf maximal 100 Zeichen lang sein");

        RuleFor(x => x.Website)
            .MaximumLength(500).WithMessage("Website-URL darf maximal 500 Zeichen lang sein")
            .Must(BeValidUrlOrEmpty).WithMessage("Ungültige URL")
            .When(x => !string.IsNullOrEmpty(x.Website));

        RuleFor(x => x.Email)
            .MaximumLength(200).WithMessage("E-Mail darf maximal 200 Zeichen lang sein")
            .EmailAddress().WithMessage("Ungültige E-Mail-Adresse")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.SocialMediaLinks).ValidateSocialMediaLinks();
    }

    private static bool BeValidUrlOrEmpty(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
               && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
}

/// <summary>
/// Validator for department contact assignment updates.
/// </summary>
public class UpdateDepartmentContactsDtoValidator : AbstractValidator<UpdateClubContactsDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IContactPositionRepository _contactPositionRepository;

    public UpdateDepartmentContactsDtoValidator(
        IUserRepository userRepository,
        IContactPositionRepository contactPositionRepository)
    {
        _userRepository = userRepository;
        _contactPositionRepository = contactPositionRepository;

        RuleForEach(x => x.ContactAssignments).ChildRules(assignment =>
        {
            assignment.RuleFor(a => a.UserId)
                .NotEmpty().WithMessage("Benutzer-ID ist erforderlich")
                .MustAsync(async (userId, cancellation) =>
                {
                    var user = await _userRepository.GetByIdAsync(userId, cancellation);
                    return user != null;
                }).WithMessage("Der angegebene Benutzer existiert nicht oder ist inaktiv");

            assignment.RuleFor(a => a.ContactPositionId)
                .MustAsync(async (positionId, cancellation) =>
                {
                    if (!positionId.HasValue) return true;
                    var position = await _contactPositionRepository.GetByIdAsync(positionId.Value, cancellation);
                    return position != null;
                })
                .When(a => a.ContactPositionId.HasValue)
                .WithMessage("Die angegebene Position existiert nicht oder ist inaktiv");
        });

        // Ensure no duplicate user IDs in the same request
        RuleFor(x => x.ContactAssignments)
            .Must(assignments => assignments.Select(a => a.UserId).Distinct().Count() == assignments.Count)
            .WithMessage("Ein Benutzer kann nicht mehrfach als Kontaktperson hinzugefügt werden");
    }
}
