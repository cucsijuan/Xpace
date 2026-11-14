// A debug sphere (scripts/Debug/LawZones.cs): a faint fill, a brighter rim where it turns away, and a grid of
// meridians and parallels, so it reads both from outside and from inside.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, blend_mix;

uniform vec4 colour : source_color = vec4(1.0, 0.45, 0.1, 1.0);
uniform float fill = 0.012;          // seen from outside only: inside, the sky keeps its colour
uniform float rim = 0.25;
uniform float meridians = 24.0;
uniform float grid = 0.25;

void fragment() {
    float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
    float edge = pow(1.0 - facing, 3.0);
    vec2 g = UV * vec2(meridians, meridians * 0.5);
    vec2 w = max(fwidth(g), vec2(1e-5));
    vec2 d = abs(fract(g - 0.5) - 0.5) / w;
    float line = 1.0 - clamp(min(d.x, d.y), 0.0, 1.0);
    ALBEDO = colour.rgb;
    float outside = FRONT_FACING ? 1.0 : 0.0;
    ALPHA = clamp(outside * (fill + rim * edge) + grid * line, 0.0, 1.0) * colour.a;
}
