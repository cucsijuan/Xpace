using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Ship;

namespace Xpace.Space;

/// <summary>How a station's security sees a ship.</summary>
public enum Standing
{
    /// <summary>Nothing against it.</summary>
    Clean,
    /// <summary>It fired in the zone and was told to stop.</summary>
    Warned,
    /// <summary>The police go after it.</summary>
    Hostile,
}

/// <summary>
/// A station's security, beside its radio (<see cref="StationRadio"/>) in
/// the station's scene. It knows nothing of who is good or bad: only what it
/// sees ships do (<see cref="Combat"/>) inside the radio's laws' zone:
///
///   a weapon going off   the first time, a warning by radio
///                        (<see cref="WarningText"/>); going on firing more
///                        than <see cref="WarnGraceSeconds"/> after it, the
///                        ship is hostile (<see cref="EngageText"/>). A ship
///                        hit by another in the last
///                        <see cref="DefenceSeconds"/> fires back freely;
///   a ship destroyed     one the security had nothing against: its
///                        destroyer is hostile at once, and told so
///                        (<see cref="DestroyedText"/>); destroying a
///                        warned or hostile ship is no crime;
///   going too fast       inside the speed limit's radius, faster than the
///                        limit (and <see cref="SpeedTolerance"/>) for
///                        <see cref="SpeedingSeconds"/>: a fine, told by
///                        radio (<see cref="FineText"/>) and counted in
///                        <see cref="Fines"/> (money comes later).
///
/// Hostile ships are fair game for its police: up to
/// <see cref="PoliceCount"/> of <see cref="PoliceScene"/> launch from the
/// station's docks (the door opens, they fly out), and with no hostile ship
/// near for <see cref="StandDownSeconds"/> they fly back in. A warning is
/// forgotten after <see cref="ForgetSeconds"/> without firing, a hostility
/// after as long out of the zone; a ship destroyed starts clean. Its own
/// police may fire.
///
/// In a session only the host's security acts, on every machine's ships
/// alike (<see cref="SessionNode"/> tells it what they do).
/// </summary>
public partial class StationSecurity : Node
{
    [Export] public double WarnGraceSeconds = 4;
    [Export] public double DefenceSeconds = 20;
    [Export] public double ForgetSeconds = 120;
    [Export] public float SpeedTolerance = 1.1f;
    [Export] public double SpeedingSeconds = 3;
    [Export] public double FineEverySeconds = 60;

    [ExportGroup("Police")]
    [Export] public PackedScene PoliceScene;
    [Export] public int PoliceCount = 2;
    /// <summary>How far past the zone the police still go after a hostile ship.</summary>
    [Export] public float PursuitMargin = 1000f;
    [Export] public double StandDownSeconds = 30;
    [Export] public double LaunchEverySeconds = 4;

    [ExportGroup("Texts")]
    [Export] public string WarningText = "Weapons fire in the protected zone. Cease fire or you will be engaged.";
    [Export] public string EngageText = "You were warned. Police are engaging you.";
    /// <summary>{0} the ship destroyed.</summary>
    [Export] public string DestroyedText = "You destroyed {0}. Police are engaging you.";
    /// <summary>{0} the speed (m/s), {1} the limit.</summary>
    [Export] public string FineText = "Speeding at {0} m/s, limit {1} m/s. You have been fined.";

    /// <summary>The station operator's lines for each text (see <see cref="StationRadio.Say"/>).</summary>
    [ExportGroup("Speech")]
    [Export] public string WarningSpeech = "warning";
    [Export] public string EngageSpeech = "engage";
    [Export] public string DestroyedSpeech = "destroyed";
    [Export] public string FineSpeech = "fined";

    private sealed class Record
    {
        public Standing Standing;
        public double Since, LastSeen;
    }

    public StationRadio Radio { get; private set; }

    /// <summary>Fines given, by ship (for when there is money).</summary>
    public IReadOnlyDictionary<Node3D, int> Fines => _fines;

    private readonly Dictionary<Node3D, Record> _records = new();
    private readonly Dictionary<Node3D, (Node3D By, double At)> _lastHit = new();
    private readonly Dictionary<Node3D, int> _fines = new();
    private readonly Dictionary<Node3D, (double Fast, double Fined)> _speeding = new();
    private readonly List<(Node3D Ship, FighterAi Pilot, HangarDock From)> _police = new();
    private readonly HashSet<Node3D> _returning = new();
    private double _now, _quiet, _sinceLaunch = double.MaxValue, _sinceLook;
    private int _launched;

