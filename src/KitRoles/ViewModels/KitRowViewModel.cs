using CommunityToolkit.Mvvm.ComponentModel;
using KitRoles.Midi;
using KitRoles.Model;

namespace KitRoles.ViewModels;

public enum KitState { NotScanned, UpToDate, Changed, Error }

public record PadRow(string Pad, string A, string B, string C);

public partial class KitRowViewModel(int number) : ObservableObject
{
    public int Number { get; } = number;

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subName = "";
    [ObservableProperty] private string _header = "";

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
    public KitSnapshot? Snapshot { get; private set; }
    public bool IsBlank => Category == KitCategory.UserKit;

    /// <summary>Takes over a deep scan (from cache or a fresh read).</summary>
    public void ApplySnapshot(KitSnapshot snapshot)
    {
        Snapshot = snapshot;
        Header = snapshot.Header;
        Name = snapshot.Name;
        SubName = snapshot.SubName;
        Category = snapshot.Category;
        ScannedAt = snapshot.ScannedAt;
        State = KitState.UpToDate;
        Error = null;
        Aux1 = Describe(snapshot, 20);
        Aux2 = Describe(snapshot, 22);
        Aux3 = Describe(snapshot, 24);
        Aux4 = Describe(snapshot, 26);
        Pads = Enumerable.Range(0, 28)
            .Select(u => new PadRow(KitMap.Units[u], Cell(snapshot, u, 0), Cell(snapshot, u, 1), Cell(snapshot, u, 2)))
            .ToList();
    }

    /// <summary>Takes over a freshly read header and compares it with the deep scan.</summary>
    public void ApplyHeader(byte[] common)
    {
        Header = Convert.ToHexStringLower(common);
        Name = KitSnapshot.DecodeName(common, 0, 16);
        SubName = KitSnapshot.DecodeName(common, 16, 64);
        Error = null;
        State = Snapshot is null ? KitState.NotScanned
            : Snapshot.Header == Header ? KitState.UpToDate
            : KitState.Changed;
    }

    public void Fail(string message)
    {
        State = KitState.Error;
        Error = message;
    }

    private static string Describe(KitSnapshot s, int unit)
    {
        var l = s.GetLayer(unit, 0);
        return l.On && !l.IsEmpty ? l.Name : "";
    }

    private static string Cell(KitSnapshot s, int unit, int layer)
    {
        var l = s.GetLayer(unit, layer);
        if (l.IsEmpty) return "";
        return l.On ? l.Name : $"({l.Name})";
    }
}
