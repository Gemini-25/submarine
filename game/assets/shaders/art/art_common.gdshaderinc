// 아트 키트 공용: 잡음, 젖음, 높이에서 법선 만들기 (SPEC 11절).
// 젖음은 구역마다 수위를 따로 받는다. 표현 층이 시뮬레이션의 바닥 수위로 매 프레임 맞춘다(ArtMat.SetWater).

// 구역별 수위(세계 y): 후방, 중앙, 전방. 물이 없으면 아주 낮게 둔다.
uniform vec3 zone_water = vec3(-10.0);
// 구역 경계(세계 x): 후방|중앙, 중앙|전방. 모스호의 해치 격벽과 전방 틀.
uniform vec2 zone_split = vec2(2.7, 5.4);
// 수면 위로 젖은 띠의 높이(m). 물이 튀고 스며 올라간 자국.
uniform float wet_band = 0.16;
// 전체 습기(결로) 0~1. 선체 온도와 습도에서 정한다.
uniform float dampness : hint_range(0.0, 1.0) = 0.0;

float art_hash(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float art_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(mix(art_hash(i), art_hash(i + vec3(1.0, 0.0, 0.0)), f.x),
			mix(art_hash(i + vec3(0.0, 1.0, 0.0)), art_hash(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
		mix(mix(art_hash(i + vec3(0.0, 0.0, 1.0)), art_hash(i + vec3(1.0, 0.0, 1.0)), f.x),
			mix(art_hash(i + vec3(0.0, 1.0, 1.0)), art_hash(i + vec3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
}

// 네 겹 잡음. 값은 대략 0.2~0.8에 모인다.
float art_fbm(vec3 p) {
	float a = 0.5, s = 0.0;
	for (int i = 0; i < 4; i++) {
		s += a * art_noise(p);
		p = p * 2.03 + 17.1;
		a *= 0.5;
	}
	return s / 0.9375;
}

float art_water_level(float x) {
	return x < zone_split.x ? zone_water.x : (x < zone_split.y ? zone_water.y : zone_water.z);
}

// 0(마름)~1(흠뻑). 수면 아래와 그 위 띠, 그리고 결로.
float art_wet(vec3 wp, float n) {
	float above = wp.y - art_water_level(wp.x);
	float soaked = 1.0 - smoothstep(0.0, wet_band, above + (n - 0.5) * 0.08);
	float damp = dampness * smoothstep(0.38, 0.62, n);
	return clamp(max(soaked, damp), 0.0, 1.0);
}

// 젖으면 어두워지고 매끈해진다.
void art_apply_wet(inout vec3 albedo, inout float rough, float w) {
	albedo *= mix(1.0, 0.62, w);
	rough = mix(rough, 0.06, w);
}

// 높이 값에서 법선을 기울인다(화면 미분). vpos와 n은 시점 공간.
vec3 art_bump(vec3 n, vec3 vpos, float h, float strength) {
	vec3 dpdx = dFdx(vpos);
	vec3 dpdy = dFdy(vpos);
	float dhx = dFdx(h);
	float dhy = dFdy(h);
	vec3 r1 = cross(dpdy, n);
	vec3 r2 = cross(n, dpdx);
	float det = dot(dpdx, r1);
	vec3 grad = sign(det) * (dhx * r1 + dhy * r2);
	return normalize(abs(det) * n - strength * grad);
}
