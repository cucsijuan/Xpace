// Synty's atlas with an area darkened and its cyan lights recoloured, for models
// built on the atlas that want other colours without a new texture (Redhold:
// the belt's light grey panels made dark, the cyan lights red). Red and orange
// lights stay as they are. With chase on, the lights run along the model's second
// UV (u from 0 to 1, each light at its place): a pulse going from 0 to 1 over and
// over, as runway lights lead a pilot in.
shader_type spatial;

uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D emission_map : source_color, hint_default_black, filter_linear_mipmap, repeat_disable;
uniform float emission_energy = 1.5;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.8;

// u from, u to, v from, v to (v from the image's top), and what it is multiplied by
uniform vec4 dark_area = vec4(0.0, 0.0, 0.0, 0.0);
uniform float dark_tint : hint_range(0.0, 1.0) = 1.0;

// What the cyan lights become, at their brightness
uniform vec3 glow_tint : source_color = vec3(0.0, 1.0, 1.0);

uniform bool chase = false;
uniform float chase_seconds = 1.6;      // one run from u 0 to 1
uniform float chase_width = 0.12;       // of the run
uniform float chase_rest : hint_range(0.0, 1.0) = 0.25;   // the lights' glow between pulses

bool inside(vec2 uv, vec4 r) {
    return uv.x >= r.x && uv.x < r.y && uv.y >= r.z && uv.y < r.w;
}

void fragment() {
    vec3 base = texture(atlas, UV).rgb;
    if (inside(UV, dark_area))
        base *= dark_tint;
    vec3 glow = texture(emission_map, UV).rgb;
    if (glow.b > glow.r)
        glow = glow_tint * dot(glow, vec3(0.2126, 0.7152, 0.0722));
    if (chase) {
        float head = fract(TIME / max(chase_seconds, 0.01)) * (1.0 + 2.0 * chase_width) - chase_width;
        float d = (UV2.x - head) / max(chase_width, 0.001);
        glow *= mix(chase_rest, 1.0, exp(-d * d));
    }
    ALBEDO = base;
    ROUGHNESS = roughness_value;
    EMISSION = glow * emission_energy;
}
