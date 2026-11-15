using CommunityToolkit.Mvvm.ComponentModel;
using KitRoles.Model;

namespace KitRoles.ViewModels;

public record RoleOption(Role Value, string Label);

/// <summary>The role chosen for one AUX pad.</summary>
public partial class AuxRoleViewModel(string label) : ObservableObject
{
    public static IReadOnlyList<RoleOption> Options { get; } =
        Enum.GetValues<Role>().Select(r => new RoleOption(r, r.Label())).ToList();

    public string Label { get; } = label;

    [ObservableProperty] private Role _role;
}
