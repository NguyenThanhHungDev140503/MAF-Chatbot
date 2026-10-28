using System.Diagnostics;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using Microsoft.Extensions.AI;

namespace ErpChatbot.Agent.Security;

public record ToolSecurityError(string Error, string Message);

public static class RbacFunctionInvoker
{
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
        Action<AgentToolExecutionEvent>? onExecuted = null)
    {
        return async (ctx, ct) =>
        {
            string toolName = ctx.Function.Name;
            var sw = Stopwatch.StartNew();

            // Fail-Secure: Từ chối mặc định nếu tool chưa đăng ký quyền hạn trong hệ thống
            if (!ToolPermissions.TryGetValue(toolName, out var requiredPermission))
            {
                sw.Stop();
                string errorMsg = $"Tool '{toolName}' chưa được đăng ký quyền hạn trong hệ thống (Default Deny / Fail-Secure).";
                onExecuted?.Invoke(new AgentToolExecutionEvent(toolName, SummarizeArgs(ctx.Arguments), sw.ElapsedMilliseconds, false, errorMsg));
                return new ToolSecurityError("PermissionDenied", errorMsg);
            }

            // Kiểm tra phân quyền NIST RBAC
            if (!userContext.HasPermission(requiredPermission))
            {
                sw.Stop();
                string rolesStr = string.Join(", ", userContext.CurrentUser.Roles.Select(r => r.Name));
                string errorMsg = $"Người dùng '{userContext.CurrentUser.Username}' (vai trò: {rolesStr}) không có quyền '{requiredPermission}' để thực thi tool '{toolName}'.";
                onExecuted?.Invoke(new AgentToolExecutionEvent(toolName, SummarizeArgs(ctx.Arguments), sw.ElapsedMilliseconds, false, errorMsg));
                return new ToolSecurityError("PermissionDenied", errorMsg);
            }

            try
            {
                var result = await ctx.Function.InvokeAsync(ctx.Arguments, ct);
                sw.Stop();
                onExecuted?.Invoke(new AgentToolExecutionEvent(toolName, SummarizeArgs(ctx.Arguments), sw.ElapsedMilliseconds, true));
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                onExecuted?.Invoke(new AgentToolExecutionEvent(toolName, SummarizeArgs(ctx.Arguments), sw.ElapsedMilliseconds, false, ex.Message));
                throw;
            }
        };
    }

    private static string SummarizeArgs(AIFunctionArguments? args)
    {
        if (args == null || args.Count == 0) return "";
        return string.Join(", ", args.Select(a => $"{a.Key}={a.Value}"));
    }
}
