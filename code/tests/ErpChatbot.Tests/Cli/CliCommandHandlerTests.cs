using System.Runtime.CompilerServices;
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using FluentAssertions;

namespace ErpChatbot.Tests.Cli;

public class CliCommandHandlerTests
{
    private class FakeAgentService : IErpAgentService
    {
        public bool ResetCalled { get; private set; }

        public async IAsyncEnumerable<AgentResponseEvent> RunStreamingAsync(
            string userPrompt,
            Func<AgentApprovalRequiredEvent, Task<bool>>? approvalHandler = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new AgentTextChunkEvent("mock response");
        }

        public Task ResetSessionAsync()
        {
            ResetCalled = true;
            return Task.CompletedTask;
        }

        public SessionStats GetStats() => new();
    }

    private readonly FakeAgentService _agentService = new();
    private readonly IUserContext _userContext = new UserContext("sales_user");
    private readonly LlmOptions _llmOptions = new() { Provider = "ollama", Model = "llama3.2" };
    private readonly ErpOptions _erpOptions = new() { AssistantName = "Trợ lý ERP" };

    [Fact]
    public async Task HandleInputAsync_UserCommand_SwitchesUser_AndResetsSession()
    {
        var handler = new CliCommandHandler(_agentService, _userContext, _llmOptions, _erpOptions);

        var result = await handler.HandleInputAsync("/user accountant_user", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        _userContext.CurrentUser.Username.Should().Be("accountant_user");
        _agentService.ResetCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleInputAsync_WhoamiCommand_PrintsUserInfo()
    {
        var handler = new CliCommandHandler(_agentService, _userContext, _llmOptions, _erpOptions);

        var result = await handler.HandleInputAsync("/whoami", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
    }

    [Fact]
    public async Task HandleInputAsync_ResetCommand_CallsResetSession()
    {
        var handler = new CliCommandHandler(_agentService, _userContext, _llmOptions, _erpOptions);

        var result = await handler.HandleInputAsync("/reset", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        _agentService.ResetCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleInputAsync_ExitCommand_ReturnsExit()
    {
        var handler = new CliCommandHandler(_agentService, _userContext, _llmOptions, _erpOptions);

        var result = await handler.HandleInputAsync("/exit", CancellationToken.None);

        result.Should().Be(CliInputResult.Exit);
    }

    [Fact]
    public async Task HandleInputAsync_NormalPrompt_ReturnsSendToLlm()
    {
        var handler = new CliCommandHandler(_agentService, _userContext, _llmOptions, _erpOptions);

        var result = await handler.HandleInputAsync("SP-001 còn bao nhiêu?", CancellationToken.None);

        result.Should().Be(CliInputResult.SendToLlm);
    }
}
