using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.DTOs.Clubs;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using FluentValidation;

namespace Igevo.Vernetzt.Infrastructure.Validators;

/// <summary>
/// Validator for CreateClubDto with business rule validation.
/// </summary>
public class CreateClubDtoValidator : AbstractValidator<CreateClubDto>
{
    private readonly IClubRepository _clubRepository;
    private readonly IClubTypeRepository _clubTypeRepository;

    public CreateClubDtoValidator(
        IClubRepository clubRepository,
        IClubTypeRepository clubTypeRepository)
    {
        _clubRepository = clubRepository;
        _clubTypeRepository = clubTypeRepository;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name ist erforderlich")
            .MaximumLength(200).WithMessage("Name darf maximal 200 Zeichen lang sein")
            .MustAsync(BeUniqueClubNameAsync).WithMessage("Ein Verein mit diesem Namen existiert bereits");

        RuleFor(x => x.ShortName)
            .MaximumLength(Club.MaxShortNameLength).WithMessage("Abkürzung darf maximal 20 Zeichen lang sein");

        RuleFor(x => x.ClubTypeId)
            .MustAsync(BeValidClubTypeIdAsync)
            .When(x => x.ClubTypeId.HasValue)
            .WithMessage("Der ausgewählte Vereinstyp existiert nicht");

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

        RuleFor(x => x.WebsiteUrl)
            .MaximumLength(500).WithMessage("Website-URL darf maximal 500 Zeichen lang sein")
            .Must(BeValidUrlOrEmpty).WithMessage("Ungültige URL")
            .When(x => !string.IsNullOrEmpty(x.WebsiteUrl));

        RuleFor(x => x.Email)
            .MaximumLength(200).WithMessage("E-Mail darf maximal 200 Zeichen lang sein")
            .EmailAddress().WithMessage("Ungültige E-Mail-Adresse")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.SocialMediaLinks).ValidateSocialMediaLinks();
    }

    private async Task<bool> BeUniqueClubNameAsync(string name, CancellationToken cancellationToken)
    {
        var existingClubs = await _clubRepository.GetAllAsync(cancellationToken);
        return !existingClubs.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> BeValidClubTypeIdAsync(Guid? clubTypeId, CancellationToken cancellationToken)
    {
        if (!clubTypeId.HasValue) return true;
        var clubType = await _clubTypeRepository.GetByIdAsync(clubTypeId.Value, cancellationToken);
        return clubType != null;
    }

    private static bool BeValidUrlOrEmpty(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
               && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
}

/// <summary>
/// Validator for UpdateClubDto with business rule validation.
/// </summary>
public class UpdateClubDtoValidator : AbstractValidator<UpdateClubDto>
{
    private readonly IClubRepository _clubRepository;
    private readonly IClubTypeRepository _clubTypeRepository;
    private Guid? _currentClubId;

    public UpdateClubDtoValidator(
        IClubRepository clubRepository,
        IClubTypeRepository clubTypeRepository)
    {
        _clubRepository = clubRepository;
        _clubTypeRepository = clubTypeRepository;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name ist erforderlich")
            .MaximumLength(200).WithMessage("Name darf maximal 200 Zeichen lang sein");

        RuleFor(x => x.ShortName)
            .MaximumLength(Club.MaxShortNameLength).WithMessage("Abkürzung darf maximal 20 Zeichen lang sein");

        RuleFor(x => x.ClubTypeId)
            .MustAsync(BeValidClubTypeIdAsync)
            .When(x => x.ClubTypeId.HasValue)
            .WithMessage("Der ausgewählte Vereinstyp existiert nicht");

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

        RuleFor(x => x.WebsiteUrl)
            .MaximumLength(500).WithMessage("Website-URL darf maximal 500 Zeichen lang sein")
            .Must(BeValidUrlOrEmpty).WithMessage("Ungültige URL")
            .When(x => !string.IsNullOrEmpty(x.WebsiteUrl));

        RuleFor(x => x.Email)
            .MaximumLength(200).WithMessage("E-Mail darf maximal 200 Zeichen lang sein")
            .EmailAddress().WithMessage("Ungültige E-Mail-Adresse")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.SocialMediaLinks).ValidateSocialMediaLinks();
    }

    /// <summary>
    /// Sets the current club ID for uniqueness validation excluding the current club.
    /// </summary>
    public UpdateClubDtoValidator WithCurrentClubId(Guid clubId)
    {
        _currentClubId = clubId;
        return this;
    }

    private async Task<bool> BeValidClubTypeIdAsync(Guid? clubTypeId, CancellationToken cancellationToken)
    {
        if (!clubTypeId.HasValue) return true;
        var clubType = await _clubTypeRepository.GetByIdAsync(clubTypeId.Value, cancellationToken);
        return clubType != null;
    }

    private static bool BeValidUrlOrEmpty(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
               && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
}

/// <summary>
/// Validator for contact assignment updates.
/// </summary>
public class UpdateClubContactsDtoValidator : AbstractValidator<UpdateClubContactsDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IContactPositionRepository _contactPositionRepository;

    public UpdateClubContactsDtoValidator(
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
