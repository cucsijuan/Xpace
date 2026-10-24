// Synty's ship atlas with its paint changed. A Synty ship takes its colours
// from a strip of swatches in the atlas (the body's colour, the trim's and
// the lights'): every face of the hull points at one swatch. Faces that
// point at a swatch given a colour here are painted that colour (its alpha
// is how much); everything else is the atlas as it is. The emission map
// lights the lights, as in synty_atlas.tres.
shader_type spatial;

uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D emission_map : source_color, hint_default_black, filter_linear_mipmap, repeat_disable;
uniform float emission_energy = 1.5;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.8;

// The swatches, as u from, u to, v from, v to (Texture_01_A's paint strip)
uniform vec4 primary_swatch = vec4(0.2769, 0.2974, 0.6094, 0.6230);
uniform vec4 accent_swatch = vec4(0.2974, 0.3208, 0.6094, 0.6367);
uniform vec4 lights_swatch = vec4(0.3208, 0.3384, 0.6094, 0.6230);

// The new paint (alpha 0: keep the atlas's)
uniform vec4 primary : source_color = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 accent : source_color = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 lights : source_color = vec4(0.0, 0.0, 0.0, 0.0);

bool inside(vec2 uv, vec4 r) {
    return uv.x >= r.x && uv.x < r.y && uv.y >= r.z && uv.y < r.w;
}

void fragment() {
    vec3 base = texture(atlas, UV).rgb;
    vec3 glow = texture(emission_map, UV).rgb;
    if (inside(UV, primary_swatch)) {
        base = mix(base, primary.rgb, primary.a);
    } else if (inside(UV, accent_swatch)) {
        base = mix(base, accent.rgb, accent.a);
    } else if (inside(UV, lights_swatch)) {
        base = mix(base, lights.rgb, lights.a);
        glow = mix(glow, lights.rgb * max(max(glow.r, glow.g), glow.b), lights.a);
    }
    ALBEDO = base;
    ROUGHNESS = roughness_value;
    EMISSION = glow * emission_energy;
}
