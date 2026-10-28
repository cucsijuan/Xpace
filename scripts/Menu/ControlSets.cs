using System.Collections.Generic;
using System.Linq;
using Xpace.Cockpit;
using Xpace.OnFoot;

namespace Xpace.Menu;

/// <summary>Every set of bindable actions (flying, on foot): a key does one thing within a set, and may do another in the next.</summary>
public static class ControlSets
{
    public static readonly (string Name, (string Action, string Name)[] Actions)[] Sets =
    {
        ("Flying", FlightInput.Bindable),
        ("On foot", OnFootInput.Bindable),
    };

    public static IEnumerable<(string Action, string Name)> All => Sets.SelectMany(s => s.Actions);

    /// <summary>The actions in the same set as <paramref name="action"/>.</summary>
    public static IEnumerable<(string Action, string Name)> SetOf(string action) =>
        Sets.Select(s => s.Actions).FirstOrDefault(a => a.Any(x => x.Action == action)) ?? System.Array.Empty<(string, string)>();

    public static void Register()
    {
        FlightInput.Register();
        OnFootInput.Register();
    }
}
