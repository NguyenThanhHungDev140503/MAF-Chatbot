// Ex1: Tự tay chạy vòng lặp function calling (KHÔNG dùng UseFunctionInvocation)
// Mục đích: thấy rõ mô hình chỉ "xin" gọi hàm, còn code của bạn mới là bên chạy hàm.
using System.ClientModel;
using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;

IChatClient client = CreateClient(); // client "trần", chưa có middleware gọi hàm

// 1. Biến hàm C# thành tool: mô tả và JSON schema tham số được sinh từ chữ ký hàm + [Description].
//    Đặt tên tường minh: hàm local trong top-level statements bị trình biên dịch đổi tên (kiểu _Main_g_GetStock_0_1).
AIFunction getStock = AIFunctionFactory.Create(GetStock, name: "get_stock");
Console.WriteLine($"Tool: {getStock.Name}\nMô tả: {getStock.Description}\nSchema: {getStock.JsonSchema}\n");

var options = new ChatOptions { Tools = [getStock] };
List<ChatMessage> history =
[
    new(ChatRole.System, "Bạn là Trợ lý ERP. Trả lời tiếng Việt, ngắn gọn. Chỉ dùng số liệu lấy từ tool, không bịa."),
    new(ChatRole.User, "Mã SP-001 còn bao nhiêu trong kho?")
];

// 2. Lượt 1: gửi câu hỏi kèm danh sách tool
ChatResponse response = await client.GetResponseAsync(history, options);
history.AddMessages(response);

// 3. Mô hình KHÔNG trả lời, mà trả về một hoặc nhiều FunctionCallContent
var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
Console.WriteLine($"Lượt 1: mô hình xin gọi {calls.Count} hàm, FinishReason = {response.FinishReason}");

foreach (FunctionCallContent call in calls)
{
    Console.WriteLine($"  -> {call.Name}({string.Join(", ", call.Arguments!.Select(a => $"{a.Key}={a.Value}"))})  [CallId={call.CallId}]");

    // 4. CODE CỦA BẠN chạy hàm, rồi gửi kết quả lại với đúng CallId
    object? result = await getStock.InvokeAsync(new AIFunctionArguments(call.Arguments));
    history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, result)]));
}

// 5. Lượt 2: mô hình đọc kết quả hàm và viết câu trả lời cuối
response = await client.GetResponseAsync(history, options);
history.AddMessages(response);
Console.WriteLine($"\nTrợ lý: {response.Text}\n");

// 6. In toàn bộ lịch sử để thấy 4 loại tin nhắn
Console.WriteLine("Lịch sử hội thoại:");
foreach (ChatMessage m in history)
    Console.WriteLine($"  [{m.Role}] {string.Join(", ", m.Contents.Select(c => c.GetType().Name))}");

// ---- "ERP" giả: sau này sẽ thay bằng lời gọi API ERP thật ----
[Description("Tra số lượng tồn kho hiện tại của một mã sản phẩm trong ERP.")]
static StockInfo GetStock([Description("Mã sản phẩm, ví dụ SP-001")] string productCode)
{
    Console.WriteLine($"     (C# đang chạy GetStock(\"{productCode}\"))");
    return productCode.ToUpperInvariant() switch
    {
        "SP-001" => new("SP-001", "Bàn phím cơ K87", 124, "cái", "Kho Hà Nội"),
        "SP-002" => new("SP-002", "Chuột không dây M3", 8, "cái", "Kho Hà Nội"),
        _ => new(productCode, "(không tìm thấy)", 0, "", "")
    };
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

record StockInfo(string ProductCode, string ProductName, int Quantity, string Unit, string Warehouse);
