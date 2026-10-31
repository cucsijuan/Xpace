// Unity's legacy additive particles, as Synty's FX use them: the tint doubled (0.5 is neutral), times the
// vertex colour (particles' colour is there), times the texture, alpha scaling it; added to what is behind
// (glowing gas lights, it does not hide), lit by nothing, seen from both sides.
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never, shadows_disabled;

uniform vec4 tint : source_color = vec4(0.5, 0.5, 0.5, 0.5);
uniform sampler2D main_texture : source_color, hint_default_white;

void fragment() {
	vec4 c = 2.0 * tint * COLOR * texture(main_texture, UV);
	ALBEDO = c.rgb * clamp(c.a, 0.0, 1.0);
}
