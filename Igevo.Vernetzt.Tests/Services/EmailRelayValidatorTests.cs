using Igevo.Vernetzt.Core.DTOs.EmailRelay;
using Igevo.Vernetzt.Infrastructure.Validators;

namespace Igevo.Vernetzt.Tests.Services;

[TestClass]
public class EmailRelayValidatorTests
{
    private CreateEmailRelayTokenRequestValidator _validator = null!;

    [TestInitialize]
    public void Setup()
    {
        _validator = new CreateEmailRelayTokenRequestValidator();
    }

    [TestMethod]
    public async Task Validate_EmptyRecipientUserIds_HasError()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid>()
        };

        var result = await _validator.ValidateAsync(request);

        Assert.IsFalse(result.IsValid);
        // Both lists empty → root-level error (property name is empty string)
        Assert.IsTrue(result.Errors.Any(e => e.PropertyName == string.Empty));
    }

    [TestMethod]
    public async Task Validate_TooManyRecipients_HasError()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList()
        };

        var result = await _validator.ValidateAsync(request);

        Assert.IsFalse(result.IsValid);
        // Combined count > 500 → root-level error (property name is empty string)
        Assert.IsTrue(result.Errors.Any(e => e.PropertyName == string.Empty));
    }

    [TestMethod]
    public async Task Validate_EmptyGuidInRecipients_HasError()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { Guid.NewGuid(), Guid.Empty }
        };

        var result = await _validator.ValidateAsync(request);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => e.PropertyName.Contains("RecipientUserIds")));
    }

    [TestMethod]
    public async Task Validate_SubjectHintTooLong_HasError()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { Guid.NewGuid() },
            SubjectHint = new string('x', 201)
        };

        var result = await _validator.ValidateAsync(request);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => e.PropertyName == "SubjectHint"));
    }

    [TestMethod]
    public async Task Validate_ValidRequest_NoErrors()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
            SubjectHint = "Newsletter Mai"
        };

        var result = await _validator.ValidateAsync(request);

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public async Task Validate_NullSubjectHint_NoErrors()
    {
        var request = new CreateEmailRelayTokenRequest
        {
            RecipientUserIds = new List<Guid> { Guid.NewGuid() },
            SubjectHint = null
        };

        var result = await _validator.ValidateAsync(request);

        Assert.IsTrue(result.IsValid);
    }
}
