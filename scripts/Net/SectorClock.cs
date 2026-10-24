using Godot;

namespace Xpace.Net;

/// <summary>
/// The sector's tick: one per physics step, the same number on every
/// machine in the session (the session will keep them in step; alone it
/// just counts). Snapshots and fire events are stamped with it.
/// </summary>
public partial class SectorClock : Node
{
    public static SectorClock Instance { get; private set; }

    public uint Tick { get; set; }

    public override void _EnterTree()
    {
        Instance = this;
        ProcessPhysicsPriority = -1000;         // before everything that reads it
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _PhysicsProcess(double delta) => Tick++;
}
