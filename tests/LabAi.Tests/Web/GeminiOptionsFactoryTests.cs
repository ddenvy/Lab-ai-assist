using LabAi.Infrastructure.Ai;
using LabAi.Web.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace LabAi.Tests.Web;

public sealed class GeminiOptionsFactoryTests
{
    [Fact]
    public void EnvironmentWinsOverConfiguration()
    {
        var configuration = ConfigurationWith(
            ("Gemini:ApiKey", "from-appsettings"),
            ("Gemini:ChatModelId", "config-chat"),
            ("Gemini:EmbeddingModelId", "config-embedding"));

        var options = GeminiOptionsFactory.Create(configuration, key => key switch
        {
            "GEMINI_API_KEY" => "from-env",
            "GEMINI_MODEL" => "env-chat",
            "GEMINI_EMBEDDING_MODEL" => "env-embedding",
            _ => null,
        });

        options.ApiKey.Should().Be("from-env");
        options.ChatModelId.Should().Be("env-chat");
        options.EmbeddingModelId.Should().Be("env-embedding");
    }

    [Fact]
    public void FallsBackToConfiguration_WhenEnvironmentIsNotSet()
    {
        var configuration = ConfigurationWith(("Gemini:ApiKey", "from-appsettings"));

        var options = GeminiOptionsFactory.Create(configuration, _ => null);

        options.ApiKey.Should().Be("from-appsettings");
        options.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void TreatsAWhitespaceEnvironmentValue_AsAbsent()
    {
        var configuration = ConfigurationWith(("Gemini:ChatModelId", "config-chat"));

        var options = GeminiOptionsFactory.Create(configuration, _ => "   ");

        options.ChatModelId.Should().Be("config-chat");
    }

    [Fact]
    public void FallsBackToDefaults_WhenNeitherSourceProvidesAValue()
    {
        var options = GeminiOptionsFactory.Create(EmptyConfiguration(), _ => null);

        // Compared against GeminiOptions rather than a literal: Google retires model names
        // (gemini-2.5-flash began answering 404 for new keys), and a literal here would fail an
        // unrelated test every time the default model is updated.
        var defaults = new GeminiOptions();
        options.ChatModelId.Should().Be(defaults.ChatModelId);
        options.EmbeddingModelId.Should().Be(defaults.EmbeddingModelId);
    }

    [Fact]
    public void ReportsNotConfigured_WhenNoKeyIsPresentAnywhere()
    {
        // The committed appsettings.json ships an empty ApiKey on purpose, so this is the state a
        // fresh clone and the whole test suite run in.
        var options = GeminiOptionsFactory.Create(ConfigurationWith(("Gemini:ApiKey", "")), _ => null);

        options.ApiKey.Should().BeEmpty();
        options.IsConfigured.Should().BeFalse();
    }

    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigurationWith(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
}
