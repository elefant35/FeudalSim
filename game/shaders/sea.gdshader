// M2-FP4: the sea at 0 m — depth-tinted, a fresnel sheen toward the sky colour, and drifting ripples in the normal.
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform vec4 water_color : source_color = vec4(0.13, 0.27, 0.32, 0.86);
uniform vec4 sky_color : source_color = vec4(0.70, 0.78, 0.83, 1.0);

void fragment() {
	vec3 wp = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 r = vec2(sin(wp.x * 0.31 + TIME * 0.9) + sin(wp.z * 0.17 - TIME * 0.6), cos(wp.z * 0.27 + TIME * 1.1) + cos(wp.x * 0.13 + TIME * 0.5));
	vec3 n = normalize(vec3(r.x * 0.045, 1.0, r.y * 0.045));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
	float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
	ALBEDO = mix(water_color.rgb, sky_color.rgb, f * 0.65);
	ALPHA = water_color.a;
	ROUGHNESS = 0.06;
	METALLIC = 0.05;
	SPECULAR = 0.6;
}
