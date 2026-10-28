using Godot;

namespace Xpace.World;

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

    public string Id => ResourcePath.GetFile().GetBaseName();
}
