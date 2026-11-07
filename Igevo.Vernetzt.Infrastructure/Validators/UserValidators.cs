using Igevo.Vernetzt.Core.DTOs.Users;
using Igevo.Vernetzt.Core.Interfaces.Repositories;
using FluentValidation;

namespace Igevo.Vernetzt.Infrastructure.Validators;

/// <summary>
/// Validator for CreateUserDto with business rule validation.
/// </summary>
public class CreateUserDtoValidator : AbstractValidator<CreateUserDto>
{
    private readonly IUserRepository _userRepository;

    public CreateUserDtoValidator(IUserRepository userRepository)
    {
        _userRepository = userRepository;

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-Mail ist erforderlich")
            .EmailAddress().WithMessage("Ungültige E-Mail-Adresse")
            .MaximumLength(200).WithMessage("E-Mail darf maximal 200 Zeichen lang sein")
            .MustAsync(BeUniqueEmailAsync).WithMessage("Ein Benutzer mit dieser E-Mail-Adresse existiert bereits");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("Vorname ist erforderlich")
            .MaximumLength(100).WithMessage("Vorname darf maximal 100 Zeichen lang sein")
            .Must(NotContainHtml).WithMessage("Vorname darf keine HTML-Tags enthalten");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Nachname ist erforderlich")
            .MaximumLength(100).WithMessage("Nachname darf maximal 100 Zeichen lang sein")
            .Must(NotContainHtml).WithMessage("Nachname darf keine HTML-Tags enthalten");

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(50).WithMessage("Telefonnummer darf maximal 50 Zeichen lang sein")
            .Matches(@"^[\d\s\+\-\(\)\/]+$")
            .When(x => !string.IsNullOrEmpty(x.PhoneNumber))
            .WithMessage("Telefonnummer enthält ungültige Zeichen");

        RuleFor(x => x.EmailVisibility)
            .IsInEnum().WithMessage("Ungültiger Wert für E-Mail-Sichtbarkeit");

        RuleFor(x => x.PhoneVisibility)
            .IsInEnum().WithMessage("Ungültiger Wert für Telefon-Sichtbarkeit");
    }

    private async Task<bool> BeUniqueEmailAsync(string email, CancellationToken cancellationToken)
    {
        var existingUser = await _userRepository.GetByEmailAsync(email, cancellationToken);
        return existingUser == null;
    }

    private static bool NotContainHtml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        return !value.Contains('<') && !value.Contains('>');
    }
}

/// <summary>
/// Validator for UpdateUserDto with business rule validation.
/// </summary>
public class UpdateUserDtoValidator : AbstractValidator<UpdateUserDto>
{
    public UpdateUserDtoValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("Vorname ist erforderlich")
            .MaximumLength(100).WithMessage("Vorname darf maximal 100 Zeichen lang sein")
            .Must(NotContainHtml).WithMessage("Vorname darf keine HTML-Tags enthalten");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Nachname ist erforderlich")
            .MaximumLength(100).WithMessage("Nachname darf maximal 100 Zeichen lang sein")
            .Must(NotContainHtml).WithMessage("Nachname darf keine HTML-Tags enthalten");

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(50).WithMessage("Telefonnummer darf maximal 50 Zeichen lang sein")
            .Matches(@"^[\d\s\+\-\(\)\/]+$")
            .When(x => !string.IsNullOrEmpty(x.PhoneNumber))
            .WithMessage("Telefonnummer enthält ungültige Zeichen");

        RuleFor(x => x.EmailVisibility)
            .IsInEnum().WithMessage("Ungültiger Wert für E-Mail-Sichtbarkeit");

        RuleFor(x => x.PhoneVisibility)
            .IsInEnum().WithMessage("Ungültiger Wert für Telefon-Sichtbarkeit");
    }

    private static bool NotContainHtml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        return !value.Contains('<') && !value.Contains('>');
    }
}
