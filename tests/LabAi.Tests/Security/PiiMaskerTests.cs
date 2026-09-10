using LabAi.Application.Security;
using Xunit;

namespace LabAi.Tests.Security;

public class PiiMaskerTests
{
    private readonly RegexPiiMasker _masker = new();

    [Fact]
    public void Mask_Emails_AreRedacted()
    {
        var result = _masker.Mask("Contact john.doe@example.com for info.");
        Assert.DoesNotContain("john.doe@example.com", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Mask_PhoneNumbers_AreRedacted()
    {
        var result = _masker.Mask("Call +1-555-123-4567 or 555-987-6543.");
        Assert.DoesNotContain("+1-555-123-4567", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Mask_SSN_AreRedacted()
    {
        var result = _masker.Mask("SSN: 123-45-6789 is required.");
        Assert.DoesNotContain("123-45-6789", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Mask_NoPII_ReturnsOriginal()
    {
        var result = _masker.Mask("The procedure requires gloves.");
        Assert.Equal("The procedure requires gloves.", result);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void Mask_NullOrEmpty_HandledCorrectly(string? input, string expected)
    {
        var result = _masker.Mask(input);
        Assert.Equal(expected, result);
    }
}
