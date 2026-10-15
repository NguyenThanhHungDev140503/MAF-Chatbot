using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;

Console.OutputEncoding = System.Text.Encoding.UTF8;

IChatClient client = new OllamaApiClient(new Uri("http://localhost:11434"), "llama3.2");

// Cùng IChatClient ấy, bọc thành một agent của MAF
AIAgent agent = client.AsAIAgent(instructions: "Bạn là trợ lý ERP. Trả lời ngắn, bằng tiếng Việt.");

AgentSession session = await agent.CreateSessionAsync();
Console.WriteLine(await agent.RunAsync("Chào bạn, tôi là Hùng ở phòng kho.", session));
Console.WriteLine(await agent.RunAsync("Tôi ở phòng nào?", session)); // session tự giữ lịch sử
