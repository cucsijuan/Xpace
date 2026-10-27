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
///             cleared for the ship: its door opens and its beacon lights;
///             the clearance lasts <see cref="ClearanceSeconds"/>, and on
///             while the ship is in the hangar. A ship the station's
///             security has a quarrel with is refused.
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
    /// <summary>{0} the dock's number.</summary>
    [Export] public string ClearedText = "Cleared to dock, hangar {0}. Follow the lights in.";
    [Export] public string NoDockText = "No hangar free. Hold outside and ask again.";
    [Export] public string DeniedText = "Docking denied.";
    /// <summary>{0} the dock's number.</summary>
    [Export] public string ExpiredText = "Your clearance for hangar {0} has expired.";

    [ExportGroup("Speech")]
    [Export] public string WelcomeSpeech = "welcome";
    [Export] public string ClearedSpeech = "cleared hangar_{0} follow_lights";
    [Export] public string NoDockSpeech = "no_dock";
    [Export] public string DeniedSpeech = "denied";
    [Export] public string ExpiredSpeech = "expired";

    public static readonly List<StationRadio> All = new();

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
    private readonly Dictionary<HangarDock, (IRadioReceiver Ship, double Until)> _cleared = new();
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
        if (what == RadioRequest.Docking && Net.SessionNode.Instance is { Active: true } session && !session.Session.IsHost)
            session.Request(from, this, what);      // the host's station answers
        else
            _replies.Add((from, what, _now + ReplySeconds));
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

    /// <summary>The dock cleared for the ship, if any.</summary>
    public HangarDock DockOf(IRadioReceiver ship) => _cleared.FirstOrDefault(kv => kv.Value.Ship == ship).Key;

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
        HangarDock dock = DockOf(ship);
        if (dock == null)
        {
            float best = float.MaxValue;
            foreach (HangarDock d in _docks)
            {
                float range = d.GlobalPosition.DistanceTo(ship.RadioPosition);
                if (d.ClearedFor == null && range < best)
                {
                    best = range;
                    dock = d;
                }
            }
        }
        if (dock == null)
        {
            Say(ship, NoDockText, NoDockSpeech);
            return;
        }
        _cleared[dock] = (ship, _now + ClearanceSeconds);
        dock.Clear(ShipOf(ship));
        ship.Cleared(this, dock.Number);
        Say(ship, string.Format(ClearedText, dock.Number), ClearedSpeech, dock.Number);
    }

    /// <summary>A dock the station's own ships use (its police): opened for them, not given to anyone else meanwhile.</summary>
    public void Hold(HangarDock dock, Node3D ship, double seconds)
    {
        _cleared[dock] = (null, _now + seconds);
        dock.Clear(ship);
    }

    // A clearance lasts its time, and on while the ship is in the hangar
    private void Clearances()
    {
        foreach (HangarDock dock in _cleared.Keys.ToList())
        {
            var (ship, until) = _cleared[dock];
            bool gone = ship != null && (ship is not GodotObject o || !IsInstanceValid(o));
            if (!gone && ship != null && dock.Holds(ShipOf(ship)))
            {
                _cleared[dock] = (ship, _now + ClearanceSeconds);
                continue;
            }
            if (!gone && until > _now)
                continue;
            _cleared.Remove(dock);
            dock.Close();
            if (!gone && ship != null)
            {
                ship.Cleared(this, 0);
                Say(ship, string.Format(ExpiredText, dock.Number), ExpiredSpeech, dock.Number);
            }
        }
    }

    private static Node3D ShipOf(IRadioReceiver radio) => Combat.ShipOf(radio as Node) ?? radio as Node3D;
}
