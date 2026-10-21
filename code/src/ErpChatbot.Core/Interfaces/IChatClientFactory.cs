using Microsoft.Extensions.AI;

namespace ErpChatbot.Core.Interfaces;

public interface IChatClientFactory
{
    IChatClient CreateClient();
    Task<(bool IsHealthy, string Message)> CheckHealthAsync(CancellationToken cancellationToken = default);
}
