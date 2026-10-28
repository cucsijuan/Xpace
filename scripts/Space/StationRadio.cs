using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.Space;

/// <summary>
/// A station's radio and traffic control, placed in the station's scene:
/// other radios list it by <see cref="Callsign"/>. It sends the station's
/// <see cref="Laws"/> to every ship radio that comes into the laws' zone
/// (once, as it comes in: a radio that was off then missed them), with a
/// word of welcome, and answers what ships ask (<see cref="Request"/>),
/// <see cref="ReplySeconds"/> later:
///
///   laws      sent again;
///   docking   a dock (<see cref="HangarDock"/>, any below the station) is
///             cleared for the ship, and in it a landing pad that fits it
///             and is no one else's (several ships can share a hangar, one
///             to a pad; a hangar with no pads takes one ship): its door
///             opens and its beacon lights, and only that pad lights; the
///             clearance lasts <see cref="ClearanceSeconds"/>, and on while
///             the ship is in the hangar. A ship the station's security has
///             a quarrel with is refused. The door is open while a ship it
///             cleared is on its way in or out, and shuts once every one of
///             them is docked;
///   launch    a docked ship asks to leave: the door opens, the pad lets
///             it go, and once it is out of the hangar the door shuts and
///             the clearance is over.
///
/// In a session the host's station is the station: a guest's request for a
/// dock goes to it, and its answers, words and docks reach the guest (see
/// <see cref="SessionNode"/>). A guest's own copy only sends the laws (the
/// same everywhere) as its ship comes into the zone.
///
/// What it says is in the exported texts ({0}, {1}... as each one tells),
/// shown on the ships' screens, and spoken by its operator's
/// <see cref="Voice"/> as the speech beside each text (lines of the voice,
/// {0} as in the text: see <see cref="RadioVoicePack"/>).
/// </summary>
public partial class StationRadio : Node3D, IRadio
{
    /// <summary>Up to 8 characters; empty: the station's node name.</summary>
    [Export] public string Callsign { get; set; } = "";
    /// <summary>How far a plain receiver hears it, metres.</summary>
    [Export] public float Range = 40000f;
    [Export] public StationLaws Laws;
    /// <summary>How long traffic control takes to answer a ship that asks.</summary>
    [Export] public double ReplySeconds = 0.5;
    [Export] public double ClearanceSeconds = 300;
    [Export] public bool On { get; set; } = true;
    /// <summary>Its operator's voice; none: one of <see cref="Voices"/>, picked by the callsign (the same on every machine).</summary>
    [Export] public RadioVoicePack Voice;
    [Export] public Godot.Collections.Array<RadioVoicePack> Voices = new();

    [ExportGroup("Texts")]
    /// <summary>{0} the zone (km), {1} the speed limit (m/s), {2} where it holds (km).</summary>
    [Export] public string WelcomeText = "Welcome. No weapons within {0} km. Speed limit {1} m/s within {2} km.";
    /// <summary>{0} the dock's number (a hangar with no pads).</summary>
    [Export] public string ClearedText = "Cleared to dock, hangar {0}. Follow the lights in.";
    /// <summary>{0} the dock's number, {1} the pad's.</summary>
    [Export] public string ClearedPadText = "Cleared to dock, hangar {0}, pad {1}. Follow the lights in.";
    [Export] public string NoDockText = "No hangar free. Hold outside and ask again.";
    [Export] public string DeniedText = "Docking denied.";
    /// <summary>{0} the dock's number, {1} the pad's.</summary>
    [Export] public string DockedText = "Docked on pad {1}, hangar {0}. Welcome aboard.";
    /// <summary>{0} the dock's number, {1} the pad's.</summary>
    [Export] public string LaunchText = "Hangar {0} opening. Pad {1}, cleared for launch.";
    [Export] public string NotDockedText = "You are not docked here.";
    /// <summary>{0} the bounty.</summary>
    [Export] public string HeldText = "You are wanted here. Launch denied until your bounty of {0} credits is paid.";
    /// <summary>{0} the dock's number.</summary>
    [Export] public string DepartedText = "Hangar {0} closing. Fly safe.";
    /// <summary>{0} the dock's number.</summary>
    [Export] public string ExpiredText = "Your clearance for hangar {0} has expired.";

