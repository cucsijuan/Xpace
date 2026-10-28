// Paint stencilled from white glyphs: the texture's brightness (Synty's sign
// atlas, white on black) or its alpha (PolygonGeneric decals, white on clear)
// says where the paint is, `paint` says its colour. Lit like the surface it
// is painted on.
shader_type spatial;
render_mode cull_back;

uniform sampler2D glyphs : source_color, filter_linear_mipmap, repeat_disable;
uniform vec4 paint : source_color = vec4(1.0, 0.78, 0.05, 1.0);
uniform float roughness = 0.65;
uniform float cutoff = 0.5;
uniform bool cover_from_alpha = false;

void fragment() {
    vec4 glyph = texture(glyphs, UV);
    float cover = cover_from_alpha ? glyph.a : glyph.r;
    ALBEDO = paint.rgb;
    ROUGHNESS = roughness;
    ALPHA = cover;
    ALPHA_SCISSOR_THRESHOLD = cutoff;
}
