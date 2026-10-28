namespace ErpChatbot.Core.Models.Rbac;

public record Role(string Name, string Description, IReadOnlySet<string> Permissions);

public record User(
    string Id,
    string Username,
    string FullName,
    IReadOnlyList<Role> Roles)
{
    public IReadOnlySet<string> EffectivePermissions { get; } =
        Roles.SelectMany(r => r.Permissions).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