    [ExportGroup("Speech")]
    [Export] public string WelcomeSpeech = "welcome";
    [Export] public string ClearedSpeech = "cleared hangar_{0} follow_lights";
    [Export] public string NoDockSpeech = "no_dock";
    [Export] public string DeniedSpeech = "denied";
    [Export] public string ExpiredSpeech = "expired";
    [Export] public string DockedSpeech = "";
    [Export] public string LaunchSpeech = "";
    [Export] public string NotDockedSpeech = "";
    [Export] public string HeldSpeech = "";
    [Export] public string DepartedSpeech = "";

    public static readonly List<StationRadio> All = new();

    // A ship cleared to a dock (and a pad in it), or the dock held for the station's own ship (no radio)
    private sealed class Clearance
    {
        public HangarDock Dock;
        public LandingPad Pad;
        public IRadioReceiver Ship;
        public Node3D Held;
        public double Until;
        public bool Docked, Departing;
    }

    /// <summary>Its security, if the station has one (beside it in the station's scene).</summary>
    public StationSecurity Security { get; private set; }

    /// <summary>Its callsign, as radios send it.</summary>
    public string Sign => Radio.Callsign(Callsign, GetParent() ?? this);

    public IReadOnlyList<HangarDock> Docks => _docks;

    string IRadio.Callsign => Sign;
    char IRadio.Kind => 'S';
    Vector3 IRadio.RadioPosition => GlobalPosition;
    float IRadio.RadioRange => Range;
    bool IRadio.RadioOn => On;

    private readonly HashSet<IRadioReceiver> _inside = new();
    private readonly List<(IRadioReceiver To, RadioRequest What, double At)> _replies = new();
    private readonly List<Clearance> _cleared = new();
    private List<HangarDock> _docks = new();
    private double _now;

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    public override void _Ready()
    {
        AddToGroup(Radio.Group);
        Node station = GetParent() ?? this;
        _docks = station.FindChildren("*", "", true, false).OfType<HangarDock>().ToList();
        for (int i = 0; i < _docks.Count; i++)
            if (_docks[i].Number == 0)
                _docks[i].Number = i + 1;
        _docks.Sort((a, b) => a.Number.CompareTo(b.Number));
        foreach (HangarDock d in _docks)
            d.Station = this;
        foreach (Node n in station.GetChildren())
            Security ??= n as StationSecurity;
    }

    /// <summary>The station whose protected zone holds the point, or null.</summary>
    public static StationRadio ProtectingAt(Vector3 point)
    {
        foreach (StationRadio s in All)
            if (s.Protects(point))
                return s;
        return null;
    }

