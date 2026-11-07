using Igevo.Vernetzt.Core.DTOs.ContactPositions;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using FluentValidation;

namespace Igevo.Vernetzt.Infrastructure.Validators;

/// <summary>
/// Validator for CreateContactPositionDto with business rule validation.
/// </summary>
public class CreateContactPositionDtoValidator : AbstractValidator<CreateContactPositionDto>
{
    private readonly IContactPositionRepository _contactPositionRepository;

    public CreateContactPositionDtoValidator(IContactPositionRepository contactPositionRepository)
    {
        _contactPositionRepository = contactPositionRepository;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name ist erforderlich")
            .MaximumLength(200).WithMessage("Name darf maximal 200 Zeichen lang sein")
            .MustAsync(BeUniqueNameAsync).WithMessage("Eine Position mit diesem Namen existiert bereits");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Beschreibung darf maximal 1000 Zeichen lang sein");
    }

    private async Task<bool> BeUniqueNameAsync(string name, CancellationToken cancellationToken)
    {
        var existingPositions = await _contactPositionRepository.GetAllAsync(cancellationToken);
        return !existingPositions.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Validator for UpdateContactPositionDto with business rule validation.
/// </summary>
public class UpdateContactPositionDtoValidator : AbstractValidator<UpdateContactPositionDto>
{
    public UpdateContactPositionDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name ist erforderlich")
            .MaximumLength(200).WithMessage("Name darf maximal 200 Zeichen lang sein");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Beschreibung darf maximal 1000 Zeichen lang sein");
    }
}
