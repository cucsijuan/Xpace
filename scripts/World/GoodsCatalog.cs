using System.Linq;
using Godot;

namespace Xpace.World;

/// <summary>Every good there is (assets/goods/catalog.tres).</summary>
[GlobalClass]
public partial class GoodsCatalog : Resource
{
    public const string Path = "res://assets/goods/catalog.tres";

    [Export] public Good[] Goods = System.Array.Empty<Good>();

    private static GoodsCatalog _all;

    public static GoodsCatalog All => _all ??= GD.Load<GoodsCatalog>(Path) ?? new GoodsCatalog();

    public static Good Find(string id) => All.Goods.FirstOrDefault(g => g?.Id == id);
}
