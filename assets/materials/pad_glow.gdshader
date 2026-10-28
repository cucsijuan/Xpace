// A landing pad's glow: light rising from the pad, brightest at the foot
// and along its sides as seen (a column of light), fading with the height.
// Its colour is the pad's: each mesh's instance parameter glow_color, which
// the LandingPad script sets as its state changes. The mesh is centred on
// its own origin, as Godot's cylinder is, and `height` tall.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

instance uniform vec4 glow_color : source_color = vec4(1.0, 0.1, 0.05, 1.0);
uniform float strength = 1.5;
uniform float height = 30.0;
uniform float edge_sharpness = 2.0;
uniform float pulse = 0.25;
uniform float pulse_speed = 1.5;

varying vec3 local;

void vertex() {
    local = VERTEX;
}

void fragment() {
    float t = clamp(local.y / max(height, 0.001) + 0.5, 0.0, 1.0);
    float fade = (1.0 - t) * (1.0 - t);
    // With multisampling a fragment can be shaded off its triangle, where the normal comes out longer than 1:
    // normalised and clamped, the base of pow never goes below 0 (NaN, which the glow spreads as a white blot)
    float facing = clamp(abs(dot(normalize(NORMAL), VIEW)), 0.0, 1.0);
    float edge = pow(max(1.0 - facing, 0.0), edge_sharpness);
    float beat = 1.0 - pulse + pulse * (0.5 + 0.5 * sin(TIME * pulse_speed * 6.2832 - t * 6.0));
    ALBEDO = min(glow_color.rgb * strength * fade * (0.15 + edge) * beat, vec3(4.0));
}
