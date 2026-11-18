using System.Runtime.CompilerServices;
using System.Text.Json;
using ErpChatbot.Agent.Security;
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
    public async Task RunStreamingAsync_ToolExecutionEvent_ComesBeforeFollowingTextAndTurnCompleted()
    {
        var fakeClient = new FakeToolCallingChatClient("Còn 124 cái");
        var erpService = new InMemoryErpDataService();
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Tồn kho SP-001?"))
        {
            events.Add(evt);
        }

        var toolIdx = events.FindIndex(e => e is AgentToolExecutionEvent t && t.ToolName == "get_stock" && t.Success);
        var textIdx = events.FindIndex(e => e is AgentTextChunkEvent t && t.Text.Contains("124"));
        var doneIdx = events.FindIndex(e => e is AgentTurnCompletedEvent);

        toolIdx.Should().BeGreaterThanOrEqualTo(0);
        textIdx.Should().BeGreaterThan(toolIdx);
        doneIdx.Should().BeGreaterThan(textIdx);
    }

    [Fact]
    public async Task RunStreamingAsync_AfterCancelledTurn_DoesNotReplayStaleToolEvents()
    {
        using var cts = new CancellationTokenSource();
        // Huỷ ngay trong lúc tool đang chạy: stream ném OCE; lượt sau không được còn event tool nào từ lượt huỷ.
        var erpService = new CancellingErpDataService(new InMemoryErpDataService(), () => cts.Cancel());
        var fakeClient = new FakeToolCallingChatClient("ok");
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        var act = async () =>
        {
            await foreach (var _ in service.RunStreamingAsync("Tồn kho SP-001?", null, cts.Token)) { }
        };
        await act.Should().ThrowAsync<OperationCanceledException>();

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Chào", null, CancellationToken.None))
        {
            events.Add(evt);
        }

        events.Should().NotContain(e => e is AgentToolExecutionEvent);
        events.Should().Contain(e => e is AgentTextChunkEvent && ((AgentTextChunkEvent)e).Text == "ok");
    }

    // Lần gọi 1 yield tool call get_stock; các lần sau yield finalText.
    private class FakeToolCallingChatClient : IChatClient
    {
        private readonly string _finalText;
        private int _calls = 0;

        public FakeToolCallingChatClient(string finalText)
        {
            _finalText = finalText;
        }

        public ChatClientMetadata Metadata => new("FakeToolCallingChatClient");
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _calls++;
            if (_calls == 1)
            {
                var update = new ChatResponseUpdate(ChatRole.Assistant, "");
                update.Contents.Add(new FunctionCallContent("c1", "get_stock", new Dictionary<string, object?>
                {
                    ["productCode"] = "SP-001"
                }));
                yield return update;
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, _finalText);
        }
    }

    // Decorator quanh InMemoryErpDataService: get_stock chạy onCall (test huỷ cts) rồi kiểm tra token.
    private class CancellingErpDataService : IErpDataService
    {
        private readonly IErpDataService _inner;
        private readonly Action _onCall;

        public CancellingErpDataService(IErpDataService inner, Action onCall)
        {
            _inner = inner;
            _onCall = onCall;
        }

        public Task<StockInfo> GetStockAsync(string productCode, CancellationToken ct = default)
        {
            _onCall();
            ct.ThrowIfCancellationRequested();
            return _inner.GetStockAsync(productCode, ct);
        }

        public Task<OrderStatusInfo> GetOrderStatusAsync(string orderCode, CancellationToken ct = default)
            => _inner.GetOrderStatusAsync(orderCode, ct);

        public Task<IReadOnlyList<StockInfo>> ListLowStockAsync(int threshold, CancellationToken ct = default)
            => _inner.ListLowStockAsync(threshold, ct);

        public Task<CustomerDebtInfo> GetCustomerDebtAsync(string customerCode, CancellationToken ct = default)
            => _inner.GetCustomerDebtAsync(customerCode, ct);

        public Task<PurchaseRequestResult> CreatePurchaseRequestAsync(string productCode, int quantity, CancellationToken ct = default)
            => _inner.CreatePurchaseRequestAsync(productCode, quantity, ct);
    }

    private static FunctionCallContent CreatePurchaseCall(string callId, int quantity) => new(callId, "create_purchase_request", new Dictionary<string, object?>
    {
        ["productCode"] = "SP-003",
        ["quantity"] = quantity
    });

    [Fact]
    public async Task Approval_AutoApprovedBelowThreshold_CreatesPurchaseRequestInErp()
    {
        var erpService = new CountingErpDataService(new InMemoryErpDataService());
        var fakeClient = new FakeApprovalFlowChatClient(CreatePurchaseCall("call_flow_a", 20));
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", AutoApproveQuantityThreshold = 50 });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        bool handlerCalled = false;
        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Tạo đề xuất mua 20 cái", req =>
        {
            handlerCalled = true;
            return Task.FromResult(true);
        }))
        {
            events.Add(evt);
        }

        handlerCalled.Should().BeFalse();
        erpService.Created.Should().HaveCount(1);
        erpService.Created[0].RequestId.Should().MatchRegex(@"^PR-\d{4}$");
        erpService.Created[0].Quantity.Should().Be(20);
        var prEvents = events.OfType<AgentToolExecutionEvent>().Where(e => e.ToolName == "create_purchase_request").ToList();
        prEvents.Should().ContainSingle();
        prEvents[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task Approval_DefaultOptions_AsksUserEvenForSmallQuantity()
    {
        // Mặc định mới (ngưỡng 0): mọi phiếu đều phải hỏi, kể cả 20 cái. Từ chối thì không tạo phiếu.
        var erpService = new CountingErpDataService(new InMemoryErpDataService());
        var fakeClient = new FakeApprovalFlowChatClient(CreatePurchaseCall("call_flow_default", 20));
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" });

        var service = new ErpAgentService(fakeClient, erpService, new UserContext("sales_user"), erpOptions);

        int handlerCallCount = 0;
        await foreach (var _ in service.RunStreamingAsync("Tạo đề xuất mua 20 cái", req =>
        {
            handlerCallCount++;
            return Task.FromResult(false);
        })) { }

        handlerCallCount.Should().Be(1);
        erpService.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_UserRejects_ModelIsToldTheUserRejected()
    {
        // Model 3B hay bịa lý do ("trưởng phòng không duyệt"); phải nói rõ do người dùng từ chối.
        var erpService = new CountingErpDataService(new InMemoryErpDataService());
        var fakeClient = new FakeApprovalFlowChatClient(CreatePurchaseCall("call_reject_reason", 5));
        var service = new ErpAgentService(fakeClient, erpService, new UserContext("sales_user"),
            Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" }));

        await foreach (var _ in service.RunStreamingAsync("Tạo đề xuất mua 5 cái", req => Task.FromResult(false))) { }

        var seen = string.Join(" | ", fakeClient.SecondCallMessages.SelectMany(m => m.Contents).Select(c => c switch
        {
            FunctionResultContent fr => $"result:{fr.Result}",
            ToolApprovalResponseContent ar => $"approval:{ar.Reason}",
            _ => ""
        }));
        seen.ToLowerInvariant().Should().Contain("người dùng đã tự từ chối", "model phải được báo lý do từ chối. Thấy: " + seen);
    }

    [Fact]
    public async Task Approval_UserRejects_DoesNotCreatePurchaseRequest()
    {
        var erpService = new CountingErpDataService(new InMemoryErpDataService());
        var fakeClient = new FakeApprovalFlowChatClient(CreatePurchaseCall("call_flow_b", 100));
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        int handlerCallCount = 0;
        await foreach (var _ in service.RunStreamingAsync("Tạo đề xuất mua 100 cái", req =>
        {
            handlerCallCount++;
            return Task.FromResult(false);
        })) { }

        handlerCallCount.Should().Be(1);
        erpService.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_TwoRequestsInOneTurn_SecondExceedsCumulativeThreshold_AsksUser()
    {
        var erpService = new CountingErpDataService(new InMemoryErpDataService());
        var fakeClient = new FakeApprovalFlowChatClient(CreatePurchaseCall("call_flow_c1", 30), CreatePurchaseCall("call_flow_c2", 25));
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", AutoApproveQuantityThreshold = 50 });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        var askedEvents = new List<AgentApprovalRequiredEvent>();
        await foreach (var _ in service.RunStreamingAsync("Tạo 2 đề xuất mua", req =>
        {
            askedEvents.Add(req);
            return Task.FromResult(true);
        })) { }

        askedEvents.Should().HaveCount(1);
        askedEvents[0].Arguments["quantity"]!.ToString().Should().Be("25");
        erpService.Created.Should().HaveCount(2);
    }

    [Fact]
    public async Task Approval_WhenToolThrows_EmitsSafeErrorEvent()
    {
        var erpService = new CountingErpDataService(new InMemoryErpDataService()) { ThrowOnCreate = true };
        var fakeClient = new FakeApprovalFlowChatClient(CreatePurchaseCall("call_flow_d", 20));
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", AutoApproveQuantityThreshold = 50 });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Tạo đề xuất mua 20 cái"))
        {
            events.Add(evt);
        }

        erpService.CreateCalls.Should().Be(1);
        erpService.Created.Should().BeEmpty();
        var prEvents = events.OfType<AgentToolExecutionEvent>().Where(e => e.ToolName == "create_purchase_request").ToList();
        prEvents.Should().ContainSingle();
        prEvents[0].Success.Should().BeFalse();
        prEvents[0].Error.Should().Be(RbacFunctionInvoker.ToolFailedMessage);
        events.OfType<AgentToolExecutionEvent>().Should().NotContain(e => e.Success && e.ToolName == "create_purchase_request");
        events.OfType<AgentToolExecutionEvent>().Should().NotContain(e => (e.Error ?? "").Contains("SELECT"));
        events.Should().Contain(e => e is AgentTurnCompletedEvent);
    }

    [Fact]
    public async Task ToolEvent_WhenToolThrowsWithoutApproval_EmitsSafeErrorEvent()
    {
        var erpService = new CountingErpDataService(new InMemoryErpDataService()) { ThrowOnGetStock = true };
        var fakeClient = new FakeApprovalFlowChatClient(new FunctionCallContent("call_s1", "get_stock", new Dictionary<string, object?> { ["productCode"] = "SP-001" }));
        var service = new ErpAgentService(fakeClient, erpService, new UserContext("sales_user"), Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" }));

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Tồn kho SP-001?"))
        {
            events.Add(evt);
        }

        var toolEvents = events.OfType<AgentToolExecutionEvent>().ToList();
        toolEvents.Should().ContainSingle();
        toolEvents[0].Success.Should().BeFalse();
        toolEvents[0].Error.Should().Be(RbacFunctionInvoker.ToolFailedMessage);
        toolEvents.Should().NotContain(e => (e.Error ?? "").Contains("SELECT") || e.ArgumentsSummary.Contains("SELECT"));
    }

    [Fact]
    public async Task ToolEvent_WhenUserRejectsApproval_EmitsNoToolExecutionEvent()
    {
        var erpService = new CountingErpDataService(new InMemoryErpDataService());
        var fakeClient = new FakeApprovalFlowChatClient(CreatePurchaseCall("call_rej", 100));
        var service = new ErpAgentService(fakeClient, erpService, new UserContext("sales_user"), Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" }));

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Tạo đề xuất mua 100 cái", req => Task.FromResult(false)))
        {
            events.Add(evt);
        }

        erpService.Created.Should().BeEmpty();
        events.OfType<AgentToolExecutionEvent>().Should().NotContain(e => e.ToolName == "create_purchase_request");
    }

    [Fact]
    public async Task ToolEvent_WhenInvokerDenies_EmitsFailedEventWithPermissionError()
    {
        var userContext = new UserContext("accountant_user");
        var fakeClient = new FakeApprovalFlowChatClient(new FunctionCallContent("call_deny", "get_customer_debt", new Dictionary<string, object?> { ["customerCode"] = "KH-001" }));
        var service = new ErpAgentService(fakeClient, new InMemoryErpDataService(), userContext, Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" }));

        // Không dựng lại agent: tool vẫn có mặt, chỉ người dùng đổi.
        userContext.SwitchUser("sales_user");

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Công nợ KH-001?"))
        {
            events.Add(evt);
        }

        var toolEvents = events.OfType<AgentToolExecutionEvent>().ToList();
        toolEvents.Should().ContainSingle();
        toolEvents[0].ToolName.Should().Be("get_customer_debt");
        toolEvents[0].Success.Should().BeFalse();
        toolEvents[0].Error.Should().Contain("Debt:Read");
    }

    [Fact]
    public async Task ToolEvent_TwoCallsInOneUpdate_EmitsTwoEventsInOrderWithMatchingArgs()
    {
        var fakeClient = new FakeApprovalFlowChatClient(
            new FunctionCallContent("call_m1", "get_stock", new Dictionary<string, object?> { ["productCode"] = "SP-001" }),
            new FunctionCallContent("call_m2", "get_stock", new Dictionary<string, object?> { ["productCode"] = "SP-002" }));
        var service = new ErpAgentService(fakeClient, new InMemoryErpDataService(), new UserContext("sales_user"), Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" }));

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Tồn kho SP-001 và SP-002?"))
        {
            events.Add(evt);
        }

        var toolEvents = events.OfType<AgentToolExecutionEvent>().ToList();
        toolEvents.Should().HaveCount(2);
        toolEvents.Select(e => e.ToolName).Should().Equal("get_stock", "get_stock");
        toolEvents.Select(e => e.ArgumentsSummary).Should().Equal("productCode=SP-001", "productCode=SP-002");
        toolEvents.Should().OnlyContain(e => e.Success);
    }

    private static FunctionCallContent StockCall(string callId) => new(callId, "get_stock", new Dictionary<string, object?>
    {
        ["productCode"] = "SP-001"
    });

    private static ErpAgentService CreateService(IChatClient client, IErpDataService erp, UserContext user, int? threshold = null)
        => new(client, erp, user, Options.Create(threshold is int t
            ? new ErpOptions { AssistantName = "Trợ lý ERP", AutoApproveQuantityThreshold = t }
            : new ErpOptions { AssistantName = "Trợ lý ERP" }));

    private static async Task<List<AgentResponseEvent>> RunAsync(ErpAgentService service, string prompt, Func<AgentApprovalRequiredEvent, Task<bool>>? handler = null)
    {
        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync(prompt, handler)) events.Add(evt);
        return events;
    }

    // Mỗi lần gọi model lấy nội dung từ turn(số thứ tự lần gọi, bắt đầu từ 1); ghi lại mọi request gửi lên model.
    private sealed class ScriptedTurnsChatClient(Func<int, AIContent[]> turn) : IChatClient
    {
        public int ModelCalls { get; private set; }
        public List<List<ChatMessage>> Requests { get; } = [];

        public ChatClientMetadata Metadata => new("ScriptedTurnsChatClient");
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ModelCalls++;
            Requests.Add(chatMessages.ToList());
            var contents = turn(ModelCalls);

            var nonText = contents.Where(c => c is not TextContent).ToList();
            if (nonText.Count > 0)
            {
                var update = new ChatResponseUpdate(ChatRole.Assistant, "");
                foreach (var c in nonText) update.Contents.Add(c);
                yield return update;
            }

            foreach (var text in contents.OfType<TextContent>())
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, text.Text);
            }
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TC09_ToolThrows_ModelDoesNotReceiveSqlInFunctionResult()
    {
        var erp = new CountingErpDataService(new InMemoryErpDataService()) { ThrowOnGetStock = true };
        var client = new ScriptedTurnsChatClient(n => n == 1 ? [StockCall("c1")] : [new TextContent("Xin lỗi")]);
        var service = CreateService(client, erp, new UserContext("sales_user"));

        await RunAsync(service, "Tồn kho SP-001?");

        client.Requests.Should().HaveCountGreaterThanOrEqualTo(2);
        var result = client.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single(r => r.CallId == "c1");
        result.Result?.ToString().Should().NotBeNullOrEmpty().And.NotContain("SELECT");
    }

    [Fact]
    public async Task TC26_ModelKeepsCallingTools_StopsWithinFiveIterations()
    {
        var erp = new CountingErpDataService(new InMemoryErpDataService());
        var client = new ScriptedTurnsChatClient(n => [StockCall($"loop{n}")]);
        var service = CreateService(client, erp, new UserContext("sales_user"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await foreach (var _ in service.RunStreamingAsync("Tồn kho SP-001?", null, cts.Token)) { }
        }
        catch (Exception)
        {
            // Framework được phép kết thúc bình thường hoặc ném khi chạm giới hạn; chỉ kiểm số lần gọi.
        }

        cts.IsCancellationRequested.Should().BeFalse("vòng lặp tool phải tự dừng, không bị treo");
        // Số đo thực tế: 5 lần gọi tool, 6 lần gọi model (giới hạn 5 vòng có hiệu lực).
        erp.GetStockCalls.Should().BeInRange(1, 5);
        client.ModelCalls.Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public async Task TC27_ToolFailsRepeatedly_StopsAfterTwoConsecutiveErrorsWithSafeEvents()
    {
        var erp = new CountingErpDataService(new InMemoryErpDataService()) { ThrowOnGetStock = true };
        var client = new ScriptedTurnsChatClient(n => [StockCall($"err{n}")]);
        var service = CreateService(client, erp, new UserContext("sales_user"));

        var events = new List<AgentResponseEvent>();
        Exception? thrown = null;
        try
        {
            await foreach (var evt in service.RunStreamingAsync("Tồn kho SP-001?")) events.Add(evt);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        thrown.Should().BeOfType<InvalidOperationException>();
        // Số đo thực tế: 3 lần gọi tool (2 lỗi liên tiếp được chịu, lần 3 làm framework ném).
        erp.GetStockCalls.Should().Be(3);
        var toolEvents = events.OfType<AgentToolExecutionEvent>().ToList();
        toolEvents.Should().OnlyContain(e => e.Error == RbacFunctionInvoker.ToolFailedMessage && !e.Error.Contains("SELECT"));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(100)]
    public async Task TC13_ApprovalStep_UserLacksPrPermission_RejectsWithoutAskingAndErpNotCalled(int qty)
    {
        var userContext = new UserContext("sales_user");
        var erp = new CountingErpDataService(new InMemoryErpDataService());
        var service = CreateService(new FakeApprovalFlowChatClient(CreatePurchaseCall("call_np", qty)), erp, userContext);
        // Đổi user mà không reset: tool ghi vẫn còn trong agent, chỉ bước duyệt của service chặn được.
        userContext.SwitchUser("accountant_user");

        int handlerCalls = 0;
        var events = await RunAsync(service, "Tạo đề xuất mua", _ => { handlerCalls++; return Task.FromResult(true); });

        handlerCalls.Should().Be(0);
        events.Should().NotContain(e => e is AgentApprovalRequiredEvent);
        var prEvents = events.OfType<AgentToolExecutionEvent>().Where(e => e.ToolName == "create_purchase_request").ToList();
        prEvents.Should().ContainSingle();
        prEvents[0].Success.Should().BeFalse();
        prEvents[0].Error.Should().StartWith("Từ chối: Tài khoản 'accountant_user'");
        erp.CreateCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(true, -10)]
    [InlineData(true, 0)]
    [InlineData(false, null)]
    [InlineData(true, "abc")]
    [InlineData(true, 20.5)]
    public async Task TC14_ApprovalStep_InvalidQuantity_RejectsWithoutAskingAndErpNotCalled(bool hasQuantity, object? quantity)
    {
        var args = new Dictionary<string, object?> { ["productCode"] = "SP-003" };
        if (hasQuantity) args["quantity"] = quantity;
        var erp = new CountingErpDataService(new InMemoryErpDataService());
        var client = new FakeApprovalFlowChatClient(new FunctionCallContent("call_bad", "create_purchase_request", args));
        var service = CreateService(client, erp, new UserContext("sales_user"));

        int handlerCalls = 0;
        var events = await RunAsync(service, "Tạo đề xuất mua", _ => { handlerCalls++; return Task.FromResult(true); });

        handlerCalls.Should().Be(0);
        events.Should().NotContain(e => e is AgentApprovalRequiredEvent);
        var prEvents = events.OfType<AgentToolExecutionEvent>().Where(e => e.ToolName == "create_purchase_request").ToList();
        prEvents.Should().ContainSingle();
        prEvents[0].Success.Should().BeFalse();
        prEvents[0].Error.Should().Contain("nguyên dương");
        erp.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task TC19_ApprovalHandlerNull_RejectsAndDoesNotCreatePr()
    {
        var erp = new CountingErpDataService(new InMemoryErpDataService());
        var service = CreateService(new FakeApprovalFlowChatClient(CreatePurchaseCall("call_null", 80)), erp, new UserContext("sales_user"));

        var events = await RunAsync(service, "Tạo đề xuất 80 cái", handler: null);

        events.Should().Contain(e => e is AgentApprovalRequiredEvent);
        erp.CreateCalls.Should().Be(0);
        events.OfType<AgentToolExecutionEvent>().Should().NotContain(e => e.ToolName == "create_purchase_request" && e.Success);
    }

    // Mặc định ngưỡng = 0 (luôn hỏi), nên phải kiểm cả hai phía của ngưỡng cấu hình 10:
    // qty 5 tự duyệt (chứng minh service đọc cấu hình, không dùng mặc định), qty 20 vẫn hỏi.
    [Theory]
    [InlineData(5, false)]
    [InlineData(20, true)]
    public async Task TC16_ConfiguredThreshold_IsUsedByService(int qty, bool expectAsked)
    {
        var erp = new CountingErpDataService(new InMemoryErpDataService());
        var service = CreateService(new FakeApprovalFlowChatClient(CreatePurchaseCall("call_thr", qty)), erp, new UserContext("sales_user"), threshold: 10);

        int handlerCalls = 0;
        await RunAsync(service, "Tạo đề xuất mua", _ => { handlerCalls++; return Task.FromResult(false); });

        handlerCalls.Should().Be(expectAsked ? 1 : 0);
        if (expectAsked) erp.Created.Should().BeEmpty();
        else erp.Created.Should().ContainSingle().Which.Quantity.Should().Be(qty);
    }

    [Fact]
    public async Task TC21_CumulativeQuantity_SpansApprovalRounds_SecondRoundAsks()
    {
        var erp = new CountingErpDataService(new InMemoryErpDataService());
        var client = new ScriptedTurnsChatClient(n => n switch
        {
            1 => [CreatePurchaseCall("r1", 30)],
            2 => [CreatePurchaseCall("r2", 25)],
            _ => [new TextContent("Xong")]
        });
        var service = CreateService(client, erp, new UserContext("sales_user"), threshold: 50);

        var asked = new List<AgentApprovalRequiredEvent>();
        var events = await RunAsync(service, "Mua 2 lần", req => { asked.Add(req); return Task.FromResult(true); });

        client.ModelCalls.Should().BeGreaterThanOrEqualTo(2);
        asked.Should().ContainSingle();
        asked[0].Arguments["quantity"]!.ToString().Should().Be("25");
        erp.Created.Should().HaveCount(2);
        events.OfType<AgentToolExecutionEvent>().Where(e => e.ToolName == "create_purchase_request" && e.Success).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TC22_CumulativeQuantity_CountsOnlyApproved(bool approveFirst)
    {
        var erp = new CountingErpDataService(new InMemoryErpDataService());
        var client = new FakeApprovalFlowChatClient(CreatePurchaseCall("big", 60), CreatePurchaseCall("small", 30));
        var service = CreateService(client, erp, new UserContext("sales_user"), threshold: 50);

        var askedQty = new List<string>();
        await RunAsync(service, "Mua 60 rồi 30", req =>
        {
            var qty = req.Arguments["quantity"]!.ToString()!;
            askedQty.Add(qty);
            return Task.FromResult(qty == "60" ? approveFirst : true);
        });

        if (approveFirst)
        {
            askedQty.Should().Equal("60", "30");
            erp.Created.Should().HaveCount(2);
        }
        else
        {
            askedQty.Should().Equal("60");
            erp.Created.Should().ContainSingle().Which.Quantity.Should().Be(30);
        }
    }

    [Fact]
    public async Task TC23_CumulativeQuantity_ResetsEachTurn()
    {
        var erp = new CountingErpDataService(new InMemoryErpDataService());
        var client = new ScriptedTurnsChatClient(n => n switch
        {
            1 => [CreatePurchaseCall("t1", 40)],
            3 => [CreatePurchaseCall("t2", 40)],
            _ => [new TextContent("Xong")]
        });
        var service = CreateService(client, erp, new UserContext("sales_user"), threshold: 50);

        int handlerCalls = 0;
        Task<bool> handler(AgentApprovalRequiredEvent _) { handlerCalls++; return Task.FromResult(false); }
        await RunAsync(service, "Mua 40 SP-003", handler);
        await RunAsync(service, "Mua thêm 40 SP-003", handler);

        client.ModelCalls.Should().Be(4);
        handlerCalls.Should().Be(0);
        erp.Created.Should().HaveCount(2);
    }

    [Fact]
    public async Task TC24_QuantityAsJsonElement_AutoApprovesLikeRealOllama()
    {
        var args = new Dictionary<string, object?>
        {
            ["productCode"] = "SP-003",
            ["quantity"] = JsonSerializer.SerializeToElement(20)
        };
        var erp = new CountingErpDataService(new InMemoryErpDataService());
        var client = new FakeApprovalFlowChatClient(new FunctionCallContent("call_json", "create_purchase_request", args));
        var service = CreateService(client, erp, new UserContext("sales_user"), threshold: 50);

        int handlerCalls = 0;
        await RunAsync(service, "Mua 20 SP-003", _ => { handlerCalls++; return Task.FromResult(false); });

        handlerCalls.Should().Be(0);
        erp.Created.Should().ContainSingle().Which.Quantity.Should().Be(20);
    }

    // Lần gọi 1 yield các FunctionCallContent đã cấu hình (tool ghi bị ApprovalRequiredAIFunction đổi thành yêu cầu duyệt); các lần sau yield "Đã xử lý".
    private class FakeApprovalFlowChatClient : IChatClient
    {
        private readonly List<FunctionCallContent> _calls;
        private int _step = 0;

        // Tin nhắn model nhận ở lần gọi thứ hai (sau bước duyệt).
        public List<ChatMessage> SecondCallMessages { get; } = [];

        public FakeApprovalFlowChatClient(params FunctionCallContent[] calls)
        {
            _calls = [.. calls];
        }

        public ChatClientMetadata Metadata => new("FakeApprovalFlowChatClient");
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
                foreach (var call in _calls)
                {
                    update.Contents.Add(call);
                }
                yield return update;
            }
            else
            {
                SecondCallMessages.AddRange(chatMessages);
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Đã xử lý");
            }
        }
    }

    // Decorator quanh InMemoryErpDataService: đếm lần gọi tạo PR, lưu PR đã tạo; ThrowOnCreate mô phỏng lỗi chứa SQL.
    private class CountingErpDataService : IErpDataService
    {
        private readonly IErpDataService _inner;

        public CountingErpDataService(IErpDataService inner)
        {
            _inner = inner;
        }

        public int CreateCalls { get; private set; }
        public int GetStockCalls { get; private set; }
        public bool ThrowOnCreate { get; init; }
        public bool ThrowOnGetStock { get; init; }
        public List<PurchaseRequestResult> Created { get; } = [];

        public Task<StockInfo> GetStockAsync(string productCode, CancellationToken ct = default)
        {
            GetStockCalls++;
            return ThrowOnGetStock ? throw new InvalidOperationException("SELECT * FROM Items") : _inner.GetStockAsync(productCode, ct);
        }

        public Task<OrderStatusInfo> GetOrderStatusAsync(string orderCode, CancellationToken ct = default)
            => _inner.GetOrderStatusAsync(orderCode, ct);

        public Task<IReadOnlyList<StockInfo>> ListLowStockAsync(int threshold, CancellationToken ct = default)
            => _inner.ListLowStockAsync(threshold, ct);

        public Task<CustomerDebtInfo> GetCustomerDebtAsync(string customerCode, CancellationToken ct = default)
            => _inner.GetCustomerDebtAsync(customerCode, ct);

        public async Task<PurchaseRequestResult> CreatePurchaseRequestAsync(string productCode, int quantity, CancellationToken ct = default)
        {
            CreateCalls++;
            if (ThrowOnCreate)
            {
                throw new InvalidOperationException("SELECT * FROM Items");
            }

            var result = await _inner.CreatePurchaseRequestAsync(productCode, quantity, ct);
            Created.Add(result);
            return result;
        }
    }
}
