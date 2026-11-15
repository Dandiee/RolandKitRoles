using KitRoles.Midi;

namespace KitRoles.Model;

public enum Role { Keep, Snare, Tom1, Tom2, Tom3, Tom4, Crash1, Crash2, Ride, China, Splash, ClosedHiHat }

/// <summary>A physical pad: head/rim unit indices (0-based, see KitMap.Units) and its KitPad index (1-based).</summary>
public record PadRef(string Label, int Head, int Rim, int Pad);

/// <summary>What will be written for one AUX pad of one kit (Blocks empty = already in place).</summary>
public record PadChange(string Text, Dictionary<string, byte[]> Blocks);

/// <summary>Adjusts a copied layer block; gets the target block name (e.g. "A23") and the block.</summary>
internal delegate void LayerPatch(string blockName, byte[] block);

public static class RolePlanner
{
    public static readonly PadRef Snare = new("Snare", 1, 2, 2), Tom1 = new("Tom 1", 3, 4, 3), Tom2 = new("Tom 2", 5, 6, 4),
        Tom3 = new("Tom 3", 7, 8, 5), Tom4 = new("Tom 4", 9, 10, 6), HiHat = new("Hi-hat", 11, 12, 7),
        Crash1 = new("Crash 1", 13, 14, 8), Crash2 = new("Crash 2", 15, 16, 9), Ride = new("Ride", 17, 18, 10);

    public static readonly PadRef[] Aux =
        [new("AUX1", 20, 21, 11), new("AUX2", 22, 23, 12), new("AUX3", 24, 25, 13), new("AUX4", 26, 27, 14)];

    /// <summary>Where to look for a kit's own China/splash, in order of preference.</summary>
    private static readonly PadRef[] SearchOrder = [Crash1, Crash2, Ride, .. Aux, HiHat, Snare, Tom1, Tom2, Tom3, Tom4];

    private const int VEdit2 = 0x31 + 4; // V-EDIT parameter 2 = fixed position for hi-hat sounds on non-HH pads
    private const int HiHatClosed = 2;

    public static string Label(this Role r) => r switch
    {
        Role.Tom1 => "Tom 1", Role.Tom2 => "Tom 2", Role.Tom3 => "Tom 3", Role.Tom4 => "Tom 4",
        Role.Crash1 => "Crash 1", Role.Crash2 => "Crash 2", Role.ClosedHiHat => "Closed hi-hat", _ => r.ToString(),
    };

    /// <summary>Plans one AUX pad (0..3). Returns null for Keep.</summary>
    public static PadChange? Plan(KitSnapshot s, int auxIndex, Role role)
    {
        var target = Aux[auxIndex];
        var (source, text, patch) = role switch
        {
            Role.Keep => (null, "", null),
            Role.Snare => (Snare, "Snare", null),
            Role.Tom1 => (Tom1, "Tom 1", null),
            Role.Tom2 => (Tom2, "Tom 2", null),
            Role.Tom3 => (Tom3, "Tom 3", null),
            Role.Tom4 => (Tom4, "Tom 4", null),
            Role.Crash1 => (Crash1, "Crash 1", null),
            Role.Crash2 => (Crash2, "Crash 2", null),
            Role.Ride => (Ride, "Ride", null),
            Role.ClosedHiHat => (HiHat, "closed hi-hat", (LayerPatch)CloseHiHat),
            Role.China => FindOrDefault(s, target, InstType.China, "Mn Bz Trad Ch", "Mn Bz Trad ChE"),
            Role.Splash => FindOrDefault(s, target, InstType.Splash, "Mn Bz Dual Sp", "Mn Bz Dual SpE"),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        if (source is null) return null;

        var blocks = new Dictionary<string, byte[]>
        {
            [$"uc{target.Head + 1}"] = s.Block($"uc{source.Head + 1}"),
            [$"uc{target.Rim + 1}"] = s.Block($"uc{source.Rim + 1}"),
            [$"pad{target.Pad}"] = s.Block($"pad{source.Pad}"),
        };
        foreach (var layer in "ABC")
        {
            blocks[$"{layer}{target.Head + 1}"] = s.Block($"{layer}{source.Head + 1}");
            blocks[$"{layer}{target.Rim + 1}"] = s.Block($"{layer}{source.Rim + 1}");
        }
        if (patch is not null)
            foreach (var (name, block) in blocks.Where(kv => kv.Key[0] is 'A' or 'B' or 'C')) patch(name, block);

        // Only write blocks that differ from what the kit has now.
        var changed = blocks.Where(kv => !kv.Value.AsSpan().SequenceEqual(s.Block(kv.Key))).ToDictionary();
        return new PadChange(changed.Count == 0 ? $"{target.Label} already {text}" : $"{target.Label} ← {text}", changed);
    }

    /// <summary>Uses the kit's own China/splash pad; otherwise Crash 1's setup with a default built-in sound.</summary>
    private static (PadRef?, string, LayerPatch?) FindOrDefault(KitSnapshot s, PadRef target, InstType type,
        string headName, string edgeName)
    {
        var label = type.ToString();
        if (HasType(s, target, type)) return (target, label, null);
        if (SearchOrder.FirstOrDefault(p => p != target && HasType(s, p, type)) is { } own)
            return (own, $"{label} (from {own.Label})", null);

        int head = InstrumentCatalog.FindBuiltIn(headName), edge = InstrumentCatalog.FindBuiltIn(edgeName);
        string headLayerA = $"A{target.Head + 1}";
        return (Crash1, $"{label} (default sound)", (name, b) =>
        {
            if (name[0] != 'A') { b[0] = 0; return; } // layers B/C off
            b[0] = 1;
            KitMap.SetNib(b, 0x01, name == headLayerA ? head : edge);
            KitMap.SetNib(b, 0x05, 0); // built-in bank
        });
    }

    private static bool HasType(KitSnapshot s, PadRef pad, InstType type) =>
        Enumerable.Range(0, 3).Select(l => s.GetLayer(pad.Head, l)).Any(l => l.On && l.Type == type);

    private static void CloseHiHat(string name, byte[] layer)
    {
        if (Layer.Decode(layer).Type == InstType.HiHat) KitMap.SetNib(layer, VEdit2, HiHatClosed);
    }
}
