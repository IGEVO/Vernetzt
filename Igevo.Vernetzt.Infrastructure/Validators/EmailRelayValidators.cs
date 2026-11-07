using Igevo.Vernetzt.Core.DTOs.EmailRelay;
using FluentValidation;

namespace Igevo.Vernetzt.Infrastructure.Validators;

/// <summary>
/// Validator for CreateEmailRelayTokenRequest.
/// </summary>
public class CreateEmailRelayTokenRequestValidator : AbstractValidator<CreateEmailRelayTokenRequest>
{
    public CreateEmailRelayTokenRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.RecipientAssignmentIds.Count > 0 || x.RecipientUserIds.Count > 0)
            .WithMessage("Mindestens ein Empfänger ist erforderlich");

        RuleFor(x => x)
            .Must(x => (x.RecipientAssignmentIds.Count + x.RecipientUserIds.Count) <= 500)
            .WithMessage("Maximal 500 Empfänger pro Token erlaubt");

        RuleForEach(x => x.RecipientAssignmentIds)
            .NotEmpty().WithMessage("Zuordnungs-ID darf nicht leer sein");

        RuleForEach(x => x.RecipientUserIds)
            .NotEmpty().WithMessage("Empfänger-ID darf nicht leer sein");

        RuleFor(x => x.SubjectHint)
            .MaximumLength(200).WithMessage("Betreffhinweis darf maximal 200 Zeichen lang sein");
    }
}
