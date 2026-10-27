// 낡은 도장 강철: 모서리 닳음(방청 도료와 맨 강철), 아래쪽 때, 판 이음선과 녹물 줄, 바닥판 무늬, 젖음.
// 모서리 마스크는 꼭짓점 색의 R(ArtMesh가 넣는다). 판 이음선은 UV(m) 기준.
shader_type spatial;

#include "res://assets/shaders/art/art_common.gdshaderinc"

uniform vec3 paint_color : source_color = vec3(0.40, 0.43, 0.38);
uniform vec3 primer_color : source_color = vec3(0.40, 0.17, 0.11);
uniform vec3 bare_color : source_color = vec3(0.52, 0.52, 0.53);
uniform vec3 grime_color : source_color = vec3(0.075, 0.062, 0.05);
uniform vec3 rust_color : source_color = vec3(0.30, 0.13, 0.05);
uniform float wear : hint_range(0.0, 1.0) = 0.45;
uniform float grime : hint_range(0.0, 1.0) = 0.5;
// 바닥(y = 0)에서 이 높이까지 때가 탄다.
uniform float grime_height = 0.7;
uniform float rust : hint_range(0.0, 1.0) = 0.3;
uniform float noise_scale = 2.5;
uniform float paint_roughness = 0.6;
// 판 이음선 간격(UV m). 0이면 없다.
uniform vec2 seam_spacing = vec2(0.0);
uniform vec2 seam_offset = vec2(0.0);
// 바닥판 미끄럼 방지 무늬 0~1.
uniform float tread : hint_range(0.0, 1.0) = 0.0;
// 모서리 마스크를 얼마나 믿을지. 키트 밖 메시(꼭짓점 색 없음)는 0.
uniform float edge_weight : hint_range(0.0, 1.0) = 1.0;
// CC0 잔결 질감(docs/ASSETS.md '아트 키트 질감'). UV(m)에 detail_tile번 되풀이한다.
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_rough : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D rust_tex : source_color, hint_default_black, filter_linear_mipmap, repeat_enable;
uniform sampler2D rust_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float detail_tile = 1.6;

varying vec3 lpos;
varying vec3 wpos;
varying float edge;

void vertex() {
	lpos = VERTEX;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	edge = COLOR.r * edge_weight;
}

float seam_dist(float u, float spacing, float offset) {
	return abs(fract((u - offset) / spacing + 0.5) - 0.5) * spacing;
}

void fragment() {
	float n = art_fbm(lpos * noise_scale);
	float n2 = art_fbm(lpos * noise_scale * 5.0 + 3.7);
	float h = 0.0;

	// 모서리 닳음: 마스크에 잡음을 섞어 들쭉날쭉하게. 맨 강철 둘레에 방청 도료가 드러난다.
	float chip = edge * 1.1 + (n2 - 0.5) * 1.0 + (n - 0.5) * 0.45;
	float th = 1.08 - wear * 0.7;
	float primer = smoothstep(th - 0.1, th - 0.06, chip);
	float bare = smoothstep(th - 0.02, th + 0.03, chip);
	// 판 가운데의 작은 칠 떨어짐
	float speck = smoothstep(0.76, 0.79, n2) * step(0.4, wear) * smoothstep(0.55, 0.75, n);
	bare = max(bare, speck);
	primer = max(primer, speck);
	h += (1.0 - primer) * 0.4;

	vec3 col = paint_color * (0.84 + 0.32 * n);
	col = mix(col, primer_color * (0.8 + 0.4 * n2), primer * 0.85);
	col = mix(col, bare_color * (0.65 + 0.5 * n2), bare);
	// 드러난 강철은 녹이 슬어 있다(녹 질감). 녹이 많을수록 넓게.
	vec2 duv = UV * detail_tile;
	vec3 rust_c = texture(rust_tex, duv * 0.7).rgb;
	float rusted = bare * smoothstep(0.45, 0.7, n + rust * 0.4);
	col = mix(col, rust_c, rusted * 0.85);

	// 판 이음선: 겹친 판의 홈, 그 아래로 흘러내린 녹물 줄
	float seam = 0.0;
	if (seam_spacing.x > 0.0) seam = max(seam, 1.0 - smoothstep(0.002, 0.007, seam_dist(UV.x, seam_spacing.x, seam_offset.x)));
	if (seam_spacing.y > 0.0) {
		float d = seam_dist(UV.y, seam_spacing.y, seam_offset.y);
		seam = max(seam, 1.0 - smoothstep(0.002, 0.007, d));
		float below = mod(UV.y - seam_offset.y, seam_spacing.y);
		below = seam_spacing.y - below;
		float streak = (1.0 - smoothstep(0.0, 0.35, below)) * smoothstep(0.58, 0.78, art_noise(vec3(UV.x * 28.0, UV.y * 1.6, 5.0)));
		col = mix(col, mix(rust_color, rust_c, 0.6) * (0.7 + 0.6 * n), streak * rust * 0.85);
	}
	col *= 1.0 - seam * 0.55;
	h -= seam;

	// 바닥판 무늬: 엇갈린 길쭉한 돌기
	if (tread > 0.0) {
		vec2 q = UV * 30.0;
		vec2 c = fract(q) - 0.5;
		float alt = mod(floor(q.x) + floor(q.y), 2.0);
		vec2 r = alt > 0.5 ? c : c.yx;
		float bump = 1.0 - smoothstep(0.1, 0.16, length(vec2(r.x * 0.3, r.y)));
		h += bump * tread * 0.8;
		// 돌기 꼭대기는 발에 닳아 맨 강철이 보인다.
		col = mix(col, bare_color * (0.7 + 0.4 * n2), bump * tread * 0.55);
	}

	// 아래쪽 때: 바닥에 가까울수록 진하다.
	float gh = clamp(1.0 - (wpos.y + (n - 0.5) * 0.4) / grime_height, 0.0, 1.0);
	float g = clamp(grime * gh * gh * 1.2, 0.0, 0.9);
	col = mix(col, grime_color, g);

	float tex_rough = texture(detail_rough, duv).r;
	float rough = mix(paint_roughness, 0.38, bare) * (0.75 + 0.5 * tex_rough) + g * 0.2 + rusted * 0.3 + (n2 - 0.5) * 0.08;
	// 실내에는 반사광이 거의 없어서 금속도를 높이면 맨 강철이 검게 보인다. 반만 금속으로.
	float metal = mix(0.05, 0.45, bare * (1.0 - g) * (1.0 - rusted));
	float w = art_wet(wpos, n);
	art_apply_wet(col, rough, w);

	ALBEDO = col;
	ROUGHNESS = clamp(rough, 0.04, 1.0);
	METALLIC = metal;
	NORMAL = art_bump(NORMAL, VERTEX, h * 0.0025, 1.0);
	vec3 nm = mix(texture(detail_normal, duv).rgb, texture(rust_normal, duv * 0.7).rgb, rusted);
	NORMAL_MAP = nm;
	NORMAL_MAP_DEPTH = mix(0.5, 1.0, rusted) * (1.0 - w * 0.6);
}
