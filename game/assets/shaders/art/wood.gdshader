// 나무(쐐기, 버팀목, 손잡이, 의자). 결은 물체의 grain_axis를 따라간다. 모서리는 닳아 밝다. 젖음.
shader_type spatial;

#include "res://assets/shaders/art/art_common.gdshaderinc"

uniform vec3 light_color : source_color = vec3(0.56, 0.40, 0.24);
uniform vec3 dark_color : source_color = vec3(0.30, 0.19, 0.10);
uniform vec3 grain_axis = vec3(1.0, 0.0, 0.0);
uniform float ring_density = 55.0;
uniform float varnish : hint_range(0.0, 1.0) = 0.3;

varying vec3 lpos;
varying vec3 wpos;
varying float edge;

void vertex() {
	lpos = VERTEX;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	edge = COLOR.r;
}

void fragment() {
	vec3 ax = normalize(grain_axis);
	float along = dot(lpos, ax);
	vec3 across = lpos - ax * along;
	float warp = art_fbm(vec3(along * 1.5, across.yz * 3.0) + 4.0);
	float rings = fract(length(across + 0.3) * ring_density + warp * 4.0);
	float grain = smoothstep(0.0, 0.25, rings) * (1.0 - smoothstep(0.55, 1.0, rings));
	float fine = art_noise(vec3(along * 3.0, across.yz * 160.0));
	vec3 col = mix(dark_color, light_color, grain * 0.7 + fine * 0.3);
	col = mix(col, light_color * 1.25, clamp(edge * 0.6, 0.0, 1.0));
	float rough = mix(0.78, 0.4, varnish) + (fine - 0.5) * 0.1;
	float w = art_wet(wpos, warp);
	art_apply_wet(col, rough, w);
	ALBEDO = col;
	ROUGHNESS = clamp(rough, 0.05, 1.0);
	METALLIC = 0.0;
}
