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
        var fakeClient = new FakeStreamingChatClient(msgs => shouldThrow ? ThrowAfterFirstChunk() : CreateChunks(["Được rồi"]));
        var service = new ErpAgentService(fakeClient, Options.Create(new ErpOptions()));

        await foreach (var _ in service.RunStreamingAsync("Ghi nhớ: mã kho là K1")) { }

        // Turn 2: đã nhận ≥1 chunk rồi mạng rớt
        shouldThrow = true;
        var received = new List<string>();
        var act = async () =>
        {
            await foreach (var chunk in service.RunStreamingAsync("Gặp lỗi nè")) received.Add(chunk);
        };
        await act.Should().ThrowAsync<HttpRequestException>();
        received.Should().NotBeEmpty();

        // Turn 3: ngữ cảnh Turn 1 vẫn còn
        shouldThrow = false;
        await foreach (var _ in service.RunStreamingAsync("Mã kho là gì?")) { }

        fakeClient.RecordedHistory.Last().Should().Contain(m => m.Text != null && m.Text.Contains("Ghi nhớ: mã kho là K1"));
    }

    [Fact]
    public async Task RunStreamingAsync_WhenCanceledMidStream_DoesNotResetSession()
    {
        bool hang = false;
        var fakeClient = new FakeStreamingChatClient(msgs => hang ? HangAfterFirstChunk() : CreateChunks(["OK"]));
        var service = new ErpAgentService(fakeClient, Options.Create(new ErpOptions()));

        await foreach (var _ in service.RunStreamingAsync("Nhớ chữ X")) { }

        // Turn 2: hủy sau khi đã nhận chunk đầu
        hang = true;
        using var cts = new CancellationTokenSource();
        var received = new List<string>();
        var act = async () =>
        {
            await foreach (var chunk in service.RunStreamingAsync("Câu bị cancel", cts.Token))
            {
                received.Add(chunk);
                cts.Cancel();
            }
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
        received.Should().HaveCount(1);

        // Turn 3: ngữ cảnh vẫn còn
        hang = false;
        await foreach (var _ in service.RunStreamingAsync("Nhắc lại chữ X")) { }

        fakeClient.RecordedHistory.Last().Should().Contain(m => m.Text != null && m.Text.Contains("Nhớ chữ X"));
    }

    [Fact]
    public async Task TwoConversations_ShouldHaveIsolatedHistoryAndStats()
    {
        var fakeClient1 = new FakeStreamingChatClient(msgs => CreateChunksWithUsage(["Trả lời phiên 1"], inTokens: 10, outTokens: 5));
        var fakeClient2 = new FakeStreamingChatClient(msgs => CreateChunksWithUsage(["Trả lời phiên 2"], inTokens: 20, outTokens: 8));

        var erpOptions = Options.Create(new ErpOptions());
        var service1 = new ErpAgentService(fakeClient1, erpOptions);
        var service2 = new ErpAgentService(fakeClient2, erpOptions);

        // Turn trên service1
        await foreach (var _ in service1.RunStreamingAsync("Xin chào phiên 1")) { }

        // Turn trên service2
        await foreach (var _ in service2.RunStreamingAsync("Xin chào phiên 2")) { }

        // Verify history isolation
        fakeClient1.RecordedHistory.Should().HaveCount(1);
        fakeClient1.RecordedHistory[0].Should().Contain(m => m.Text != null && m.Text.Contains("Xin chào phiên 1"));
        fakeClient1.RecordedHistory[0].Should().NotContain(m => m.Text != null && m.Text.Contains("Xin chào phiên 2"));

        fakeClient2.RecordedHistory.Should().HaveCount(1);
        fakeClient2.RecordedHistory[0].Should().Contain(m => m.Text != null && m.Text.Contains("Xin chào phiên 2"));
        fakeClient2.RecordedHistory[0].Should().NotContain(m => m.Text != null && m.Text.Contains("Xin chào phiên 1"));

        // Verify stats isolation
        var stats1 = service1.GetStats();
        stats1.TurnCount.Should().Be(1);
        stats1.TotalInputTokens.Should().Be(10);
        stats1.TotalOutputTokens.Should().Be(5);

        var stats2 = service2.GetStats();
        stats2.TurnCount.Should().Be(1);
        stats2.TotalInputTokens.Should().Be(20);
        stats2.TotalOutputTokens.Should().Be(8);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ThrowAfterFirstChunk()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Đang trả lờ");
        await Task.Yield();
        throw new HttpRequestException("Mạng chập chờn");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> HangAfterFirstChunk([EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Đang trả lời...");
        await Task.Delay(Timeout.Infinite, ct);
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
