using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Security;
using ErpChatbot.Agent.Tools;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using ErpChatbot.Infrastructure.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FluentAssertions;
using Xunit.Abstractions;

namespace ErpChatbot.Tests.Agent;

// SPIKE (Ngày 2 → 4): quan sát chính xác nội dung stream của agent khi tool chạy.
// Agent được dựng giống hệt ErpAgentService.BuildAgent. Không dùng để kiểm hành vi production.
public class ToolEventsFromStreamSpikeTests
{
    private readonly ITestOutputHelper _output;

    public ToolEventsFromStreamSpikeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed record Observed(int Update, AIContent Content);

    private sealed record RunResult(List<Observed> Items, Exception? Thrown, int ModelCalls);

    // Mỗi lần GetStreamingResponseAsync lấy một "lượt" kịch bản; mỗi content là một update riêng,
    // trừ khi OneUpdatePerTurn = true thì mọi content của lượt nằm trong MỘT update.
    private sealed class ScriptedChatClient : IChatClient
    {
        private readonly Queue<AIContent[]> _turns;

        public ScriptedChatClient(params AIContent[][] turns)
        {
            _turns = new Queue<AIContent[]>(turns);
        }

        public bool OneUpdatePerTurn { get; init; }
        public int ModelCalls { get; private set; }

        public ChatClientMetadata Metadata => new("ScriptedChatClient");
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
            AIContent[] turn = _turns.Count > 0 ? _turns.Dequeue() : [new TextContent("(hết kịch bản)")];

            if (OneUpdatePerTurn)
            {
                var one = new ChatResponseUpdate(ChatRole.Assistant, "");
                foreach (var c in turn)
                {
                    one.Contents.Add(c);
                }
                yield return one;
                yield break;
            }

            foreach (var content in turn)
            {
                if (content is TextContent text)
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, text.Text);
                    continue;
                }

