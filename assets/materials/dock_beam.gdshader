// The beam out of a cleared dock: light along the edges of a box whose
// length runs along Z, from the door (z = -Length/2) outwards, fading with
// the distance from the door, with bands drifting in towards it, and only a
// faint veil between the edges.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 color : source_color = vec4(0.25, 1.0, 0.7, 1.0);
uniform float strength = 0.5;
uniform float veil = 0.03;
uniform float length = 220.0;
uniform vec2 half_size = vec2(27.0, 11.0);
uniform float edge_sharpness = 10.0;
uniform float bands = 9.0;
uniform float speed = 0.5;

varying vec3 local;

void vertex() {
    local = VERTEX;
}

void fragment() {
    float t = clamp(local.z / length + 0.5, 0.0, 1.0);
    float band = 0.5 + 0.5 * sin((t * bands + TIME * speed) * 6.2832);
    float fade = (1.0 - t) * smoothstep(0.0, 0.03, t);
    // On a side face one of these is 1: the other says how near an edge
    float across = min(abs(local.x) / half_size.x, abs(local.y) / half_size.y);
    float edge = pow(clamp(across, 0.0, 1.0), edge_sharpness);
    ALBEDO = color.rgb * fade * (strength * edge * (0.4 + 0.6 * band) + veil * band);
}
