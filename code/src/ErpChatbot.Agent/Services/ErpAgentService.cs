using System.Diagnostics;
using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Security;
using ErpChatbot.Agent.Tools;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ErpChatbot.Agent.Services;

public class ErpAgentService : IErpAgentService
{
    private readonly AIAgent _agent;
    private readonly SessionStats _stats = new();
    private AgentSession? _session;

    public ErpAgentService(
        IChatClient chatClient,
        IErpDataService erpDataService,
        IUserContext userContext,
        IOptions<ErpOptions> erpOptions)
    {
        var options = erpOptions.Value;
        var tools = ErpTools.GetTools(erpDataService);

        _agent = chatClient.AsAIAgent(
            instructions: options.Instructions,
            name: options.AssistantName,
            tools: tools
        );
    }

    public static bool EvaluateRequiresApproval(string toolName, IReadOnlyDictionary<string, object?>? arguments)
    {
        if (toolName.Equals("create_purchase_request", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments != null && arguments.TryGetValue("quantity", out var qtyObj))
            {
                if (int.TryParse(qtyObj?.ToString(), out int qty))
                {
                    return qty > 50;
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
        var turnSw = Stopwatch.StartNew();

        object currentInput = userPrompt;

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

                bool requiresApproval = EvaluateRequiresApproval(toolName, argsDict);
                bool approved;

                if (!requiresApproval)
                {
                    // Tự động duyệt vì số lượng <= 50
                    approved = true;
                    yield return new AgentToolExecutionEvent(toolName, "Tự động phê duyệt (số lượng <= 50)", 0, true);
                }
                else
                {
                    // Cần người dùng phê duyệt
                    var approvalEvent = new AgentApprovalRequiredEvent(toolName, argsDict, $"Yêu cầu phê duyệt thực thi '{toolName}'");
                    yield return approvalEvent;

                    approved = approvalHandler != null && await approvalHandler(approvalEvent);
                }

                approvalResponses.Add(req.CreateResponse(approved));
            }

            currentInput = new ChatMessage(ChatRole.User, approvalResponses);
        }

        turnSw.Stop();
        _stats.RecordTurn(turnInputTokens, turnOutputTokens);
        yield return new AgentTurnCompletedEvent(turnInputTokens, turnOutputTokens, turnSw.ElapsedMilliseconds);
    }

    public async Task ResetSessionAsync()
    {
        _session = await _agent.CreateSessionAsync();
    }

    public SessionStats GetStats() => _stats;
}
