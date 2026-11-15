using KitRoles.Midi;

namespace KitRoles.Model;

public enum Role { Keep, Snare, Tom1, Tom2, Tom3, Tom4, Crash1, Crash2, Ride, China, Splash, ClosedHiHat }

/// <summary>A physical pad: head/rim unit indices (0-based, see KitMap.Units) and its KitPad index (1-based).</summary>
public record PadRef(string Label, int Head, int Rim, int Pad)
{
    /// <summary>All blocks that make up this pad: unit commons, layers A/B/C of head and rim, KitPad.</summary>
    public IEnumerable<string> Blocks =>
        new[] { $"uc{Head + 1}", $"uc{Rim + 1}", $"pad{Pad}" }
            .Concat("ABC".SelectMany(l => new[] { $"{l}{Head + 1}", $"{l}{Rim + 1}" }));
}

/// <summary>Adjusts a copied layer block; gets the target block name (e.g. "A23") and the block.</summary>
public delegate void LayerPatch(string blockName, byte[] block);

/// <summary>What one AUX role means for one kit: copy Source onto Target, then apply Patch to the layers.</summary>
public record PadPlan(PadRef Target, PadRef Source, string Text, bool AlreadyDone, LayerPatch? Patch);

public static class RolePlanner
{
    public static readonly PadRef Snare = new("Snare", 1, 2, 2), Tom1 = new("Tom 1", 3, 4, 3), Tom2 = new("Tom 2", 5, 6, 4),
        Tom3 = new("Tom 3", 7, 8, 5), Tom4 = new("Tom 4", 9, 10, 6), HiHat = new("Hi-hat", 11, 12, 7),
        Crash1 = new("Crash 1", 13, 14, 8), Crash2 = new("Crash 2", 15, 16, 9), Ride = new("Ride", 17, 18, 10);

    public static readonly PadRef[] Aux =
        [new("AUX1", 20, 21, 11), new("AUX2", 22, 23, 12), new("AUX3", 24, 25, 13), new("AUX4", 26, 27, 14)];

    /// <summary>Where to look for a kit's own China/splash, in order of preference.</summary>
    private static readonly PadRef[] SearchOrder = [Crash1, Crash2, Ride, .. Aux, HiHat, Snare, Tom1, Tom2, Tom3, Tom4];

    private const int HiHatClosed = 2;

    public static string Label(this Role r) => r switch
    {
        Role.Tom1 => "Tom 1", Role.Tom2 => "Tom 2", Role.Tom3 => "Tom 3", Role.Tom4 => "Tom 4",
        Role.Crash1 => "Crash 1", Role.Crash2 => "Crash 2", Role.ClosedHiHat => "Closed hi-hat", _ => r.ToString(),
    };

    /// <summary>Picks the source pad for one AUX pad (0..3) from the kit overview. Returns null for Keep.</summary>
    public static PadPlan? Choose(KitInfo k, int auxIndex, Role role)
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
            Role.China => FindOrDefault(k, target, InstType.China, "Mn Bz Trad Ch", "Mn Bz Trad ChE"),
            Role.Splash => FindOrDefault(k, target, InstType.Splash, "Mn Bz Dual Sp", "Mn Bz Dual SpE"),
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        if (source is null) return null;
        bool done = IsDone(k, source, target, patch);
        return new PadPlan(target, source, done ? $"{target.Label} already {text}" : $"{target.Label} ← {text}", done, patch);
    }

    /// <summary>Builds the target blocks from the full source blocks.</summary>
    public static Dictionary<string, byte[]> Build(PadPlan plan, Func<string, byte[]> sourceBlock)
    {
        var blocks = plan.Target.Blocks.Zip(plan.Source.Blocks)
            .ToDictionary(p => p.First, p => (byte[])sourceBlock(p.Second).Clone());
        if (plan.Patch is not null)
            foreach (var (name, block) in blocks.Where(kv => kv.Key[0] is 'A' or 'B' or 'C')) plan.Patch(name, block);
        return blocks;
    }

    /// <summary>True if the target's layers already match what copying + patching the source would give.</summary>
    private static bool IsDone(KitInfo k, PadRef source, PadRef target, LayerPatch? patch)
    {
        foreach (var (src, dst) in new[] { (source.Head, target.Head), (source.Rim, target.Rim) })
            for (int l = 0; l < 3; l++)
            {
                var expected = k.Get(src, l);
                if (patch is not null)
                {
                    // Run the patch on a minimal block holding just the fields LayerInfo knows about.
                    var b = new byte[LayerInfo.VEdit2Offset + 4];
                    b[0] = (byte)(expected.On ? 1 : 0);
                    KitMap.SetNib(b, 0x01, expected.Inst);
                    KitMap.SetNib(b, 0x05, expected.Bank);
                    KitMap.SetNib(b, LayerInfo.VEdit2Offset, expected.VEdit2);
                    patch($"{"ABC"[l]}{dst + 1}", b);
                    expected = LayerInfo.Decode(b);
                }
                var actual = k.Get(dst, l);
                if (expected.On != actual.On || (expected.On && expected != actual)) return false;
            }
        return true;
    }

    /// <summary>Uses the kit's own China/splash pad; otherwise Crash 1's setup with a default built-in sound.</summary>
    private static (PadRef?, string, LayerPatch?) FindOrDefault(KitInfo k, PadRef target, InstType type,
        string headName, string edgeName)
    {
        var label = type.ToString();
        if (HasType(k, target, type)) return (target, label, null);
        if (SearchOrder.FirstOrDefault(p => p != target && HasType(k, p, type)) is { } own)
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

    private static bool HasType(KitInfo k, PadRef pad, InstType type) =>
        Enumerable.Range(0, 3).Select(l => k.Get(pad.Head, l)).Any(l => l.On && l.Type == type);

    private static void CloseHiHat(string name, byte[] layer)
    {
        if (LayerInfo.Decode(layer).Type == InstType.HiHat) KitMap.SetNib(layer, LayerInfo.VEdit2Offset, HiHatClosed);
    }
}
