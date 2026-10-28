using System.Diagnostics;
using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Security;
using ErpChatbot.Agent.Tools;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ErpChatbot.Agent.Services;

public class ErpAgentService : IErpAgentService
{
    private readonly IChatClient _baseChatClient;
    private readonly IErpDataService _erpDataService;
    private readonly IUserContext _userContext;
    private readonly ErpOptions _options;
    private readonly System.Collections.Concurrent.ConcurrentQueue<AgentResponseEvent> _eventQueue = new();
    private readonly SessionStats _stats = new();

    private AIAgent _agent;
    private AgentSession? _session;

    public ErpAgentService(
        IChatClient chatClient,
        IErpDataService erpDataService,
        IUserContext userContext,
        IOptions<ErpOptions> erpOptions)
    {
        _baseChatClient = chatClient;
        _erpDataService = erpDataService;
        _userContext = userContext;
        _options = erpOptions.Value;

        _agent = BuildAgent();
    }

    private AIAgent BuildAgent()
    {
        var tools = ErpTools.GetTools(_erpDataService, _userContext);
        var invoker = RbacFunctionInvoker.CreateInvoker(_userContext, evt => _eventQueue.Enqueue(evt));

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

    public static bool EvaluateRequiresApproval(string toolName, IReadOnlyDictionary<string, object?>? arguments, int cumulativeTurnQty = 0)
    {
        if (toolName.Equals("create_purchase_request", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments != null && arguments.TryGetValue("quantity", out var qtyObj))
            {
                if (int.TryParse(qtyObj?.ToString(), out int qty))
                {
                    if (qty <= 0) return true; // Số lượng không hợp lệ không được auto-duyệt
                    return (cumulativeTurnQty + qty) > 50;
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

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (_eventQueue.TryDequeue(out var pendingEvt))
            {
                yield return pendingEvt;
            }

            var responseStream = currentInput switch
            {
                string text => _agent.RunStreamingAsync(text, _session, options: null, cancellationToken: cancellationToken),
                ChatMessage chatMsg => _agent.RunStreamingAsync([chatMsg], _session, options: null, cancellationToken: cancellationToken),
                _ => throw new InvalidOperationException($"Input type không được hỗ trợ: {currentInput.GetType()}")
            };

            List<ToolApprovalRequestContent> pendingApprovalRequests = [];

            await foreach (var update in responseStream.WithCancellation(cancellationToken))
            {
                while (_eventQueue.TryDequeue(out var toolEvt))
                {
                    yield return toolEvt;
                }

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
                    }
                }
            }

            while (_eventQueue.TryDequeue(out var toolEvt))
            {
                yield return toolEvt;
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

                // 1. Kiểm tra RBAC trước: nếu thiếu quyền, từ chối ngay lập tức, KHÔNG hỏi duyệt!
                if (!_userContext.HasPermission(ErpPermissions.PurchaseRequestCreate))
                {
                    approved = false;
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
                    yield return new AgentToolExecutionEvent(
                        toolName, 
                        $"productCode={argsDict.GetValueOrDefault("productCode")}, quantity={requestedQty}", 
                        0, 
                        false, 
                        "Từ chối: Số lượng đề xuất mua hàng phải là số nguyên dương (> 0).");
                }
                else
                {
                    bool requiresApproval = EvaluateRequiresApproval(toolName, argsDict, turnCumulativePrQuantity);
                    if (!requiresApproval)
                    {
                        // Tự động duyệt vì số lượng > 0 và tổng cộng dồn trong lượt <= 50
                        approved = true;
                        turnCumulativePrQuantity += requestedQty;
                        yield return new AgentToolExecutionEvent(
                            toolName, 
                            $"productCode={argsDict.GetValueOrDefault("productCode")}, quantity={requestedQty}", 
                            0, 
                            true);
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
                    }
                }

                approvalResponses.Add(req.CreateResponse(approved));
            }

            currentInput = new ChatMessage(ChatRole.User, approvalResponses);
        }

        while (_eventQueue.TryDequeue(out var finalEvt))
        {
            yield return finalEvt;
        }

        turnSw.Stop();
        _stats.RecordTurn(turnInputTokens, turnOutputTokens);
        yield return new AgentTurnCompletedEvent(turnInputTokens, turnOutputTokens, turnSw.ElapsedMilliseconds);
    }

    public async Task ResetSessionAsync()
    {
        _agent = BuildAgent();
        _session = await _agent.CreateSessionAsync();
    }

    public SessionStats GetStats() => _stats;
}
