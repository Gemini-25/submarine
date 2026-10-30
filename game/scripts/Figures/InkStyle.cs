using System.IO.Compression;
using Godot;

namespace DeepLine.Game.Figures;

/// <summary>
/// 교범 그림의 색과 먹선 처리, 16색 팔레트 PNG 쓰기. 종이색은 교범 화면의 종이(UiKit.Paper)와 같다.
/// </summary>
public static class InkStyle
{
    public static readonly Color Paper = new(0.90f, 0.86f, 0.75f);
    public static readonly Color Light = new(0.80f, 0.755f, 0.645f);
    public static readonly Color Mid = new(0.655f, 0.61f, 0.51f);
    public static readonly Color Ink = new(0.13f, 0.10f, 0.08f);
    /// <summary>붉은 먹: 장비의 자리, 손이 가는 곳, 고장 난 곳.</summary>
    public static readonly Color Red = new(0.66f, 0.17f, 0.10f);
    public static readonly Color RedLight = new(0.86f, 0.62f, 0.52f);

    /// <summary>그을음, 탄 자국처럼 아주 검은 것은 먹에 가깝게 칠한다 (고장 모습이 종이색 그림에서 드러나게).</summary>
    public static readonly Color Soot = Mid.Lerp(Ink, 0.5f);

    /// <summary>16색. 순수 색 일곱과 그 사이 섞임(선의 가장자리). 그림의 모든 화소는 이 중 하나로 바뀐다.</summary>
    public static readonly Color[] Palette = BuildPalette();

    static Color[] BuildPalette()
    {
        var p = new List<Color> { Paper, Light, Mid, Ink, Red, RedLight };
        foreach (float t in new[] { 0.25f, 0.5f, 0.75f }) p.Add(Paper.Lerp(Ink, t));
        p.Add(Soot);
        p.Add(Light.Lerp(Ink, 0.5f));
        p.Add(Paper.Lerp(Red, 0.5f));
        p.Add(Red.Lerp(Ink, 0.5f));
        p.Add(RedLight.Lerp(Red, 0.5f));
        p.Add(Paper.Lerp(Mid, 0.5f));
        p.Add(Mid.Lerp(Light, 0.5f).Lerp(Ink, 0.25f));
        return p.ToArray();
    }

    /// <summary>시점 공간의 빛 방향 (왼쪽 위 앞).</summary>
    static readonly Vector3 LightDir = new Vector3(-0.45f, 0.65f, 0.62f).Normalized();

