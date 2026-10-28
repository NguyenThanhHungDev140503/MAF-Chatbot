namespace ErpChatbot.Core.Models;

public abstract record AgentResponseEvent;

public record AgentTextChunkEvent(string Text) : AgentResponseEvent;

public record AgentToolExecutionEvent(
    string ToolName,
    string ArgumentsSummary,
    long ElapsedMs,
    bool Success,
    string? Error = null) : AgentResponseEvent;

public record AgentApprovalRequiredEvent(
    string ToolName,
    IReadOnlyDictionary<string, object?> Arguments,
    string Summary) : AgentResponseEvent;

public record AgentTurnCompletedEvent(
    long InputTokens,
    long OutputTokens,
    long ElapsedMs) : AgentResponseEvent;
