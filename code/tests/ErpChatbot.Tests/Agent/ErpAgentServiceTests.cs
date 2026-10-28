using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Services;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using ErpChatbot.Infrastructure.Services;
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
}
