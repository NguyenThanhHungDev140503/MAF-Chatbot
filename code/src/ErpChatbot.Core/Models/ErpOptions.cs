namespace ErpChatbot.Core.Models;

public class ErpOptions
{
    public const string SectionName = "Erp";
    public const int DefaultAutoApproveQuantityThreshold = 0;

    // Số lượng tối đa tự duyệt không hỏi y/n; mặc định 0 = mọi phiếu đều phải hỏi (chốt 2026-11-18, spec §6.3).
    public int AutoApproveQuantityThreshold { get; set; } = DefaultAutoApproveQuantityThreshold;
    public string AssistantName { get; set; } = "Trợ lý ERP";
    public string CompanyName { get; set; } = "Doanh nghiệp Demo";
    public string Instructions { get; set; } =
        "Bạn là 'Trợ lý ERP' của công ty. Trả lời bằng tiếng Việt, ngắn gọn. " +
        "Luôn dùng tool để lấy số liệu tồn kho, đơn hàng, công nợ hoặc tạo đề xuất mua hàng. " +
        "Tuyệt đối không bao giờ tự bịa số. Nếu tool báo lỗi, thiếu quyền truy cập hoặc không tìm thấy dữ liệu, " +
        "hãy giải thích rõ ràng và lịch sự cho người dùng. " +
        "Nếu thao tác bị từ chối, chỉ nói lý do được nêu trong kết quả tool (ví dụ người dùng tự từ chối); tuyệt đối không bịa người khác từ chối.";
}
