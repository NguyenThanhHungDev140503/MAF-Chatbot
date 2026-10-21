using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Services;
using ErpChatbot.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace ErpChatbot.Tests.Agent;

public class ErpAgentServiceTests
{
    private class FakeStreamingChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, IAsyncEnumerable<ChatResponseUpdate>> _streamGenerator;
        public List<List<ChatMessage>> RecordedHistory { get; } = [];

        public FakeStreamingChatClient(Func<IEnumerable<ChatMessage>, IAsyncEnumerable<ChatResponseUpdate>> streamGenerator)
        {
            _streamGenerator = streamGenerator;
        }

        public ChatClientMetadata Metadata => new("fake-agent");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RecordedHistory.Add(chatMessages.ToList());
            await foreach (var update in _streamGenerator(chatMessages).WithCancellation(cancellationToken))
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task RunStreamingAsync_YieldsStreamUpdatesInOrder()
    {
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunks(["Xin ", "chào ", "bạn!"]));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        var chunks = new List<string>();
        await foreach (var chunk in service.RunStreamingAsync("Chào"))
        {
            chunks.Add(chunk);
        }

        string.Join("", chunks).Should().Be("Xin chào bạn!");
    }

    [Fact]
    public async Task RunStreamingAsync_RetainsContextAcrossConsecutiveTurns()
    {
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunks(["OK"]));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Tôi tên Hùng ở kho")) { }
        await foreach (var _ in service.RunStreamingAsync("Tôi ở phòng nào?")) { }

        fakeClient.RecordedHistory.Should().HaveCount(2);
        var secondTurnMessages = fakeClient.RecordedHistory[1];
        secondTurnMessages.Should().Contain(m => m.Text != null && m.Text.Contains("Tôi tên Hùng ở kho"));
        secondTurnMessages.Should().Contain(m => m.Text != null && m.Text.Contains("Tôi ở phòng nào?"));
    }

    [Fact]
    public async Task RunStreamingAsync_AccumulatesTokensViaUsageContent()
    {
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunksWithUsage(["Xin ", "chào!"], inTokens: 15, outTokens: 5));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Lượt 1")) { }
        await foreach (var _ in service.RunStreamingAsync("Lượt 2")) { }

        var stats = service.GetStats();
        stats.TurnCount.Should().Be(2);
        stats.TotalInputTokens.Should().Be(30);
        stats.TotalOutputTokens.Should().Be(10);
    }

    [Fact]
    public async Task ResetSessionAsync_CreatesNewSession_RetainsSessionStats()
    {
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunksWithUsage(["OK"], 10, 5));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Lượt 1")) { }
        await service.ResetSessionAsync();
        await foreach (var _ in service.RunStreamingAsync("Lượt 2 sau reset")) { }

        var stats = service.GetStats();
        stats.TurnCount.Should().Be(2);
        stats.TotalInputTokens.Should().Be(20);

        // Turn 2 không được chứa context của Turn 1
        var secondTurnMessages = fakeClient.RecordedHistory[1];
        secondTurnMessages.Should().NotContain(m => m.Text != null && m.Text.Contains("Lượt 1"));
    }

    [Fact]
    public async Task RunStreamingAsync_WhenClientThrowsException_DoesNotResetSession()
    {
        bool shouldThrow = false;
        var fakeClient = new FakeStreamingChatClient(msgs =>
        {
            if (shouldThrow) throw new HttpRequestException("Mạng chập chờn");
            return CreateChunks(["Được rồi"]);
        });
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        // Turn 1 thành công
        await foreach (var _ in service.RunStreamingAsync("Ghi nhớ: mã kho là K1")) { }

        // Turn 2 lỗi mạng
        shouldThrow = true;
        var act = async () =>
        {
            await foreach (var _ in service.RunStreamingAsync("Gặp lỗi nè")) { }
        };
        await act.Should().ThrowAsync<HttpRequestException>();

        // Turn 3 thành công lại -> Ngữ cảnh Turn 1 vẫn còn, session không bị reset nhầm
        shouldThrow = false;
        await foreach (var _ in service.RunStreamingAsync("Mã kho là gì?")) { }

        var lastTurnMessages = fakeClient.RecordedHistory.Last();
        lastTurnMessages.Should().Contain(m => m.Text != null && m.Text.Contains("Ghi nhớ: mã kho là K1"));
    }

    [Fact]
    public async Task RunStreamingAsync_WhenCanceled_DoesNotResetSession()
    {
        var cts = new CancellationTokenSource();
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunks(["Đang trả lời..."]));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Nhớ chữ X")) { }

        // Cancel lượt 2
        cts.Cancel();
        var act = async () =>
        {
            await foreach (var _ in service.RunStreamingAsync("Câu bị cancel", cts.Token)) { }
        };
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Lượt 3: context vẫn còn
        await foreach (var _ in service.RunStreamingAsync("Nhắc lại chữ X")) { }

        var lastTurnMessages = fakeClient.RecordedHistory.Last();
        lastTurnMessages.Should().Contain(m => m.Text != null && m.Text.Contains("Nhớ chữ X"));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateChunks(string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateChunksWithUsage(string[] chunks, long inTokens, long outTokens)
    {
        foreach (var chunk in chunks)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            await Task.Yield();
        }

        var updateWithUsage = new ChatResponseUpdate();
        updateWithUsage.Contents.Add(new UsageContent(new UsageDetails
        {
            InputTokenCount = inTokens,
            OutputTokenCount = outTokens
        }));
        yield return updateWithUsage;
    }
}
