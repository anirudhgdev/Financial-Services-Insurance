using ClaimSettlement.Api.Claims;
using Xunit;

namespace ClaimSettlement.Api.Tests;

public sealed class ClaimIntakeValidationServiceTests
{
    [Fact]
    public void ReturnsMissingMandatoryFields_WhenFieldsAreIncomplete()
    {
        var service = new ClaimIntakeValidationService();

        var input = new Dictionary<string, string>
        {
            ["PolicyNumber"] = "POL-001",
            ["ClaimType"] = "auto"
        };

        var gaps = service.GetMandatoryFieldGaps(input, "auto", "provider-1", "{}", "[]");

        Assert.NotEmpty(gaps);
        Assert.Contains(gaps, x => x.FieldName == "DateOfLoss");
        Assert.Contains(gaps, x => x.FieldName == "LossAmount");
    }

    [Fact]
    public void ReturnsUnsupportedClaimTypeGap_WhenTypeNotConfigured()
    {
        var service = new ClaimIntakeValidationService();

        var input = new Dictionary<string, string>
        {
            ["ClaimType"] = "marine"
        };

        var gaps = service.GetMandatoryFieldGaps(input, "marine", "provider-1", "{}", "[\"auto\",\"property\"]");

        Assert.Single(gaps);
        Assert.Equal("ClaimType", gaps[0].FieldName);
    }
}
