using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Exceptions;

namespace ErpChatbot.Infrastructure.Factories;

public class ChatClientFactory : IChatClientFactory
{
    private readonly LlmOptions _options;
    private readonly Func<LlmOptions, IChatClient>? _clientOverride;
    private readonly int _timeoutMs;

    public ChatClientFactory(IOptions<LlmOptions> options)
        : this(options, null, 5000)
    {
    }

    // Constructor phục vụ Dependency Injection và Unit Testing (inject mock/timeout)
    public ChatClientFactory(
        IOptions<LlmOptions> options,
        Func<LlmOptions, IChatClient>? clientOverride = null,
        int timeoutMs = 5000)
    {
        _options = options.Value;
        _clientOverride = clientOverride;
        _timeoutMs = timeoutMs;
    }

    public IChatClient CreateClient()
    {
        if (_clientOverride != null)
        {
            return _clientOverride(_options);
        }

        return _options.Provider.ToLowerInvariant() switch
        {
            "ollama" => new ChatClientBuilder(CreateOllamaClient())
                .ConfigureOptions(opt =>
                {
                    opt.Temperature ??= _options.Temperature;
                    opt.MaxOutputTokens ??= _options.MaxOutputTokens;
                })
                .Build(),
            _ => throw new InvalidOperationException(
                $"LLM_PROVIDER không hợp lệ: '{_options.Provider}'. Ngày 1 chỉ hỗ trợ 'ollama'.")
        };
    }

    private OllamaApiClient CreateOllamaClient()
    {
        var ollama = _options.Ollama;
        if (string.IsNullOrWhiteSpace(ollama.AccessClientId) || string.IsNullOrWhiteSpace(ollama.AccessClientSecret))
        {
            return new OllamaApiClient(new Uri(ollama.Endpoint), _options.Model);
        }

        var http = new HttpClient { BaseAddress = new Uri(ollama.Endpoint) };
        http.DefaultRequestHeaders.Add("CF-Access-Client-Id", ollama.AccessClientId);
        http.DefaultRequestHeaders.Add("CF-Access-Client-Secret", ollama.AccessClientSecret);
        return new OllamaApiClient(http, _options.Model);
    }

    public async Task<(bool IsHealthy, string Message)> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(_timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using var client = CreateClient();
            // Gửi một tin nhắn kiểm tra cực ngắn để xác thực cả Endpoint và sự tồn tại của Model
            var testMessage = new ChatMessage(ChatRole.User, "ping");
            var testOptions = new ChatOptions { MaxOutputTokens = 1 };

            await client.GetResponseAsync([testMessage], testOptions, linkedCts.Token);
            return (true, $"Ollama và model '{_options.Model}' đã sẵn sàng kết nối.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return (false, $"Không phản hồi sau {_timeoutMs / 1000}s. Vui lòng kiểm tra xem Ollama có bị treo không.");
        }
        // Chỉ lỗi do chính Ollama báo "model ... not found"; 404 chung của endpoint sai là HttpRequestException bên dưới
        catch (OllamaException ex) when (ex.Message.Contains("model", StringComparison.OrdinalIgnoreCase) &&
                                         ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Model '{_options.Model}' không tìm thấy trên Ollama. Vui lòng chạy lệnh: 'ollama pull {_options.Model}'.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Không thể kết nối tới Ollama tại '{_options.Ollama.Endpoint}'. Chi tiết: {ex.Message}. Vui lòng bật Ollama: 'ollama serve'.");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kiểm tra kết nối LLM: {ex.Message}");
        }
    }
}
