using Godot;

namespace Xpace.Space;

/// <summary>
/// Something a station's factory makes and its arms dealer sells by the piece, straight onto the ship: a missile, a
/// bundle of chaff, a flare. Each model is a .tres of its own kind of spec; its id is the file's name.
/// </summary>
[GlobalClass]
public partial class Outfit : Resource
{
    /// <summary>The model's name, as a program or a fittings list shows it.</summary>
    [Export] public string Model = "";

    /// <summary>What it costs where the market has the stock it wants.</summary>
    [Export] public long Price = 100;

    /// <summary>How many one Buy at the dealer takes (rounds come in boxes).</summary>
    [Export] public int Lot = 1;

    /// <summary>Its id in records and requests: the file's name (none for one made in code).</summary>
    public string Id => string.IsNullOrEmpty(ResourcePath) ? null : ResourcePath.GetFile().GetBaseName();

    /// <summary>What it is for, in a line, as the dealer's card says it.</summary>
    public virtual string About => "";

    /// <summary>What it can do, in figures.</summary>
    public virtual string Details => "";

    /// <summary>The pilot carries it (<see cref="Xpace.World.PilotRecord.Items"/>), not the ship's racks: one bought is one thing in hand.</summary>
    public virtual bool Carried => false;

    /// <summary>A carried one, as the pilot holds it, newly made (<paramref name="state"/>: the universe it comes into).</summary>
    public virtual Xpace.World.ItemRecord MakeItem(Xpace.World.UniverseState state) => null;

    /// <summary>The thing the pilot holds is one of these.</summary>
    public virtual bool Makes(Xpace.World.ItemRecord item) => false;
}
