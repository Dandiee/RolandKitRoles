using CommunityToolkit.Mvvm.ComponentModel;
using KitRoles.Midi;
using KitRoles.Model;

namespace KitRoles.ViewModels;

public enum KitState { NotScanned, UpToDate, Error }

public record PadRow(string Pad, string A, string B, string C);

public partial class KitRowViewModel(int number) : ObservableObject
{
    public int Number { get; } = number;

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoryText))]
    private KitCategory _category;

    [ObservableProperty] private KitState _state;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _plan = "";
    [ObservableProperty] private DateTime? _scannedAt;
    [ObservableProperty] private string _aux1 = "";
    [ObservableProperty] private string _aux2 = "";
    [ObservableProperty] private string _aux3 = "";
    [ObservableProperty] private string _aux4 = "";
    [ObservableProperty] private IReadOnlyList<PadRow> _pads = [];

    public string CategoryText => Category.Label();
    public KitInfo? Info { get; private set; }
    public bool IsBlank => Name == "User Kit";

    /// <summary>Shows a scanned kit.</summary>
    public void Apply(KitInfo info)
    {
        Info = info;
        Name = info.Name;
        SubName = info.SubName;
        Category = info.Category;
        ScannedAt = info.ScannedAt;
        State = KitState.UpToDate;
        Error = null;
        Aux1 = Describe(info, 20);
        Aux2 = Describe(info, 22);
        Aux3 = Describe(info, 24);
        Aux4 = Describe(info, 26);
        Pads = Enumerable.Range(0, 28)
            .Select(u => new PadRow(KitMap.Units[u], Cell(info, u, 0), Cell(info, u, 1), Cell(info, u, 2)))
            .ToList();
    }

    /// <summary>Shows only the header (blank user kits are not scanned).</summary>
    public void ApplyHeader(byte[] common)
    {
        Info = null;
        Name = KitInfo.DecodeName(common, 0, 16);
        SubName = KitInfo.DecodeName(common, 16, 64);
        Category = KitCategory.UserKit;
        State = KitState.NotScanned;
        Error = null;
        Aux1 = Aux2 = Aux3 = Aux4 = "";
        Pads = [];
    }

    public void Fail(string message)
    {
        State = KitState.Error;
        Error = message;
    }

    private static string Describe(KitInfo k, int unit) => k.Get(unit, 0) is { IsActive: true } l ? l.Name : "";

    private static string Cell(KitInfo k, int unit, int layer)
    {
        var l = k.Get(unit, layer);
        if (l.IsEmpty) return "";
        return l.On ? l.Name : $"({l.Name})";
    }
}
