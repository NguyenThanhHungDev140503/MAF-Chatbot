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
    private readonly Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>? _functionInvoker;
    private readonly Func<LlmOptions, IChatClient>? _clientOverride;
    private readonly int _timeoutMs;

    public ChatClientFactory(IOptions<LlmOptions> options)
        : this(options, (Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>?)null, null, 30000)
    {
    }

    public ChatClientFactory(
        IOptions<LlmOptions> options,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>? functionInvoker)
        : this(options, functionInvoker, null, 30000)
    {
    }

    public ChatClientFactory(
        IOptions<LlmOptions> options,
        Func<LlmOptions, IChatClient>? clientOverride,
        int timeoutMs = 30000)
        : this(options, null, clientOverride, timeoutMs)
    {
    }

    public ChatClientFactory(
        IOptions<LlmOptions> options,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>? functionInvoker,
        Func<LlmOptions, IChatClient>? clientOverride,
        int timeoutMs = 30000)
    {
        _options = options.Value;
        _functionInvoker = functionInvoker;
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
                .UseFunctionInvocation(configure: f =>
                {
                    f.MaximumIterationsPerRequest = 5;          // Chặn vòng lặp vô hạn theo chuẩn Ex3
                    f.MaximumConsecutiveErrorsPerRequest = 2;   // Ngắt nếu lỗi 2 lần liên tiếp
                    f.IncludeDetailedErrors = false;            // Không lộ SQL/stack trace cho LLM
                    f.AllowConcurrentInvocation = false;        // Thực thi tuần tự

                    if (_functionInvoker != null)
                    {
                        f.FunctionInvoker = _functionInvoker;
                    }
                })
                .Build(),
            _ => throw new InvalidOperationException(
                $"LLM_PROVIDER không hợp lệ: '{_options.Provider}'. Chỉ hỗ trợ 'ollama'.")
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
            var testMessage = new ChatMessage(ChatRole.User, "ping");
            var testOptions = new ChatOptions { MaxOutputTokens = 1 };

            await client.GetResponseAsync([testMessage], testOptions, linkedCts.Token);
            return (true, $"Ollama và model '{_options.Model}' đã sẵn sàng kết nối.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return (false, $"Không phản hồi sau {_timeoutMs / 1000}s. Vui lòng kiểm tra xem Ollama có bị treo không.");
        }
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
