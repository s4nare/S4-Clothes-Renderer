namespace S4ClothesRenderer;

public enum View
{
    Front,
    Back,
    Head,
    Wrist,
}

public sealed record Component(string Key, string Cat, string Folder, string Slot, View View)
{
    public bool Prop => Key.StartsWith("p_");

    public static readonly Component[] All =
    [
        new("berd", "masks", "mask", "1", View.Front),
        new("hair", "hair", "hair", "2", View.Front),
        new("uppr", "torsos", "torso", "3", View.Front),
        new("lowr", "pants", "leg", "4", View.Front),
        new("hand", "bags", "bag", "5", View.Back),
        new("feet", "shoes", "shoes", "6", View.Front),
        new("teef", "necks", "accessory", "7", View.Front),
        new("accs", "undershirts", "undershirt", "8", View.Front),
        new("task", "vests", "kevlar", "9", View.Front),
        new("decl", "decals", "decals", "10", View.Front),
        new("jbib", "jackets", "torso2", "11", View.Front),
        new("p_head", "hats", "hat", "prop_0", View.Head),
        new("p_eyes", "glasses", "glasses", "prop_1", View.Head),
        new("p_ears", "earrings", "ear", "prop_2", View.Head),
        new("p_lwrist", "watches", "watch", "prop_6", View.Wrist),
        new("p_rwrist", "bracelets", "bracelet", "prop_7", View.Wrist),
    ];

    public static readonly Dictionary<string, Component> ByKey = All.ToDictionary(c => c.Key);
    public static readonly Dictionary<string, int> Order = All.Select((c, i) => (c.Key, i)).ToDictionary(x => x.Key, x => x.i);
}
