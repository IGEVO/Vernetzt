using Igevo.Vernetzt.Core.DTOs.Common;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Validators;
using FluentValidation;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class SocialMediaLinkValidatorTests
{
    private sealed class Owner
    {
        public List<SocialMediaLinkDto>? SocialMediaLinks { get; set; } = new();
    }

    private sealed class OwnerValidator : AbstractValidator<Owner>
    {
        public OwnerValidator() => RuleFor(x => x.SocialMediaLinks).ValidateSocialMediaLinks();
    }

    private static Owner With(params (SocialMediaPlatform Platform, string Url)[] links) => new()
    {
        SocialMediaLinks = links.Select(l => new SocialMediaLinkDto { Platform = l.Platform, Url = l.Url }).ToList()
    };

    [TestMethod]
    public void Validate_ValidLinks_Passes()
    {
        var result = new OwnerValidator().Validate(With(
            (SocialMediaPlatform.Facebook, "https://facebook.com/verein"),
            (SocialMediaPlatform.Mastodon, "https://social.example/@verein")));

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    [DataRow("javascript:alert(1)")]
    [DataRow("ftp://example.org")]
    [DataRow("instagram.com/verein")]
    [DataRow("")]
    public void Validate_InvalidUrl_Fails(string url)
    {
        var result = new OwnerValidator().Validate(With((SocialMediaPlatform.Instagram, url)));

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void Validate_UnknownPlatform_Fails()
    {
        var result = new OwnerValidator().Validate(With(((SocialMediaPlatform)99, "https://example.org")));

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void Validate_NullList_Passes()
    {
        Assert.IsTrue(new OwnerValidator().Validate(new Owner { SocialMediaLinks = null }).IsValid);
    }

    [TestMethod]
    public void Validate_DuplicateUrl_Fails()
    {
        var result = new OwnerValidator().Validate(With(
            (SocialMediaPlatform.Instagram, "https://instagram.com/verein"),
            (SocialMediaPlatform.Instagram, "https://Instagram.com/verein ")));

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void Validate_TooManyLinks_Fails()
    {
        var links = Enumerable.Range(0, 11)
            .Select(i => (SocialMediaPlatform.Facebook, $"https://facebook.com/verein{i}"))
            .ToArray();

        var result = new OwnerValidator().Validate(With(links));

        Assert.IsFalse(result.IsValid);
    }
}
