using System.ClientModel;
using System.Text;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// ---- Cấu hình qua biến môi trường (mặc định: Ollama trên máy) ----
string provider = Env("LLM_PROVIDER", "ollama");
string model    = Env("LLM_MODEL", "llama3.2");

IChatClient client = provider switch
{
    // Ollama, API gốc của Ollama, qua thư viện OllamaSharp
    "ollama" => new OllamaApiClient(new Uri(Env("OLLAMA_ENDPOINT", "http://localhost:11434")), model),

    // Bất kỳ dịch vụ nào nói chuẩn OpenAI: OpenAI thật, hoặc chính Ollama ở /v1
    "openai" => new OpenAIClient(
                    new ApiKeyCredential(Env("OPENAI_API_KEY", "ollama")),
                    new OpenAIClientOptions { Endpoint = new Uri(Env("OPENAI_ENDPOINT", "http://localhost:11434/v1")) })
                .GetChatClient(model)
                .AsIChatClient(),

    _ => throw new InvalidOperationException($"LLM_PROVIDER không hợp lệ: {provider}")
};

// ---- Lịch sử hội thoại: TỰ TAY giữ, vì mô hình không nhớ gì giữa các lần gọi ----
List<ChatMessage> history =
[
    new(ChatRole.System,
        """
        Bạn là "Trợ lý ERP" của công ty. Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng.
        Bạn CHƯA được kết nối dữ liệu ERP. Nếu người dùng hỏi số liệu cụ thể
        (tồn kho, đơn hàng, công nợ...), hãy nói rõ là bạn chưa có quyền truy cập dữ liệu, không được bịa số.
        """)
];

var options = new ChatOptions { Temperature = 0.2f };
long totalIn = 0, totalOut = 0;

Console.WriteLine($"Trợ lý ERP ({provider}/{model}). Gõ /reset để xoá lịch sử, /exit để thoát.");
while (true)
{
    Console.Write("\nBạn: ");
    string? input = Console.ReadLine()?.Trim();
    if (input is null || input == "/exit") break;
    if (input.Length == 0) continue;
    if (input == "/reset") { history.RemoveRange(1, history.Count - 1); Console.WriteLine("(đã xoá lịch sử)"); continue; }

    history.Add(new ChatMessage(ChatRole.User, input));

    Console.Write("Trợ lý: ");
    List<ChatResponseUpdate> updates = [];
    await foreach (var update in client.GetStreamingResponseAsync(history, options))
    {
        Console.Write(update.Text);
        updates.Add(update);
    }
    Console.WriteLine();

    // Gộp các mẩu stream thành một ChatResponse hoàn chỉnh rồi thêm vào lịch sử
    ChatResponse response = updates.ToChatResponse();
    history.AddMessages(response);

    totalIn  += response.Usage?.InputTokenCount ?? 0;
    totalOut += response.Usage?.OutputTokenCount ?? 0;
    Console.WriteLine($"  [lịch sử: {history.Count} tin | token lượt này: {response.Usage?.InputTokenCount}/{response.Usage?.OutputTokenCount} | cộng dồn: {totalIn}/{totalOut}]");
}

static string Env(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;
