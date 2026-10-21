namespace ErpChatbot.Core.Models;

public class SessionStats
{
    public int TurnCount { get; private set; }
    public long TotalInputTokens { get; private set; }
    public long TotalOutputTokens { get; private set; }
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public void RecordTurn(long inputTokens, long outputTokens)
    {
        TurnCount++;
        TotalInputTokens += inputTokens;
        TotalOutputTokens += outputTokens;
    }
}
