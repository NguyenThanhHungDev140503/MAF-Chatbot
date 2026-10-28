using ErpChatbot.Core.Models;

namespace ErpChatbot.Core.Interfaces;

/// <summary>
/// Quản lý vòng đời hội thoại của Trợ lý ERP sử dụng Microsoft Agent Framework (MAF).
/// <para>
/// <b>LƯU Ý SỞ HỮU TRẠNG THÁI (Session Ownership):</b><br/>
/// Mỗi instance của implementation giữ một phiên <c>AgentSession</c> và <c>SessionStats</c> làm mutable field riêng.
/// Giả định: <b>1 instance = 1 phiên hội thoại của 1 người dùng</b>.<br/>
/// Ở Giai đoạn 4 (Web API / Multi-tenant), BẮT BUỘC đăng ký service này theo Scoped hoặc Keyed DI per-connection/user.
/// TUYỆT ĐỐI KHÔNG đăng ký làm Singleton trong môi trường Web để tránh rò rỉ dữ liệu giữa các người dùng.
/// </para>
/// </summary>
public interface IErpAgentService
{
    IAsyncEnumerable<AgentResponseEvent> RunStreamingAsync(
        string userPrompt,
        Func<AgentApprovalRequiredEvent, Task<bool>>? approvalHandler = null,
        CancellationToken cancellationToken = default);

    Task ResetSessionAsync();
    SessionStats GetStats();
}
