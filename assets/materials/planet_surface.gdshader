// A planet's surface, on the Body of a DistantBody: it is drawn nearer and smaller than it is (share), so it writes the
// depth of where it really is, and whatever stands in front of it or behind it stays there.
shader_type spatial;

uniform vec3 albedo : source_color = vec3(1.0);
uniform sampler2D albedo_texture : source_color, hint_default_white;
uniform float roughness : hint_range(0.0, 1.0) = 1.0;

instance uniform float share = 1.0;

void fragment() {
	ALBEDO = albedo * texture(albedo_texture, UV).rgb;
	ROUGHNESS = roughness;
	// OpenGL's depth buffer can't tell these distances from the sky: there it keeps the depth it is drawn at
#if CURRENT_RENDERER != RENDERER_COMPATIBILITY
	vec4 real = PROJECTION_MATRIX * vec4(0.0, 0.0, VERTEX.z / share, 1.0);
	DEPTH = max(real.z / real.w, 1e-8);	// past the far plane it still has to stay in front of the sky
#endif
}
