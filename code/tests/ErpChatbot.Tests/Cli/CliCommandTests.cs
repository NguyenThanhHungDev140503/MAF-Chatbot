using System.Runtime.CompilerServices;
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using FluentAssertions;
using Xunit;

namespace ErpChatbot.Tests.Cli;

public class CliCommandTests
{
    private class FakeAgentService : IErpAgentService
    {
        public int ResetCalls { get; private set; }
        public int PromptCalls { get; private set; }
        public SessionStats Stats { get; } = new();

        public async IAsyncEnumerable<string> RunStreamingAsync(
            string userPrompt,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            PromptCalls++;
            yield return "Phản hồi";
            await Task.Yield();
        }

        public Task ResetSessionAsync()
        {
            ResetCalls++;
            return Task.CompletedTask;
        }

        public SessionStats GetStats() => Stats;
    }

    [Fact]
    public async Task ProcessInput_WithResetCommand_ResetsSessionAndDoesNotCallLlm()
    {
        var service = new FakeAgentService();
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("/reset", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        service.ResetCalls.Should().Be(1);
        service.PromptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessInput_WithStatsCommand_ShowsStatsAndDoesNotCallLlm()
    {
        var service = new FakeAgentService();
        service.Stats.RecordTurn(50, 20);
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("/stats", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        service.PromptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessInput_WithInfoCommand_DisplaysConfiguration()
    {
        var service = new FakeAgentService();
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("/info", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        service.PromptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessInput_WithInvalidSlashCommand_ShowsHintWithoutCallingLlm()
    {
        var service = new FakeAgentService();
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("/unknown", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        service.PromptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessInput_WithEmptyOrWhitespace_IsIgnoredWithoutCallingLlm()
    {
        var service = new FakeAgentService();
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("   ", CancellationToken.None);

        result.Should().Be(CliInputResult.Ignored);
        service.PromptCalls.Should().Be(0);
    }
}
