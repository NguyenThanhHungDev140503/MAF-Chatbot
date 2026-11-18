using System.Runtime.CompilerServices;
using System.Text.Json;
using ErpChatbot.Agent.Services;
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using ErpChatbot.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using FluentAssertions;

namespace ErpChatbot.Tests.Cli;

[Collection("Console")]
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

    // Ghi lại nội dung mọi message của từng lần gọi model; lượt 1 gọi tool công nợ, các lần sau trả text.
    private class RecordingToolChatClient : IChatClient
    {
        private int _callCount;
        public List<string> Calls { get; } = [];
        public List<List<string>> ToolNames { get; } = [];

        public ChatClientMetadata Metadata => new("RecordingToolChatClient");
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(string.Join("\n", chatMessages.SelectMany(m => m.Contents).Select(Flatten)));
            ToolNames.Add(options?.Tools?.Select(t => t.Name).ToList() ?? []);
            _callCount++;

            if (_callCount == 1)
            {
                var callUpdate = new ChatResponseUpdate();
                callUpdate.Contents.Add(new FunctionCallContent("c1", "get_customer_debt",
                    new Dictionary<string, object?> { ["customerCode"] = "KH-002" }));
                yield return callUpdate;
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, _callCount == 2 ? "Công nợ đã tra" : "ok");
            }
        }

        private static string Flatten(AIContent content) => content switch
        {
            TextContent t => t.Text ?? "",
            FunctionCallContent f => $"{f.Name}({JsonSerializer.Serialize(f.Arguments)})",
            FunctionResultContent r => r.Result?.ToString() ?? "",
            _ => ""
        };
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
    public async Task TC32_Whoami_PrintsUserRolesAndPermissions()
    {
        var userContext = new UserContext("admin_user");
        var handler = new CliCommandHandler(_agentService, userContext, _llmOptions, _erpOptions);

        using var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var result = await handler.HandleInputAsync("/whoami", CancellationToken.None);
            result.Should().Be(CliInputResult.Handled);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        sw.ToString().Should()
            .Contain("admin_user").And.Contain("Lê Quản Trị").And.Contain("Admin")
            .And.Contain("Inventory:Read").And.Contain("Orders:Read").And.Contain("Debt:Read").And.Contain("PurchaseRequest:Create");
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

    [Fact]
    public async Task HandleInputAsync_UserCommand_RealAgentSession_DoesNotCarryPreviousDebtResult()
    {
        var chatClient = new RecordingToolChatClient();
        var userContext = new UserContext("accountant_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", Instructions = "Hướng dẫn" });
        var agentService = new ErpAgentService(chatClient, new InMemoryErpDataService(), userContext, erpOptions);
        var handler = new CliCommandHandler(agentService, userContext, _llmOptions, _erpOptions);

        await foreach (var _ in agentService.RunStreamingAsync("Công nợ KH-002?")) { }

        // Đối chứng: lần gọi model thứ 2 của lượt 1 phải thấy kết quả công nợ thật.
        chatClient.Calls.Should().HaveCountGreaterThanOrEqualTo(2);
        chatClient.Calls[1].Should().Contain("Hoàng Gia");

        int turn1CallCount = chatClient.Calls.Count;
        await handler.HandleInputAsync("/user sales_user", CancellationToken.None);
        await foreach (var _ in agentService.RunStreamingAsync("Tồn kho SP-001?")) { }

        var turn2Calls = chatClient.Calls.Skip(turn1CallCount).ToList();
        turn2Calls.Should().NotBeEmpty();
        turn2Calls.Should().NotContain(c => c.Contains("Hoàng Gia"));
        turn2Calls.Should().NotContain(c => c.Contains("120500000"));
    }

    [Fact]
    public async Task TC30_UserCommand_RebuildsToolListForNewUser()
    {
        var chatClient = new RecordingToolChatClient();
        var userContext = new UserContext("accountant_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", Instructions = "Hướng dẫn" });
        var agentService = new ErpAgentService(chatClient, new InMemoryErpDataService(), userContext, erpOptions);
        var handler = new CliCommandHandler(agentService, userContext, _llmOptions, _erpOptions);

        await foreach (var _ in agentService.RunStreamingAsync("Công nợ KH-002?")) { }

        // Đối chứng: accountant được đưa tool công nợ ở lượt 1.
        chatClient.ToolNames[0].Should().Contain("get_customer_debt");

        int turn1CallCount = chatClient.ToolNames.Count;
        await handler.HandleInputAsync("/user sales_user", CancellationToken.None);
        await foreach (var _ in agentService.RunStreamingAsync("Tồn kho SP-001?")) { }

        var turn2Tools = chatClient.ToolNames.Skip(turn1CallCount).ToList();
        turn2Tools.Should().NotBeEmpty();
        turn2Tools.Should().OnlyContain(t => !t.Contains("get_customer_debt") && t.Contains("create_purchase_request"));
    }
}
