using System.Runtime.CompilerServices;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ErpChatbot.Agent.Services;

public class ErpAgentService : IErpAgentService
{
    private readonly AIAgent _agent;
    private readonly SessionStats _stats = new();
    private AgentSession? _session;

    public ErpAgentService(IChatClient chatClient, IOptions<ErpOptions> erpOptions)
    {
        var options = erpOptions.Value;

        // Bọc IChatClient thành AIAgent của Microsoft Agent Framework
        _agent = chatClient.AsAIAgent(
            instructions: options.Instructions,
            name: options.AssistantName
        );
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            yield break;
        }

        // Tự khởi tạo session nếu là lượt đầu tiên
        _session ??= await _agent.CreateSessionAsync(cancellationToken);

        long turnInputTokens = 0;
        long turnOutputTokens = 0;

        // Gọi RunStreamingAsync của AIAgent. Trả về IAsyncEnumerable<AgentResponseUpdate>
        var responseStream = _agent.RunStreamingAsync(userPrompt, _session, options: null, cancellationToken: cancellationToken);

        await using var enumerator = responseStream.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            AgentResponseUpdate update;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
                update = enumerator.Current;
            }
            catch
            {
                // Khi xảy ra Exception hoặc CancelKeyPress giữa chừng:
                // KHÔNG reset _session! Để nguyên session để người dùng không bị mất ngữ cảnh đã trao đổi trước đó.
                throw;
            }

            // Trích xuất text để stream ra UI
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }

            // Trích xuất số token qua UsageContent trong Contents
            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent && usageContent.Details != null)
                    {
                        turnInputTokens += usageContent.Details.InputTokenCount ?? 0;
                        turnOutputTokens += usageContent.Details.OutputTokenCount ?? 0;
                    }
                }
            }
        }

        // Cập nhật thống kê token sau khi stream hoàn tất trọn vẹn
        _stats.RecordTurn(turnInputTokens, turnOutputTokens);
    }

    public async Task ResetSessionAsync()
    {
        // Tạo một session mới hoàn toàn, dọn dẹp ngữ cảnh cũ
        _session = await _agent.CreateSessionAsync();
        // LƯU Ý: Không reset _stats vì stats thống kê toàn bộ phiên làm việc của người dùng
    }

    public SessionStats GetStats() => _stats;
}
