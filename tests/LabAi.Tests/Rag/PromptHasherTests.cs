using LabAi.Application.Rag;
using Xunit;

namespace LabAi.Tests.Rag;

public class PromptHasherTests
{
    [Fact]
    public void Compute_SameInputs_ProducesSameHash()
    {
        var hash1 = PromptHasher.Compute("system", "user", "model-1");
        var hash2 = PromptHasher.Compute("system", "user", "model-1");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Compute_DifferentSystemPrompt_ProducesDifferentHash()
    {
        var hash1 = PromptHasher.Compute("system A", "user", "model-1");
        var hash2 = PromptHasher.Compute("system B", "user", "model-1");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Compute_DifferentUserPrompt_ProducesDifferentHash()
    {
        var hash1 = PromptHasher.Compute("system", "user A", "model-1");
        var hash2 = PromptHasher.Compute("system", "user B", "model-1");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Compute_DifferentModel_ProducesDifferentHash()
    {
        var hash1 = PromptHasher.Compute("system", "user", "model-1");
        var hash2 = PromptHasher.Compute("system", "user", "model-2");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Compute_ReturnsLowercaseHex()
    {
        var hash = PromptHasher.Compute("system", "user", "model-1");
        Assert.Matches("^[a-f0-9]{64}$", hash); // SHA256 = 64 hex chars
    }

    [Fact]
    public void Compute_PreventsFieldBoundaryCollision()
    {
        // ("ab", "c") should not equal ("a", "bc") due to length-prefixing
        var hash1 = PromptHasher.Compute("ab", "c", "m");
        var hash2 = PromptHasher.Compute("a", "bc", "m");
        Assert.NotEqual(hash1, hash2);
    }
}
