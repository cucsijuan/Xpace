using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Ship;
using Xpace.World;

namespace Xpace.Space;

/// <summary>
/// Shows the universe's cargo ships (<see cref="CargoShipRecord"/>) where someone can see them: on the universe's
/// owner, for every loaded sector, the real ship of each record that is there: on its pad, or on its way
/// (<see cref="CargoRun"/>). Other machines get them as any ship the game brings in, so its scene must be one of the
/// <see cref="SessionNode.Spawnables"/>. A record stays the truth: the ships shown come and go with it.
/// </summary>
public partial class CargoView : Node
{
    /// <summary>How often it looks at the records, seconds.</summary>
    [Export] public double LookEverySeconds = 1;
    /// <summary>How long a door stays open for a ship after it last asked, seconds.</summary>
    [Export] public double DoorSeconds = 10;

    /// <summary>How long after anyone last dealt with a ship its computer goes on running, seconds.</summary>
    [Export] public double RestAfterSeconds = 60;

    /// <summary>How long a destroyed ship's wreck stays, seconds.</summary>
    [Export] public double WreckSeconds = 60;

    /// <summary>The ships shown now, by the record's id.</summary>
    public IReadOnlyDictionary<string, ShipBody> Shown => _shown;

    private readonly Dictionary<string, ShipBody> _shown = new();
    private readonly Dictionary<string, double> _dealt = new();
    private readonly Dictionary<ShipBody, double> _wrecks = new();
    private readonly HashSet<ShipBody> _struck = new();
    private double _wait;

    public override void _PhysicsProcess(double delta)
    {
        _wait -= delta;
        if (_wait > 0 || Universe.Current is not Universe universe || universe.IsGuest || universe.Book == null || SectorHost.Current is not SectorHost host)
            return;
        _wait = LookEverySeconds;
        foreach (CargoShipRecord record in universe.State.CargoShips.Values.ToList())
            Look(record, universe, host);
        foreach (ShipBody wreck in _wrecks.Keys.ToList())
            if (!IsInstanceValid(wreck))
                _wrecks.Remove(wreck);
            else if ((_wrecks[wreck] -= LookEverySeconds) <= 0)
            {
                wreck.QueueFree();
                _wrecks.Remove(wreck);
            }
    }

    private void Look(CargoShipRecord record, Universe universe, SectorHost host)
    {
        _shown.TryGetValue(record.Id, out ShipBody ship);
        if (ship != null && !IsInstanceValid(ship))
        {
            _shown.Remove(record.Id);
            ship = null;
        }
        // Destroyed where it can be seen, it is gone from the universe: the wreck stays a while
        if (ship?.Find<ShipHull>()?.Wrecked == true)
        {
            CargoShips.Lose(universe.State, record.Id);
            _shown.Remove(record.Id);
            _wrecks[ship] = WreckSeconds;
            universe.Touch();
            return;
        }
        ShipModel model = ResourceLoader.Exists(record.Model) ? GD.Load<ShipModel>(record.Model) : null;
        if (model == null)
            return;
        StationBook.Fix? fix = universe.Book.FixOf(record, model, universe.State.Clock);
        FloatingOrigin root = fix is StationBook.Fix f ? host.Root(f.Sector) : null;
        if (ship != null && (root == null || ship.GetParent() != root))
        {
            if (ship.Holder is CargoRun)
                ship.QueueFree();
            _shown.Remove(record.Id);
            ship = null;
        }
        if (ship == null && root != null && model.Scene != null)
            ship = Bring(record, model, fix.Value, root);
        if (ship != null)
        {
            Doors(record, ship, model, universe);
            Detail(record.Id, ship);
        }
    }

    // Level 0 while someone deals with it: it is hit, or a radar is locked on it; level 1 again a while after
    private void Detail(string id, ShipBody ship)
    {
        if (Dealt(ship))
            _dealt[id] = Time.GetTicksMsec() / 1000.0;
        if (!_dealt.TryGetValue(id, out double at))
            return;
        if (Time.GetTicksMsec() / 1000.0 - at <= RestAfterSeconds)
            ship.Wake();
        else
        {
            ship.Rest();
            _dealt.Remove(id);
        }
    }

    private bool Dealt(ShipBody ship)
    {
        if (_struck.Remove(ship))
            return true;
        foreach (Node node in Sectors.InGroup(ship, "radar_emitters"))
            if ((node as INetRadar ?? node.GetParent() as INetRadar)?.Tracked is Node tracked && Combat.ShipOf(tracked) == ship)
                return true;
        return false;
    }

