namespace KitRoles.Midi;

public record BlockDef(string Name, int Offset, int Size);

/// <summary>V51 address map (see docs/V51_MIDI_Implementation). Addresses and sizes are 7-bit per byte.</summary>
public static class KitMap
{
    public static readonly string[] Units =
    [
        "Kick", "Snare", "Snare rim", "Tom 1", "Tom 1 rim", "Tom 2", "Tom 2 rim", "Tom 3", "Tom 3 rim",
        "Tom 4", "Tom 4 rim", "Hi-hat", "Hi-hat edge", "Crash 1", "Crash 1 edge", "Crash 2", "Crash 2 edge",
        "Ride", "Ride edge", "Ride bell", "AUX1", "AUX1 rim", "AUX2", "AUX2 rim", "AUX3", "AUX3 rim", "AUX4", "AUX4 rim",
    ];

    public const int KitCount = 200;

    /// <summary>Converts an address written in the PDF (e.g. 0x04000000) to a linear 7-bit value.</summary>
    public static int H(int pdfHex) =>
        From7([(byte)(pdfHex >> 24), (byte)(pdfHex >> 16), (byte)(pdfHex >> 8), (byte)pdfHex]);

    public static int From7(ReadOnlySpan<byte> b)
    {
        int n = 0;
        foreach (var x in b) n = (n << 7) | x;
        return n;
    }

    public static byte[] To7(int n, int width = 4)
    {
        var o = new byte[width];
        for (int i = width - 1; i >= 0; i--, n >>= 7) o[i] = (byte)(n & 0x7F);
        return o;
    }

    public static int KitBase(int kit) => H(0x04000000) + (kit - 1) * H(0x040000);

    /// <summary>All 148 blocks of one kit, as listed in the MIDI implementation, by name.</summary>
    public static readonly IReadOnlyDictionary<string, BlockDef> Blocks = BuildBlocks().ToDictionary(b => b.Name);

    public static BlockDef Common => Blocks["common"];

    public static int Address(int kit, string block) => KitBase(kit) + Blocks[block].Offset;

    /// <summary>Layer block name, e.g. unit 22, layer 0 = "A23".</summary>
    public static string LayerBlock(int unit, int layer) => $"{"ABC"[layer]}{unit + 1}";

    private static IEnumerable<BlockDef> BuildBlocks()
    {
        yield return new("common", 0, H(0x141));
        yield return new("midi", H(0x000200), H(0x154));
        yield return new("mcomp", H(0x000400), H(0x46));
        yield return new("meq", H(0x000500), H(0x2A));
        for (int i = 0; i < 8; i++) yield return new($"fx{i + 1}", H(0x001000) + i * H(0x0200), H(0x102));
        for (int i = 0; i < 28; i++) yield return new($"uc{i + 1}", H(0x002000) + i * H(0x0100), H(0x34));
        yield return new("xstick", H(0x004000), H(0x151));
        foreach (var (layer, start) in new[] { ("A", 0x005000), ("B", 0x011000), ("C", 0x015000) })
            for (int i = 0; i < 28; i++) yield return new($"{layer}{i + 1}", H(start) + i * H(0x0200), H(0x151));
        for (int i = 0; i < 14; i++) yield return new($"pad{i + 1}", H(0x021000) + i * H(0x0100), H(0x47));
        for (int i = 0; i < 4; i++) yield return new($"bus{i + 1}", H(0x022000) + i * H(0x0100), H(0x1C));
        yield return new("room", H(0x023000), H(0x12E));
        yield return new("oh", H(0x025000), H(0x113));
        yield return new("rev", H(0x027000), H(0x66));
        yield return new("busrev", H(0x030000), H(0x66));
        yield return new("reso", H(0x031000), H(0x49));
    }

    /// <summary>Encodes a nibble-packed value (two's complement for negatives).</summary>
    public static void SetNib(byte[] d, int offset, int value, int n = 4)
    {
        for (int i = 0; i < n; i++) d[offset + i] = (byte)((value >> (4 * (n - 1 - i))) & 0x0F);
    }

    /// <summary>Decodes nibble-packed values (n bytes of 0000xxxx), two's complement when signed.</summary>
    public static int Nib(byte[] d, int offset, int n = 4, bool signed = false)
    {
        int v = 0;
        for (int i = 0; i < n; i++) v = (v << 4) | (d[offset + i] & 0x0F);
        if (signed && v >= 1 << (4 * n - 1)) v -= 1 << (4 * n);
        return v;
    }
}
