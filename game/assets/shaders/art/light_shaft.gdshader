// 현창으로 새어 드는 빛줄기(가짜 부피광). 원뿔 메시의 지역 y가 0(현창)에서 -shaft_length(안쪽)로 간다.
// 호환 렌더러에는 부피 안개가 없어서 이것으로 대신한다.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 color : source_color = vec3(0.62, 0.78, 1.0);
uniform float intensity = 0.0;
uniform float shaft_length = 2.0;
uniform float time_offset = 0.0;

varying vec3 lpos;

float sh_hash(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

void vertex() {
	lpos = VERTEX;
}

void fragment() {
	float along = clamp(-lpos.y / shaft_length, 0.0, 1.0);
	float fade = (1.0 - along) * (1.0 - along) * smoothstep(0.0, 0.06, along);
	// 가장자리는 옅게
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float soft = facing * facing;
	// 물속 부유물이 흐르는 얼룩
	float motes = 0.75 + 0.25 * sh_hash(floor(vec3(lpos.x * 40.0, lpos.y * 12.0 + TIME * 0.7 + time_offset, lpos.z * 40.0)));
	ALBEDO = color;
	ALPHA = clamp(intensity * fade * soft * motes, 0.0, 1.0);
}
