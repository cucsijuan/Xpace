using System.Linq;
using Godot;
using Xpace.World;

namespace Xpace.Space;

/// <summary>
/// A wreck's distress beacon: a radio that only sends its ship's callsign, heard far off, so COMMS lists the wreck as a
/// ship and a course can be set for it. Other machines are told no callsigns: there the beacon takes the wreck its pilot
/// is looking for (<see cref="JournalEntry.Wreck"/>).
/// </summary>
public partial class DistressBeacon : Node3D, IRadio
{
    /// <summary>How far a plain receiver hears it, metres.</summary>
    [Export] public float Range = 150000f;

    private string _callsign = "";

    public string Callsign
    {
        get
        {
            if (_callsign == "" && Universe.Current is { IsGuest: true } universe)
                return Radio.Callsign(universe.Me?.Journal.FirstOrDefault(j => j.Wreck != "")?.Wreck, this);
            return Radio.Callsign(_callsign, this);
        }
        set => _callsign = value ?? "";
    }

    public char Kind => 'V';
    public Vector3 RadioPosition => GlobalPosition;
    public float RadioRange => Range;
    public bool RadioOn => true;

    public override void _Ready() => AddToGroup(Radio.Group);
}
