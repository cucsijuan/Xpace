using System;
using System.Linq;
using Godot;

namespace Xpace.Debug;

/// <summary>
/// What the debug menu shows (the game menu's Debug page, <see cref="DebugMenu"/>): drawings of what the game keeps
/// hidden. Only in debug builds (the editor, the tests, a debug export) or with --debug on the command line; kept
/// while the game runs.
/// </summary>
public static class DebugOptions
{
    /// <summary>The debug menu is offered.</summary>
    public static bool Available => OS.IsDebugBuild() || OS.GetCmdlineUserArgs().Contains("--debug");

    /// <summary>Stations' and gates' law zones: the protected zone and the speed limit's radius (<see cref="LawZones"/>).</summary>
    public static bool LawZones
    {
        get => _lawZones;
        set
        {
            if (_lawZones == value)
                return;
            _lawZones = value;
            Changed?.Invoke();
        }
    }

    /// <summary>An option changed.</summary>
    public static event Action Changed;

    private static bool _lawZones;
}
