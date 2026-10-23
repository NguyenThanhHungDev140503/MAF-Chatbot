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
    private class ScriptedAgentService(Func<CancellationToken, IAsyncEnumerable<string>> script) : IErpAgentService
    {
        public int ResetCalls { get; private set; }

        public IAsyncEnumerable<string> RunStreamingAsync(string userPrompt, CancellationToken cancellationToken = default)
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

    private static async IAsyncEnumerable<string> ChunkThen(Exception error)
    {
        yield return "Đang trả lời";
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

        async IAsyncEnumerable<string> Script([EnumeratorCancellation] CancellationToken ct)
        {
            yield return "Đang trả lời";
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
}
