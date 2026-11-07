using Igevo.Vernetzt.Core.DTOs.ClubTypes;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using FluentValidation;

namespace Igevo.Vernetzt.Infrastructure.Validators;

/// <summary>
/// Validator for CreateClubTypeDto with business rule validation.
/// </summary>
public class CreateClubTypeDtoValidator : AbstractValidator<CreateClubTypeDto>
{
    private readonly IClubTypeRepository _clubTypeRepository;

    public CreateClubTypeDtoValidator(IClubTypeRepository clubTypeRepository)
    {
        _clubTypeRepository = clubTypeRepository;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name ist erforderlich")
            .MaximumLength(200).WithMessage("Name darf maximal 200 Zeichen lang sein")
            .MustAsync(BeUniqueNameAsync).WithMessage("Ein Vereinstyp mit diesem Namen existiert bereits");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Beschreibung darf maximal 1000 Zeichen lang sein");
    }

    private async Task<bool> BeUniqueNameAsync(string name, CancellationToken cancellationToken)
    {
        var existingClubTypes = await _clubTypeRepository.GetAllAsync(cancellationToken);
        return !existingClubTypes.Any(ct => ct.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Validator for UpdateClubTypeDto with business rule validation.
/// </summary>
public class UpdateClubTypeDtoValidator : AbstractValidator<UpdateClubTypeDto>
{
    public UpdateClubTypeDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name ist erforderlich")
            .MaximumLength(200).WithMessage("Name darf maximal 200 Zeichen lang sein");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Beschreibung darf maximal 1000 Zeichen lang sein");
    }
}
