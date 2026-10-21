namespace ErpChatbot.Core.Models;

public record ChatTurnResponse(
    string Content,
    long InputTokens,
    long OutputTokens,
    TimeSpan Duration,
    string Provider,
    string Model
);
