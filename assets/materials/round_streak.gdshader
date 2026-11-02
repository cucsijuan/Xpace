shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

// A round in flight as a light: a white-hot round head at the front, a core
// thinning down its tail, and a soft glow round both. Everything is worked out
// from the distance to that shape, in half widths of the strip, so the head is
// a true circle at any length. INSTANCE_CUSTOM.x: the strip's length in half widths.

uniform vec3 core_colour : source_color = vec3(1.0, 0.85, 0.55);
uniform vec3 glow_colour : source_color = vec3(1.0, 0.45, 0.12);
uniform float energy = 3.0;
uniform float core_radius = 0.17;   // the head's radius, in half widths of the strip
uniform float tail_radius = 0.03;
uniform float head_heat = 2.2;      // how hard the head burns, white
uniform float head_size = 0.26;     // the white-hot spot's radius, in half widths of the strip
uniform float white_from = 0.72;    // the core turns white from here along it to the head

varying float strip_length;

void vertex() {
    strip_length = INSTANCE_CUSTOM.x;
}

void fragment() {
    float x = UV.x * 2.0 - 1.0;
    float from_tail = (1.0 - UV.y) * strip_length;     // a quad's V runs down from its +Y end, the head
    float head_at = strip_length - 1.0;                // the head's centre: one half width back from the strip's end

    // How far along the body, 0 at the tail and 1 at the head; ahead of the head's centre it stays there, so the end is round
    float along = clamp(from_tail / max(head_at, 0.001), 0.0, 1.0);
    float past = max(from_tail - head_at, 0.0);
    float d = length(vec2(x, past));

    float radius = mix(tail_radius, core_radius, along * along);
    float tail_fade = smoothstep(0.0, 0.25, along);

    // Softened by a pixel's width: a core thinner than a pixel dims, and doesn't break into dashes
    float pixel = fwidth(x);
    float soft = max(radius, pixel);
    float core = smoothstep(soft + pixel, soft * 0.45 - pixel, d) * mix(0.35, 1.0, along) * min(radius / pixel + 0.25, 1.0);
    float glow = exp(-d * d / (0.1 + 0.16 * along * along)) * tail_fade;
    vec2 to_head = vec2(x, from_tail - head_at);
    float hot = exp(-dot(to_head, to_head) / (head_size * head_size));

    vec3 body = mix(glow_colour, core_colour, along);
    body = mix(body, vec3(1.0), smoothstep(white_from, 1.0, along));
    ALBEDO = (body * core + glow_colour * glow * 0.6) * energy + vec3(1.0, 0.96, 0.88) * hot * head_heat;
}
