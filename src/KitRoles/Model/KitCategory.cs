namespace KitRoles.Model;

public enum KitCategory { Unknown, BuiltIn, Expansion, KitPack, UserKit }

public static class KitClassifier
{
    /// <summary>
    /// Classifies a kit by the instrument banks it uses: expansion banks are 2010+, kit pack banks 300-399.
    /// Uses the banks of all scanned layers.
    /// </summary>
    public static KitCategory Classify(string name, string subName, IEnumerable<int> banks)
    {
        if (name == "User Kit") return KitCategory.UserKit;
        var set = banks.ToHashSet();
        if (set.Any(b => b >= 2010)) return KitCategory.Expansion;
        if (set.Any(b => b is >= 300 and < 400) || subName.StartsWith("Inspired by", StringComparison.OrdinalIgnoreCase))
            return KitCategory.KitPack;
        return KitCategory.BuiltIn;
    }

    public static string Label(this KitCategory c) => c switch
    {
        KitCategory.BuiltIn => "Built-in",
        KitCategory.Expansion => "Expansion",
        KitCategory.KitPack => "Kit pack",
        KitCategory.UserKit => "User kit",
        _ => "",
    };
}
