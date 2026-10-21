namespace ErpChatbot.Core.Models;

public class ErpOptions
{
    public const string SectionName = "Erp";

    public string AssistantName { get; set; } = "Trợ lý ERP";
    public string CompanyName { get; set; } = "Doanh nghiệp Demo";
    public string Instructions { get; set; } =
        "Bạn là 'Trợ lý ERP' của công ty. Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng. " +
        "Bạn CHƯA được kết nối dữ liệu ERP. Nếu người dùng hỏi số liệu cụ thể (tồn kho, đơn hàng, công nợ...), " +
        "hãy nói rõ là bạn chưa có quyền truy cập dữ liệu, tuyệt đối không được bịa số.";
}