    /// <summary>The point, or the nearest one outside every protected zone (with a margin) if it is in one.</summary>
    public static Vector3 Outside(Vector3 point, float margin = 500f)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            StationRadio s = ProtectingAt(point);
            if (s == null)
                break;
            Vector3 away = point - s.GlobalPosition;
            if (away.LengthSquared() < 1)
                away = Vector3.Right;
            point = s.GlobalPosition + away.Normalized() * (s.Laws.ZoneRadius + margin);
        }
        return point;
    }

    public bool Protects(Vector3 point) => Laws != null && GlobalPosition.DistanceTo(point) <= Laws.ZoneRadius;

    /// <summary>A ship's radio asks something.</summary>
    public void Request(IRadioReceiver from, RadioRequest what)
    {
        if (!On || what == RadioRequest.None || !from.RadioOn || from.RadioPosition.DistanceTo(GlobalPosition) > from.RadioRange)
            return;
        if (what is (RadioRequest.Docking or RadioRequest.Launch) && Net.SessionNode.Instance is { Active: true } session && !session.Session.IsHost)
            session.Request(from, this, what);      // the host's station answers
        else
            _replies.Add((from, what, _now + ReplySeconds));
    }

    /// <summary>The station <paramref name="node"/> is part of (its radio is beside it or beside something above it), or null.</summary>
    public static StationRadio Above(Node node)
    {
        for (Node n = node; n != null; n = n.GetParent())
            foreach (Node child in n.GetChildren())
                if (child is StationRadio radio)
                    return radio;
        return null;
    }

    /// <summary>The station of that callsign, if there is one here.</summary>
    public static StationRadio Called(string callsign) => All.FirstOrDefault(s => s.Sign == callsign);

    /// <summary>The voice its operator speaks with (null: none).</summary>
    public RadioVoicePack Operator => Voice ?? RadioVoicePack.Pick(Voices, Sign);

    /// <summary>Says something to a ship's radio (if it hears the station): the words, and the operator's lines for them ({0}... from <paramref name="args"/>).</summary>
    public void Say(IRadioReceiver to, string text, string lines = "", params object[] args)
    {
        if (On && to != null && Radio.Hears(to, this))
            to.Hear(this, text, Operator?.Speech(lines, args) ?? "");
    }

    public override void _PhysicsProcess(double delta)
    {
        _now += delta;
        if (Laws == null)
            return;
        foreach (Node node in GetTree().GetNodesInGroup(Radio.Group))
        {
            if (node is not IRadioReceiver ship || ship.Relayed || !IsInstanceValid(node))
                continue;
            bool inside = Protects(ship.RadioPosition);
            if (inside && _inside.Add(ship))
                SendLaws(ship, welcome: true);
            else if (!inside)
                _inside.Remove(ship);
        }
        _inside.RemoveWhere(r => r is not GodotObject o || !IsInstanceValid(o));
        for (int i = _replies.Count - 1; i >= 0; i--)
        {
            if (_replies[i].At > _now)
                continue;
            var (to, what, _) = _replies[i];
            _replies.RemoveAt(i);
            if (to is not GodotObject o || !IsInstanceValid(o))
                continue;
            if (what == RadioRequest.Laws)
                SendLaws(to, welcome: false);
            else if (what == RadioRequest.Docking)
                Dock(to);
            else if (what == RadioRequest.Launch)
                Launch(to);
        }
        Clearances();
    }

    private void SendLaws(IRadioReceiver ship, bool welcome)
    {
        if (!On || !Radio.Hears(ship, this))
            return;
        ship.Receive(this, Laws);
        if (welcome)
            Say(ship, string.Format(System.Globalization.CultureInfo.InvariantCulture, WelcomeText,
                Laws.ZoneRadius / 1000, Laws.SpeedLimit, Laws.SpeedRadius / 1000), WelcomeSpeech);
    }

    private void Dock(IRadioReceiver ship)
    {
        if (Security?.Refuses(ShipOf(ship)) == true)
        {
            Say(ship, DeniedText, DeniedSpeech);
            return;
        }
        Clearance c = Of(ship);
        if (c == null)
        {
            Node3D body = ShipOf(ship);
            float best = float.MaxValue;
            foreach (HangarDock d in _docks)
            {
                float range = d.GlobalPosition.DistanceTo(ship.RadioPosition);
                if (range < best && Free(d, body, out LandingPad pad))
                {
                    best = range;
                    c = new Clearance { Dock = d, Pad = pad, Ship = ship };
                }
            }
            if (c == null)
            {
                Say(ship, NoDockText, NoDockSpeech);
                return;
            }
            _cleared.Add(c);
            if (c.Pad != null)
                c.Pad.AssignedTo = body;
        }
        c.Until = _now + ClearanceSeconds;
        Doors(c.Dock);
        ship.Cleared(this, c.Dock.Number, c.Pad?.Number ?? 0);
        int number = c.Pad?.Number ?? 0;
        Say(ship, string.Format(c.Pad != null ? ClearedPadText : ClearedText, c.Dock.Number, number), ClearedSpeech, c.Dock.Number, number);
    }

    // The dock can take the ship: not held for the station's own ships; a pad that fits it and is no one else's, or
    // no pads and no one else in it
    private bool Free(HangarDock dock, Node3D ship, out LandingPad pad)
    {
        pad = null;
        if (_cleared.Any(c => c.Dock == dock && (c.Ship == null || c.Pad == null)))
            return false;
        if (dock.Pads.Count == 0)
            return true;
        pad = dock.Pads.FirstOrDefault(p => p.Fits(ship) && _cleared.All(c => c.Pad != p));
        return pad != null;
    }

    private void Launch(IRadioReceiver ship)
    {
        Clearance c = Of(ship);
        if (c == null || !c.Docked)
        {
            Say(ship, NotDockedText, NotDockedSpeech);
            return;
        }
        if (Security?.Holds(ShipOf(ship)) == true && Security.Jurisdiction(ShipOf(ship)) is (_, World.CrimeRecord record))
        {
            Say(ship, string.Format(HeldText, record.Bounty), HeldSpeech);
            return;
        }
        c.Departing = true;
        Doors(c.Dock);
        Say(ship, string.Format(LaunchText, c.Dock.Number, c.Pad?.Number ?? 0), LaunchSpeech, c.Dock.Number);
    }

    /// <summary>The ship is in one of the station's hangars (docked or not: a ship that slipped in counts).</summary>
    public bool Inside(Node3D ship) => ship != null && _docks.Any(d => d.Holds(ship));

    /// <summary>The ship is docked here, on the pad it was cleared to (and not leaving).</summary>
    public bool DockedHere(Node3D ship) =>
        ship != null && _cleared.Any(c => c.Docked && !c.Departing && c.Ship is GodotObject o && IsInstanceValid(o) && ShipOf(c.Ship) == ship);

    /// <summary>A dock the station's own ships use (its police): opened for them, not given to anyone else meanwhile.</summary>
    public void Hold(HangarDock dock, Node3D ship, double seconds)
    {
        _cleared.Add(new Clearance { Dock = dock, Held = ship, Until = _now + seconds });
        Doors(dock);
    }

    // A clearance lasts its time, and on while the ship is in the hangar; docked on its pad, it no longer needs the
    // door; leaving after a launch, once out of the hangar, the clearance is over
    private void Clearances()
    {
        foreach (Clearance c in _cleared.ToList())
        {
            if (c.Ship == null)
            {
                if (c.Until <= _now)
                    End(c);
                continue;
            }
            bool gone = c.Ship is not GodotObject o || !IsInstanceValid(o);
            Node3D body = gone ? null : ShipOf(c.Ship);
            if (!gone && c.Dock.Holds(body))
            {
                c.Until = _now + ClearanceSeconds;
                if (!c.Docked && c.Pad != null && c.Pad.Docks(body))
                {
                    c.Docked = true;
                    Doors(c.Dock);
                    Say(c.Ship, string.Format(DockedText, c.Dock.Number, c.Pad.Number), DockedSpeech, c.Dock.Number);
                }
                continue;
            }
            if (!gone && !c.Departing && c.Until > _now)
                continue;
            End(c);
            if (gone)
                continue;
            c.Ship.Cleared(this, 0, 0);
            if (c.Departing)
                Say(c.Ship, string.Format(DepartedText, c.Dock.Number), DepartedSpeech, c.Dock.Number);
            else
                Say(c.Ship, string.Format(ExpiredText, c.Dock.Number), ExpiredSpeech, c.Dock.Number);
        }
    }

    private void End(Clearance c)
    {
        _cleared.Remove(c);
        if (c.Pad != null && c.Pad.AssignedTo == ShipOf(c.Ship))
            c.Pad.AssignedTo = null;
        Doors(c.Dock);
    }

    // A dock's door is open while a ship it cleared is on its way in or out (or the station's own ships use it)
    private void Doors(HangarDock dock)
    {
        Node3D user = null;
        bool open = false;
        foreach (Clearance c in _cleared)
        {
            if (c.Dock != dock)
                continue;
            if (c.Ship == null || c.Pad == null)
                user = c.Held ?? ShipOf(c.Ship);
            open |= c.Ship == null || c.Pad == null || !c.Docked || c.Departing;
        }
        dock.Use(user, open);
    }

    private Clearance Of(IRadioReceiver ship) => ship == null ? null : _cleared.FirstOrDefault(c => c.Ship == ship);

    /// <summary>The dock cleared for the ship, if any.</summary>
    public HangarDock DockOf(IRadioReceiver ship) => Of(ship)?.Dock;

    /// <summary>The pad in it cleared for the ship, if any.</summary>
    public LandingPad PadOf(IRadioReceiver ship) => Of(ship)?.Pad;

    private static Node3D ShipOf(IRadioReceiver radio) => Combat.ShipOf(radio as Node) ?? radio as Node3D;
}
