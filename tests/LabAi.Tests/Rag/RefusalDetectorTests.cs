using LabAi.Application.Rag;
using LabAi.Domain.Enums;
using Xunit;

namespace LabAi.Tests.Rag;

public class RefusalDetectorTests
{
    [Theory]
    [InlineData("Not found in sources.", true)]
    [InlineData("not found in sources", true)]
    [InlineData("NOT FOUND IN SOURCES!", true)]
    [InlineData("The procedure requires gloves [S1].", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void IsRefusal_DetectsCorrectly(string? answer, bool expected)
    {
        Assert.Equal(expected, RefusalDetector.IsRefusal(answer));
    }

    [Fact]
    public void DetermineStage_ThresholdGate_ReturnsThresholdGate()
    {
        var stage = RefusalDetector.DetermineStage(false, null);
        Assert.Equal(RefusalStage.ThresholdGate, stage);
    }

    [Fact]
    public void DetermineStage_ModelRefusal_ReturnsModelRefusal()
    {
        var stage = RefusalDetector.DetermineStage(true, "Not found in sources.");
        Assert.Equal(RefusalStage.ModelRefusal, stage);
    }

    [Fact]
    public void DetermineStage_SubstantiveAnswer_ReturnsNone()
    {
        var stage = RefusalDetector.DetermineStage(true, "The answer is X [S1].");
        Assert.Equal(RefusalStage.None, stage);
    }

    [Fact]
    public void ExtractRationale_FindsMarker()
    {
        var answer = "The answer is X [S1].\n\n--- Rationale: Fragment S1 states...";
        var rationale = RefusalDetector.ExtractRationale(answer);
        Assert.Equal("Fragment S1 states...", rationale);
    }

    [Fact]
    public void ExtractRationale_NoMarker_ReturnsNull()
    {
        var rationale = RefusalDetector.ExtractRationale("Just an answer.");
        Assert.Null(rationale);
    }
}