    /// <summary>
    /// 찍은 결과를 먹선 그림(찍은 크기 그대로)으로. 윤곽선은 물체가 바뀌는 곳(굵게), 꺾임 선은 한 물체 안에서 법선이 꺾이는 곳(가늘게).
    /// </summary>
    public static Image ToInk(InkRenderer.Capture c)
    {
        int w = c.W, h = c.H, n = w * h;
        var ids = c.Ids;
        var nrm = c.Normals;
        var outline = new bool[n];
        var crease = new bool[n];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int id = ids[i];
                if (x + 1 < w) Mark(i, i + 1);
                if (y + 1 < h) Mark(i, i + w);
                void Mark(int a, int b)
                {
                    if (ids[a] != ids[b]) { outline[ids[a] == 0 ? b : a] = true; return; }
                    if (id != 0 && nrm[a].Dot(nrm[b]) < 0.82f) crease[a] = true;
                }
            }
        }
        // 같은 면에 겹친 두 물체는 번호가 화소마다 뒤섞여 선이 면을 덮는다(z-fighting). 둘레 9×9에서 선 화소가 너무 많은 곳의 선은 지운다.
        // 곧은 선 하나는 9분의 1, 나란한 두 선도 3분의 1이 안 된다.
        Thin(outline, w, h);
        Thin(crease, w, h);
        // 굵기: 윤곽선은 반지름 2(찍은 화소), 꺾임 선은 반지름 1. 그림 크기로 줄이면 약 2.5px과 1.5px.
        var line = Dilate(outline, w, h, 2);
        var thin = Dilate(crease, w, h, 1);
        var data = new byte[n * 3];
        for (int i = 0; i < n; i++)
        {
            Color col;
            int id = ids[i];
            if (line[i] || thin[i]) col = c.Accent.Contains(id) && !line[i] ? Red : Ink;
            else if (id == 0) col = Paper;
            else col = Tone(c.Albedo[id], nrm[i], c.Accent.Contains(id));
            data[i * 3] = (byte)(col.R * 255);
            data[i * 3 + 1] = (byte)(col.G * 255);
            data[i * 3 + 2] = (byte)(col.B * 255);
        }
        return Image.CreateFromData(w, h, false, Image.Format.Rgb8, data);
    }

    /// <summary>세 단계 명암. 아주 어두운 재질(베이클라이트, 검은 칠, 고무)은 한 단계 짙게, 밝은 재질(자기, 눈금판)은 한 단계 옅게.</summary>
    static Color Tone(float albedo, Vector3 n, bool accent)
    {
        if (albedo < 0.05f && !accent) return Soot;
        // 재질 밝기가 단계를 정하고(0.55 넘으면 종이, 0.33 넘으면 옅은 음영, 그 아래 짙은 음영: 진보라 지시약 창(0.31)이 짙게 나온다), 빛 방향은 한 단계만 더하고 뺀다.
        // 밝게 올리는 것은 재질이 밝은 편(0.45 넘게)일 때만: 보라 지시약 창처럼 중간 밝기의 색이 빛을 받아 흰 것과 같아지지 않게.
        // 그래야 흰 지시약 창과 보라 창, 깨끗한 바구니와 막힌 바구니처럼 재질 밝기의 차이가 그림에 남는다.
        float lit = Math.Clamp(n.Dot(LightDir), 0, 1);
        int level = albedo > 0.55f ? 0 : albedo > 0.33f ? 1 : 2;
        if (lit > 0.7f && albedo > 0.45f) level--;
        else if (lit < 0.2f) level++;
        level = Math.Clamp(level, 0, 2);
        if (accent) return level == 0 ? RedLight : RedLight.Lerp(Red, 0.5f);
        return level switch { 0 => Paper, 1 => Light, _ => Mid };
    }

    static void Thin(bool[] e, int w, int h)
    {
        const int r = 4;
        var sum = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            int row = 0;
            for (int x = 0; x < w; x++)
            {
                if (e[y * w + x]) row++;
                sum[(y + 1) * (w + 1) + x + 1] = sum[y * (w + 1) + x + 1] + row;
            }
        }
        var drop = new List<int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!e[y * w + x]) continue;
                int x0 = Math.Max(0, x - r), x1 = Math.Min(w, x + r + 1), y0 = Math.Max(0, y - r), y1 = Math.Min(h, y + r + 1);
                int n = sum[y1 * (w + 1) + x1] - sum[y0 * (w + 1) + x1] - sum[y1 * (w + 1) + x0] + sum[y0 * (w + 1) + x0];
                if (n > (x1 - x0) * (y1 - y0) * 0.36f) drop.Add(y * w + x);
            }
        }
        foreach (int i in drop) e[i] = false;
    }

    static bool[] Dilate(bool[] src, int w, int h, int r)
    {
        var dst = new bool[src.Length];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!src[y * w + x]) continue;
                for (int dy = -r; dy <= r; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= h) continue;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= w || dx * dx + dy * dy > r * r + r) continue;
                        dst[yy * w + xx] = true;
                    }
                }
            }
        }
        return dst;
    }

    /// <summary>화소마다 가장 가까운 팔레트 색의 번호.</summary>
    public static byte[] Quantize(Image img)
    {
        img.Convert(Image.Format.Rgb8);
        var d = img.GetData();
        int n = img.GetWidth() * img.GetHeight();
        var idx = new byte[n];
        var pal = Palette.Select(c => (R: c.R * 255, G: c.G * 255, B: c.B * 255)).ToArray();
        var cache = new Dictionary<int, byte>();
        for (int i = 0; i < n; i++)
        {
            int key = (d[i * 3] << 16) | (d[i * 3 + 1] << 8) | d[i * 3 + 2];
            if (!cache.TryGetValue(key, out byte best))
            {
                float bd = float.MaxValue;
                for (int k = 0; k < pal.Length; k++)
                {
                    float dr = d[i * 3] - pal[k].R, dg = d[i * 3 + 1] - pal[k].G, db = d[i * 3 + 2] - pal[k].B;
                    float dist = dr * dr * 0.3f + dg * dg * 0.59f + db * db * 0.11f;
                    if (dist < bd) { bd = dist; best = (byte)k; }
                }
                cache[key] = best;
            }
            idx[i] = best;
        }
        return idx;
    }

    /// <summary>4비트 팔레트 PNG를 쓴다 (Godot의 SavePng는 팔레트를 쓰지 못한다).</summary>
    public static void WritePalettePng(string path, int w, int h, byte[] indices)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BigEndian(ihdr, 0, (uint)w);
        BigEndian(ihdr, 4, (uint)h);
        ihdr[8] = 4; // 비트 깊이
        ihdr[9] = 3; // 팔레트
        Chunk(fs, "IHDR", ihdr);
        var plte = new byte[Palette.Length * 3];
        for (int k = 0; k < Palette.Length; k++)
        {
            plte[k * 3] = (byte)Math.Round(Palette[k].R * 255);
            plte[k * 3 + 1] = (byte)Math.Round(Palette[k].G * 255);
            plte[k * 3 + 2] = (byte)Math.Round(Palette[k].B * 255);
        }
        Chunk(fs, "PLTE", plte);
        int stride = (w + 1) / 2;
        var raw = new byte[(stride + 1) * h];
        for (int y = 0; y < h; y++)
        {
            int o = y * (stride + 1);
            raw[o] = 0;
            for (int x = 0; x < w; x++)
            {
                byte v = indices[y * w + x];
                if ((x & 1) == 0) raw[o + 1 + x / 2] = (byte)(v << 4);
                else raw[o + 1 + x / 2] |= v;
            }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw);
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BigEndian(len, 0, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint crc = Crc(t, 0xFFFFFFFFu);
        crc = Crc(data, crc) ^ 0xFFFFFFFFu;
        var c = new byte[4];
        BigEndian(c, 0, crc);
        s.Write(c);
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc(byte[] buf, uint crc)
    {
        foreach (var b in buf) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    static void BigEndian(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }
}
