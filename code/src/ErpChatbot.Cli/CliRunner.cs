using System.Diagnostics;
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

public class CliChatLoop
{
    private readonly IErpAgentService _agentService;
    private readonly CliCommandHandler _commandHandler;

    public CliChatLoop(IErpAgentService agentService, CliCommandHandler commandHandler)
    {
        _agentService = agentService;
        _commandHandler = commandHandler;
    }

    public static void PrintHealth(bool isHealthy, string message)
    {
        Console.ForegroundColor = isHealthy ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine(isHealthy ? $"[SẴN SÀNG] {message}\n" : $"\n[CẢNH BÁO KẾT NỐI] {message}\n");
        Console.ResetColor();
    }

    /// <summary>Xử lý một dòng input. Trả về false khi người dùng thoát.</summary>
    /// <param name="userCancellation">Token chỉ bị hủy khi người dùng bấm Ctrl+C.</param>
    public async Task<bool> ProcessLineAsync(string rawInput, CancellationToken userCancellation)
    {
        try
        {
            var inputResult = await _commandHandler.HandleInputAsync(rawInput, userCancellation);
            if (inputResult == CliInputResult.Exit)
            {
                Console.WriteLine("Tạm biệt!");
                return false;
            }
            if (inputResult != CliInputResult.SendToLlm)
            {
                return true;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("Trợ lý: ");
            Console.ResetColor();

            var stopwatch = Stopwatch.StartNew();
            long prevTotalTokens = _agentService.GetStats().TotalTokens;

            await foreach (var chunk in _agentService.RunStreamingAsync(rawInput, userCancellation))
            {
                Console.Write(chunk);
            }
            stopwatch.Stop();
            Console.WriteLine();

            var currentStats = _agentService.GetStats();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [Thời gian: {stopwatch.ElapsedMilliseconds}ms | Token lượt này: {currentStats.TotalTokens - prevTotalTokens} | Token tích luỹ: {currentStats.TotalTokens}]");
            Console.ResetColor();
            Console.WriteLine();
        }
        catch (OperationCanceledException) when (userCancellation.IsCancellationRequested)
        {
            WriteLine(ConsoleColor.Yellow, "\n(đã hủy phản hồi)");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            // OperationCanceledException tới đây là timeout của HttpClient, không phải user hủy
            WriteLine(ConsoleColor.Red, $"\n[Mất kết nối tới Ollama: {ex.Message}. Thử lại hoặc gõ /reset]");
        }
        catch (Exception ex)
        {
            WriteLine(ConsoleColor.Red, $"\n[Lỗi xử lý: {ex.Message}]");
        }

        return true;
    }

    private static void WriteLine(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
