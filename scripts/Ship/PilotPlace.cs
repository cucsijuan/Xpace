using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.Space;
using Xpace.World;

namespace Xpace.Ship;

/// <summary>
/// Keeps where a player's ship was left (<see cref="ShipRecord.Place"/>) and puts it back there when the game starts
/// again: docked, on its pad (or another free one at that station), in space, where it was and as it was moving. A new
/// pilot starts docked at the map's start station (<see cref="SectorMap.StartStation"/>). Only in a universe kept on
/// disk and played alone or as a session's host; a guest's ship starts where the session puts it. Put it under the
/// player's <see cref="ShipBody"/>.
/// </summary>
public partial class PilotPlace : Node
{
    /// <summary>How many steps it waits for the station to be ready before giving up and leaving the ship where it is.</summary>
    [Export] public int PatienceSteps = 120;

    private ShipBody _ship;
    private Universe _universe;
    private bool _placed;
    private int _steps;

    /// <summary>The ship's place is kept here: a universe on disk, not played as a session's guest.</summary>
    public static bool Keeps =>
        Universe.Current is { Folder: not null, IsGuest: false } && OnlinePlay.Current?.Transport == null &&
        !OS.GetCmdlineUserArgs().Any(a => a.Split('=')[0] is "--join" or "--online");

    private static ShipPlace Saved => Keeps ? Universe.Current.Me?.Ship.Place : null;

    /// <summary>The sector the pilot's ship was left in, if it is kept and on the map.</summary>
    public static Sector SavedSector(SectorMap map) => Saved?.Sector is string id ? map?.Find(id) : null;

    public override void _EnterTree()
    {
        _ship = GetParentOrNull<ShipBody>();
        _universe = Universe.Current;
        if (_universe != null)
            _universe.Saving += Keep;
    }

    public override void _ExitTree()
    {
        if (!Sectors.Moving)
            Write();
        if (_universe != null)
            _universe.Saving -= Keep;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_placed || _ship == null)
            return;
        if (!Keeps || Place() || ++_steps >= PatienceSteps)
            _placed = true;
    }

    // Puts the ship where its record says; false: the station isn't ready yet
    private bool Place()
    {
        ShipPlace place = Saved;
        string station = place == null ? SectorHost.Current?.Map?.StartStation : place.Station;
        if (!string.IsNullOrEmpty(station))
        {
            if (StationRadio.Called(station) is not StationRadio radio || radio.Docks.Count == 0)
                return false;
            if (_ship.Device<RadioDevice>() is RadioDevice receiver && radio.Berth(receiver, place?.Dock ?? 0, place?.Pad ?? 0))
            {
                Spawned();
                return true;
            }
        }
        if (place != null && FloatingOrigin.Of(_ship) is FloatingOrigin origin && origin.Name == place.Sector)
        {
            var point = new SectorPoint(place.X, place.Y, place.Z);
            origin.Shift(origin.Origin.To(point));          // the ship's place becomes the origin: no precision lost far out
            _ship.State = new ShipState
            {
                Position = origin.Origin.To(point),
                Orientation = Turn(place.Turn),
                Velocity = Vector(place.Velocity),
                AngularVelocity = Vector(place.Spin),
            };
            Spawned();
        }
        return true;
    }

    // A ship the insurer replaces comes back where this one started
    private void Spawned()
    {
        if (ShipBody.Part<ShipHull>(_ship) is ShipHull hull)
            hull.Spawn = _ship.GlobalTransform;
    }

    private void Keep() => Write();

    // Puts where the ship is in its record; false if there was nothing to keep (wrecked, between sectors, not placed yet)
    private bool Write()
    {
        if (!_placed || _ship == null || !IsInstanceValid(_ship) || !_ship.IsInsideTree() || _ship.Wrecked || !Keeps ||
            _ship.Holder is GateTransit || _universe?.RecordOf(_ship) is not PilotRecord pilot ||
            FloatingOrigin.Of(_ship) is not FloatingOrigin origin)
            return false;
        ShipState state = _ship.State;
        SectorPoint at = origin.Origin + state.Position;
        var place = new ShipPlace
        {
            Sector = origin.Name,
            X = at.X,
            Y = at.Y,
            Z = at.Z,
            Turn = new[] { state.Orientation.X, state.Orientation.Y, state.Orientation.Z, state.Orientation.W },
            Velocity = new[] { state.Velocity.X, state.Velocity.Y, state.Velocity.Z },
            Spin = new[] { state.AngularVelocity.X, state.AngularVelocity.Y, state.AngularVelocity.Z },
        };
        if (_ship.Holder is LandingPad { State: LandingPad.PadState.Docked or LandingPad.PadState.Holding, Dock.Station: StationRadio station } pad)
        {
            place.Station = station.Id;
            place.Dock = pad.Dock.Number;
            place.Pad = pad.Number;
        }
        if (Same(pilot.Ship.Place, place))
            return false;
        pilot.Ship.Place = place;
        _universe.Touch();
        return true;
    }

    // Nothing worth writing again: the same pad, or within a metre and nearly still
    private static bool Same(ShipPlace a, ShipPlace b) =>
        a != null && a.Sector == b.Sector && a.Station == b.Station && a.Dock == b.Dock && a.Pad == b.Pad &&
        (a.Station != null || new SectorPoint(a.X, a.Y, a.Z).To(new SectorPoint(b.X, b.Y, b.Z)).Length() < 1 &&
            Vector(a.Velocity).DistanceTo(Vector(b.Velocity)) < 0.5f && Turn(a.Turn).AngleTo(Turn(b.Turn)) < 0.02f);

    private static Quaternion Turn(float[] q) =>
        q is { Length: 4 } && new Quaternion(q[0], q[1], q[2], q[3]) is var t && t.LengthSquared() > 0.5f ? t.Normalized() : Quaternion.Identity;

    private static Vector3 Vector(float[] v) => v is { Length: 3 } ? new Vector3(v[0], v[1], v[2]) : Vector3.Zero;
}
