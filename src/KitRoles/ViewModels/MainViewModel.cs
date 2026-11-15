using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitRoles.Midi;
using KitRoles.Model;

namespace KitRoles.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly string[] ProbeBlocks = ["A1", "A2", "A21"]; // kick, snare, AUX1 layer A

    /// <summary>Every block an apply can touch: AUX unit commons, layers and pads. Undo restores these.</summary>
    private static readonly string[] AuxBlocks =
        RolePlanner.Aux.SelectMany(p => new[] { $"uc{p.Head + 1}", $"uc{p.Rim + 1}", $"pad{p.Pad}" }
            .Concat("ABC".SelectMany(l => new[] { $"{l}{p.Head + 1}", $"{l}{p.Rim + 1}" }))).ToArray();

    private V51Client? _client;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<int, IndexEntry> _index = KitCache.LoadIndex();

    public ObservableCollection<KitRowViewModel> Kits { get; } = [];
    public IReadOnlyList<KitGroupViewModel> Groups { get; }
    public IReadOnlyList<AuxRoleViewModel> Roles { get; } = RolePlanner.Aux.Select(a => new AuxRoleViewModel(a.Label)).ToList();

    [ObservableProperty] private KitRowViewModel? _selectedKit;
    [ObservableProperty] private string _connection = "Not connected";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _selection = "";
    [ObservableProperty] private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(DeepScanCommand), nameof(ApplyCommand), nameof(UndoCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(DeepScanCommand), nameof(ApplyCommand), nameof(UndoCommand),
        nameof(ConnectCommand), nameof(CancelCommand))]
    private bool _isBusy;

    public MainViewModel()
    {
        for (int n = 1; n <= KitMap.KitCount; n++)
        {
            var row = new KitRowViewModel(n);
            if (KitCache.Load(n) is { } snapshot) row.ApplySnapshot(snapshot);
            else if (_index.TryGetValue(n, out var entry)) row.Category = entry.Category;
            row.PropertyChanged += OnKitChanged;
            Kits.Add(row);
        }
        Groups =
        [
            new("All", Kits, _ => true),
            new("Built-in", Kits, k => k.Category == KitCategory.BuiltIn),
            new("Expansion", Kits, k => k.Category == KitCategory.Expansion),
            new("Kit packs", Kits, k => k.Category == KitCategory.KitPack),
            new("User kits", Kits, k => k.Category == KitCategory.UserKit),
        ];
        foreach (var r in Roles) r.PropertyChanged += (_, _) => { UpdatePlans(); ApplyCommand.NotifyCanExecuteChanged(); };
        UpdateSummary();
    }

    private void OnKitChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(KitRowViewModel.IsChecked) or nameof(KitRowViewModel.Category))) return;
        foreach (var g in Groups) g.Refresh();
        Selection = $"{Kits.Count(k => k.IsChecked)} kits checked";
        DeepScanCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        if (e.PropertyName == nameof(KitRowViewModel.IsChecked)) UpdatePlan((KitRowViewModel)sender!);
    }

    private bool CanRun() => IsConnected && !IsBusy;
    private bool CanConnect() => !IsBusy;
    private bool CanDeepScan() => CanRun() && Kits.Any(k => k.IsChecked);
    private bool CanApply() => CanDeepScan() && Roles.Any(r => r.Role != Role.Keep);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        _client?.Dispose();
        _client = V51Client.TryOpen();
        var version = _client is null ? null : await _client.IdentifyAsync();
        IsConnected = version is not null;
        Connection = IsConnected ? $"V51 connected · firmware {version}" : "V51 not found";
        if (IsConnected) await RefreshAsync();
    }

    /// <summary>Reads all kit headers (~15 s); probes the category of kits that are new or changed.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        await RunAsync("Reading header of", Kits, async (row, ct) =>
        {
            row.ApplyHeader(await ReadAsync(KitMap.KitBase(row.Number), KitMap.Common.Size, ct));
            if (row.State == KitState.UpToDate) return; // category known from the deep scan
            if (_index.TryGetValue(row.Number, out var known) && known.Header == row.Header)
                row.Category = known.Category;
            else
                row.Category = await ProbeAsync(row, ct);
            _index[row.Number] = new IndexEntry(row.Header, row.Category);
        });
        KitCache.SaveIndex(_index);
    }

    [RelayCommand(CanExecute = nameof(CanDeepScan))]
    private async Task DeepScanAsync()
    {
        await RunAsync("Deep scanning", Checked(), async (row, ct) => Store(row, await DeepReadAsync(row.Number, ct)));
        KitCache.SaveIndex(_index);
    }

    /// <summary>
    /// For each checked kit: re-read it live, back it up, write the changed AUX blocks, read them back to verify.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var kits = Checked();
        var roles = string.Join(", ", Roles.Where(r => r.Role != Role.Keep).Select(r => $"{r.Label} = {r.Role.Label()}"));
        if (!Confirm($"Write {roles} to {kits.Count} kits?\n\nEach kit is backed up first; use Undo to restore.")) return;

        var session = KitCache.NewBackupSession();
        await RunAsync("Applying to", kits, async (row, ct) =>
        {
            var fresh = await DeepReadAsync(row.Number, ct);
            KitCache.SaveBackup(session, fresh);
            var blocks = Roles.Select((r, i) => RolePlanner.Plan(fresh, i, r.Role))
                .OfType<PadChange>().SelectMany(c => c.Blocks).ToList();
            foreach (var (name, stored) in await WriteKitAsync(row.Number, blocks, backup: fresh))
                fresh.Blocks[name] = Convert.ToHexStringLower(stored);
            Store(row, fresh);
        });
        KitCache.SaveIndex(_index);
    }

    /// <summary>Restores the AUX pads of the most recent apply from its backup, then drops that backup.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task UndoAsync()
    {
        var (session, backups) = KitCache.LoadLastBackup();
        if (session is null || backups.Count == 0) { Status = "Nothing to undo."; return; }
        if (!Confirm($"Restore the AUX pads of {backups.Count} kits to their state before the apply of {session}?")) return;

        var byKit = backups.ToDictionary(b => b.Kit);
        bool ok = true;
        await RunAsync("Restoring", Kits.Where(k => byKit.ContainsKey(k.Number)).ToList(), async (row, ct) =>
        {
            var backup = byKit[row.Number];
            var current = KitCache.Load(row.Number) ?? backup;
            var differing = new List<KeyValuePair<string, byte[]>>();
            foreach (var name in AuxBlocks)
            {
                var live = await ReadBlockAsync(row.Number, name, ct);
                current.Blocks[name] = Convert.ToHexStringLower(live);
                if (!live.AsSpan().SequenceEqual(backup.Block(name))) differing.Add(new(name, backup.Block(name)));
            }
            foreach (var (name, stored) in await WriteKitAsync(row.Number, differing, backup: current))
                current.Blocks[name] = Convert.ToHexStringLower(stored);
            Store(row, current);
        }, () => ok = false);
        if (ok) KitCache.DeleteBackup(session);
    }

    /// <summary>Checks every non-blank kit whose deep scan is missing or outdated.</summary>
    [RelayCommand]
    private void SelectNeedsScan()
    {
        foreach (var k in Kits) k.IsChecked = !k.IsBlank && k.State != KitState.UpToDate;
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    private List<KitRowViewModel> Checked() => Kits.Where(k => k.IsChecked).ToList();

    private void Store(KitRowViewModel row, KitSnapshot snapshot)
    {
        KitCache.Save(snapshot);
        row.ApplySnapshot(snapshot);
        _index[row.Number] = new IndexEntry(row.Header, row.Category);
        UpdatePlan(row);
    }

    private void UpdatePlans()
    {
        foreach (var k in Kits) UpdatePlan(k);
    }

    private void UpdatePlan(KitRowViewModel row)
    {
        if (!row.IsChecked || Roles.All(r => r.Role == Role.Keep)) { row.Plan = ""; return; }
        if (row.Snapshot is null) { row.Plan = "Deep scan needed for a preview"; return; }
        row.Plan = string.Join(" · ", Roles.Select((r, i) => RolePlanner.Plan(row.Snapshot, i, r.Role))
            .OfType<PadChange>().Select(c => c.Text));
    }

    private async Task<KitSnapshot> DeepReadAsync(int kit, CancellationToken ct)
    {
        var snapshot = new KitSnapshot { Kit = kit, ScannedAt = DateTime.Now };
        foreach (var block in KitMap.KitBlocks)
            snapshot.Blocks[block.Name] =
                Convert.ToHexStringLower(await ReadAsync(KitMap.KitBase(kit) + block.Offset, block.Size, ct));
        return snapshot;
    }

    /// <summary>
    /// Writes the blocks of one kit and verifies each by reading it back; returns what the module actually stored.
    /// Not cancellable, so a kit is never left half-written. On failure, blocks already written are put back
    /// from <paramref name="backup"/> before the error is reported.
    /// </summary>
    private async Task<Dictionary<string, byte[]>> WriteKitAsync(int kit, IReadOnlyList<KeyValuePair<string, byte[]>> blocks,
        KitSnapshot backup)
    {
        var stored = new Dictionary<string, byte[]>();
        try
        {
            foreach (var (name, data) in blocks)
            {
                stored[name] = data; // mark as touched before writing, so a failure also rolls this block back
                await _client!.WriteAsync(BlockAddress(kit, name), data);
                var back = await ReadBlockAsync(kit, name);
                if (!Matches(name, data, back))
                    throw new InvalidOperationException($"Verification failed for block {name}: the module holds different data.");
                stored[name] = back;
            }
            return stored;
        }
        catch (Exception ex)
        {
            try
            {
                foreach (var name in stored.Keys) await _client!.WriteAsync(BlockAddress(kit, name), backup.Block(name));
            }
            catch (Exception restoreError)
            {
                throw new InvalidOperationException(
                    $"{ex.Message} Restoring the kit also failed ({restoreError.Message}); check it on the module.", ex);
            }
            throw new InvalidOperationException($"{ex.Message} The kit was restored to its previous state.", ex);
        }
    }

    /// <summary>
    /// Layer blocks must keep on/off, instrument, bank and V-EDIT 2 (hi-hat position); the module may adjust
    /// dependent values itself (e.g. V-EDIT 1 when a hi-hat is fixed closed). Other blocks must match exactly.
    /// </summary>
    private static bool Matches(string name, byte[] sent, byte[] back) =>
        name[0] is 'A' or 'B' or 'C'
            ? back.AsSpan(0, 9).SequenceEqual(sent.AsSpan(0, 9)) && back.AsSpan(0x35, 4).SequenceEqual(sent.AsSpan(0x35, 4))
            : back.AsSpan().SequenceEqual(sent);

    private static int BlockAddress(int kit, string name) =>
        KitMap.KitBase(kit) + KitMap.KitBlocks.First(b => b.Name == name).Offset;

    private Task<byte[]> ReadBlockAsync(int kit, string name, CancellationToken ct = default) =>
        ReadAsync(BlockAddress(kit, name), KitMap.KitBlocks.First(b => b.Name == name).Size, ct);

    private async Task<KitCategory> ProbeAsync(KitRowViewModel row, CancellationToken ct)
    {
        if (row.Name == "User Kit") return KitCategory.UserKit;
        var banks = new List<int>();
        foreach (var name in ProbeBlocks)
        {
            var block = KitMap.KitBlocks.First(b => b.Name == name);
            banks.Add(Layer.Decode(await ReadAsync(KitMap.KitBase(row.Number) + block.Offset, block.Size, ct)).Bank);
        }
        return KitClassifier.Classify(row.Name, row.SubName, banks);
    }

    private async Task<byte[]> ReadAsync(int address, int size, CancellationToken ct) =>
        await _client!.ReadAsync(address, size, ct)
        ?? throw new TimeoutException($"No answer from the module (address {address:X}).");

    private static bool Confirm(string message) =>
        MessageBox.Show(message, "KitRoles", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    /// <summary>Runs work per kit; a failing kit is marked red and the run continues.</summary>
    private async Task RunAsync(string verb, IReadOnlyList<KitRowViewModel> rows,
        Func<KitRowViewModel, CancellationToken, Task> work, Action? onFailure = null)
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            for (int i = 0; i < rows.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                Status = $"{verb} kit {rows[i].Number} ({i + 1}/{rows.Count})…";
                Progress = 100.0 * i / rows.Count;
                try { await work(rows[i], _cts.Token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    rows[i].Fail(ex.Message);
                    onFailure?.Invoke();
                }
            }
            UpdateSummary();
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
            onFailure?.Invoke();
        }
        finally
        {
            Progress = 0;
            IsBusy = false;
        }
    }

    private void UpdateSummary()
    {
        var relevant = Kits.Where(k => !k.IsBlank).ToList();
        Status = $"{relevant.Count(k => k.State == KitState.UpToDate)} deep scanned · " +
                 $"{relevant.Count(k => k.State == KitState.Changed)} changed · " +
                 $"{relevant.Count(k => k.State == KitState.NotScanned)} not scanned · " +
                 $"{Kits.Count(k => k.State == KitState.Error)} errors · {Kits.Count - relevant.Count} blank user kits";
    }
}
