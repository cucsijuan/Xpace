using Godot;

namespace Xpace.World;

/// <summary>
/// The free people of the universe, as a designer sets them up: assets/people/population.tres. They roam on their own
/// account, by the same rules as anyone (see <see cref="People"/>), and everyone has a first name and a surname.
/// </summary>
[GlobalClass]
public partial class PopulationSpec : Resource
{
    [Export] public PeopleLine[] Lines = [];
    [Export] public string[] FirstNames = [];
    [Export] public string[] Surnames = [];
    /// <summary>How near one ship must pass another for it to be noticed and gone for (m).</summary>
    [Export] public float MeetMeters = 20000f;
    /// <summary>What stolen goods fetch, of a market's price: nobody asks where they came from, and pays for it.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double FenceShare = 0.6;
    /// <summary>Someone with less than this can't keep going, and after <see cref="GiveUpHours"/> of that gives up: sells their ship and leaves.</summary>
    [Export] public long BrokeBelow = 300;
    [Export] public double GiveUpHours = 48;
    /// <summary>How much of a grudge sends someone after a person with nothing on their head.</summary>
    [Export] public double GrudgeToHunt = 1;
}
