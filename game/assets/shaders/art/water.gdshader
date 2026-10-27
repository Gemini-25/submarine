// 바닥에 고인 물과 빌지 물. 잔물결 법선이 등빛을 잘게 되비춰서 어두운 실내에서도 물이 있는 것이 보인다.
// 물의 빛은 등에서만 온다(스스로 빛나지 않는다).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://assets/shaders/art/art_common.gdshaderinc"

uniform vec3 water_color : source_color = vec3(0.06, 0.12, 0.12);
uniform float opacity : hint_range(0.0, 1.0) = 0.45;
// 잔물결 세기. 누수가 떨어지는 곳은 높이고 고인 물은 낮춘다.
uniform float ripple : hint_range(0.0, 1.0) = 0.35;
uniform float flow_speed = 0.15;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 p = vec3(wpos.x * 9.0, TIME * flow_speed * 3.0, wpos.z * 9.0);
	float h = art_fbm(p) + 0.5 * art_noise(p * 3.1 + vec3(0.0, TIME * 0.6, 0.0));
	NORMAL = art_bump(NORMAL, VERTEX, h * 0.004 * ripple, 1.0);
	float n = art_noise(vec3(wpos.xz * 2.0, 1.0));
	ALBEDO = water_color * (0.8 + 0.4 * n);
	ROUGHNESS = 0.06;
	METALLIC = 0.0;
	SPECULAR = 0.8;
	// 비스듬히 볼수록 반사가 세지고 덜 비친다.
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALPHA = clamp(opacity + fres * 0.3, 0.0, 1.0);
}
