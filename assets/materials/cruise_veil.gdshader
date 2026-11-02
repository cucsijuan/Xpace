shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_front, shadows_disabled, fog_disabled;

// The thinned veil as the pilot sees it from inside: clouds of colour flowing
// along the way the ship is going, round the edges of the view, clear ahead.
// Smooth noise, its coordinates bent by more noise, wrapped round the axis on a
// circle so it has no seam.

uniform vec3 way = vec3(0.0, 0.0, -1.0);    // the ship's way, world axes
uniform float level = 0.0;                  // 0 none, 1 cruising fast
uniform float rush = 0.45;                  // how fast the clouds go by
uniform vec3 colour_a : source_color = vec3(0.25, 0.75, 1.0);
uniform vec3 colour_b : source_color = vec3(0.85, 0.25, 1.0);
uniform vec3 colour_c : source_color = vec3(1.0, 0.65, 0.25);

varying vec3 direction;

vec3 slope(vec3 p) {
    p = vec3(dot(p, vec3(127.1, 311.7, 74.7)), dot(p, vec3(269.5, 183.3, 246.1)), dot(p, vec3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453);
}

float noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return mix(mix(mix(dot(slope(i), f), dot(slope(i + vec3(1, 0, 0)), f - vec3(1, 0, 0)), u.x),
                   mix(dot(slope(i + vec3(0, 1, 0)), f - vec3(0, 1, 0)), dot(slope(i + vec3(1, 1, 0)), f - vec3(1, 1, 0)), u.x), u.y),
               mix(mix(dot(slope(i + vec3(0, 0, 1)), f - vec3(0, 0, 1)), dot(slope(i + vec3(1, 0, 1)), f - vec3(1, 0, 1)), u.x),
                   mix(dot(slope(i + vec3(0, 1, 1)), f - vec3(0, 1, 1)), dot(slope(i + vec3(1, 1, 1)), f - vec3(1, 1, 1)), u.x), u.y), u.z);
}

float clouds(vec3 p) {
    return noise(p) + 0.5 * noise(p * 2.03 + 3.1) + 0.25 * noise(p * 4.01 + 7.7);
}

void vertex() {
    direction = (MODEL_MATRIX * vec4(VERTEX, 0.0)).xyz;
}

void fragment() {
    vec3 d = normalize(direction);
    float along = dot(d, way);
    vec3 side = normalize(cross(way, abs(way.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    vec3 other = cross(way, side);
    float angle = atan(dot(d, other), dot(d, side));
    vec2 ring = vec2(cos(angle), sin(angle));

    float t = TIME * rush;
    vec3 p = vec3(ring * 1.7, along * 1.1 - t);
    vec2 bend = vec2(noise(p * 0.8 + 11.0), noise(p * 0.8 + 37.0)) * 1.1;
    float big = clouds(p + vec3(bend, 0.0));
    float wisps = clouds(vec3(ring * 5.5 + bend * 2.0, along * 0.5 - t * 1.8));

    float density = smoothstep(-0.25, 0.8, big);
    density = density * density * (0.55 + 0.75 * smoothstep(-0.35, 0.7, wisps));
    float edge = smoothstep(0.95, 0.3, abs(along));

    float hue = clamp(0.5 + 0.9 * noise(p * 0.45 + 5.0), 0.0, 1.0);
    vec3 tint = hue < 0.5 ? mix(colour_a, colour_b, hue * 2.0) : mix(colour_b, colour_c, hue * 2.0 - 1.0);

    ALBEDO = tint * density * edge * level * 0.75;
}