                var update = new ChatResponseUpdate(ChatRole.Assistant, "");
                update.Contents.Add(content);
                yield return update;
            }
        }
    }

    // Decorator: get_stock ném exception chứa SQL (mô phỏng lỗi ERP thật), các hàm khác đi qua InMemory.
    private sealed class ThrowingErpDataService : IErpDataService
    {
        private readonly IErpDataService _inner = new InMemoryErpDataService();

        public Task<StockInfo> GetStockAsync(string productCode, CancellationToken ct = default)
            => throw new InvalidOperationException("SELECT * FROM Items");

        public Task<OrderStatusInfo> GetOrderStatusAsync(string orderCode, CancellationToken ct = default)
            => _inner.GetOrderStatusAsync(orderCode, ct);

        public Task<IReadOnlyList<StockInfo>> ListLowStockAsync(int threshold, CancellationToken ct = default)
            => _inner.ListLowStockAsync(threshold, ct);

        public Task<CustomerDebtInfo> GetCustomerDebtAsync(string customerCode, CancellationToken ct = default)
            => _inner.GetCustomerDebtAsync(customerCode, ct);

        public Task<PurchaseRequestResult> CreatePurchaseRequestAsync(string productCode, int quantity, CancellationToken ct = default)
            => _inner.CreatePurchaseRequestAsync(productCode, quantity, ct);
    }

    // Decorator đếm số lần ERP thật sự bị gọi tạo PR (dùng để biết tool có chạy hay không).
    private sealed class CountingErpSpike : IErpDataService
    {
        private readonly IErpDataService _inner = new InMemoryErpDataService();

        public int CreateCalls { get; private set; }

        public Task<StockInfo> GetStockAsync(string productCode, CancellationToken ct = default)
            => _inner.GetStockAsync(productCode, ct);

        public Task<OrderStatusInfo> GetOrderStatusAsync(string orderCode, CancellationToken ct = default)
            => _inner.GetOrderStatusAsync(orderCode, ct);

        public Task<IReadOnlyList<StockInfo>> ListLowStockAsync(int threshold, CancellationToken ct = default)
            => _inner.ListLowStockAsync(threshold, ct);

        public Task<CustomerDebtInfo> GetCustomerDebtAsync(string customerCode, CancellationToken ct = default)
            => _inner.GetCustomerDebtAsync(customerCode, ct);

        public Task<PurchaseRequestResult> CreatePurchaseRequestAsync(string productCode, int quantity, CancellationToken ct = default)
        {
            CreateCalls++;
            return _inner.CreatePurchaseRequestAsync(productCode, quantity, ct);
        }
    }

    // Dựng giống hệt ErpAgentService.BuildAgent (không dùng callback event: null).
    private static AIAgent BuildAgentLikeService(IChatClient baseClient, IErpDataService erpService, IUserContext userContext)
    {
        var tools = ErpTools.GetTools(erpService, userContext);
        var invoker = RbacFunctionInvoker.CreateInvoker(userContext, null);

        var clientWithFunctions = new ChatClientBuilder(baseClient)
            .UseFunctionInvocation(configure: f =>
            {
                f.MaximumIterationsPerRequest = 5;
                f.MaximumConsecutiveErrorsPerRequest = 2;
                f.IncludeDetailedErrors = false;
                f.AllowConcurrentInvocation = false;
                f.FunctionInvoker = invoker;
            })
            .Build();

        return clientWithFunctions.AsAIAgent(instructions: "x", name: "x", tools: tools);
    }

    private async Task<RunResult> RunAsync(AIAgent agent, ScriptedChatClient client, string prompt)
    {
        var session = await agent.CreateSessionAsync();
        return await CollectAsync(agent.RunStreamingAsync(prompt, session, options: null, cancellationToken: CancellationToken.None), client, prompt);
    }

    private async Task<RunResult> CollectAsync(IAsyncEnumerable<AgentResponseUpdate> stream, ScriptedChatClient client, string label)
    {
        _output.WriteLine($"===== {label} =====");
        var items = new List<Observed>();
        Exception? thrown = null;
        int updateIndex = 0;

        try
        {
            await foreach (var update in stream)
            {
                foreach (var content in update.Contents)
                {
                    items.Add(new Observed(updateIndex, content));
                    _output.WriteLine(Describe(updateIndex, content));
                }
                if (!string.IsNullOrEmpty(update.Text))
                {
                    _output.WriteLine($"upd#{updateIndex} Update.Text='{update.Text}'");
                }
                updateIndex++;
            }
        }
        catch (Exception ex)
        {
            thrown = ex;
            _output.WriteLine($"THROWN out of RunStreamingAsync: {ex.GetType().FullName}: {ex.Message}");
        }

        return new RunResult(items, thrown, client.ModelCalls);
    }

    private static string Describe(int u, AIContent c) => c switch
    {
        FunctionCallContent fc => $"upd#{u} FunctionCallContent name={fc.Name} callId={fc.CallId} args=[{string.Join(", ", (fc.Arguments ?? new Dictionary<string, object?>()).Select(kv => $"{kv.Key}={kv.Value}"))}]",
        FunctionResultContent fr => $"upd#{u} FunctionResultContent callId={fr.CallId} resultType={fr.Result?.GetType().FullName ?? "null"} result={fr.Result} exType={fr.Exception?.GetType().FullName ?? "null"} exMsg={fr.Exception?.Message}",
        ToolApprovalRequestContent ar => $"upd#{u} ToolApprovalRequestContent requestId={ar.RequestId} toolCallType={ar.ToolCall?.GetType().Name} toolName={(ar.ToolCall as FunctionCallContent)?.Name}",
        TextContent tc => $"upd#{u} TextContent '{tc.Text}'",
        UsageContent => $"upd#{u} UsageContent",
        _ => $"upd#{u} {c.GetType().FullName}"
    };

    [Fact]
    public async Task Case1_SuccessfulTool_StreamHasCallThenResultThenText()
    {
        var client = new ScriptedChatClient(
            [new FunctionCallContent("c1", "get_stock", new Dictionary<string, object?> { ["productCode"] = "SP-001" })],
            [new TextContent("Còn 124 cái")]);
        var agent = BuildAgentLikeService(client, new InMemoryErpDataService(), new UserContext("sales_user"));

        var run = await RunAsync(agent, client, "Tồn kho SP-001?");

        run.Thrown.Should().BeNull();
        var call = run.Items.Select(i => i.Content).OfType<FunctionCallContent>().Should().ContainSingle().Subject;
        call.CallId.Should().Be("c1");
        var result = run.Items.Select(i => i.Content).OfType<FunctionResultContent>().Should().ContainSingle().Subject;
        result.CallId.Should().Be("c1");
        result.Exception.Should().BeNull();
        // Thực tế: AIFunctionFactory đã serialize kết quả thành JsonElement, không còn là StockInfo.
        result.Result.Should().BeOfType<System.Text.Json.JsonElement>();
        ((System.Text.Json.JsonElement)result.Result!).GetProperty("quantity").GetInt32().Should().Be(124);

        int callIdx = run.Items.FindIndex(i => i.Content is FunctionCallContent);
        int resultIdx = run.Items.FindIndex(i => i.Content is FunctionResultContent);
        int textIdx = run.Items.FindIndex(i => i.Content is TextContent t && t.Text.Contains("124"));
        callIdx.Should().BeGreaterThanOrEqualTo(0);
        resultIdx.Should().BeGreaterThan(callIdx);
        textIdx.Should().BeGreaterThan(resultIdx);
    }

    [Fact]
    public async Task Case2_ToolThrows_ResultCarriesExceptionAndRawMessage()
    {
        var client = new ScriptedChatClient(
            [new FunctionCallContent("c2", "get_stock", new Dictionary<string, object?> { ["productCode"] = "SP-001" })],
            [new TextContent("Có lỗi")]);
        var agent = BuildAgentLikeService(client, new ThrowingErpDataService(), new UserContext("sales_user"));

        var run = await RunAsync(agent, client, "Tồn kho SP-001?");

        run.Thrown.Should().BeNull();
        var result = run.Items.Select(i => i.Content).OfType<FunctionResultContent>().Should().ContainSingle().Subject;
        result.CallId.Should().Be("c2");
        result.Exception.Should().NotBeNull();
        result.Exception.Should().BeOfType<InvalidOperationException>();
        result.Exception!.Message.Should().Contain("SELECT");
        result.Result.Should().BeOfType<string>();
        result.Result!.ToString().Should().NotContain("SELECT");
    }

    [Fact]
    public async Task Case3_InvokerDenied_ResultIsToolSecurityError()
    {
        var userContext = new UserContext("accountant_user");
        var client = new ScriptedChatClient(
            [new FunctionCallContent("c3", "get_customer_debt", new Dictionary<string, object?> { ["customerCode"] = "KH-001" })],
            [new TextContent("Không có quyền")]);
        var agent = BuildAgentLikeService(client, new InMemoryErpDataService(), userContext);

        // Không dựng lại agent: tool get_customer_debt vẫn có mặt, chỉ người dùng đổi.
        userContext.SwitchUser("sales_user");

        var run = await RunAsync(agent, client, "Công nợ KH-001?");

        run.Thrown.Should().BeNull();
        var result = run.Items.Select(i => i.Content).OfType<FunctionResultContent>().Should().ContainSingle().Subject;
        result.CallId.Should().Be("c3");
        result.Exception.Should().BeNull();
        result.Result.Should().BeOfType<ToolSecurityError>();
        ((ToolSecurityError)result.Result!).Error.Should().Be("PermissionDenied");
    }

    [Fact]
    public async Task Case4_WriteTool_StreamHasApprovalRequestBeforeAnyResult()
    {
        var client = new ScriptedChatClient(
            [new FunctionCallContent("c4", "create_purchase_request", new Dictionary<string, object?> { ["productCode"] = "SP-003", ["quantity"] = 20 })],
            [new TextContent("Đã xử lý")]);
        var agent = BuildAgentLikeService(client, new InMemoryErpDataService(), new UserContext("sales_user"));

        var run = await RunAsync(agent, client, "Tạo đề xuất mua 20 cái");

        run.Thrown.Should().BeNull();
        var approval = run.Items.Select(i => i.Content).OfType<ToolApprovalRequestContent>().Should().ContainSingle().Subject;
        ((FunctionCallContent)approval.ToolCall).Name.Should().Be("create_purchase_request");
        run.Items.Select(i => i.Content).OfType<FunctionResultContent>().Should().BeEmpty();
        run.ModelCalls.Should().Be(1);
    }

    [Fact]
    public async Task Case5_Approved_SecondStreamHasResultAndErpCreatesPr()
    {
        var erp = new CountingErpSpike();
        var client = new ScriptedChatClient(
            [new FunctionCallContent("c5", "create_purchase_request", new Dictionary<string, object?> { ["productCode"] = "SP-003", ["quantity"] = 20 })],
            [new TextContent("Xong")]);
        var agent = BuildAgentLikeService(client, erp, new UserContext("sales_user"));
        var session = await agent.CreateSessionAsync();

        var first = await CollectAsync(agent.RunStreamingAsync("Tạo đề xuất mua 20 cái", session, options: null, cancellationToken: CancellationToken.None), client, "CASE5 luot 1");
        var req = first.Items.Select(i => i.Content).OfType<ToolApprovalRequestContent>().Should().ContainSingle().Subject;

        var second = await CollectAsync(agent.RunStreamingAsync(new ChatMessage(ChatRole.User, [req.CreateResponse(true)]), session, options: null, cancellationToken: CancellationToken.None), client, "CASE5 luot 2 (duyet)");

        _output.WriteLine($"ERP CreateCalls={erp.CreateCalls} ModelCalls={client.ModelCalls}");
        second.Thrown.Should().BeNull();
        erp.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Case6_Rejected_SecondStreamResultAndErpNotCalled()
    {
        var erp = new CountingErpSpike();
        var client = new ScriptedChatClient(
            [new FunctionCallContent("c6", "create_purchase_request", new Dictionary<string, object?> { ["productCode"] = "SP-003", ["quantity"] = 20 })],
            [new TextContent("Đã hủy")]);
        var agent = BuildAgentLikeService(client, erp, new UserContext("sales_user"));
        var session = await agent.CreateSessionAsync();

        var first = await CollectAsync(agent.RunStreamingAsync("Tạo đề xuất mua 20 cái", session, options: null, cancellationToken: CancellationToken.None), client, "CASE6 luot 1");
        var req = first.Items.Select(i => i.Content).OfType<ToolApprovalRequestContent>().Should().ContainSingle().Subject;

        var second = await CollectAsync(agent.RunStreamingAsync(new ChatMessage(ChatRole.User, [req.CreateResponse(false)]), session, options: null, cancellationToken: CancellationToken.None), client, "CASE6 luot 2 (tu choi)");

        _output.WriteLine($"ERP CreateCalls={erp.CreateCalls} ModelCalls={client.ModelCalls}");
        second.Thrown.Should().BeNull();
        erp.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Case7_TwoCallsInOneUpdate_ResultsAndCallIdsObserved()
    {
        var client = new ScriptedChatClient(
            [
                new FunctionCallContent("c7a", "get_stock", new Dictionary<string, object?> { ["productCode"] = "SP-001" }),
                new FunctionCallContent("c7b", "get_stock", new Dictionary<string, object?> { ["productCode"] = "SP-002" }),
            ],
            [new TextContent("Xong hai mã")])
        { OneUpdatePerTurn = true };
        var agent = BuildAgentLikeService(client, new InMemoryErpDataService(), new UserContext("sales_user"));

        var run = await RunAsync(agent, client, "Tồn kho SP-001 và SP-002?");

        _output.WriteLine($"ModelCalls={run.ModelCalls}");
        run.Thrown.Should().BeNull();
        run.Items.Select(i => i.Content).OfType<FunctionCallContent>().Select(c => c.CallId).Should().Equal("c7a", "c7b");
        run.Items.Select(i => i.Content).OfType<FunctionResultContent>().Select(r => r.CallId).Should().Equal("c7a", "c7b");
    }

    [Fact]
    public async Task Case8_TwoWriteCallsInOneResponse_TwoApprovalsThenTwoResults()
    {
        var erp = new CountingErpSpike();
        var client = new ScriptedChatClient(
            [
                new FunctionCallContent("c8a", "create_purchase_request", new Dictionary<string, object?> { ["productCode"] = "SP-001", ["quantity"] = 30 }),
                new FunctionCallContent("c8b", "create_purchase_request", new Dictionary<string, object?> { ["productCode"] = "SP-002", ["quantity"] = 25 }),
            ],
            [new TextContent("Xong hai đề xuất")])
        { OneUpdatePerTurn = true };
        var agent = BuildAgentLikeService(client, erp, new UserContext("sales_user"));
        var session = await agent.CreateSessionAsync();

        var first = await CollectAsync(agent.RunStreamingAsync("Mua 30 SP-001 và 25 SP-002", session, options: null, cancellationToken: CancellationToken.None), client, "CASE8 luot 1");
        var reqs = first.Items.Select(i => i.Content).OfType<ToolApprovalRequestContent>().ToList();
        _output.WriteLine($"approval count={reqs.Count}");
        reqs.Should().HaveCount(2);

        var responses = reqs.Select(r => (AIContent)r.CreateResponse(true)).ToList();
        var second = await CollectAsync(agent.RunStreamingAsync(new ChatMessage(ChatRole.User, responses), session, options: null, cancellationToken: CancellationToken.None), client, "CASE8 luot 2 (duyet ca hai)");

        _output.WriteLine($"ERP CreateCalls={erp.CreateCalls} ModelCalls={client.ModelCalls}");
        second.Thrown.Should().BeNull();
        second.Items.Select(i => i.Content).OfType<FunctionResultContent>().Select(r => r.CallId).Should().BeEquivalentTo("c8a", "c8b");
        erp.CreateCalls.Should().Be(2);
    }
}
