using System.Diagnostics;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
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
    private readonly IUserContext _userContext;
    private readonly LlmOptions _llmOptions;
    private readonly ErpOptions _erpOptions;

    public CliCommandHandler(
        IErpAgentService agentService,
        IOptions<LlmOptions> llmOptions,
        IOptions<ErpOptions> erpOptions)
        : this(agentService, new UserContext(), llmOptions.Value, erpOptions.Value)
    {
    }

    public CliCommandHandler(
        IErpAgentService agentService,
        LlmOptions llmOptions,
        ErpOptions erpOptions)
        : this(agentService, new UserContext(), llmOptions, erpOptions)
    {
    }

    public CliCommandHandler(
        IErpAgentService agentService,
        IUserContext userContext,
        IOptions<LlmOptions> llmOptions,
        IOptions<ErpOptions> erpOptions)
        : this(agentService, userContext, llmOptions.Value, erpOptions.Value)
    {
    }

    public CliCommandHandler(
        IErpAgentService agentService,
        IUserContext userContext,
        LlmOptions llmOptions,
        ErpOptions erpOptions)
    {
        _agentService = agentService;
        _userContext = userContext;
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

        if (trimmed.StartsWith("/user", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/role", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                Console.WriteLine("\n[HƯỚNG DẪN] Cú pháp: /user <tên_user> (ví dụ: /user sales_user, /user accountant_user, /user admin_user)\n");
                return CliInputResult.Handled;
            }

            string targetUser = parts[1];
            try
            {
                _userContext.SwitchUser(targetUser);
                await _agentService.ResetSessionAsync();
                var u = _userContext.CurrentUser;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[NGƯỜI DÙNG] Đã chuyển sang tài khoản: {u.Username} ({u.FullName})");
                Console.WriteLine($"  Vai trò: {string.Join(", ", u.Roles.Select(r => r.Name))}");
                Console.WriteLine($"  Quyền hạn: {string.Join(", ", u.EffectivePermissions)}");
                Console.WriteLine("  (Đã tự động làm mới phiên hội thoại để bảo mật dữ liệu giữa các người dùng)\n");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[LỖI] {ex.Message}\n");
                Console.ResetColor();
            }
            return CliInputResult.Handled;
        }

        if (trimmed.Equals("/whoami", StringComparison.OrdinalIgnoreCase))
        {
            var u = _userContext.CurrentUser;
            Console.WriteLine($"\n--- THÔNG TIN TÀI KHOẢN ĐANG ĐĂNG NHẬP ---");
            Console.WriteLine($"Mã:       {u.Id}");
            Console.WriteLine($"Tên user: {u.Username}");
            Console.WriteLine($"Họ tên:   {u.FullName}");
            Console.WriteLine($"Vai trò:  {string.Join(", ", u.Roles.Select(r => $"{r.Name} ({r.Description})"))}");
            Console.WriteLine($"Quyền:    {string.Join(", ", u.EffectivePermissions)}\n");
            return CliInputResult.Handled;
        }

        if (trimmed.Equals("/info", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            var u = _userContext.CurrentUser;
            Console.WriteLine($"\n--- THÔNG TIN HỆ THỐNG ---");
            Console.WriteLine($"Trợ lý:    {_erpOptions.AssistantName} ({_erpOptions.CompanyName})");
            Console.WriteLine($"Chỉ dẫn:   {_erpOptions.Instructions}");
            Console.WriteLine($"Tài khoản: {u.Username} (Vai trò: {string.Join(", ", u.Roles.Select(r => r.Name))})");
            Console.WriteLine($"Provider:  {_llmOptions.Provider} | Model: {_llmOptions.Model}");
            Console.WriteLine($"Endpoint:  {_llmOptions.Ollama.Endpoint}");
            Console.WriteLine($"Lệnh: /user <tên>, /whoami, /reset, /stats, /info, /exit\n");
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

            long turnTokens = 0;
            long elapsedMs = 0;

            await foreach (var evt in _agentService.RunStreamingAsync(rawInput, HandleApprovalRequestAsync, userCancellation))
            {
                switch (evt)
                {
                    case AgentTextChunkEvent chunk:
                        Console.Write(chunk.Text);
                        break;
                    case AgentToolExecutionEvent toolEvt:
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine(toolEvt.Success 
                            ? $"\n   [tool] {toolEvt.ToolName}({toolEvt.ArgumentsSummary}) -> ok ({toolEvt.ElapsedMs}ms)" 
                            : $"\n   [tool] {toolEvt.ToolName} -> LỖI: {toolEvt.Error}");
                        Console.ResetColor();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("Trợ lý: ");
                        Console.ResetColor();
                        break;
                    case AgentTurnCompletedEvent completed:
                        turnTokens = completed.InputTokens + completed.OutputTokens;
                        elapsedMs = completed.ElapsedMs;
                        break;
                }
            }

            Console.WriteLine();
            var currentStats = _agentService.GetStats();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [Thời gian: {elapsedMs}ms | Token lượt này: {turnTokens} | Token tích luỹ: {currentStats.TotalTokens}]");
            Console.ResetColor();
            Console.WriteLine();
        }
        catch (OperationCanceledException) when (userCancellation.IsCancellationRequested)
        {
            WriteLine(ConsoleColor.Yellow, "\n(đã hủy phản hồi)");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            WriteLine(ConsoleColor.Red, $"\n[Mất kết nối tới Ollama: {ex.Message}. Thử lại hoặc gõ /reset]");
        }
        catch (Exception ex)
        {
            WriteLine(ConsoleColor.Red, $"\n[Lỗi xử lý: {ex.Message}]");
        }

        return true;
    }

    private static Task<bool> HandleApprovalRequestAsync(AgentApprovalRequiredEvent req)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        string argsStr = string.Join(", ", req.Arguments.Select(a => $"{a.Key}={a.Value}"));
        Console.Write($"\n   [cần duyệt] {req.ToolName}({argsStr}). Đồng ý? (y/n): ");
        Console.ResetColor();

        string? response = Console.ReadLine()?.Trim().ToLowerInvariant();
        bool approved = response == "y";

        Console.ForegroundColor = approved ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(approved ? "   -> Đã duyệt thực thi." : "   -> Đã từ chối thực thi.");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Trợ lý: ");
        Console.ResetColor();

        return Task.FromResult(approved);
    }

    private static void WriteLine(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
