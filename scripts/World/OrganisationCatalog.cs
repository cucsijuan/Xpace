using System.Linq;
using Godot;

namespace Xpace.World;

/// <summary>Every organisation set up by hand (assets/organisations/catalog.tres).</summary>
[GlobalClass]
public partial class OrganisationCatalog : Resource
{
    public const string Path = "res://assets/organisations/catalog.tres";

    [Export] public Organisation[] Organisations = System.Array.Empty<Organisation>();

    private static OrganisationCatalog _all;

    public static OrganisationCatalog All => _all ??= Xpace.World.ResourceCache.Load<OrganisationCatalog>(Path) ?? new OrganisationCatalog();

    public static Organisation Find(string id) => All.Organisations.FirstOrDefault(o => o?.Id == id);
}
