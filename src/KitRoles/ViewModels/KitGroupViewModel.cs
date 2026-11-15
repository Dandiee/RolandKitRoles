using CommunityToolkit.Mvvm.ComponentModel;

namespace KitRoles.ViewModels;

/// <summary>A group checkbox (All, Built-in, ...): checked / unchecked / mixed, toggles all its kits.</summary>
public partial class KitGroupViewModel(string label, IEnumerable<KitRowViewModel> kits, Func<KitRowViewModel, bool> member)
    : ObservableObject
{
    public string Label { get; } = label;

    private IEnumerable<KitRowViewModel> Members => kits.Where(member);

    public int Count => Members.Count();

    public bool? IsChecked
    {
        get
        {
            int total = 0, on = 0;
            foreach (var k in Members) { total++; if (k.IsChecked) on++; }
            return on == 0 ? false : on == total ? true : null;
        }
        set
        {
            foreach (var k in Members.ToList()) k.IsChecked = value == true;
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(Count));
    }
}
