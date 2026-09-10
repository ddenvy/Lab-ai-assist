using LabAi.Application.Rag;
using LabAi.Domain.ValueObjects;
using Xunit;

namespace LabAi.Tests.Rag;

public class PromptComposerTests
{
    [Fact]
    public void Compose_EmptyChunks_ReturnsQuestionOnly()
    {
        var (userPrompt, citations) = PromptComposer.Compose("What is the procedure?", Array.Empty<RetrievedChunk>());

        Assert.Contains("What is the procedure?", userPrompt);
        Assert.Empty(citations);
    }

    [Fact]
    public void Compose_WithChunks_IncludesCitations()
    {
        var chunks = new[]
        {
            new RetrievedChunk(1, 100, "SOP-001", 2, "Section A", "Wear gloves at all times.", 0.85),
            new RetrievedChunk(2, 101, "SOP-002", 1, "Section B", "Calibrate daily.", 0.72)
        };

        var (userPrompt, citations) = PromptComposer.Compose("Safety procedures?", chunks.ToList());

        Assert.Contains("[S1]", userPrompt);
        Assert.Contains("[S2]", userPrompt);
        Assert.Contains("Wear gloves", userPrompt);
        Assert.Equal(2, citations.Count);
        Assert.Equal(1, citations[0].Index);
        Assert.Equal(2, citations[1].Index);
    }

    [Fact]
    public void SystemPrompt_ContainsGroundedAnswerRules()
    {
        Assert.Contains("citation", PromptComposer.SystemPrompt.ToLower());
        Assert.Contains("not found in sources", PromptComposer.SystemPrompt.ToLower());
        Assert.Contains("do not invent", PromptComposer.SystemPrompt.ToLower());
    }
}