    public override void _EnterTree() => Combat.Damaged += Struck;

    public override void _ExitTree() => Combat.Damaged -= Struck;

    private void Struck(Node3D victim, Node3D by)
    {
        if (victim is ShipBody ship && _shown.ContainsValue(ship))
            _struck.Add(ship);
    }

    private ShipBody Bring(CargoShipRecord record, ShipModel model, StationBook.Fix fix, FloatingOrigin root)
    {
        if (model.Scene.Instantiate() is not ShipBody ship)
            return null;
        ship.Name = $"Cargo_{record.Id}";
        if (ship.FindChild("Radio", true, false) is RadioDevice radio)
            radio.Callsign = CargoShips.CallsignOf(record.Id);
        var parts = ship.FindChildren("*", "", true, false).ToList();
        foreach (NetBody body in parts.OfType<NetBody>())
            body.Spawned = true;
        foreach (ShipHull hull in parts.OfType<ShipHull>())
            hull.Insured = false;
        var run = new CargoRun(record, model, fix, root);
        ship.Transform = run.Pose(fix);
        ship.Holder = run;
        foreach ((string place, string piece) in record.Fittings)
            if (ResourceLoader.Exists(piece))
                ship.Loadout[place] = GD.Load<Resource>(piece);
        if (ship.FindChild("ShipComputer", true, false) is Xpace.Computer.ShipComputer computer)
            computer.PowerOnAtStart = false;
        root.AddChild(ship);
        ship.Rest();
        _shown[record.Id] = ship;
        return ship;
    }

    // Its station opens the door for it when it is cleared to leave, or to land, until it is through
    private void Doors(CargoShipRecord record, ShipBody ship, ShipModel model, Universe universe)
    {
        double since = universe.State.Clock - record.Left;
        if (record.Phase == CargoShipRecord.Landing)
            StationRadio.Called(record.Going)?.OpenFor(record.Dock, ship, DoorSeconds);
        else if (record.Phase == CargoShipRecord.Flying && record.LeftDock != 0 && since < model.DoorSeconds + DoorSeconds * 3)
            StationRadio.Called(record.From)?.OpenFor(record.LeftDock, ship, DoorSeconds);
    }
}

/// <summary>Holds a cargo ship where its record says: on its pad, holding off a station, or on its way, turning towards it at the speed its numbers give.</summary>
public sealed class CargoRun : IShipHold
{
    /// <summary>How fast it turns to a new heading, radians a second.</summary>
    private const float TurnRate = 0.5f;

    private readonly CargoShipRecord _record;
    private readonly ShipModel _model;
    private readonly FloatingOrigin _root;
    private Quaternion _facing;

    public CargoRun(CargoShipRecord record, ShipModel model, StationBook.Fix fix, FloatingOrigin root)
    {
        _record = record;
        _model = model;
        _root = root;
        _facing = Facing(fix.Heading);
    }

    private static Quaternion Facing(Vector3 heading) =>
        Basis.LookingAt(heading.LengthSquared() > 0 ? heading : Vector3.Forward, Mathf.Abs(heading.Y) > 0.99f ? Vector3.Forward : Vector3.Up).GetRotationQuaternion();

    private Vector3 Local(Vector3 at) => _root.Origin.To(new SectorPoint(at.X, at.Y, at.Z));

    public Transform3D Pose(StationBook.Fix fix) => new(new Basis(_facing), Local(fix.At));

    public ShipState Hold(ShipBody ship, double delta)
    {
        if (ship.Find<ShipHull>()?.Wrecked == true)
        {
            ship.Holder = null;
            return ship.State;
        }
        UniverseState state = Universe.Current.State;
        StationBook.Fix? where = Universe.Current.Book.FixOf(_record, _model, state.Clock);
        if (where is not StationBook.Fix fix)
            return ship.State;
        float angle = _facing.AngleTo(Facing(fix.Heading));
        if (angle > 0.0001f)
            _facing = _facing.Slerp(Facing(fix.Heading), Mathf.Min(1, TurnRate * (float)delta / angle)).Normalized();
        bool cruising = fix.Speed > _model.Hull.TopSpeed(_model.Engine?.RatedThrust ?? _model.Hull.MainThrust) * 1.05f;
        ship.Show(fix.Speed > 0 ? 0.6f : 0, cruising ? 1 : 0);
        return new ShipState { Position = Local(fix.At), Orientation = _facing, Velocity = fix.Heading * fix.Speed };
    }
}
