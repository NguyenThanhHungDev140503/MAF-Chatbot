using System.ComponentModel;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ErpChatbot.Agent.Tools;

public static class ErpTools
{
    public static IList<AITool> GetTools(IErpDataService erpService, IUserContext? userContext = null)
    {
        var list = new List<AITool>();

        if (userContext == null || userContext.HasPermission(ErpPermissions.InventoryRead))
        {
            var getStock = AIFunctionFactory.Create(
                [Description("Tra số lượng tồn kho hiện tại và vị trí kho của một mã sản phẩm trong ERP.")]
                async ([Description("Mã sản phẩm, dạng SP-xxx (ví dụ: SP-001)")] string productCode, CancellationToken ct) =>
                    await erpService.GetStockAsync(productCode, ct),
                name: "get_stock");
            list.Add(getStock);

            var listLowStock = AIFunctionFactory.Create(
                [Description("Liệt kê danh sách các sản phẩm có số lượng tồn kho thấp hơn ngưỡng cho trước.")]
                async ([Description("Ngưỡng số lượng tồn kho, ví dụ 10")] int threshold, CancellationToken ct) =>
                    await erpService.ListLowStockAsync(threshold, ct),
                name: "list_low_stock");
            list.Add(listLowStock);
        }

        if (userContext == null || userContext.HasPermission(ErpPermissions.OrdersRead))
        {
            var getOrderStatus = AIFunctionFactory.Create(
                [Description("Xem trạng thái và ngày giao dự kiến của một đơn bán hàng trong ERP.")]
                async ([Description("Mã đơn hàng, dạng DH-xxxx (ví dụ: DH-1001)")] string orderCode, CancellationToken ct) =>
                    await erpService.GetOrderStatusAsync(orderCode, ct),
                name: "get_order_status");
            list.Add(getOrderStatus);
        }

        if (userContext == null || userContext.HasPermission(ErpPermissions.DebtRead))
        {
            var getCustomerDebt = AIFunctionFactory.Create(
                [Description("Tra cứu thông tin công nợ hiện tại và hạn thanh toán của khách hàng trong ERP.")]
                async ([Description("Mã khách hàng, dạng KH-xxx (ví dụ: KH-001)")] string customerCode, CancellationToken ct) =>
                    await erpService.GetCustomerDebtAsync(customerCode, ct),
                name: "get_customer_debt");
            list.Add(getCustomerDebt);
        }

        if (userContext == null || userContext.HasPermission(ErpPermissions.PurchaseRequestCreate))
        {
            var createPr = AIFunctionFactory.Create(
                [Description("Tạo phiếu đề xuất mua hàng (Purchase Request) mới trong ERP. Thao tác này là thao tác GHI dữ liệu.")]
                async (
                    [Description("Mã sản phẩm cần mua, dạng SP-xxx")] string productCode,
                    [Description("Số lượng cần mua (số nguyên dương > 0)")] int quantity,
                    CancellationToken ct) =>
                    await erpService.CreatePurchaseRequestAsync(productCode, quantity, ct),
                name: "create_purchase_request");
            list.Add(new ApprovalRequiredAIFunction(createPr));
        }

        return list;
    }
}
