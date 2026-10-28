using ErpChatbot.Core.Interfaces;

namespace ErpChatbot.Core.Models.Rbac;

public class UserContext : IUserContext
{
    private static readonly Role RoleSales = new(
        "Sales",
        "Nhân viên kinh doanh",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ErpPermissions.InventoryRead,
            ErpPermissions.OrdersRead,
            ErpPermissions.PurchaseRequestCreate
        });

    private static readonly Role RoleAccountant = new(
        "Accountant",
        "Kế toán công nợ",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ErpPermissions.DebtRead,
            ErpPermissions.InventoryRead,
            ErpPermissions.OrdersRead
        });

    private static readonly Role RoleAdmin = new(
        "Admin",
        "Quản trị hệ thống",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ErpPermissions.InventoryRead,
            ErpPermissions.OrdersRead,
            ErpPermissions.DebtRead,
            ErpPermissions.PurchaseRequestCreate
        });

    public static readonly IReadOnlyDictionary<string, User> MockUsers = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase)
    {
        ["sales_user"] = new("USR-01", "sales_user", "Nguyễn Văn Bán", [RoleSales]),
        ["accountant_user"] = new("USR-02", "accountant_user", "Trần Thị Kế Toán", [RoleAccountant]),
        ["admin_user"] = new("USR-03", "admin_user", "Lê Quản Trị", [RoleAdmin])
    };

    private User _currentUser;

    public UserContext(string initialUsername = "sales_user")
    {
        if (!MockUsers.TryGetValue(initialUsername, out var user))
        {
            throw new ArgumentException($"User '{initialUsername}' không tồn tại trong danh sách mẫu.", nameof(initialUsername));
        }
        _currentUser = user;
    }

    public User CurrentUser => _currentUser;

    public void SwitchUser(string username)
    {
        if (!MockUsers.TryGetValue(username, out var user))
        {
            throw new ArgumentException($"User '{username}' không tồn tại trong danh sách mẫu.", nameof(username));
        }
        _currentUser = user;
    }

    public bool HasPermission(string permission) =>
        _currentUser.EffectivePermissions.Contains(permission);
}
