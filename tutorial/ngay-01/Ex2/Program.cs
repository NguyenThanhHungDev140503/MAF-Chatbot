using Microsoft.Extensions.AI;
using OllamaSharp;

Console.OutputEncoding = System.Text.Encoding.UTF8;

IChatClient client = new OllamaApiClient(new Uri("http://localhost:11434"), "llama3.2");

List<ChatMessage> messages =
[
    new(ChatRole.System, "Bạn là trợ lý ERP. Trả lời ngắn gọn, bằng tiếng Việt."),
    new(ChatRole.User, "Phiếu nhập kho và phiếu xuất kho khác nhau thế nào?")
];

var options = new ChatOptions { Temperature = 0.2f, MaxOutputTokens = 300 };

// Stream: in từng mẩu chữ ngay khi mô hình sinh ra
await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(messages, options))
{
    Console.Write(update.Text);
}
Console.WriteLine();
