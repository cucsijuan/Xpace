using Godot;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// What others sense of the ship it sits under: a radar echo, the heat of
/// its motor and its hull to hit. The motor is as hot as the main thrust
/// it gives (brightest from behind, where the nozzles are); the hull is
/// warm even with the motor idle: <see cref="HullHeat"/>, or, if the ship
/// keeps its heat (<see cref="HeatDevice"/>), <see cref="HullHeatAtFull"/>
/// times how hot it is (cold after a heat sink, bright when overheating). A warhead or a round that hits it is
/// counted and passed on to the ship's <see cref="ShipHull"/>, if it has
/// one; a wreck shows nothing: no echo, no heat.
/// Put it at the ship's middle.
/// </summary>
public partial class ShipSignature : Node3D, IRadarTarget, IDamageable, IHeatSource
{
    [Export] public float RadarCrossSection { get; set; } = 4f;   // m²
    [Export] public float HullHeat = 8f;            // the motor idle
    [Export] public float HullHeatAtFull = 30f;     // with a HeatDevice: at 100%
    [Export] public float MotorHeat = 80f;          // at full thrust, seen from straight behind
    [Export] public float FrontHeat = 0.3f;         // the share of it seen from ahead or the side

    public ushort RadarId { get; private set; }
    public Vector3 Velocity => _ship?.LinearVelocity ?? Vector3.Zero;
    public bool Alive => !(_hull?.Wrecked ?? false);
    public int Hits { get; private set; }
    public float DamageTaken { get; private set; }

    [Signal] public delegate void HitTakenEventHandler(float damage);

    private ShipBody _ship;
    private ShipHull _hull;
    private HeatDevice _heat;
    private EngineDevice _engine;

    /// <summary>The hull's heat, 1 at 100% (the heat device's; a puppet: what its owner says).</summary>
    public float HullLevel => ShownLevel ?? _heat?.Level ?? HullHeat / HullHeatAtFull;

    /// <summary>A puppet: the owner's hull heat.</summary>
    public float? ShownLevel { get; set; }

    public override void _Ready()
    {
        RadarId = RadarIds.Next();
        AddToGroup("radar_targets");
        AddToGroup("heat_sources");
        for (Node n = GetParent(); n != null && _ship == null; n = n.GetParent())
            _ship = n as ShipBody;
        foreach (Node n in _ship?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            _hull ??= n as ShipHull;
            _heat ??= n as HeatDevice;
            _engine ??= n as EngineDevice;
        }
    }

    public void Paint() { }

    public void Hit(float damage)
    {
        if (!Alive)
            return;
        Hits++;
        DamageTaken += damage;
        EmitSignal(SignalName.HitTaken, damage);
        _hull?.Hit(damage);
    }

    public float HeatAt(Vector3 from, out Vector3 direction)
    {
        if (!Alive)
        {
            direction = Vector3.Zero;
            return 0;
        }
        float thrust = _ship == null ? 0 : Mathf.Max(0, _ship.Commands[5]);
        float heat = Heat.Point(GlobalPosition, 1, from, out direction);
        float behind = Mathf.Max(0, -direction.Dot(GlobalBasis.Z.Normalized()));
        float hull = _heat != null || ShownLevel.HasValue ? HullHeatAtFull * HullLevel : HullHeat;
        float engine = _engine?.Glow ?? 0;          // its radiators, and a core run too hot
        return heat * (hull + engine + MotorHeat * thrust * (FrontHeat + (1 - FrontHeat) * behind));
    }
}
