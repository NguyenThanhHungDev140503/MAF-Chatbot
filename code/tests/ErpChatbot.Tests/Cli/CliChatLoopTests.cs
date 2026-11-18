using System.Runtime.CompilerServices;
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using FluentAssertions;
using Xunit;

namespace ErpChatbot.Tests.Cli;

[Collection("Console")]
public class CliChatLoopTests
{
    private class ScriptedAgentService(Func<CancellationToken, IAsyncEnumerable<AgentResponseEvent>> script) : IErpAgentService
    {
        public int ResetCalls { get; private set; }

        public IAsyncEnumerable<AgentResponseEvent> RunStreamingAsync(
            string userPrompt,
            Func<AgentApprovalRequiredEvent, Task<bool>>? approvalHandler = null,
            CancellationToken cancellationToken = default)
            => script(cancellationToken);

        public Task ResetSessionAsync() { ResetCalls++; return Task.CompletedTask; }
        public SessionStats GetStats() => new();
    }

    private static async Task<(bool Continue, string Output)> RunAsync(
        IErpAgentService service, string input, CancellationToken userCt = default)
    {
        var loop = new CliChatLoop(service, new CliCommandHandler(service, new LlmOptions(), new ErpOptions()));
        using var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var keepGoing = await loop.ProcessLineAsync(input, userCt);
            return (keepGoing, sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // Giả lập agent đòi duyệt phiếu mua 100 cái và lưu quyết định CLI trả về.
    private class ApprovalAskingAgentService : IErpAgentService
    {
        public bool? LastDecision { get; private set; }

        public async IAsyncEnumerable<AgentResponseEvent> RunStreamingAsync(
            string userPrompt,
            Func<AgentApprovalRequiredEvent, Task<bool>>? approvalHandler = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (approvalHandler != null)
            {
                LastDecision = await approvalHandler(new AgentApprovalRequiredEvent(
                    "create_purchase_request",
                    new Dictionary<string, object?> { ["productCode"] = "SP-003", ["quantity"] = 100 },
                    "x"));
            }
            yield return new AgentTextChunkEvent("xong");
        }

        public Task ResetSessionAsync() => Task.CompletedTask;
        public SessionStats GetStats() => new();
    }

    private static async IAsyncEnumerable<AgentResponseEvent> ChunkThen(Exception error)
    {
        yield return new AgentTextChunkEvent("Đang trả lời");
        await Task.Yield();
        throw error;
    }

    [Fact]
    public async Task ProcessLine_WhenNetworkErrorMidStream_ShowsConnectionLostAndKeepsSession()
    {
        var service = new ScriptedAgentService(_ => ChunkThen(new HttpRequestException("rớt mạng")));

        var (keepGoing, output) = await RunAsync(service, "Xin chào");

        keepGoing.Should().BeTrue();
        output.Should().Contain("Đang trả lời").And.Contain("[Mất kết nối").And.NotContain("(đã hủy phản hồi)");
        service.ResetCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessLine_WhenNetworkOrTimeoutErrorMentionsSql_DoesNotPrintRawMessage()
    {
        foreach (Exception error in new Exception[] { new TimeoutException("SELECT * FROM Items"), new HttpRequestException("SELECT * FROM Items") })
        {
            var service = new ScriptedAgentService(_ => ChunkThen(error));

            var (keepGoing, output) = await RunAsync(service, "Xin chào");

            keepGoing.Should().BeTrue();
            output.Should().Contain("[Mất kết nối").And.NotContain("SELECT").And.NotContain("Items");
        }
    }

    [Fact]
    public async Task ProcessLine_WhenUnexpectedErrorMentionsSql_DoesNotPrintRawMessage()
    {
        // Tool lỗi lặp quá giới hạn: framework ném exception mang nguyên văn lỗi (có SQL).
        var service = new ScriptedAgentService(_ => ChunkThen(new InvalidOperationException("SELECT * FROM Secret WHERE pwd='x'")));

        var (keepGoing, output) = await RunAsync(service, "Xin chào");

        keepGoing.Should().BeTrue();
        output.Should().Contain("[Lỗi xử lý").And.NotContain("SELECT").And.NotContain("Secret");
    }

    [Fact]
    public async Task ProcessLine_WhenHttpTimeoutMidStream_ShowsConnectionLostNotUserCancel()
    {
        // HttpClient timeout ném TaskCanceledException dù user không bấm Ctrl+C
        var service = new ScriptedAgentService(_ => ChunkThen(new TaskCanceledException("timeout")));

        var (_, output) = await RunAsync(service, "Xin chào");

        output.Should().Contain("[Mất kết nối").And.NotContain("(đã hủy phản hồi)");
    }

    [Fact]
    public async Task ProcessLine_WhenUserCancelsMidStream_ShowsCancelledAndDoesNotCrash()
    {
        using var userCts = new CancellationTokenSource();
        var service = new ScriptedAgentService(Script);

        async IAsyncEnumerable<AgentResponseEvent> Script([EnumeratorCancellation] CancellationToken ct)
        {
            yield return new AgentTextChunkEvent("Đang trả lời");
            userCts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
        }

        var (keepGoing, output) = await RunAsync(service, "Xin chào", userCts.Token);

        keepGoing.Should().BeTrue();
        output.Should().Contain("(đã hủy phản hồi)").And.NotContain("[Mất kết nối");
        service.ResetCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessLine_WithExitCommand_StopsLoop()
    {
        var service = new ScriptedAgentService(_ => throw new InvalidOperationException("không được gọi LLM"));

        var (keepGoing, _) = await RunAsync(service, "/exit");

        keepGoing.Should().BeFalse();
    }

    [Fact]
    public void PrintHealth_WhenUnhealthy_PrintsWarningWithoutThrowing()
    {
        using var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            CliChatLoop.PrintHealth(false, "Model 'x' không tìm thấy trên Ollama.");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        sw.ToString().Should().Contain("[CẢNH BÁO KẾT NỐI]").And.Contain("Model 'x' không tìm thấy");
    }

    [Theory]
    [InlineData("y\n", true)]
    [InlineData("Y\n", true)]
    [InlineData(" y \n", true)]
    [InlineData("n\n", false)]
    [InlineData("\n", false)]
    [InlineData("yes\n", false)]
    [InlineData("", false)]
    public async Task TC34_ApprovalPrompt_OnlyYMeansApprove(string consoleInput, bool expected)
    {
        var service = new ApprovalAskingAgentService();
        var originalIn = Console.In;
        Console.SetIn(new StringReader(consoleInput));
        try
        {
            var (_, output) = await RunAsync(service, "Tạo đề xuất mua 100 cái SP-003");

            service.LastDecision.Should().Be(expected);
            output.Should().Contain("[cần duyệt] create_purchase_request(productCode=SP-003, quantity=100)");
        }
        finally
        {
            Console.SetIn(originalIn);
        }
    }
}
