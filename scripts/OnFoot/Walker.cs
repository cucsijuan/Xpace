using System;
using Godot;
using Xpace.Menu;

namespace Xpace.OnFoot;

/// <summary>
/// A person on foot, seen from behind the right shoulder as in Helldivers 2. The mouse turns the camera; WASD walk
/// (jog) where the camera looks, Shift sprints, Space jumps; the body turns to where it goes, or, while aiming (right
/// button), to where the camera looks. The dot in the middle of the view is where the camera looks; the circle is
/// where the arms point: they follow the camera with a lag of their own (<see cref="AimFollow"/>), so a weapon's sway
/// and recoil will move the circle and not the view. The camera sits on two spring arms (to the shoulder, then back)
/// that stop short of whatever is in the way (<see cref="WalkSurface.Layer"/>): ceilings and walls never hide the
/// body.
///
/// Gravity pulls down the parent's -Y (a station's floor, say: its artificial gravity), and the walker keeps the
/// parent's frame, so a station turned any way in space is walked the same; the walker rides along when the world
/// shifts (<see cref="Space.FloatingOrigin"/>). What it meets are the walk surfaces (<see cref="WalkSurface"/>).
/// </summary>
public partial class Walker : CharacterBody3D
{
    [Export] public float JogSpeed = 3.6f;
    [Export] public float SprintSpeed = 6.0f;
    [Export] public float AimSpeed = 1.5f;
    [Export] public float Acceleration = 20f;
    [Export] public float AirAcceleration = 3f;
    [Export] public float Gravity = 9.81f;
    [Export] public float JumpSpeed = 4.2f;
    /// <summary>A ledge up to this high (m) is stepped onto, not walked into: a stair's top, a door's sill.</summary>
    [Export] public float StepHeight = 0.35f;
    /// <summary>How fast the body turns to where it goes, 1/s.</summary>
    [Export] public float TurnRate = 12f;

    [Export] public float LookSensitivity = 0.003f;
    [Export] public bool InvertMouseY;
    [Export(PropertyHint.Range, "-89,0")] public float MinPitch = -70f;
    [Export(PropertyHint.Range, "0,89")] public float MaxPitch = 60f;
    /// <summary>Field of view while aiming (degrees); the camera's own when not.</summary>
    [Export] public float AimFov = 50f;

    /// <summary>How fast the arms follow the camera (1/s): the circle trails the dot by this.</summary>
    [Export] public float AimFollow = 14f;
    /// <summary>How far the aim looks for what it points at (m).</summary>
    [Export] public float AimRange = 300f;

    /// <summary>The body's animations (speed, ground, seat).</summary>
    [Export] public BodyAnimator Body;
    [Export] public Node3D Yaw;
    [Export] public Node3D Pitch;
    [Export] public Camera3D Camera;
    [Export] public SpringArm3D SideArm;
    [Export] public SpringArm3D BackArm;
    /// <summary>Where the arms aim from (the chest); the circle is where its ray along the aim meets something.</summary>
    [Export] public Node3D Chest;

    /// <summary>What the player can do here now, shown at the bottom ("E: board the ship"); empty for nothing.</summary>
    public string Prompt { get; set; } = "";

    /// <summary>The interact key was pressed this frame (the mode that owns the walker reads it).</summary>
    public event Action Interacted;

    /// <summary>Where the arms point (scene direction).</summary>
    public Vector3 AimDirection { get; private set; } = Vector3.Forward;

    /// <summary>The point the arms' aim meets, and the point the camera's middle meets.</summary>
    public Vector3 AimPoint { get; private set; }

    public bool Aiming { get; private set; }

    /// <summary>The camera's yaw and pitch (radians) in the walker's frame.</summary>
    public float LookYaw { get; set; }
    public float LookPitch { get; set; }

    private float _facing, _fov;
    private float _sensitivity;
    private bool _invert;
    private int _settleFrames;

    public override void _Ready()
    {
        OnFootInput.Register();
        _sensitivity = LookSensitivity;
        _invert = InvertMouseY;
        if (GameSettings.Current is GameSettings settings)
        {
            Settle(settings);
            settings.Changed += Settle;
            TreeExiting += () => settings.Changed -= Settle;
        }
        foreach (SpringArm3D arm in new[] { SideArm, BackArm })
            if (arm != null)
            {
                arm.CollisionMask = WalkSurface.Layer;
                arm.AddExcludedObject(GetRid());
            }
        CollisionLayer = 0;                     // walkers do not stand on each other (yet)
        CollisionMask = WalkSurface.Layer;
        if (Camera != null)
            _fov = Camera.Fov;
        _facing = LookYaw;
        AimDirection = CameraForward;
    }

    private void Settle() => Settle(GameSettings.Current);

    private void Settle(GameSettings settings)
    {
        LookSensitivity = _sensitivity * settings.LookSensitivity;
        InvertMouseY = _invert != settings.InvertMouseY;
    }

