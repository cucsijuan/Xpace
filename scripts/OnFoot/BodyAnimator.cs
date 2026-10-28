using Godot;

namespace Xpace.OnFoot;

/// <summary>
/// Moves a character's body as its owner says: how fast it goes over the ground, whether it stands on something, and
/// whether it sits. The body scene's AnimationTree has a state machine with Move (a blend space by speed, m/s: idle,
/// walk, jog, sprint), Air (falling or jumping) and Sit; this node only feeds it, so the animations, their speeds and
/// the cross-fades are the designer's, in the scene. The same body shows the local walker and the other players'.
/// </summary>
public partial class BodyAnimator : Node3D
{
    [Export] public AnimationTree Tree;

    /// <summary>How fast the blend follows the speed (1/s): the steps change pace smoothly.</summary>
    [Export] public float BlendRate = 8f;

    /// <summary>Speed over the ground, m/s.</summary>
    public float Speed { get; set; }

    public bool Grounded { get; set; } = true;

    public bool Seated { get; set; }

    private float _blend;
    private AnimationNodeStateMachinePlayback _playback;

    public override void _Ready()
    {
        if (Tree != null)
        {
            Tree.Active = true;
            _playback = Tree.Get("parameters/playback").As<AnimationNodeStateMachinePlayback>();
        }
    }

    public override void _Process(double delta)
    {
        if (Tree == null)
            return;
        _blend = Mathf.Lerp(_blend, Speed, 1 - Mathf.Exp(-BlendRate * (float)delta));
        Tree.Set("parameters/Move/blend_position", _blend);
        string state = Seated ? "Sit" : Grounded ? "Move" : "Air";
        if (_playback != null && _playback.GetCurrentNode() != state)
            _playback.Travel(state);
    }
}
