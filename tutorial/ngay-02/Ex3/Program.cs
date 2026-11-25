// Ex3: Nâng cấp ErpChat của ngày 1: nhiều tool, streaming, cấu hình FunctionInvokingChatClient, log mỗi lần gọi hàm
using System.ClientModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

IChatClient client = new ChatClientBuilder(CreateClient())
    .UseFunctionInvocation(configure: f =>
    {
        f.MaximumIterationsPerRequest = 5;          // mặc định 40: chặn vòng lặp gọi hàm vô hạn
        f.MaximumConsecutiveErrorsPerRequest = 2;   // mặc định 3
        f.IncludeDetailedErrors = false;            // mặc định false: không đưa nội dung exception cho mô hình
        f.AllowConcurrentInvocation = false;        // mặc định false: các hàm chạy lần lượt

        // Bọc MỌI lần gọi hàm: chỗ để log, đo thời gian, kiểm tra quyền...
        f.FunctionInvoker = async (ctx, ct) =>
        {
            var sw = Stopwatch.StartNew();
            Console.Write($"\n   [tool] {ctx.Function.Name}({string.Join(", ", ctx.Arguments.Select(a => $"{a.Key}={a.Value}"))})");
            try
            {
                object? result = await ctx.Function.InvokeAsync(ctx.Arguments, ct);
                Console.WriteLine($" -> ok, {sw.ElapsedMilliseconds} ms (vòng {ctx.Iteration})");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" -> LỖI: {ex.Message}");
                throw; // để FunctionInvokingChatClient báo lỗi cho mô hình (dạng chung chung vì IncludeDetailedErrors=false)
            }
        };
    })
    .Build();

var options = new ChatOptions
{
    Temperature = 0.1f,
    Tools =
    [
        AIFunctionFactory.Create(Erp.GetStock, name: "get_stock"),
        AIFunctionFactory.Create(Erp.GetOrderStatus, name: "get_order_status"),
        AIFunctionFactory.Create(Erp.ListLowStock, name: "list_low_stock"),
    ]
};

List<ChatMessage> history =
[
    new(ChatRole.System,
        """
        Bạn là "Trợ lý ERP" của công ty. Trả lời bằng tiếng Việt, ngắn gọn.
        Luôn dùng tool để lấy số liệu tồn kho, đơn hàng. Không bao giờ tự bịa số.
        Nếu tool báo lỗi hoặc không tìm thấy, nói rõ cho người dùng.
        """)
];

Console.WriteLine("ErpChat ngày 2 (có tool). Thử: 'SP-002 còn bao nhiêu?', 'Đơn DH-1001 tới đâu rồi?', 'Mặt hàng nào sắp hết dưới 10?'. /exit để thoát.");
while (true)
{
    Console.Write("\nBạn: ");
    string? input = Console.ReadLine()?.Trim();
    if (input is null || input == "/exit") break;
    if (input.Length == 0) continue;

    history.Add(new ChatMessage(ChatRole.User, input));
    Console.Write("Trợ lý: ");
    List<ChatResponseUpdate> updates = [];
    await foreach (var update in client.GetStreamingResponseAsync(history, options))
    {
        Console.Write(update.Text);
        updates.Add(update);
    }
    Console.WriteLine();

    // Các update gồm cả FunctionCallContent/FunctionResultContent -> lịch sử giữ đủ ngữ cảnh gọi hàm
    history.AddMessages(updates.ToChatResponse());
    Console.WriteLine($"  [lịch sử: {history.Count} tin]");
}

static IChatClient CreateClient()
{
    string provider = Env("LLM_PROVIDER", "ollama");
    string model = Env("LLM_MODEL", "llama3.2");
    return provider switch
    {
        "ollama" => new OllamaApiClient(new Uri(Env("OLLAMA_ENDPOINT", "http://localhost:11434")), model),
        "openai" => new OpenAIClient(
                        new ApiKeyCredential(Env("OPENAI_API_KEY", "ollama")),
                        new OpenAIClientOptions { Endpoint = new Uri(Env("OPENAI_ENDPOINT", "http://localhost:11434/v1")) })
                    .GetChatClient(model).AsIChatClient(),
        _ => throw new InvalidOperationException($"LLM_PROVIDER không hợp lệ: {provider}")
    };
}

static string Env(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

// ---- "ERP" giả: dữ liệu trong bộ nhớ. Giai đoạn sau thay bằng gọi API ERP thật / MCP server ----
static class Erp
{
    static readonly Dictionary<string, (string Name, int Qty, string Unit)> Stock = new()
    {
        ["SP-001"] = ("Bàn phím cơ K87", 124, "cái"),
        ["SP-002"] = ("Chuột không dây M3", 8, "cái"),
        ["SP-003"] = ("Màn hình 27 inch Q27", 3, "chiếc"),
        ["SP-004"] = ("Cáp HDMI 2m", 560, "sợi"),
    };

    static readonly Dictionary<string, (string Customer, string Status, string Eta)> Orders = new()
    {
        ["DH-1001"] = ("Công ty An Phát", "Đang giao", "2026-10-23"),
        ["DH-1002"] = ("Cửa hàng Minh Long", "Chờ xuất kho", "2026-10-25"),
    };

    [Description("Tra số lượng tồn kho hiện tại của một mã sản phẩm.")]
    public static object GetStock([Description("Mã sản phẩm, dạng SP-xxx")] string productCode)
    {
        string code = productCode.Trim().ToUpperInvariant();
        if (!Stock.TryGetValue(code, out var s))
            throw new KeyNotFoundException($"Không có sản phẩm {code} trong bảng Items (SQL: SELECT ... WHERE Code='{code}')");
        return new { productCode = code, productName = s.Name, quantity = s.Qty, unit = s.Unit };
    }

    [Description("Xem trạng thái và ngày giao dự kiến của một đơn bán hàng.")]
    public static object GetOrderStatus([Description("Mã đơn hàng, dạng DH-xxxx")] string orderCode)
    {
        string code = orderCode.Trim().ToUpperInvariant();
        return Orders.TryGetValue(code, out var o)
            ? new { orderCode = code, customer = o.Customer, status = o.Status, eta = o.Eta }
            : new { orderCode = code, customer = "", status = "Không tìm thấy", eta = "" };
    }

    [Description("Liệt kê các sản phẩm có tồn kho thấp hơn ngưỡng cho trước.")]
    public static object[] ListLowStock([Description("Ngưỡng số lượng, ví dụ 10")] int threshold) =>
        Stock.Where(kv => kv.Value.Qty < threshold)
             .Select(kv => (object)new { productCode = kv.Key, productName = kv.Value.Name, quantity = kv.Value.Qty })
             .ToArray();
}
