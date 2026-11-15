using System.Text;
using KitRoles.Midi;

namespace KitRoles.Model;

public record Layer(bool On, int Inst, int Bank)
{
    public static Layer Decode(byte[] block) => new(block[0] != 0, KitMap.Nib(block, 0x01), KitMap.Nib(block, 0x05));

    public bool IsEmpty => InstrumentCatalog.IsEmpty(Bank, Inst);
    public string Name => IsEmpty ? "" : InstrumentCatalog.Name(Bank, Inst);
    public InstType Type => InstrumentCatalog.Type(Bank, Inst);
}

/// <summary>Raw blocks of one kit as read from the module (hex strings, same format as tools/scanall.py).</summary>
public sealed class KitSnapshot
{
    public int Kit { get; set; }
    public DateTime ScannedAt { get; set; }
    public Dictionary<string, string> Blocks { get; set; } = [];

    public byte[] Block(string name) => Convert.FromHexString(Blocks[name]);

    public string Header => Blocks["common"];
    public string Name => DecodeName(Block("common"), 0, 16);
    public string SubName => DecodeName(Block("common"), 16, 64);

    public static string DecodeName(byte[] common, int offset, int length) =>
        Encoding.ASCII.GetString(common, offset, length).TrimEnd(' ', '\0');

    /// <summary>Layer 0..2 (A/B/C) of unit 0..27.</summary>
    public Layer GetLayer(int unit, int layer) => Layer.Decode(Block($"{"ABC"[layer]}{unit + 1}"));

    public KitCategory Category => KitClassifier.Classify(Name, SubName,
        from u in Enumerable.Range(0, 28)
        from l in Enumerable.Range(0, 3)
        let layer = GetLayer(u, l)
        where layer.On && !layer.IsEmpty
        select layer.Bank);
}
