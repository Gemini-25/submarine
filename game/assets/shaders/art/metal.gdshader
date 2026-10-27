// 황동, 구리, 청동, 맨 강철. 변색과 녹청, 손이 닿는 모서리는 반들반들하다. 젖음.
shader_type spatial;

#include "res://assets/shaders/art/art_common.gdshaderinc"

uniform vec3 base_color : source_color = vec3(0.80, 0.62, 0.32);
uniform vec3 tarnish_color : source_color = vec3(0.28, 0.21, 0.11);
uniform vec3 patina_color : source_color = vec3(0.22, 0.40, 0.33);
uniform float tarnish : hint_range(0.0, 1.0) = 0.5;
uniform float patina : hint_range(0.0, 1.0) = 0.0;
// 반사광이 거의 없는 실내라서 1에 가깝게 두면 검게 보인다(ArtMat 값 참고).
uniform float metallic_amount : hint_range(0.0, 1.0) = 0.6;
uniform float rough_polished = 0.22;
uniform float rough_tarnished = 0.55;
uniform float noise_scale = 7.0;
// CC0 긁힘 마스크(docs/ASSETS.md '아트 키트 질감'). UV(m)에 scratch_tile번.
uniform sampler2D scratch_mask : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float scratch_tile = 4.0;
uniform float scratches : hint_range(0.0, 1.0) = 0.6;

varying vec3 lpos;
varying vec3 wpos;
varying float edge;

void vertex() {
	lpos = VERTEX;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	edge = COLOR.r;
}

void fragment() {
	float n = art_fbm(lpos * noise_scale);
	float n2 = art_fbm(lpos * noise_scale * 3.1 + 1.7);
	// 손이 닿는 모서리와 튀어나온 곳은 변색이 벗겨진다.
	float polish = clamp(edge * 0.8, 0.0, 1.0);
	float t = smoothstep(0.38, 0.68, n + tarnish * 0.35 - polish * 0.6) * tarnish;
	vec3 col = mix(base_color * (0.9 + 0.2 * n2), tarnish_color, t);
	float p = patina * smoothstep(0.55, 0.72, n2) * (1.0 - polish);
	col = mix(col, patina_color, p);
	// 긁힌 자리는 변색 밑의 밝은 금속이 드러나고 결이 거칠다.
	float sc = texture(scratch_mask, UV * scratch_tile).r * scratches;
	col = mix(col, base_color * 1.1, sc * t * 0.8);
	float rough = mix(rough_polished, rough_tarnished, t) + p * 0.3 + (n2 - 0.5) * 0.08 + sc * 0.15;
	float metal = metallic_amount * (1.0 - p) * (1.0 - t * 0.35);
	float w = art_wet(wpos, n);
	art_apply_wet(col, rough, w);
	ALBEDO = col;
	ROUGHNESS = clamp(rough, 0.04, 1.0);
	METALLIC = metal;
}
