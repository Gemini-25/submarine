// 배전반 대리석(검은 대리석, 흰 결). 반들반들하지만 아래쪽에 손때와 그을음이 탄다.
shader_type spatial;

#include "res://assets/shaders/art/art_common.gdshaderinc"

uniform vec3 base_color : source_color = vec3(0.09, 0.085, 0.08);
uniform vec3 vein_color : source_color = vec3(0.55, 0.53, 0.50);
uniform float vein_scale = 3.0;
uniform float soot : hint_range(0.0, 1.0) = 0.3;

varying vec3 lpos;
varying vec3 wpos;

void vertex() {
	lpos = VERTEX;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float n = art_fbm(lpos * vein_scale);
	float v = abs(sin((lpos.x * 1.3 + lpos.y * 2.1) * vein_scale + n * 9.0));
	float cloud = art_fbm(lpos * vein_scale * 2.7 + 9.0);
	float vein = (1.0 - smoothstep(0.0, 0.035, v)) * smoothstep(0.35, 0.6, cloud);
	vec3 col = base_color * (0.8 + 0.5 * cloud);
	col = mix(col, vein_color, vein * 0.3);
	float s = soot * smoothstep(0.5, 0.75, art_fbm(lpos * 9.0 + 2.0));
	col *= 1.0 - s * 0.6;
	float rough = 0.2 + s * 0.4 + cloud * 0.1;
	float w = art_wet(wpos, n);
	art_apply_wet(col, rough, w);
	ALBEDO = col;
	ROUGHNESS = rough;
	METALLIC = 0.0;
	SPECULAR = 0.6;
}
