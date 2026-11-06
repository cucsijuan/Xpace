using Godot;

namespace Xpace.World;

/// <summary>What a base needs a good for: its people, its power, or what it makes.</summary>
public enum GoodUse { Production, Workforce, Energy }

/// <summary>
/// A kind of cargo, traded by the tonne (assets/goods). Its id is its file's name, which records keep: renaming the
/// file loses what was held of it.
/// </summary>
[GlobalClass]
public partial class Good : Resource
{
    /// <summary>As screens show it (12 characters at most fit a ship's computer).</summary>
    [Export] public string Name = "";

    /// <summary>What a tonne is worth where there is as much of it as a market wants (credits).</summary>
    [Export] public long BasePrice = 100;

    /// <summary>What a base wants it for: a base stops without workforce or energy, not without a material.</summary>
    [Export] public GoodUse Use = GoodUse.Production;

    /// <summary>It can be bought and sold: off, it stays where it is made, out of markets and news.</summary>
    [Export] public bool Traded = true;

    public string Id => ResourcePath.GetFile().GetBaseName();
}
