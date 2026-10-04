// M2-FP4: foliage, reeds and grass sway (art contract §2: vertex colour R = wind weight, 0 at the trunk or root, 1 at the
// tips). The phase follows the instance's world position so a stand doesn't move in lockstep; `strength` follows the
// sim's wind (set by the client once a second).
shader_type spatial;
render_mode cull_disabled;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap;
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform float strength = 0.08;

void vertex() {
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float w = COLOR.r;
	float t = TIME * 1.4 + origin.x * 0.13 + origin.z * 0.09;
	float gust = 0.6 + 0.4 * sin(TIME * 0.31 + origin.x * 0.01);
	VERTEX.x += sin(t) * strength * gust * w * (1.0 + VERTEX.y * 0.06);
	VERTEX.z += cos(t * 0.73) * strength * 0.5 * gust * w;
}

void fragment() {
	vec4 c = texture(albedo_tex, UV) * albedo_color;
	ALBEDO = c.rgb;
	ROUGHNESS = 0.92;
}
