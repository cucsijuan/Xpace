using Godot;
using Xpace.Menu;

namespace Xpace.OnFoot;

/// <summary>
/// The walker's view marks: a dot in the middle (where the camera looks) and a circle where the arms point
/// (<see cref="Walker.AimPoint"/>), apart from the dot while the arms catch up or something stands between the
/// shoulder and what the camera sees; and the prompt at the bottom (<see cref="Walker.Prompt"/>).
/// </summary>
public partial class WalkerHud : Control
{
    [Export] public Walker Walker;
    [Export] public Label PromptLabel;
    [Export] public Color DotColor = new(1, 1, 1, 0.9f);
    [Export] public Color CircleColor = new(1, 1, 1, 0.75f);
    [Export] public float DotRadius = 2.5f;
    [Export] public float CircleRadius = 14f;
    /// <summary>The circle's strength while not aiming (it is fully drawn while aiming).</summary>
    [Export(PropertyHint.Range, "0,1")] public float IdleCircle = 0.35f;

    public override void _Process(double delta)
    {
        if (PromptLabel != null)
            PromptLabel.Text = GameMenu.IsOpen || Walker == null ? "" : Walker.Prompt;
        Visible = Walker?.Camera?.Current == true && !GameMenu.IsOpen;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Walker?.Camera is not Camera3D camera)
            return;
        Vector2 middle = GetViewportRect().Size / 2;
        DrawCircle(middle, DotRadius, DotColor);
        if (camera.IsPositionBehind(Walker.AimPoint))
            return;
        Vector2 at = camera.UnprojectPosition(Walker.AimPoint);
        Color color = CircleColor;
        color.A *= Walker.Aiming ? 1 : IdleCircle;
        DrawArc(at, CircleRadius, 0, Mathf.Tau, 40, color, 1.5f, true);
    }
}
