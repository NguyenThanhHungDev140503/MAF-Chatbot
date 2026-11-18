using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models.Rbac;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ErpChatbot.Agent.Security;

public record ToolSecurityError(string Error, string Message);

public static class RbacFunctionInvoker
{
    // Thông điệp an toàn đưa vào event; chi tiết exception (có thể chứa SQL) chỉ ghi log.
    public const string ToolFailedMessage = "Tool gặp lỗi khi thực thi.";

    public static readonly IReadOnlyDictionary<string, string> ToolPermissions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["get_stock"] = ErpPermissions.InventoryRead,
        ["list_low_stock"] = ErpPermissions.InventoryRead,
        ["get_order_status"] = ErpPermissions.OrdersRead,
        ["get_customer_debt"] = ErpPermissions.DebtRead,
        ["create_purchase_request"] = ErpPermissions.PurchaseRequestCreate
    };

    public static Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> CreateInvoker(
        IUserContext userContext,
        ILogger? logger = null)
    {
        return async (ctx, ct) =>
        {
            string toolName = ctx.Function.Name;

            // Fail-Secure: Từ chối mặc định nếu tool chưa đăng ký quyền hạn trong hệ thống
            if (!ToolPermissions.TryGetValue(toolName, out var requiredPermission))
            {
                string errorMsg = $"Tool '{toolName}' chưa được đăng ký quyền hạn trong hệ thống (Default Deny / Fail-Secure).";
                return new ToolSecurityError("PermissionDenied", errorMsg);
            }

            // Kiểm tra phân quyền NIST RBAC
            if (!userContext.HasPermission(requiredPermission))
            {
                string rolesStr = string.Join(", ", userContext.CurrentUser.Roles.Select(r => r.Name));
                string errorMsg = $"Người dùng '{userContext.CurrentUser.Username}' (vai trò: {rolesStr}) không có quyền '{requiredPermission}' để thực thi tool '{toolName}'.";
                return new ToolSecurityError("PermissionDenied", errorMsg);
            }

            try
            {
                return await ctx.Function.InvokeAsync(ctx.Arguments, ct);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Tool {Tool} lỗi khi thực thi", toolName);
                throw;
            }
        };
    }
}