    /// <summary>Takes the mouse and the view.</summary>
    public void TakeControl()
    {
        Camera?.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _settleFrames = 3;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (GameMenu.IsOpen || Camera == null || !Camera.Current)
            return;
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured && _settleFrames == 0:
                float sense = LookSensitivity * Camera.Fov / _fov;
                LookYaw -= motion.Relative.X * sense;
                LookPitch = Mathf.Clamp(LookPitch - motion.Relative.Y * sense * (InvertMouseY ? -1 : 1),
                                        Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
                break;
            case InputEventMouseButton { Pressed: true } when Input.MouseMode != Input.MouseModeEnum.Captured:
                TakeControl();                  // the window had lost the mouse: this click only takes it back
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                GetViewport().SetInputAsHandled();
                GameMenu.Open();
                break;
            default:
                if (e.IsActionPressed(OnFootInput.Interact, false, true))
                    Interacted?.Invoke();
                break;
        }
    }

    private Vector3 Up => GlobalBasis.Y.Normalized();

    private Vector3 CameraForward => Camera != null ? -Camera.GlobalBasis.Z : -GlobalBasis.Z;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_settleFrames > 0)
            _settleFrames--;
        // The walker keeps its parent's frame: only its place is its own
        if (GetParent() is Node3D parent)
            GlobalBasis = parent.GlobalBasis.Orthonormalized();
        UpDirection = Up;

        bool controls = !GameMenu.IsOpen && Camera != null && Camera.Current;
        Vector2 input = controls ? Input.GetVector(OnFootInput.Left, OnFootInput.Right, OnFootInput.Forward, OnFootInput.Back) : Vector2.Zero;
        Aiming = controls && Input.IsActionPressed(OnFootInput.Aim);
        bool sprint = controls && !Aiming && Input.IsActionPressed(OnFootInput.Sprint) && input.Y < 0;

        // Walk where the camera looks (its yaw, in the walker's frame)
        var yaw = new Basis(Vector3.Up, LookYaw);
        Vector3 wish = yaw * new Vector3(input.X, 0, input.Y);
        float speed = Aiming ? AimSpeed : sprint ? SprintSpeed : JogSpeed;
        Vector3 local = GlobalBasis.Inverse() * Velocity;
        var flat = new Vector3(local.X, 0, local.Z);
        flat = flat.MoveToward(wish * speed, (IsOnFloor() ? Acceleration : AirAcceleration) * dt);
        float rise = local.Y;
        if (IsOnFloor())
        {
            rise = Mathf.Max(rise, 0);
            if (controls && Input.IsActionJustPressed(OnFootInput.Jump))
                rise = JumpSpeed;
        }
        else
            rise -= Gravity * dt;
        Velocity = GlobalBasis * new Vector3(flat.X, rise, flat.Z);
        MoveAndSlide();
        if (IsOnFloor() && IsOnWall() && wish.LengthSquared() > 0.01f)
            StepUp(GlobalBasis * wish.Normalized());

        // The body turns to where it goes, or to the camera while aiming
        float target = _facing;
        if (Aiming)
            target = LookYaw;
        else if (flat.LengthSquared() > 0.04f)
            target = Mathf.Atan2(-flat.X, -flat.Z);
        _facing = Mathf.LerpAngle(_facing, target, 1 - Mathf.Exp(-TurnRate * dt));
        if (Body != null)
        {
            Body.Rotation = new Vector3(0, _facing, 0);
            Body.Speed = flat.Length();
            Body.Grounded = IsOnFloor();
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Yaw != null)
            Yaw.Rotation = new Vector3(0, LookYaw, 0);
        if (Pitch != null)
            Pitch.Rotation = new Vector3(LookPitch, 0, 0);
        if (Camera != null)
            Camera.Fov = Mathf.Lerp(Camera.Fov, Aiming ? AimFov : _fov, 1 - Mathf.Exp(-10 * dt));

        // The arms follow the camera with their own lag
        Vector3 look = CameraForward;
        AimDirection = AimDirection.Slerp(look, 1 - Mathf.Exp(-AimFollow * dt)).Normalized();
        Vector3 from = Chest?.GlobalPosition ?? GlobalPosition + Up * 1.4f;
        AimPoint = Hit(from, AimDirection);
    }

    // Walking into a low ledge: if there is room above it and a little way on, step up there (the floor snap puts
    // the feet down on it)
    private void StepUp(Vector3 ahead)
    {
        Vector3 up = Up * StepHeight;
        Transform3D at = GlobalTransform;
        if (TestMove(at, up) || TestMove(at.Translated(up), ahead * 0.15f))
            return;
        GlobalPosition += up + ahead * 0.15f;
    }

    /// <summary>Where a ray meets a walk surface, or its end.</summary>
    public Vector3 Hit(Vector3 from, Vector3 direction)
    {
        Vector3 to = from + direction * AimRange;
        var query = PhysicsRayQueryParameters3D.Create(from, to, WalkSurface.Layer, new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["position"].AsVector3() : to;
    }
}
