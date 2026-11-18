using System.Diagnostics;
using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Security;
using ErpChatbot.Agent.Tools;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ErpChatbot.Agent.Services;

public class ErpAgentService : IErpAgentService
{
    private readonly IChatClient _baseChatClient;
    private readonly IErpDataService _erpDataService;
    private readonly IUserContext _userContext;
    private readonly ErpOptions _options;
    private readonly ILogger<ErpAgentService>? _logger;
    private readonly SessionStats _stats = new();

    private AIAgent _agent;
    private AgentSession? _session;

    public ErpAgentService(
        IChatClient chatClient,
        IErpDataService erpDataService,
        IUserContext userContext,
        IOptions<ErpOptions> erpOptions,
        ILogger<ErpAgentService>? logger = null)
    {
        _baseChatClient = chatClient;
        _erpDataService = erpDataService;
        _userContext = userContext;
        _options = erpOptions.Value;
        _logger = logger;

        _agent = BuildAgent();
    }

    private AIAgent BuildAgent()
    {
        var tools = ErpTools.GetTools(_erpDataService, _userContext);
        var invoker = RbacFunctionInvoker.CreateInvoker(_userContext, _logger);

        var clientWithFunctions = new ChatClientBuilder(_baseChatClient)
            .UseFunctionInvocation(configure: f =>
            {
                f.MaximumIterationsPerRequest = 5;          // Chặn vòng lặp vô hạn theo chuẩn Ex3
                f.MaximumConsecutiveErrorsPerRequest = 2;   // Ngắt nếu lỗi 2 lần liên tiếp
                f.IncludeDetailedErrors = false;            // Không lộ SQL/stack trace cho LLM
                f.AllowConcurrentInvocation = false;        // Thực thi tuần tự
                f.FunctionInvoker = invoker;
            })
            .Build();

        return clientWithFunctions.AsAIAgent(
            instructions: _options.Instructions,
            name: _options.AssistantName,
            tools: tools
        );
    }

    public static bool EvaluateRequiresApproval(string toolName, IReadOnlyDictionary<string, object?>? arguments, int cumulativeTurnQty = 0, int threshold = ErpOptions.DefaultAutoApproveQuantityThreshold)
    {
        if (toolName.Equals("create_purchase_request", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments != null && arguments.TryGetValue("quantity", out var qtyObj))
            {
                if (int.TryParse(qtyObj?.ToString(), out int qty))
                {
                    if (qty <= 0) return true; // Số lượng không hợp lệ không được auto-duyệt
                    return (cumulativeTurnQty + qty) > threshold;
                }
            }
        }
        return true;
    }

    public async IAsyncEnumerable<AgentResponseEvent> RunStreamingAsync(
        string userPrompt,
        Func<AgentApprovalRequiredEvent, Task<bool>>? approvalHandler = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            yield break;
        }

        _session ??= await _agent.CreateSessionAsync(cancellationToken);

        long turnInputTokens = 0;
        long turnOutputTokens = 0;
        int turnCumulativePrQuantity = 0;
        var turnSw = Stopwatch.StartNew();

        object currentInput = userPrompt;

        // Sống suốt cả lượt (kể cả các vòng duyệt): call ở stream này, result có thể tới ở stream sau.
        var calls = new Dictionary<string, (string Name, string Args, long StartTs)>();
        // Call bị từ chối ở bước duyệt: framework vẫn phát FunctionResultContent "rejected", không được báo là thực thi.
        var skipResultCallIds = new HashSet<string>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var responseStream = currentInput switch
            {
                string text => _agent.RunStreamingAsync(text, _session, options: null, cancellationToken: cancellationToken),
                ChatMessage chatMsg => _agent.RunStreamingAsync([chatMsg], _session, options: null, cancellationToken: cancellationToken),
                _ => throw new InvalidOperationException($"Input type không được hỗ trợ: {currentInput.GetType()}")
            };

            List<ToolApprovalRequestContent> pendingApprovalRequests = [];

