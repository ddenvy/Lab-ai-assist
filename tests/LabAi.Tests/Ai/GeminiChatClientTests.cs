using LabAi.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using NSubstitute;

namespace LabAi.Tests.Ai;

/// <summary>
/// Locks the contract of the chat adapter, including the two settings the audit story depends on:
/// <c>Temperature = 0</c> and <c>CandidateCount = 1</c> are what make a recorded <c>PromptHash</c>
/// imply a reproducible answer.
/// </summary>
public sealed class GeminiChatClientTests
{
    private static readonly GeminiOptions Configured = new() { ApiKey = "not-a-real-key" };

    [Fact]
    public async Task ThrowsNotConfigured_AndNeverTouchesTheConnector_WhenTheKeyIsMissing()
    {
        var connectorResolved = false;
        var client = new GeminiChatClient(
            new GeminiOptions(),
            () =>
            {
                connectorResolved = true;
                return Substitute.For<IChatCompletionService>();
            },
            NullLogger<GeminiChatClient>.Instance);

        var act = () => client.CompleteAsync("system", "user");

        await act.Should().ThrowAsync<GeminiNotConfiguredException>();
        connectorResolved.Should().BeFalse("the adapter must fail before resolving the alpha connector");
    }

    [Fact]
    public async Task SendsSystemAndUserPrompts_AsTwoSeparateMessages()
    {
        ChatHistory? history = null;
        var client = ClientWith(Capture(history: h => history = h));

        await client.CompleteAsync("the system prompt", "the user prompt");

        history.Should().NotBeNull();
        history!.Should().HaveCount(2);
        history[0].Role.Should().Be(AuthorRole.System);
        history[0].Content.Should().Be("the system prompt");
        history[1].Role.Should().Be(AuthorRole.User);
        history[1].Content.Should().Be("the user prompt");
    }

    [Fact]
    public async Task UsesTemperatureZeroAndASingleCandidate()
    {
        PromptExecutionSettings? settings = null;
        var client = ClientWith(Capture(settings: s => settings = s));

        await client.CompleteAsync("system", "user");

        var gemini = settings.Should().BeOfType<GeminiPromptExecutionSettings>().Subject;
        gemini.Temperature.Should().Be(0.0, "reproducibility is what makes PromptHash evidence");
        gemini.CandidateCount.Should().Be(1, "Gemini requires temperature 1.0 above one candidate");
    }

    [Fact]
    public async Task ReturnsTheCandidateText_AndTheConfiguredModelId()
    {
        var client = ClientWith(Replying(new ChatMessageContent(AuthorRole.Assistant, "the model answer")));

        var result = await client.CompleteAsync("system", "user");

        result.Text.Should().Be("the model answer");
        result.ModelId.Should().Be(Configured.ChatModelId);
    }

    [Fact]
    public async Task ReturnsEmptyText_WhenTheModelProducesNoCandidates()
    {
        var client = ClientWith(Replying());

        var result = await client.CompleteAsync("system", "user");

        result.Text.Should().BeEmpty("a safety-blocked turn must not invent content");
    }

    [Fact]
    public async Task TreatsANullCandidateBody_AsEmptyText()
    {
        var client = ClientWith(Replying(new ChatMessageContent(AuthorRole.Assistant, content: null)));

        var result = await client.CompleteAsync("system", "user");

        result.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task PropagatesTheCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken token = default;
        var client = ClientWith(Capture(token: t => token = t));

        await client.CompleteAsync("system", "user", cts.Token);

        token.Should().Be(cts.Token, "a cancelled Blazor circuit must abort the paid call");
    }

    private static GeminiChatClient ClientWith(IChatCompletionService service) =>
        new(Configured, () => service, NullLogger<GeminiChatClient>.Instance);

    private static IChatCompletionService Replying(params ChatMessageContent[] candidates)
    {
        var service = Substitute.For<IChatCompletionService>();
        service.GetChatMessageContentsAsync(
                Arg.Any<ChatHistory>(),
                Arg.Any<PromptExecutionSettings>(),
                Arg.Any<Kernel>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ChatMessageContent>>(candidates));
        return service;
    }

    private static IChatCompletionService Capture(
        Action<ChatHistory>? history = null,
        Action<PromptExecutionSettings>? settings = null,
        Action<CancellationToken>? token = null)
    {
        var service = Substitute.For<IChatCompletionService>();
        service.GetChatMessageContentsAsync(
                Arg.Do<ChatHistory>(captured => history?.Invoke(captured)),
                Arg.Do<PromptExecutionSettings>(captured => settings?.Invoke(captured)),
                Arg.Any<Kernel>(),
                Arg.Do<CancellationToken>(captured => token?.Invoke(captured)))
            .Returns(Task.FromResult<IReadOnlyList<ChatMessageContent>>(
                [new ChatMessageContent(AuthorRole.Assistant, "ok")]));
        return service;
    }
}
