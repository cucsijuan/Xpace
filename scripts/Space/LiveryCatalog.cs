using Godot;

namespace Xpace.Space;

/// <summary>
/// The paints owners' ships wear: an owner's livery is a paint material like any other (<c>synty_paint.gdshader</c>, as
/// assets/materials/paint_*.tres), set on its organisation; owners with none (people, companies born in play) wear one of
/// <see cref="Free"/>, picked by their id. A ship wears it through its <see cref="PaintTheme"/>, and a livery travels
/// between machines as its place in <see cref="Paints"/>.
/// </summary>
[GlobalClass]
public partial class LiveryCatalog : Resource
{
    public const string Path = "res://assets/materials/liveries.tres";

    /// <summary>Every paint a ship can wear as a livery: the organisations' and the free ones.</summary>
    [Export] public Material[] Paints = [];
    /// <summary>What owners with no livery of their own wear.</summary>
    [Export] public Material[] Free = [];

    private static LiveryCatalog _loaded;
    public static LiveryCatalog Loaded => _loaded ??= ResourceLoader.Exists(Path) ? ResourceLoader.Load<LiveryCatalog>(Path) : new LiveryCatalog();

    /// <summary>The paint's number on the wire: 0 for none.</summary>
    public static byte IndexOf(Material paint)
    {
        if (paint == null)
            return 0;
        int i = System.Array.IndexOf(Loaded.Paints, paint);
        return i < 0 || i > 254 ? (byte)0 : (byte)(i + 1);
    }

    public static Material At(byte index) => index == 0 || index > Loaded.Paints.Length ? null : Loaded.Paints[index - 1];

    /// <summary>What an owner's ships wear: its organisation's livery, or a free one picked by its id.</summary>
    public static Material Of(string owner) => Xpace.World.OrganisationCatalog.Find(owner)?.Livery ?? Pick(owner);

    /// <summary>A free livery for an owner with none, the same for the same id on every machine.</summary>
    public static Material Pick(string id)
    {
        Material[] free = Loaded.Free;
        if (free.Length == 0 || string.IsNullOrEmpty(id))
            return null;
        uint hash = 2166136261;
        foreach (char c in id)
            hash = (hash ^ c) * 16777619;
        return free[hash % (uint)free.Length];
    }

    /// <summary>
    /// The ship wears the paint: its theme (made if it has none) puts it in place of Synty's atlas and of any other
    /// paint under it, and its <c>NetBody</c> tells other machines which. Before the ship joins the tree, so it is
    /// painted from the first frame.
    /// </summary>
    public static void Wear(Node ship, Material paint)
    {
        if (ship == null || paint == null)
            return;
        if (ship.GetNodeOrNull<PaintTheme>(PaintTheme.NodeName) is not PaintTheme theme)
        {
            theme = new PaintTheme { Name = PaintTheme.NodeName };
            ship.AddChild(theme);
        }
        theme.Livery = paint;
        foreach (Node child in ship.GetChildren())
            if (child is Xpace.Net.NetBody body)
                body.Look = IndexOf(paint);
    }
}
