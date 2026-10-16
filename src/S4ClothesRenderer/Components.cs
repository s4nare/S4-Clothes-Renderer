namespace S4ClothesRenderer;

public enum View
{
    Front,
    Back,
    Head,
    Wrist,
}

public sealed record Component(string Key, string Cat, string Slot, View View)
{
    public bool Prop => Key.StartsWith("p_");

    public static readonly Component[] All =
    [
        new("berd", "masks", "1", View.Front),
        new("hair", "hair", "2", View.Front),
        new("uppr", "torsos", "3", View.Front),
        new("lowr", "pants", "4", View.Front),
        new("hand", "bags", "5", View.Back),
        new("feet", "shoes", "6", View.Front),
        new("teef", "necks", "7", View.Front),
        new("accs", "undershirts", "8", View.Front),
        new("task", "vests", "9", View.Front),
        new("decl", "decals", "10", View.Front),
        new("jbib", "jackets", "11", View.Front),
        new("p_head", "hats", "prop_0", View.Head),
        new("p_eyes", "glasses", "prop_1", View.Head),
        new("p_ears", "earrings", "prop_2", View.Head),
        new("p_lwrist", "watches", "prop_6", View.Wrist),
        new("p_rwrist", "bracelets", "prop_7", View.Wrist),
    ];

    public static readonly Dictionary<string, Component> ByKey = All.ToDictionary(c => c.Key);
    public static readonly Dictionary<string, int> Order = All.Select((c, i) => (c.Key, i)).ToDictionary(x => x.Key, x => x.i);
}
