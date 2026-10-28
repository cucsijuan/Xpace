using Godot;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// The radio of a ship another machine flies (made below a
/// <see cref="RemoteShip"/>): listed by the radios here under the ship's
/// name, and what a radio here says to it goes to its owner's machine,
/// where the ship's own radio hears it (only the host's radios can send:
/// the host's station speaks for everyone), and so do the docks a station
/// here clears it to. Its callsign and power are the owner's
/// (<see cref="Follow"/>). A station's laws it leaves to the owner's machine,
/// whose station sends them to the ship's radio there.
/// </summary>
public partial class RemoteRadio : Node3D, IRadioReceiver
{
    /// <summary>How far a plain receiver hears it, metres (a ship's radio's).</summary>
    [Export] public float Range = 20000f;
    [Export] public float Sensitivity { get; set; } = 1f;

    public string Callsign => Radio.Callsign(_callsign, GetParent() ?? this);
    public char Kind => 'V';
    public Vector3 RadioPosition => GlobalPosition;
    public float RadioRange => Range;
    public bool RadioOn => _on && (GetParent() is not RemoteShip ship || ship.Alive);
    public bool Relayed => true;

    private string _callsign = "";
    private bool _on = true;

    public override void _Ready() => AddToGroup(Radio.Group);

    /// <summary>The owner's radio: its callsign and whether it is on.</summary>
    public void Follow(string callsign, bool on)
    {
        _callsign = callsign ?? "";
        _on = on;
    }

    public void Receive(StationRadio from, StationLaws laws) { }

    public void Cleared(StationRadio from, int dock, int pad)
    {
        if (GetParent() is RemoteShip ship && SessionNode.Instance?.Session is Session session)
            session.Cleared(ship.NetId, from.Sign, dock, pad);
    }

    public void Hear(IRadio from, string text, string speech)
    {
        if (GetParent() is RemoteShip ship && SessionNode.Instance?.Session is Session session)
            session.Say(ship.NetId, from.Callsign, text, speech);
    }
}
