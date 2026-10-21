using ErpChatbot.Core.Models;
using ErpChatbot.Infrastructure.Factories;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Xunit;

namespace ErpChatbot.Tests.Infrastructure;

public class ChatClientFactoryTests
{
    private class FakeChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> _responder;

        public FakeChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> responder)
        {
            _responder = responder;
        }

        public ChatClientMetadata Metadata => new("fake");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => _responder(chatMessages, options, cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public void CreateClient_WithOllamaProvider_ReturnsConfiguredChatClient()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "llama3.2", Temperature = 0.5f, MaxOutputTokens = 1000 });
        var factory = new ChatClientFactory(options);

        var client = factory.CreateClient();

        client.Should().NotBeNull();
        client.Should().BeOfType<ConfigureOptionsChatClient>();
        client.GetService(typeof(OllamaApiClient)).Should().NotBeNull();
    }

    [Fact]
    public void CreateClient_WithUnsupportedProvider_ThrowsInvalidOperationException()
    {
        var options = Options.Create(new LlmOptions { Provider = "openai" });
        var factory = new ChatClientFactory(options);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LLM_PROVIDER không hợp lệ*");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenModelReady_ReturnsHealthy()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "llama3.2" });
        var fakeClient = new FakeChatClient((msgs, opt, ct) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))));

        var factory = new ChatClientFactory(options, _ => fakeClient);

        var (isHealthy, message) = await factory.CheckHealthAsync();

        isHealthy.Should().BeTrue();
        message.Should().Contain("sẵn sàng");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenModelMissing_ReturnsModelNotFoundMessage()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "non-existent-model" });
        var fakeClient = new FakeChatClient((msgs, opt, ct) =>
            throw new InvalidOperationException("model 'non-existent-model' not found, try pulling it first"));

        var factory = new ChatClientFactory(options, _ => fakeClient);

        var (isHealthy, message) = await factory.CheckHealthAsync();

        isHealthy.Should().BeFalse();
        message.Should().Contain("không tìm thấy").And.Contain("ollama pull");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTimeoutExceeded_ReturnsTimeoutMessage()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "llama3.2" });
        var fakeClient = new FakeChatClient(async (msgs, opt, ct) =>
        {
            await Task.Delay(10000, ct); // Giả lập treo quá 5s
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "late"));
        });

        var factory = new ChatClientFactory(options, _ => fakeClient, timeoutMs: 50); // Timeout nhanh cho test

        var (isHealthy, message) = await factory.CheckHealthAsync();

        isHealthy.Should().BeFalse();
        message.Should().Contain("Không phản hồi");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointUnreachable_ReturnsConnectionErrorMessage()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "llama3.2" });
        var fakeClient = new FakeChatClient((msgs, opt, ct) =>
            throw new HttpRequestException("Connection refused"));

        var factory = new ChatClientFactory(options, _ => fakeClient);

        var (isHealthy, message) = await factory.CheckHealthAsync();

        isHealthy.Should().BeFalse();
        message.Should().Contain("Không thể kết nối tới Ollama");
    }
}
