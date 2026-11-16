using Godot;

namespace Xpace.World;

/// <summary>
/// How owners post work on their stations' contract boards, the same for all (assets/contracts/rules.tres, on the
/// sector map). A station short of a good it uses asks for it: the tonnes it lacks, as far as <see cref="MostTonnes"/>,
/// for what they cost at its own market and <see cref="Premium"/> over, set aside from its owner's money when posted.
/// </summary>
[GlobalClass]
public partial class ContractRules : Resource
{
    /// <summary>A station asks for a good once its stock is below this share of what it wants.</summary>
    [Export] public float AskBelow = 0.4f;

    /// <summary>The most tonnes one delivery asks for.</summary>
    [Export] public int MostTonnes = 20;

    /// <summary>The fewest tonnes worth posting.</summary>
    [Export] public int FewestTonnes = 2;

    /// <summary>What a delivery pays over the market's own price for the same tonnes (0.25: a quarter more).</summary>
    [Export] public float Premium = 0.25f;

    /// <summary>How long an offer stays on the board, hours.</summary>
    [Export] public float OfferHours = 6;

    /// <summary>How long a taker has to do it, hours (0: no limit).</summary>
    [Export] public float DueHours = 4;

    /// <summary>Most open offers a station has on its board at once.</summary>
    [Export] public int MostOffers = 4;

    /// <summary>The most an owner sets aside for its boards: this share of its money.</summary>
    [Export] public float MoneyShare = 0.2f;

    /// <summary>Someone the giver feels this badly about (<see cref="Memories.Feeling"/>) is offered nothing.</summary>
    [Export] public float RefuseBelow = -1f;
}
