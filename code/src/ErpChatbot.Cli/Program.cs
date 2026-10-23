using System.Text;
using ErpChatbot.Agent.Services;
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Infrastructure.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// Cấu hình phân tầng: appsettings.json + Ghi đè bằng biến môi trường (theo chuẩn Ex3)
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<ErpOptions>(builder.Configuration.GetSection(ErpOptions.SectionName));

// Ghi đè biến môi trường trực tiếp theo quy ước tài liệu Ngày 1
var envProvider = Environment.GetEnvironmentVariable("LLM_PROVIDER");
var envModel = Environment.GetEnvironmentVariable("LLM_MODEL");
var envEndpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT");

builder.Services.PostConfigure<LlmOptions>(opts =>
{
    if (!string.IsNullOrWhiteSpace(envProvider)) opts.Provider = envProvider;
    if (!string.IsNullOrWhiteSpace(envModel)) opts.Model = envModel;
    if (!string.IsNullOrWhiteSpace(envEndpoint)) opts.Ollama.Endpoint = envEndpoint;
});

// COMPOSITION ROOT:
// Đăng ký Factory và tạo IChatClient qua Factory, sau đó inject vào Agent
builder.Services.AddSingleton<IChatClientFactory, ChatClientFactory>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IChatClientFactory>().CreateClient());

// Đăng ký ErpAgentService (1 instance per conversation)
builder.Services.AddScoped<IErpAgentService, ErpAgentService>();
builder.Services.AddScoped<CliCommandHandler>();

using var host = builder.Build();

using var scope = host.Services.CreateScope();
var factory = scope.ServiceProvider.GetRequiredService<IChatClientFactory>();
var llmOptions = scope.ServiceProvider.GetRequiredService<IOptions<LlmOptions>>().Value;
var erpOptions = scope.ServiceProvider.GetRequiredService<IOptions<ErpOptions>>().Value;
var agentService = scope.ServiceProvider.GetRequiredService<IErpAgentService>();
var commandHandler = scope.ServiceProvider.GetRequiredService<CliCommandHandler>();

// 1. In Banner ứng dụng
Console.WriteLine("===============================================================");
Console.WriteLine($"   {erpOptions.AssistantName} — Microsoft Agent Framework (.NET 10)");
Console.WriteLine($"   Provider: {llmOptions.Provider} | Model: {llmOptions.Model}");
Console.WriteLine($"   Endpoint: {llmOptions.Ollama.Endpoint}");
Console.WriteLine("===============================================================");
Console.WriteLine("Gõ /info để xem trợ giúp, /reset để xoá lịch sử, /exit để thoát.\n");

// 2. Health Check kiểm tra kết nối tới mô hình
Console.Write("Đang kiểm tra kết nối LLM... ");
var (isHealthy, healthMessage) = await factory.CheckHealthAsync();
CliChatLoop.PrintHealth(isHealthy, healthMessage);

// 3. Vòng lặp Chat chính
var chatLoop = new CliChatLoop(agentService, commandHandler);
while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Bạn: ");
    Console.ResetColor();

    string? rawInput = Console.ReadLine();
    if (rawInput == null) break;

    using var turnCts = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, e) =>
    {
        e.Cancel = true; // Không tắt app, chỉ cancel lượt stream hiện tại
        turnCts.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        if (!await chatLoop.ProcessLineAsync(rawInput, turnCts.Token)) break;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}
