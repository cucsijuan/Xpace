// A Synty skybox: six square images, one per face of a cube around the
// camera, as Unity's "Skybox/6 Sided" shader draws them. Front looks down
// -Z (where an unturned camera looks), Left lies along +X and Right along
// -X, Back along +Z; Up's bottom edge and Down's top edge meet Front.
// Tint and exposure work as Unity's: a grey tint (0.5) leaves the images
// as they are.
// The sun is drawn where the first directional light comes from: a disc,
// a corona round it and a wide glow, all far brighter than the stars so
// the environment's glow blooms it. Nothing is drawn while that light is
// off or absent.
shader_type sky;

uniform sampler2D front_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D back_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D left_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D right_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D up_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D down_tex : source_color, filter_linear, repeat_disable;
uniform vec4 tint : source_color = vec4(0.5, 0.5, 0.5, 1.0);
uniform float exposure : hint_range(0.0, 8.0) = 1.0;
// Turns the sky about the vertical axis, in degrees.
uniform float rotation : hint_range(0.0, 360.0) = 0.0;
uniform vec4 sun_color : source_color = vec4(1.0, 0.92, 0.8, 1.0);
// The disc's angular radius, in degrees.
uniform float sun_size : hint_range(0.05, 10.0) = 0.6;
uniform float sun_energy : hint_range(0.0, 200.0) = 40.0;
// The corona: brightness at the disc's edge and how fast it falls (degrees).
uniform float corona_energy : hint_range(0.0, 50.0) = 3.0;
uniform float corona_size : hint_range(0.1, 30.0) = 1.2;
// The wide glow round the sun, scattered by the optics more than space.
uniform float halo_energy : hint_range(0.0, 5.0) = 0.06;
uniform float halo_size : hint_range(1.0, 90.0) = 6.0;

// Where a direction meets a face: u to the face's right, v down it.
vec2 face_uv(vec3 d, vec3 right, vec3 up, float major) {
    return vec2(0.5 + 0.5 * dot(d, right) / major, 0.5 - 0.5 * dot(d, up) / major);
}

void sky() {
    float a = radians(rotation);
    vec3 d = EYEDIR;
    d.xz = mat2(vec2(cos(a), -sin(a)), vec2(sin(a), cos(a))) * d.xz;
    vec3 m = abs(d);
    vec3 c;
    if (m.x >= m.y && m.x >= m.z) {
        c = d.x > 0.0
            ? texture(left_tex, face_uv(d, vec3(0, 0, 1), vec3(0, 1, 0), m.x)).rgb
            : texture(right_tex, face_uv(d, vec3(0, 0, -1), vec3(0, 1, 0), m.x)).rgb;
    } else if (m.y >= m.z) {
        c = d.y > 0.0
            ? texture(up_tex, face_uv(d, vec3(1, 0, 0), vec3(0, 0, 1), m.y)).rgb
            : texture(down_tex, face_uv(d, vec3(1, 0, 0), vec3(0, 0, -1), m.y)).rgb;
    } else {
        c = d.z < 0.0
            ? texture(front_tex, face_uv(d, vec3(1, 0, 0), vec3(0, 1, 0), m.z)).rgb
            : texture(back_tex, face_uv(d, vec3(-1, 0, 0), vec3(0, 1, 0), m.z)).rgb;
    }
    COLOR = c * tint.rgb * 2.0 * exposure;
    if (LIGHT0_ENABLED) {
        float angle = degrees(acos(clamp(dot(EYEDIR, LIGHT0_DIRECTION), -1.0, 1.0)));
        float disc = 1.0 - smoothstep(sun_size * 0.9, sun_size, angle);
        float edge = max(angle - sun_size, 0.0);
        float corona = corona_energy * exp(-edge / corona_size);
        float halo = halo_energy * exp(-angle / halo_size);
        vec3 sun = sun_color.rgb * LIGHT0_COLOR * LIGHT0_ENERGY;
        COLOR = mix(COLOR, sun * sun_energy, disc) + sun * (corona * (1.0 - disc) + halo);
    }
}