            await foreach (var update in responseStream.WithCancellation(cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return new AgentTextChunkEvent(update.Text);
                }

                if (update.Contents != null)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is UsageContent usageContent && usageContent.Details != null)
                        {
                            turnInputTokens += usageContent.Details.InputTokenCount ?? 0;
                            turnOutputTokens += usageContent.Details.OutputTokenCount ?? 0;
                        }
                        else if (content is ToolApprovalRequestContent approvalReq)
                        {
                            pendingApprovalRequests.Add(approvalReq);
                        }
                        else if (content is FunctionCallContent fc)
                        {
                            calls[fc.CallId] = (fc.Name, SummarizeArgs(fc.Arguments), Stopwatch.GetTimestamp());
                        }
                        else if (content is FunctionResultContent fr && !skipResultCallIds.Remove(fr.CallId))
                        {
                            var hasCall = calls.TryGetValue(fr.CallId, out var call);
                            // Tuyệt đối không dùng fr.Exception.Message / chuỗi Result của lỗi: có thể chứa SQL.
                            yield return new AgentToolExecutionEvent(
                                hasCall ? call.Name : "unknown_tool",
                                hasCall ? call.Args : "",
                                hasCall ? (long)Stopwatch.GetElapsedTime(call.StartTs).TotalMilliseconds : 0,
                                fr.Exception == null && fr.Result is not ToolSecurityError,
                                fr.Result is ToolSecurityError se ? se.Message : (fr.Exception != null ? RbacFunctionInvoker.ToolFailedMessage : null));
                        }
                    }
                }
            }

            if (pendingApprovalRequests.Count == 0)
            {
                break;
            }

            // Xử lý các yêu cầu phê duyệt
            List<AIContent> approvalResponses = [];
            foreach (var req in pendingApprovalRequests)
            {
                var toolCall = req.ToolCall as FunctionCallContent;
                string toolName = toolCall?.Name ?? "unknown_tool";
                var argsDict = toolCall?.Arguments?.ToDictionary(k => k.Key, v => (object?)v.Value) 
                               ?? new Dictionary<string, object?>();

                int requestedQty = 0;
                if (argsDict.TryGetValue("quantity", out var qObj) && int.TryParse(qObj?.ToString(), out int qVal))
                {
                    requestedQty = qVal;
                }

                bool approved;
                string? rejectReason = null;

                // 1. Kiểm tra RBAC trước: nếu thiếu quyền, từ chối ngay lập tức, KHÔNG hỏi duyệt!
                if (!_userContext.HasPermission(ErpPermissions.PurchaseRequestCreate))
                {
                    approved = false;
                    rejectReason = "Tài khoản hiện tại không có quyền tạo đề xuất mua hàng.";
                    yield return new AgentToolExecutionEvent(
                        toolName, 
                        $"productCode={argsDict.GetValueOrDefault("productCode")}, quantity={requestedQty}", 
                        0, 
                        false, 
                        $"Từ chối: Tài khoản '{_userContext.CurrentUser.Username}' không có quyền '{ErpPermissions.PurchaseRequestCreate}'.");
                }
                // 2. Chặn đầu vào không hợp lệ (số lượng <= 0)
                else if (requestedQty <= 0)
                {
                    approved = false;
                    rejectReason = "Số lượng không hợp lệ (phải là số nguyên dương).";
                    yield return new AgentToolExecutionEvent(
                        toolName, 
                        $"productCode={argsDict.GetValueOrDefault("productCode")}, quantity={requestedQty}", 
                        0, 
                        false, 
                        "Từ chối: Số lượng đề xuất mua hàng phải là số nguyên dương (> 0).");
                }
                else
                {
                    bool requiresApproval = EvaluateRequiresApproval(toolName, argsDict, turnCumulativePrQuantity, _options.AutoApproveQuantityThreshold);
                    if (!requiresApproval)
                    {
                        // Tự động duyệt vì số lượng > 0 và tổng cộng dồn trong lượt <= 50
                        approved = true;
                        turnCumulativePrQuantity += requestedQty;
                    }
                    else
                    {
                        // Cần người dùng phê duyệt
                        var approvalEvent = new AgentApprovalRequiredEvent(
                            toolName, 
                            argsDict, 
                            $"Yêu cầu phê duyệt thực thi '{toolName}' (Số lượng: {requestedQty}, cộng dồn lượt: {turnCumulativePrQuantity + requestedQty})");
                        yield return approvalEvent;

                        // Mặc định từ chối an toàn nếu approvalHandler == null
                        approved = approvalHandler != null && await approvalHandler(approvalEvent);
                        if (approved)
                        {
                            turnCumulativePrQuantity += requestedQty;
                        }
                        else
                        {
                            // Nói rõ ai từ chối để model 3B không bịa "trưởng phòng không duyệt".
                            rejectReason = "Người dùng đã tự từ chối thao tác này. Không phải do trưởng phòng hay người khác.";
                        }
                    }
                }

                if (!approved && toolCall != null)
                {
                    skipResultCallIds.Add(toolCall.CallId);
                }

                approvalResponses.Add(req.CreateResponse(approved, rejectReason));
            }

            currentInput = new ChatMessage(ChatRole.User, approvalResponses);
        }

        turnSw.Stop();
        _stats.RecordTurn(turnInputTokens, turnOutputTokens);
        yield return new AgentTurnCompletedEvent(turnInputTokens, turnOutputTokens, turnSw.ElapsedMilliseconds);
    }

    private static string SummarizeArgs(IDictionary<string, object?>? args)
        => args == null || args.Count == 0 ? "" : string.Join(", ", args.Select(a => $"{a.Key}={a.Value}"));

    public async Task ResetSessionAsync()
    {
        _agent = BuildAgent();
        _session = await _agent.CreateSessionAsync();
    }

    public SessionStats GetStats() => _stats;
}
