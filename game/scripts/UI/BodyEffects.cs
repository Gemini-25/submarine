using DeepLine.Game.Sim;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.UI;

/// <summary>
/// 몸의 상태를 화면 효과로만 보인다 (SPEC 7절 '몸의 상태'). 숫자나 막대는 없다.
/// 이산화탄소는 흐림과 숨 가쁨, 산소 부족은 시야 축소, 염소는 누런 기운과 눈물, 감전은 번쩍임과 흔들림,
/// 숨은 체력이 줄면 화면이 어둡고 빛이 바랜다.
/// </summary>
public partial class BodyEffects : CanvasLayer
{
    const string ShaderCode = """
        shader_type canvas_item;
        uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
        uniform float blur = 0.0;
        uniform float tunnel = 0.0;
        uniform float dark = 0.0;
        uniform float flash = 0.0;
        uniform vec3 flash_color = vec3(0.8, 0.9, 1.0);
        uniform float tint = 0.0;
        uniform vec2 shake = vec2(0.0);
        void fragment() {
            vec2 uv = SCREEN_UV + shake;
            vec3 c = textureLod(screen_tex, uv, blur * 3.5).rgb;
            float d = distance(SCREEN_UV, vec2(0.5));
            float inner = 0.62 - tunnel * 0.42;
            float v = 1.0 - smoothstep(inner, inner + 0.32, d);
            c *= mix(1.0, v, clamp(tunnel * 1.2 + dark * 0.5, 0.0, 1.0));
            float grey = dot(c, vec3(0.3, 0.59, 0.11));
            c = mix(c, vec3(grey), dark * 0.6);
            c *= 1.0 - dark * 0.35;
            c = mix(c, c * vec3(1.05, 1.0, 0.55) + vec3(0.04, 0.05, 0.0), tint);
            c = mix(c, flash_color, flash);
            COLOR = vec4(c, 1.0);
        }
        """;

    SimRunner _runner = null!;
    ShaderMaterial _mat = null!;
    ColorRect _rect = null!;
    readonly RandomNumberGenerator _rng = new();
    float _flash, _breath, _blur, _tunnel, _tint, _dark;
    Color _flashColor = new(0.8f, 0.9f, 1f);

    public void Setup(SimRunner runner)
    {
        _runner = runner;
        Layer = 4;
        _mat = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _rect = UiKit.Full(new ColorRect { Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore });
        AddChild(_rect);
        _runner.EventRaised += e =>
        {
            if (e.Kind == SimEventKind.Shock) { _flash = 0.85f; _flashColor = new Color(0.85f, 0.92f, 1f); }
            else if (e.Kind == SimEventKind.Pinch) { _flash = 0.6f; _flashColor = new Color(0.7f, 0.05f, 0.03f); }
            else if (e.Kind == SimEventKind.Explosion) { _flash = 1f; _flashColor = new Color(1f, 0.85f, 0.6f); }
            // 화상은 주황, 증기는 흰 김, 물 분출은 희푸른 물보라 (M2 설계 'HUD, body and flow'의 회색 상자 버전).
            else if (e.Kind == SimEventKind.Hurt && e.Detail == "spray") { _flash = 0.7f; _flashColor = new Color(0.75f, 0.85f, 0.95f); }
            else if (e.Kind == SimEventKind.Hurt && e.Detail == "steam") { _flash = 0.75f; _flashColor = new Color(0.95f, 0.95f, 0.92f); }
            else if (e.Kind == SimEventKind.Hurt) { _flash = 0.6f; _flashColor = new Color(1f, 0.5f, 0.15f); }
        };
    }

    public override void _Process(double delta)
    {
        var s = _runner.Snapshot;
        if (s == null) return;
        float dt = (float)delta;
        // 숨: 이산화탄소가 높을수록 빠르고 깊다.
        _breath += dt * (0.25f + 0.25f * s.Co2Level) * Mathf.Tau;
        float breath = 0.5f + 0.5f * Mathf.Sin(_breath);
        float blur = s.Co2Level * 0.22f * (0.6f + 0.4f * breath) + s.Cl2Level * 0.15f;
        float tunnel = s.O2Level / 3f;
        float tint = s.Cl2Level / 3f * 0.6f;
        float dark = (float)Math.Clamp(1 - s.Crew.Vitality, 0, 1) * 0.9f;
        _blur = Mathf.Lerp(_blur, blur, Math.Min(1, dt * 3));
        _tunnel = Mathf.Lerp(_tunnel, tunnel, Math.Min(1, dt * 1.5f));
        _tint = Mathf.Lerp(_tint, tint, Math.Min(1, dt * 1.5f));
        _dark = Mathf.Lerp(_dark, dark, Math.Min(1, dt * 2));
        _flash = Math.Max(0, _flash - dt * 2.2f);
        var shake = Vector2.Zero;
        if (s.Crew.Stunned) shake = new Vector2(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)) * 0.006f;

        _mat.SetShaderParameter("blur", _blur);
        _mat.SetShaderParameter("tunnel", _tunnel);
        _mat.SetShaderParameter("tint", _tint);
        _mat.SetShaderParameter("dark", _dark);
        _mat.SetShaderParameter("flash", _flash);
        _mat.SetShaderParameter("flash_color", new Vector3(_flashColor.R, _flashColor.G, _flashColor.B));
        _mat.SetShaderParameter("shake", shake);
        // 효과가 없으면 화면 복사를 하지 않는다.
        _rect.Visible = _blur > 0.01f || _tunnel > 0.01f || _tint > 0.01f || _dark > 0.01f || _flash > 0.01f || s.Crew.Stunned;
    }

    /// <summary>음향 쪽이 쓰는 숨의 위상(0~1).</summary>
    public float Breath => 0.5f + 0.5f * Mathf.Sin(_breath);
}
