using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using Microsoft.Extensions.Options;

namespace ErpChatbot.Cli;

public enum CliInputResult
{
    Handled,
    Exit,
    Ignored,
    SendToLlm
}

public class CliCommandHandler
{
    private readonly IErpAgentService _agentService;
    private readonly LlmOptions _llmOptions;
    private readonly ErpOptions _erpOptions;

    public CliCommandHandler(
        IErpAgentService agentService,
        IOptions<LlmOptions> llmOptions,
        IOptions<ErpOptions> erpOptions)
        : this(agentService, llmOptions.Value, erpOptions.Value)
    {
    }

    public CliCommandHandler(IErpAgentService agentService, LlmOptions llmOptions, ErpOptions erpOptions)
    {
        _agentService = agentService;
        _llmOptions = llmOptions;
        _erpOptions = erpOptions;
    }

    public async Task<CliInputResult> HandleInputAsync(string input, CancellationToken cancellationToken)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return CliInputResult.Ignored;
        }

        if (trimmed.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        {
            return CliInputResult.Exit;
        }

        if (trimmed.Equals("/reset", StringComparison.OrdinalIgnoreCase))
        {
            await _agentService.ResetSessionAsync();
            Console.WriteLine("\n[HỆ THỐNG] Đã xoá lịch sử phiên chat. Agent đã bắt đầu một phiên mới.\n");
            return CliInputResult.Handled;
        }

        if (trimmed.Equals("/stats", StringComparison.OrdinalIgnoreCase))
        {
            var stats = _agentService.GetStats();
            Console.WriteLine($"\n--- THỐNG KÊ PHIÊN CHAT ---");
            Console.WriteLine($"Số lượt trò chuyện: {stats.TurnCount}");
            Console.WriteLine($"Tổng Token vào (Input):  {stats.TotalInputTokens}");
            Console.WriteLine($"Tổng Token ra (Output): {stats.TotalOutputTokens}");
            Console.WriteLine($"Tổng Token đã dùng:     {stats.TotalTokens}\n");
            return CliInputResult.Handled;
        }

        if (trimmed.Equals("/info", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"\n--- THÔNG TIN HỆ THỐNG ---");
            Console.WriteLine($"Trợ lý:    {_erpOptions.AssistantName} ({_erpOptions.CompanyName})");
            Console.WriteLine($"Chỉ dẫn:   {_erpOptions.Instructions}");
            Console.WriteLine($"Provider:  {_llmOptions.Provider}");
            Console.WriteLine($"Model:     {_llmOptions.Model}");
            Console.WriteLine($"Endpoint:  {_llmOptions.Ollama.Endpoint}");
            Console.WriteLine($"Lệnh sẵn sàng: /reset, /stats, /info, /exit\n");
            return CliInputResult.Handled;
        }

        if (trimmed.StartsWith("/"))
        {
            Console.WriteLine($"\n[CẢNH BÁO] Lệnh '{trimmed}' không hợp lệ. Gõ /info hoặc /help để xem danh sách lệnh.\n");
            return CliInputResult.Handled;
        }

        return CliInputResult.SendToLlm;
    }
}
