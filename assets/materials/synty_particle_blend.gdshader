// Unity's legacy Particles/Alpha Blended, as Synty's FX materials use it: the tint doubled (0.5 is neutral),
// times the vertex colour (alpha too: Synty's light meshes fade by it, and particles' colour is there), times the
// texture; blended over what is behind, lit by nothing, seen from both sides. With billboard, each particle faces
// the eye at its own turn about the line of sight and keeps its scale (Unity's view alignment).
shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;

uniform vec4 tint : source_color = vec4(0.5, 0.5, 0.5, 0.5);
uniform sampler2D main_texture : source_color, hint_default_white;
uniform bool billboard = false;

void vertex() {
	if (billboard) {
		mat4 facing = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
		float a = INSTANCE_CUSTOM.x;
		facing = facing * mat4(vec4(cos(a), -sin(a), 0.0, 0.0), vec4(sin(a), cos(a), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
		MODELVIEW_MATRIX = VIEW_MATRIX * facing * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
			vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
		MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	}
}

void fragment() {
	vec4 c = 2.0 * tint * COLOR * texture(main_texture, UV);
	ALBEDO = c.rgb;
	ALPHA = clamp(c.a, 0.0, 1.0);
}
