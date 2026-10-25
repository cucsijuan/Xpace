namespace Xpace.Ship;

/// <summary>
/// The pilot's stick, throttle and buttons, wherever they come from (mouse,
/// keyboard, joystick). Axes go from -1 to 1. Pitch up, roll right, yaw
/// right, right and up are positive; a negative throttle is reverse.
/// </summary>
public struct PilotControls
{
    public float Pitch, Roll, Yaw;
    public float Right, Up, Throttle;
    public bool AssistButton;
    public bool BoostButton;
    public bool Trigger;            // primary fire
    public bool TargetAhead;
    public bool NextTarget;
    public bool NextWeapon;         // cycles the weapons (Elite's next fire group)
    public bool Chaff;              // the countermeasures dispenser
    public bool Flare;
    public bool SafeButton;         // FLIGHT's safe turns, on and off
    public bool PipSystems;         // the power distributor: a pip to systems, engines, weapons, or even them out
    public bool PipEngines;
    public bool PipWeapons;
    public bool PipReset;
    public bool HeatSink;
}
