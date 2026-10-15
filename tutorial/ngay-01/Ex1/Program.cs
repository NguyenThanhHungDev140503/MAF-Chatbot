using Microsoft.Extensions.AI;
using OllamaSharp;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// 1. Tạo IChatClient trỏ tới Ollama đang chạy trên máy
IChatClient client = new OllamaApiClient(new Uri("http://localhost:11434"), "llama3.2");

// 2. Gửi một câu hỏi, chờ trả lời trọn vẹn
ChatResponse response = await client.GetResponseAsync("Giải thích ERP là gì trong 2 câu.");

// 3. In kết quả và số token đã dùng
Console.WriteLine(response.Text);
Console.WriteLine($"Token vào: {response.Usage?.InputTokenCount}, token ra: {response.Usage?.OutputTokenCount}");
