// A picture tube: the screen's picture behind curved glass, drawn in scanlines through the phosphor's mask, with the
// glow of bright phosphor bleeding round it, a dim edge and a faint flicker; a monochrome tube shows the picture's
// brightness in its one phosphor's colour. CrtMonitor holds the settings.
shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2D picture : source_color, hint_default_black, filter_linear, repeat_disable;
uniform float brightness = 1.1;
uniform float scanlines : hint_range(0.0, 1.0) = 0.4;
uniform float mask : hint_range(0.0, 1.0) = 0.0;
uniform float bloom : hint_range(0.0, 2.0) = 0.6;
uniform float overscan : hint_range(0.0, 0.3) = 0.06;
uniform float curvature : hint_range(0.0, 0.3) = 0.05;
uniform float vignette : hint_range(0.0, 1.0) = 0.4;
uniform float flicker : hint_range(0.0, 0.3) = 0.03;
uniform float noise : hint_range(0.0, 0.3) = 0.04;
uniform float roll : hint_range(0.0, 0.3) = 0.03;
uniform float glass : hint_range(0.0, 0.3) = 0.04;
uniform vec3 tube : source_color = vec3(0.015, 0.02, 0.018);
// A monochrome tube's phosphor (alpha 0: a colour tube)
uniform vec4 monochrome : source_color = vec4(0.0);

float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

// How much of a pattern repeating every unit of x survives on screen: none once a repeat is under two pixels
float resolved(float x)
{
    return 1.0 - smoothstep(0.4, 0.75, fwidth(x));
}

void fragment()
{
    vec2 size = vec2(textureSize(picture, 0));
    vec2 c = UV * 2.0 - 1.0;
    // The bulging glass: the picture's edges bow out to the middle of the rim and its corners round off inside it
    vec2 bent = c * (1.0 + curvature * c.yx * c.yx);
    // The picture stops short of the glass's edge all round, so a bezel's lip or chamfer covers none of it
    vec2 uv = bent / (1.0 - overscan) * 0.5 + 0.5;
    vec3 colour = tube;
    if (all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0))))
    {
        vec2 texel = 1.0 / size;
        vec3 sharp = texture(picture, uv).rgb;
        vec3 halo = vec3(0.0);
        for (int i = 0; i < 8; i++)
        {
            float a = float(i) * 0.785398;
            halo += texture(picture, uv + vec2(cos(a), sin(a)) * texel * 2.5).rgb;
            halo += texture(picture, uv + vec2(cos(a + 0.39), sin(a + 0.39)) * texel * 5.0).rgb;
        }
        halo /= 16.0;
        vec3 lit = sharp + bloom * halo * halo;
        lit = mix(lit, vec3(dot(lit, vec3(0.3, 0.6, 0.1))) * monochrome.rgb * 1.6, monochrome.a);

        // The beam's line is gaussian across; bright lines swell and close the gap
        float row = uv.y * size.y;
        float across = fract(row) - 0.5;
        float width = mix(10.0, 4.0, clamp(dot(sharp, vec3(0.3, 0.6, 0.1)), 0.0, 1.0));
        float line = exp(-across * across * width) / sqrt(3.14159 / width);
        lit *= mix(1.0, line, scanlines * resolved(row));

        // An aperture grille: a red, a green and a blue stripe to each of the picture's pixels
        float stripe = uv.x * size.x * 3.0;
        float which = mod(floor(stripe), 3.0);
        vec3 grille = vec3(float(which == 0.0), float(which == 1.0), float(which == 2.0));
        lit *= mix(vec3(1.0), mix(vec3(1.0 - mask), vec3(1.0 + mask), grille), resolved(stripe / 3.0));

        float edge = dot(bent, bent) * 0.5;
        lit *= 1.0 - vignette * edge * edge;
        float band = smoothstep(0.0, 0.15, 0.15 - abs(fract(uv.y - TIME * 0.11) - 0.5));
        lit *= 1.0 + roll * band;
        lit *= 1.0 - flicker * hash(vec2(floor(TIME * 24.0), 0.37));
        float grain = hash(floor(uv * size) + fract(TIME * 7.13)) - 0.5;
        lit *= 1.0 + noise * grain * 2.0;
        colour = max(tube + lit * brightness, vec3(0.0));
    }
    // The room in the glass: a soft sheen high on the left
    float sheen = clamp(1.0 - length(c - vec2(-0.45, -0.55)) / 1.1, 0.0, 1.0);
    colour += glass * sheen * sheen;
    ALBEDO = colour;
}
