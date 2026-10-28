using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Services;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using ErpChatbot.Infrastructure.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using FluentAssertions;

namespace ErpChatbot.Tests.Agent;

public class ErpAgentServiceTests
{
    private class FakeChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("FakeChatClient");
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var responseMsg = new ChatMessage(ChatRole.Assistant, "Phản hồi giả lập");
            return Task.FromResult(new ChatResponse([responseMsg]) { Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 } });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Xin ");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "chào!");
            var usageUpdate = new ChatResponseUpdate();
            usageUpdate.Contents.Add(new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 1 }));
            yield return usageUpdate;
        }
    }

    [Fact]
    public async Task RunStreamingAsync_YieldsTextChunkEvents_AndTurnCompletedEvent()
    {
        var fakeClient = new FakeChatClient();
        var erpService = new InMemoryErpDataService();
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", Instructions = "Hướng dẫn" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Chào bạn"))
        {
            events.Add(evt);
        }

        events.Should().Contain(e => e is AgentTextChunkEvent && ((AgentTextChunkEvent)e).Text == "Xin ");
        events.Should().Contain(e => e is AgentTextChunkEvent && ((AgentTextChunkEvent)e).Text == "chào!");
        events.Should().Contain(e => e is AgentTurnCompletedEvent);

        var stats = service.GetStats();
        stats.TurnCount.Should().Be(1);
    }

    [Fact]
    public async Task ResetSessionAsync_ClearsSession_RetainsStats()
    {
        var fakeClient = new FakeChatClient();
        var erpService = new InMemoryErpDataService();
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", Instructions = "Hướng dẫn" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Lượt 1")) { }
        service.GetStats().TurnCount.Should().Be(1);

        await service.ResetSessionAsync();
        service.GetStats().TurnCount.Should().Be(1);
    }

    [Fact]
    public async Task RunStreamingAsync_WhenUserLacksPermission_RejectsWithoutAskingApproval()
    {
        var toolCall = new FunctionCallContent("call_pr_1", "create_purchase_request", new Dictionary<string, object?>
        {
            ["productCode"] = "SP-003",
            ["quantity"] = 100
        });
        var approvalReq = new ToolApprovalRequestContent("req_1", toolCall);
        var fakeClient = new FakeApprovalChatClient([approvalReq]);
        var erpService = new InMemoryErpDataService();
        // accountant_user KHÔNG có quyền PurchaseRequest:Create
        var userContext = new UserContext("accountant_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        bool handlerCalled = false;
        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Tạo đề xuất mua 100 màn hình", req =>
        {
            handlerCalled = true;
            return Task.FromResult(true);
        }))
        {
            events.Add(evt);
        }

        // BẢO VỆ AN TOÀN: Tuyệt đối không được gọi approvalHandler vì user không có quyền!
        handlerCalled.Should().BeFalse();
        events.Should().NotContain(e => e is AgentApprovalRequiredEvent);
        events.Should().Contain(e => e is AgentToolExecutionEvent && !((AgentToolExecutionEvent)e).Success && ((AgentToolExecutionEvent)e).Error!.Contains("không có quyền"));
    }

    [Fact]
    public async Task RunStreamingAsync_WhenQuantityIsNegativeOrZero_RejectsWithoutAskingApproval()
    {
        var toolCall = new FunctionCallContent("call_pr_2", "create_purchase_request", new Dictionary<string, object?>
        {
            ["productCode"] = "SP-003",
            ["quantity"] = -10
        });
        var approvalReq = new ToolApprovalRequestContent("req_2", toolCall);
        var fakeClient = new FakeApprovalChatClient([approvalReq]);
        var erpService = new InMemoryErpDataService();
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        bool handlerCalled = false;
        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Tạo đề xuất mua -10 cái", req =>
        {
            handlerCalled = true;
            return Task.FromResult(true);
        }))
        {
            events.Add(evt);
        }

        handlerCalled.Should().BeFalse();
        events.Should().NotContain(e => e is AgentApprovalRequiredEvent);
        events.Should().Contain(e => e is AgentToolExecutionEvent && !((AgentToolExecutionEvent)e).Success && ((AgentToolExecutionEvent)e).Error!.Contains("nguyên dương"));
    }

    [Fact]
    public async Task RunStreamingAsync_WhenApprovalHandlerIsNull_DefaultsToRejectSafely()
    {
        var toolCall = new FunctionCallContent("call_pr_3", "create_purchase_request", new Dictionary<string, object?>
        {
            ["productCode"] = "SP-003",
            ["quantity"] = 80
        });
        var approvalReq = new ToolApprovalRequestContent("req_3", toolCall);
        var fakeClient = new FakeApprovalChatClient([approvalReq]);
        var erpService = new InMemoryErpDataService();
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Tạo đề xuất 80 cái", approvalHandler: null))
        {
            events.Add(evt);
        }

        events.Should().Contain(e => e is AgentApprovalRequiredEvent);
        // Với approvalHandler = null, phản hồi duyệt mặc định là từ chối an toàn
    }

    private class FakeApprovalChatClient : IChatClient
    {
        private readonly List<ToolApprovalRequestContent> _requestsToYield;
        private int _step = 0;

        public FakeApprovalChatClient(List<ToolApprovalRequestContent> requestsToYield)
        {
            _requestsToYield = requestsToYield;
        }

        public ChatClientMetadata Metadata => new("FakeApprovalChatClient");
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_step == 0)
            {
                _step++;
                var update = new ChatResponseUpdate(ChatRole.Assistant, "");
                foreach (var req in _requestsToYield)
                {
                    update.Contents.Add(req);
                }
                yield return update;
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Đã xử lý");
            }
        }
    }
}
