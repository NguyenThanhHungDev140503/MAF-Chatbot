// Ex4: Cùng các tool đó nhưng dùng agent MAF; hàm GHI dữ liệu phải được người dùng duyệt trước khi chạy
using System.ClientModel;
using System.ComponentModel;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

AIFunction getStock = AIFunctionFactory.Create(GetStock, name: "get_stock");
AIFunction createPr = AIFunctionFactory.Create(CreatePurchaseRequest, name: "create_purchase_request");

// ChatClientAgent tự thêm FunctionInvokingChatClient, không cần UseFunctionInvocation
AIAgent agent = CreateClient().AsAIAgent(
    name: "TroLyERP",
    instructions: "Bạn là Trợ lý ERP. Trả lời tiếng Việt, ngắn gọn. Dùng tool để lấy số liệu và tạo đề xuất mua hàng.",
    tools: [getStock, new ApprovalRequiredAIFunction(createPr)]); // đọc: chạy luôn | ghi: phải duyệt

AgentSession session = await agent.CreateSessionAsync();

Console.WriteLine("Agent ERP (Ex4). Thử: 'SP-003 còn bao nhiêu?' rồi 'Tạo đề xuất đặt mua 20 cái SP-003'. /exit để thoát.");
while (true)
{
    Console.Write("\nBạn: ");
    string? input = Console.ReadLine()?.Trim();
    if (input is null || input == "/exit") break;
    if (input.Length == 0) continue;

    AgentResponse response = await agent.RunAsync(input, session);

    // Lặp đến khi không còn yêu cầu duyệt nào (một lượt có thể xin duyệt nhiều hàm)
    List<ToolApprovalRequestContent> requests;
    while ((requests = response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList()).Count > 0)
    {
        List<AIContent> answers = [];
        foreach (ToolApprovalRequestContent req in requests)
        {
            var call = (FunctionCallContent)req.ToolCall;
            Console.Write($"   [cần duyệt] {call.Name}({string.Join(", ", call.Arguments!.Select(a => $"{a.Key}={a.Value}"))}). Đồng ý? (y/n): ");
            bool ok = Console.ReadLine()?.Trim().ToLowerInvariant() == "y";
            Console.WriteLine(ok ? "   -> đã duyệt" : "   -> từ chối");
            answers.Add(req.CreateResponse(ok));
        }
        // Gửi câu trả lời duyệt vào CÙNG session để agent chạy tiếp (hoặc báo bị từ chối)
        response = await agent.RunAsync(new ChatMessage(ChatRole.User, answers), session);
    }

    Console.WriteLine($"Trợ lý: {response.Text}");
}

[Description("Tra số lượng tồn kho hiện tại của một mã sản phẩm.")]
static object GetStock([Description("Mã sản phẩm, dạng SP-xxx")] string productCode)
{
    Console.WriteLine($"   (C# chạy get_stock {productCode})");
    return productCode.ToUpperInvariant() switch
    {
        "SP-003" => new { productCode = "SP-003", productName = "Màn hình 27 inch Q27", quantity = 3, unit = "chiếc" },
        _ => new { productCode, productName = "(không tìm thấy)", quantity = 0, unit = "" }
    };
}

[Description("Tạo phiếu đề xuất mua hàng (purchase request) trong ERP. Đây là thao tác GHI dữ liệu.")]
static object CreatePurchaseRequest(
    [Description("Mã sản phẩm cần mua, dạng SP-xxx")] string productCode,
    [Description("Số lượng cần mua")] int quantity)
{
    string id = $"PR-{Random.Shared.Next(1000, 9999)}";
    Console.WriteLine($"   (C# GHI vào ERP: {id} = {quantity} x {productCode})");
    return new { requestId = id, productCode, quantity, status = "Chờ phê duyệt của trưởng phòng mua hàng" };
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
