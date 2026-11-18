using ErpChatbot.Core.Models;
using FluentAssertions;
using Xunit;

namespace ErpChatbot.Tests.Core;

public class CoreModelsTests
{
    [Fact]
    public void LlmOptions_ShouldHaveDefaultValues_MatchingDay01Spec()
    {
        var options = new LlmOptions();

        options.Provider.Should().Be("ollama");
        options.Model.Should().Be("llama3.2");
        options.Temperature.Should().Be(0.2f);
        options.MaxOutputTokens.Should().Be(500);
        options.Ollama.Endpoint.Should().Be("http://localhost:11434");
    }

    [Fact]
    public void ErpOptions_ShouldHaveDefaultPersona()
    {
        var options = new ErpOptions();

        options.AssistantName.Should().Be("Trợ lý ERP");
        options.Instructions.Should().Contain("Bạn là 'Trợ lý ERP' của công ty");
        options.Instructions.Should().Contain("không bao giờ tự bịa số").And.Contain("Luôn dùng tool");
    }

    [Fact]
    public void ErpOptions_ShouldHaveDefaultAutoApproveQuantityThreshold_IsZero_AlwaysAsk()
    {
        new ErpOptions().AutoApproveQuantityThreshold.Should().Be(0);
    }

    [Fact]
    public void SessionStats_ShouldAccumulateCorrectly()
    {
        var stats = new SessionStats();
        stats.RecordTurn(inputTokens: 10, outputTokens: 25);
        stats.RecordTurn(inputTokens: 20, outputTokens: 35);

        stats.TurnCount.Should().Be(2);
        stats.TotalInputTokens.Should().Be(30);
        stats.TotalOutputTokens.Should().Be(60);
        stats.TotalTokens.Should().Be(90);
    }
}
