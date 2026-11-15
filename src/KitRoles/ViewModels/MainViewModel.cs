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
    private V51Client? _client;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<int, KitInfo> _kits = KitCache.LoadKits();

    /// <summary>
    /// The V51 (2.10) answers reads of a few bytes inside a block, which is ~25x faster than whole blocks.
    /// This is not documented, so the first unanswered partial read switches to whole-block reads for the session.
    /// </summary>
    private bool _partialReads = true;

    public ObservableCollection<KitRowViewModel> Kits { get; } = [];
    public IReadOnlyList<KitGroupViewModel> Groups { get; }
    public IReadOnlyList<AuxRoleViewModel> Roles { get; } = RolePlanner.Aux.Select(a => new AuxRoleViewModel(a.Label)).ToList();

    [ObservableProperty] private KitRowViewModel? _selectedKit;
    [ObservableProperty] private string _connection = "Not connected";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _selection = "";
    [ObservableProperty] private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(RescanCommand), nameof(ApplyCommand), nameof(UndoCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(RescanCommand), nameof(ApplyCommand), nameof(UndoCommand),
        nameof(ConnectCommand), nameof(CancelCommand))]
    private bool _isBusy;

    public MainViewModel()
    {
        for (int n = 1; n <= KitMap.KitCount; n++)
        {
            var row = new KitRowViewModel(n);
            if (_kits.TryGetValue(n, out var info)) row.Apply(info);
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
        RescanCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        if (e.PropertyName == nameof(KitRowViewModel.IsChecked)) UpdatePlan((KitRowViewModel)sender!);
    }

    private bool CanRun() => IsConnected && !IsBusy;
    private bool CanConnect() => !IsBusy;
    private bool CanRunChecked() => CanRun() && Kits.Any(k => k.IsChecked);
    private bool CanApply() => CanRunChecked() && Roles.Any(r => r.Role != Role.Keep);

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

    /// <summary>Reads all kit headers (~15 s) and scans every kit that is new or whose header changed.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        await RunAsync("Checking", Kits, async (row, ct) =>
        {
            var header = await ReadAsync(KitMap.Address(row.Number, "common"), KitMap.Common.Size, ct);
            var hex = Convert.ToHexStringLower(header);
            if (_kits.TryGetValue(row.Number, out var known) && known.Header == hex) { row.Apply(known); return; }
            if (KitInfo.DecodeName(header, 0, 16) == "User Kit")
            {
                _kits.Remove(row.Number);
                row.ApplyHeader(header);
                return;
            }
            Store(row, await ScanAsync(row.Number, hex, ct));
        });
        KitCache.SaveKits(_kits);
    }

    /// <summary>Scans the checked kits again, e.g. after editing sounds on the module.</summary>
    [RelayCommand(CanExecute = nameof(CanRunChecked))]
    private async Task RescanAsync()
    {
        await RunAsync("Scanning", Checked(), async (row, ct) =>
        {
            var header = await ReadAsync(KitMap.Address(row.Number, "common"), KitMap.Common.Size, ct);
            Store(row, await ScanAsync(row.Number, Convert.ToHexStringLower(header), ct));
        });
        KitCache.SaveKits(_kits);
    }

    /// <summary>
    /// For each checked kit: scan it fresh, read the blocks involved, back up the AUX blocks that will change,
    /// write them and verify each by reading it back.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var kits = Checked();
        var roles = string.Join(", ", Roles.Where(r => r.Role != Role.Keep).Select(r => $"{r.Label} = {r.Role.Label()}"));
        if (!Confirm($"Write {roles} to {kits.Count} kits?\n\nThe changed pads are backed up first; use Undo to restore.")) return;

        var session = KitCache.NewBackupSession();
        await RunAsync("Applying to", kits, async (row, ct) =>
        {
            var header = Convert.ToHexStringLower(await ReadAsync(KitMap.Address(row.Number, "common"), KitMap.Common.Size, ct));
            var info = await ScanAsync(row.Number, header, ct);
            var plans = Roles.Select((r, i) => RolePlanner.Choose(info, i, r.Role)).OfType<PadPlan>()
                .Where(p => !p.AlreadyDone).ToList();
            if (plans.Count > 0)
            {
                var current = new Dictionary<string, byte[]>();
                foreach (var name in plans.SelectMany(p => p.Target.Blocks.Concat(p.Source.Blocks)).Distinct())
                    current[name] = await ReadBlockAsync(row.Number, name, ct);

                var writes = plans.SelectMany(p => RolePlanner.Build(p, name => current[name]))
                    .Where(kv => !kv.Value.AsSpan().SequenceEqual(current[kv.Key])).ToList();
                var backup = Snapshot(row.Number, writes.Select(w => w.Key), current);
                KitCache.SaveBackup(session, backup);
                await WriteKitAsync(row.Number, writes, backup);
                info = await ScanAsync(row.Number, header, ct);
            }
            Store(row, info);
        });
        KitCache.SaveKits(_kits);
    }

    /// <summary>Restores the pads changed by the most recent apply from its backup, then drops that backup.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task UndoAsync()
    {
        var (session, backups) = KitCache.LoadLastBackup();
        if (session is null || backups.Count == 0) { Status = "Nothing to undo."; return; }
        if (!Confirm($"Restore the pads of {backups.Count} kits to their state before the apply of {session}?")) return;

        var byKit = backups.ToDictionary(b => b.Kit);
        bool ok = true;
        await RunAsync("Restoring", Kits.Where(k => byKit.ContainsKey(k.Number)).ToList(), async (row, ct) =>
        {
            var backup = byKit[row.Number];
            var live = new Dictionary<string, byte[]>();
            foreach (var name in backup.Blocks.Keys) live[name] = await ReadBlockAsync(row.Number, name, ct);
            var writes = backup.Blocks.Keys.Select(n => new KeyValuePair<string, byte[]>(n, backup.Block(n)))
                .Where(kv => !kv.Value.AsSpan().SequenceEqual(live[kv.Key])).ToList();
            await WriteKitAsync(row.Number, writes, Snapshot(row.Number, live.Keys, live));
            var header = await ReadAsync(KitMap.Address(row.Number, "common"), KitMap.Common.Size, ct);
            Store(row, await ScanAsync(row.Number, Convert.ToHexStringLower(header), ct));
        }, () => ok = false);
        KitCache.SaveKits(_kits);
        if (ok) KitCache.DeleteBackup(session);
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    private List<KitRowViewModel> Checked() => Kits.Where(k => k.IsChecked).ToList();

    private void Store(KitRowViewModel row, KitInfo info)
    {
        _kits[row.Number] = info;
        row.Apply(info);
        UpdatePlan(row);
    }

    private void UpdatePlans()
    {
        foreach (var k in Kits) UpdatePlan(k);
    }

    private void UpdatePlan(KitRowViewModel row)
    {
        if (!row.IsChecked || Roles.All(r => r.Role == Role.Keep)) { row.Plan = ""; return; }
        if (row.Info is null) { row.Plan = "Not scanned"; return; }
        row.Plan = string.Join(" · ", Roles.Select((r, i) => RolePlanner.Choose(row.Info, i, r.Role))
            .OfType<PadPlan>().Select(p => p.Text));
    }

    private static KitSnapshot Snapshot(int kit, IEnumerable<string> names, Dictionary<string, byte[]> blocks) => new()
    {
        Kit = kit,
        ScannedAt = DateTime.Now,
        Blocks = names.ToDictionary(n => n, n => Convert.ToHexStringLower(blocks[n])),
    };

    /// <summary>Reads instrument, bank and V-EDIT 2 of all 84 layers (~0.35 s with partial reads).</summary>
    private async Task<KitInfo> ScanAsync(int kit, string header, CancellationToken ct)
    {
        var info = new KitInfo { Header = header, ScannedAt = DateTime.Now };
        for (int unit = 0; unit < 28; unit++)
            for (int layer = 0; layer < 3; layer++)
            {
                var name = KitMap.LayerBlock(unit, layer);
                var head = await ReadPartAsync(kit, name, 0, 9, ct);
                var vEdit2 = await ReadPartAsync(kit, name, LayerInfo.VEdit2Offset, 4, ct);
                info.Layers.Add(LayerInfo.From(head, vEdit2));
            }
        return info;
    }

    private async Task<byte[]> ReadPartAsync(int kit, string block, int offset, int length, CancellationToken ct)
    {
        if (_partialReads)
        {
            if (await _client!.ReadAsync(KitMap.Address(kit, block) + offset, length, ct) is { } part) return part;
            _partialReads = false;
        }
        return (await ReadBlockAsync(kit, block, ct))[offset..(offset + length)];
    }

    private Task<byte[]> ReadBlockAsync(int kit, string name, CancellationToken ct = default) =>
        ReadAsync(KitMap.Address(kit, name), KitMap.Blocks[name].Size, ct);

    private async Task<byte[]> ReadAsync(int address, int size, CancellationToken ct) =>
        await _client!.ReadAsync(address, size, ct)
        ?? throw new TimeoutException($"No answer from the module (address {address:X}).");

    /// <summary>
    /// Writes the blocks of one kit and verifies each by reading it back. Not cancellable, so a kit is never left
    /// half-written. On failure, blocks already written are put back from <paramref name="backup"/>.
    /// </summary>
    private async Task WriteKitAsync(int kit, IReadOnlyList<KeyValuePair<string, byte[]>> blocks, KitSnapshot backup)
    {
        var touched = new List<string>();
        try
        {
            foreach (var (name, data) in blocks)
            {
                touched.Add(name);
                await _client!.WriteAsync(KitMap.Address(kit, name), data);
                if (!Matches(name, data, await ReadBlockAsync(kit, name)))
                    throw new InvalidOperationException($"Verification failed for block {name}: the module holds different data.");
            }
        }
        catch (Exception ex)
        {
            try
            {
                foreach (var name in touched) await _client!.WriteAsync(KitMap.Address(kit, name), backup.Block(name));
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
            ? back.AsSpan(0, 9).SequenceEqual(sent.AsSpan(0, 9)) &&
              back.AsSpan(LayerInfo.VEdit2Offset, 4).SequenceEqual(sent.AsSpan(LayerInfo.VEdit2Offset, 4))
            : back.AsSpan().SequenceEqual(sent);

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
        Status = $"{Kits.Count(k => k.State == KitState.UpToDate)} kits scanned · " +
                 $"{Kits.Count(k => k.IsBlank)} blank user kits · {Kits.Count(k => k.State == KitState.Error)} errors" +
                 (_partialReads ? "" : " · fast reads unavailable, using full blocks");
    }
}
