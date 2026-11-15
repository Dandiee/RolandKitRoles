using System.Text;
using KitRoles.Midi;

namespace KitRoles.Model;

/// <summary>The parts of a KitUnitLayer the app needs: switch, instrument, bank and V-EDIT 2 (hi-hat position).</summary>
public record LayerInfo(bool On, int Inst, int Bank, int VEdit2)
{
    public const int VEdit2Offset = 0x31 + 4;

    public static LayerInfo Decode(byte[] block) =>
        From(block[..9], block[VEdit2Offset..(VEdit2Offset + 4)]);

    /// <summary>From the first 9 bytes of a layer block and the 4 bytes of V-EDIT 2.</summary>
    public static LayerInfo From(byte[] head, byte[] vEdit2) =>
        new(head[0] != 0, KitMap.Nib(head, 0x01), KitMap.Nib(head, 0x05), KitMap.Nib(vEdit2, 0, 4, signed: true));

    public bool IsEmpty => InstrumentCatalog.IsEmpty(Bank, Inst);
    public bool IsActive => On && !IsEmpty;
    public string Name => IsEmpty ? "" : InstrumentCatalog.Name(Bank, Inst);
    public InstType Type => InstrumentCatalog.Type(Bank, Inst);
}

/// <summary>What the app knows about one kit: its header block and the instruments of all 28 x 3 layers.</summary>
public sealed class KitInfo
{
    public string Header { get; set; } = "";
    public DateTime ScannedAt { get; set; }

    /// <summary>84 entries, index = unit * 3 + layer (layer 0..2 = A..C).</summary>
    public List<LayerInfo> Layers { get; set; } = [];

    public LayerInfo Get(int unit, int layer) => Layers[unit * 3 + layer];

    public string Name => DecodeName(Convert.FromHexString(Header), 0, 16);
    public string SubName => DecodeName(Convert.FromHexString(Header), 16, 64);

    public KitCategory Category =>
        KitClassifier.Classify(Name, SubName, Layers.Where(l => l.IsActive).Select(l => l.Bank));

    public static string DecodeName(byte[] common, int offset, int length) =>
        Encoding.ASCII.GetString(common, offset, length).TrimEnd(' ', '\0');
}

/// <summary>Raw blocks of a kit (hex), used for apply backups.</summary>
public sealed class KitSnapshot
{
    public int Kit { get; set; }
    public DateTime ScannedAt { get; set; }
    public Dictionary<string, string> Blocks { get; set; } = [];

    public byte[] Block(string name) => Convert.FromHexString(Blocks[name]);
}