    public override void _Ready()
    {
        foreach (Node n in GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
            Radio ??= n as StationRadio;
    }

    public override void _EnterTree()
    {
        Combat.Fired += OnFired;
        Combat.Damaged += OnDamaged;
        Combat.Destroyed += OnDestroyed;
    }

    public override void _ExitTree()
    {
        Combat.Fired -= OnFired;
        Combat.Damaged -= OnDamaged;
        Combat.Destroyed -= OnDestroyed;
    }

    public Standing StandingOf(Node3D ship) => ship != null && _records.TryGetValue(ship, out Record r) ? r.Standing : Standing.Clean;

    /// <summary>Traffic control does not deal with it.</summary>
    public bool Refuses(Node3D ship) => StandingOf(ship) == Standing.Hostile;

    /// <summary>The ships its police go after.</summary>
    public IEnumerable<Node3D> Hostiles => _records.Where(kv => kv.Value.Standing == Standing.Hostile && Valid(kv.Key)).Select(kv => kv.Key);

    public bool IsPolice(Node3D ship) => _police.Any(p => p.Ship == ship);

    private bool Acts => Radio?.Laws != null && !(SessionNode.Instance is SessionNode s && s.Active && !s.Session.IsHost);

    private void OnFired(Node3D ship, Vector3 at)
    {
        if (!Acts || !Radio.Protects(at) || IsPolice(ship) || Defending(ship))
            return;
        Record r = RecordOf(ship);
        r.LastSeen = _now;
        if (r.Standing == Standing.Clean)
        {
            r.Standing = Standing.Warned;
            r.Since = _now;
            Say(ship, WarningText, WarningSpeech);
        }
        else if (r.Standing == Standing.Warned && _now - r.Since > WarnGraceSeconds)
            Engage(ship, EngageText, EngageSpeech);
    }

    private void OnDamaged(Node3D victim, Node3D by) => _lastHit[victim] = (by, _now);

    private void OnDestroyed(Node3D victim, Node3D by, Vector3 at)
    {
        bool crime = Acts && by != null && Radio.Protects(at) && !IsPolice(by) && StandingOf(victim) == Standing.Clean
                     && !(_lastHit.TryGetValue(by, out var hit) && hit.By == victim && _now - hit.At < DefenceSeconds);
        _records.Remove(victim);                    // a ship destroyed starts clean
        _lastHit.Remove(victim);
        if (crime)
            Engage(by, string.Format(DestroyedText, Callsign(victim)), DestroyedSpeech);
    }

    // Hit by another ship lately: firing back is no crime
    private bool Defending(Node3D ship) => _lastHit.TryGetValue(ship, out var hit) && _now - hit.At < DefenceSeconds && hit.By != ship;

    private void Engage(Node3D ship, string text, string speech)
    {
        Record r = RecordOf(ship);
        if (r.Standing == Standing.Hostile)
            return;
        r.Standing = Standing.Hostile;
        r.Since = r.LastSeen = _now;
        Say(ship, text, speech);
    }

    private Record RecordOf(Node3D ship)
    {
        if (!_records.TryGetValue(ship, out Record r))
            _records[ship] = r = new Record { LastSeen = _now };
        return r;
    }

    private void Say(Node3D ship, string text, string speech) =>
        Radio.Say(ship.FindChildren("*", "", true, false).OfType<IRadioReceiver>().FirstOrDefault(), text, speech);

    private static string Callsign(Node3D ship) =>
        ship.FindChildren("*", "", true, false).OfType<IRadio>().FirstOrDefault()?.Callsign ?? Space.Radio.Callsign("", ship);

    private static bool Valid(Node3D ship) => IsInstanceValid(ship) && !ship.IsQueuedForDeletion();

    public override void _PhysicsProcess(double delta)
    {
        _now += delta;
        _sinceLaunch += delta;
        if (!Acts)
            return;
        Forget();
        _sinceLook += delta;
        if (_sinceLook >= 0.5)
        {
            Speeding(_sinceLook);
            _sinceLook = 0;
        }
        Police(delta);
    }

    private void Forget()
    {
        foreach (Node3D ship in _records.Keys.ToList())
        {
            Record r = _records[ship];
            if (!Valid(ship))
            {
                _records.Remove(ship);
                continue;
            }
            if (Radio.Protects(ship.GlobalPosition) && r.Standing == Standing.Hostile)
                r.LastSeen = _now;
            if (_now - r.LastSeen > ForgetSeconds)
                _records.Remove(ship);
        }
        foreach (Node3D ship in _lastHit.Keys.Where(s => !Valid(s)).ToList())
            _lastHit.Remove(ship);
    }

    private void Speeding(double dt)
    {
        StationLaws laws = Radio.Laws;
        foreach (Node node in GetTree().GetNodesInGroup(Space.Radio.Group))
        {
            if (Combat.ShipOf(node) is not Node3D ship || !Combat.IsAlive(ship) || IsPolice(ship) || Velocity(ship) is not Vector3 velocity)
                continue;
            var (fast, fined) = _speeding.TryGetValue(ship, out var s) ? s : (0, double.MinValue);
            float speed = velocity.Length();
            bool over = ship.GlobalPosition.DistanceTo(Radio.GlobalPosition) <= laws.SpeedRadius && speed > laws.SpeedLimit * SpeedTolerance;
            fast = over ? fast + dt : 0;
            if (fast >= SpeedingSeconds && _now - fined > FineEverySeconds)
            {
                fined = _now;
                _fines[ship] = (_fines.TryGetValue(ship, out int n) ? n : 0) + 1;
                Say(ship, string.Format(FineText, Mathf.RoundToInt(speed), Mathf.RoundToInt(laws.SpeedLimit)), FineSpeech);
            }
            _speeding[ship] = (fast, fined);
        }
        foreach (Node3D ship in _speeding.Keys.Where(s => !Valid(s)).ToList())
            _speeding.Remove(ship);
    }

    private static Vector3? Velocity(Node3D ship) => ship switch
    {
        ShipBody b => b.LinearVelocity,
        RemoteShip r => r.Velocity,
        _ => null,
    };

    // ---------- the police ----------

    private void Police(double delta)
    {
        _police.RemoveAll(p => !Valid(p.Ship));
        foreach (var p in _police.ToList())
            if (p.Ship is ShipBody { Wrecked: true })
            {
                _police.Remove(p);
                Node3D wreck = p.Ship;
                GetTree().CreateTimer(10).Timeout += () => { if (Valid(wreck)) wreck.QueueFree(); };
            }
        float reach = Radio.Laws.ZoneRadius + PursuitMargin;
        Node3D[] wanted = Hostiles.Where(h => Combat.IsAlive(h) && h.GlobalPosition.DistanceTo(Radio.GlobalPosition) < reach).ToArray();
        _quiet = wanted.Length > 0 ? 0 : _quiet + delta;
        if (wanted.Length > 0 && _police.Count < PoliceCount && _sinceLaunch > LaunchEverySeconds && PoliceScene != null)
            Launch(wanted[0]);
        foreach (var (ship, pilot, from) in _police)
        {
            if (pilot == null)
                continue;
            if (_quiet < StandDownSeconds)
            {
                if (_returning.Remove(ship))
                    pilot.Route.Clear();        // trouble again: back to work
                continue;
            }
            if (_returning.Add(ship))
            {
                Radio.Hold(from, ship, 180);
                pilot.Route.Clear();
                pilot.Route.Enqueue(from.ApproachPoint);
                pilot.Route.Enqueue(from.InsidePoint);
            }
            else if (pilot.Route.Count == 0 || from.Holds(ship))
            {
                _returning.Remove(ship);
                ship.QueueFree();               // back in its hangar
            }
        }
    }

    /// <summary>A police ship out of the dock nearest the trouble.</summary>
    private void Launch(Node3D trouble)
    {
        HangarDock dock = Radio.Docks.Where(d => d.ClearedFor == null)
            .OrderBy(d => d.GlobalPosition.DistanceTo(trouble.GlobalPosition)).FirstOrDefault();
        if (dock == null || PoliceScene.Instantiate() is not Node3D ship)
            return;
        _sinceLaunch = 0;
        ship.Name = $"Police{++_launched}";
        var parts = ship.FindChildren("*", "", true, false).Prepend(ship).ToList();
        foreach (NetBody body in parts.OfType<NetBody>())
            body.Spawned = true;
        foreach (ShipHull hull in parts.OfType<ShipHull>())
            hull.Insured = false;
        FighterAi pilot = parts.OfType<FighterAi>().FirstOrDefault();
        if (pilot != null)
        {
            pilot.Police = this;
            pilot.Route.Enqueue(dock.ApproachPoint);   // out of the hangar first
        }
        Node world = Radio.GetParent()?.GetParent() ?? GetTree().CurrentScene;
        world.AddChild(ship);
        // In the hangar, its nose to the door
        Basis outward = dock.GlobalBasis.Rotated(dock.GlobalBasis.Y.Normalized(), Mathf.Pi);
        ship.GlobalTransform = new Transform3D(outward.Orthonormalized(), dock.InsidePoint);
        if (pilot != null)
            pilot.Anchor = Radio.GlobalPosition;
        Radio.Hold(dock, ship, 30);
        _police.Add((ship, pilot, dock));
    }
}
