using System.Text;
using ErpChatbot.Agent.Security;
using ErpChatbot.Agent.Services;
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using ErpChatbot.Infrastructure.Factories;
using ErpChatbot.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<ErpOptions>(builder.Configuration.GetSection(ErpOptions.SectionName));

var envProvider = Environment.GetEnvironmentVariable("LLM_PROVIDER");
var envModel = Environment.GetEnvironmentVariable("LLM_MODEL");
var envEndpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT");
var envUser = Environment.GetEnvironmentVariable("ERP_USER") ?? "sales_user";

builder.Services.PostConfigure<LlmOptions>(opts =>
{
    if (!string.IsNullOrWhiteSpace(envProvider)) opts.Provider = envProvider;
    if (!string.IsNullOrWhiteSpace(envModel)) opts.Model = envModel;
    if (!string.IsNullOrWhiteSpace(envEndpoint)) opts.Ollama.Endpoint = envEndpoint;
});

// COMPOSITION ROOT
builder.Services.AddSingleton<IUserContext>(new UserContext(envUser));
builder.Services.AddSingleton<IErpDataService, InMemoryErpDataService>();
builder.Services.AddSingleton<IChatClientFactory, ChatClientFactory>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IChatClientFactory>().CreateClient());

builder.Services.AddScoped<IErpAgentService, ErpAgentService>();
builder.Services.AddScoped<CliCommandHandler>();

using var host = builder.Build();

using var scope = host.Services.CreateScope();
var factory = scope.ServiceProvider.GetRequiredService<IChatClientFactory>();
var llmOptions = scope.ServiceProvider.GetRequiredService<IOptions<LlmOptions>>().Value;
var erpOptions = scope.ServiceProvider.GetRequiredService<IOptions<ErpOptions>>().Value;
var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();
var agentService = scope.ServiceProvider.GetRequiredService<IErpAgentService>();
var commandHandler = scope.ServiceProvider.GetRequiredService<CliCommandHandler>();

Console.WriteLine("===============================================================");
Console.WriteLine($"   {erpOptions.AssistantName} — Microsoft Agent Framework (.NET 10)");
Console.WriteLine($"   Người dùng: {userContext.CurrentUser.Username} ({string.Join(", ", userContext.CurrentUser.Roles.Select(r => r.Name))})");
Console.WriteLine($"   Provider: {llmOptions.Provider} | Model: {llmOptions.Model}");
Console.WriteLine($"   Endpoint: {llmOptions.Ollama.Endpoint}");
Console.WriteLine("===============================================================");
Console.WriteLine("Gõ /whoami để xem quyền, /user <tên> để đổi user, /reset để xoá session, /exit để thoát.\n");

Console.Write("Đang kiểm tra kết nối LLM (tối đa 30s nếu model đang nạp)... ");
var (isHealthy, healthMessage) = await factory.CheckHealthAsync();
CliChatLoop.PrintHealth(isHealthy, healthMessage);

var chatLoop = new CliChatLoop(agentService, commandHandler);
while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"[{userContext.CurrentUser.Username}] Bạn: ");
    Console.ResetColor();

    string? rawInput = Console.ReadLine();
    if (rawInput == null) break;

    using var turnCts = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, e) =>
    {
        e.Cancel = true;
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
