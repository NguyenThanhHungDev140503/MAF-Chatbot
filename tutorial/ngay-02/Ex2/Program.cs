// Ex2: Để UseFunctionInvocation() tự chạy vòng lặp gọi hàm
using System.ClientModel;
using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;

// Bọc client gốc bằng middleware FunctionInvokingChatClient
IChatClient client = new ChatClientBuilder(CreateClient())
    .UseFunctionInvocation()
    .Build();

var options = new ChatOptions
{
    Tools = [AIFunctionFactory.Create(GetStock, name: "get_stock")]
};

string[] questions =
[
    "Mã SP-001 còn bao nhiêu trong kho?",
    "So sánh tồn kho SP-001 và SP-002 giúp tôi.",   // mô hình có thể xin gọi 2 hàm cùng lúc
    "Chào bạn, bạn làm được gì?"                     // không cần tool
];

foreach (string q in questions)
{
    List<ChatMessage> history =
    [
        new(ChatRole.System, "Bạn là Trợ lý ERP. Trả lời tiếng Việt, ngắn gọn. Chỉ dùng số liệu lấy từ tool, không bịa."),
        new(ChatRole.User, q)
    ];

    // MỘT lời gọi: bên trong có thể là nhiều vòng hỏi mô hình -> chạy hàm -> hỏi lại
    ChatResponse response = await client.GetResponseAsync(history, options);

    Console.WriteLine($"\nBạn: {q}");
    Console.WriteLine($"Trợ lý: {response.Text}");

    // response.Messages chứa CẢ các tin nhắn trung gian (gọi hàm + kết quả hàm)
    foreach (ChatMessage m in response.Messages)
        foreach (AIContent c in m.Contents)
            switch (c)
            {
                case FunctionCallContent call:
                    Console.WriteLine($"   [gọi]    {call.Name}({string.Join(", ", call.Arguments!.Select(a => $"{a.Key}={a.Value}"))})");
                    break;
                case FunctionResultContent result:
                    Console.WriteLine($"   [kết quả] {result.Result?.ToString()?.ReplaceLineEndings(" ")}");
                    break;
            }
    Console.WriteLine($"   ({response.Messages.Count} tin nhắn trả về, token vào/ra: {response.Usage?.InputTokenCount}/{response.Usage?.OutputTokenCount})");
}

[Description("Tra số lượng tồn kho hiện tại của một mã sản phẩm trong ERP.")]
static StockInfo GetStock([Description("Mã sản phẩm, ví dụ SP-001")] string productCode) =>
    productCode.ToUpperInvariant() switch
    {
        "SP-001" => new("SP-001", "Bàn phím cơ K87", 124, "cái", "Kho Hà Nội"),
        "SP-002" => new("SP-002", "Chuột không dây M3", 8, "cái", "Kho Hà Nội"),
        _ => new(productCode, "(không tìm thấy)", 0, "", "")
    };

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
