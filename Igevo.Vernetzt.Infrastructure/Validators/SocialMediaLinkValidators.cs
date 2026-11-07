using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.Entities;
using FluentValidation;

namespace Igevo.Vernetzt.Infrastructure.Validators;

/// <summary>
/// Shared validation rules for social media links of clubs and departments.
/// </summary>
public static class SocialMediaLinkValidationExtensions
{
    public static void ValidateSocialMediaLinks<T>(
        this IRuleBuilderInitial<T, List<SocialMediaLinkDto>?> ruleBuilder)
    {
        // A missing list (null) means "no links" and is handled by SocialMediaLinkDto.ToEntities.
        ruleBuilder
            .Must(links => links is null || links.Count <= SocialMediaLink.MaxLinksPerOwner)
            .WithMessage($"Es sind maximal {SocialMediaLink.MaxLinksPerOwner} Social-Media-Links erlaubt")
            .Must(links => links is null || links
                .Select(l => l.Url?.Trim())
                .Where(u => !string.IsNullOrEmpty(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == links.Count(l => !string.IsNullOrWhiteSpace(l.Url)))
            .WithMessage("Jeder Social-Media-Link darf nur einmal angegeben werden")
            .ForEach(link => link
                .NotNull().WithMessage("Ungültiger Social-Media-Link")
                .SetValidator(new SocialMediaLinkDtoValidator()));
    }
}

public class SocialMediaLinkDtoValidator : AbstractValidator<SocialMediaLinkDto>
{
    public SocialMediaLinkDtoValidator()
    {
        RuleFor(x => x.Platform)
            .IsInEnum().WithMessage("Unbekannte Social-Media-Plattform");

        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("Social-Media-URL ist erforderlich")
            .MaximumLength(SocialMediaLink.MaxUrlLength).WithMessage("Social-Media-URL darf maximal 500 Zeichen lang sein")
            .Must(BeHttpUrl).WithMessage("Ungültige Social-Media-URL (muss mit https:// oder http:// beginnen)");
    }

    private static bool BeHttpUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
