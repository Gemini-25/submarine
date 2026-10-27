using System.Globalization;
using System.Text;
using DeepLine.Audio;
using DeepLine.AudioRender;

// 목소리 데모를 WAV(22.05kHz 모노 16비트)로 뽑는 개발 도구.
//   render [--out 폴더] [--all]     공개 데모를 docs/reports/audio/에. --all이면 매개변수 쓸기까지 out/audio/에
//   blind [--out 폴더] [--seed N]   눈 가리고 듣기 세트(펌프 정상과 고장 3종을 섞어서). 정답은 docs/reports/sound-kit-blind-answers.md
//   levels                          데모마다 최대값과 RMS (음량 맞추기용)

const int Rate = 22050;
const int Block = 256;

var argList = args.ToList();
string Option(string name, string fallback)
{
    int i = argList.IndexOf(name);
    return i >= 0 && i + 1 < argList.Count ? argList[i + 1] : fallback;
}

string root = FindRoot(Directory.GetCurrentDirectory());

switch (argList.FirstOrDefault())
{
    case "render":
    {
        bool all = argList.Contains("--all");
        string outDir = Option("--out", all ? Path.Combine(root, "out", "audio") : Path.Combine(root, "docs", "reports", "audio"));
        Directory.CreateDirectory(outDir);
        long total = 0;
        foreach (var d in Demos.All.Where(d => all || d.Publish))
        {
            var samples = Render(d);
            string path = Path.Combine(outDir, d.File);
            Wav.Write(path, samples, Rate);
            total += new FileInfo(path).Length;
            Console.WriteLine($"{d.File,-32} {d.Seconds,4:F0}초  최대 {Peak(samples):F2}  RMS {Rms(samples):F3}  {d.Title}");
        }
        Console.WriteLine($"합계 {total / 1048576.0:F2}MB → {outDir}");
        return 0;
    }

    case "blind":
    {
        string outDir = Option("--out", Path.Combine(root, "docs", "reports", "audio", "blind"));
        int seed = int.Parse(Option("--seed", "1027"), CultureInfo.InvariantCulture);
        Directory.CreateDirectory(outDir);
        var order = PumpStates.All.Select((s, i) => i).ToArray();
        new Random(seed).Shuffle(order);
        var answers = new StringBuilder();
        answers.AppendLine("# 눈 가리고 듣기 정답 (먼저 들은 뒤에 열 것)");
        answers.AppendLine();
        answers.AppendLine($"`dotnet run --project tools/DeepLine.AudioRender -- blind --seed {seed}`로 만든 세트. 파일은 `docs/reports/audio/blind/`.");
        answers.AppendLine("네 파일 모두 펌프가 이미 돌고 있는 상태에서 8초. 음량은 손대지 않았다.");
        answers.AppendLine();
        answers.AppendLine("| 파일 | 정답 |");
        answers.AppendLine("| --- | --- |");
        for (int k = 0; k < order.Length; k++)
        {
            var state = PumpStates.All[order[k]];
            var pump = new PumpVoice(0x5EED0 + (uint)k);
            state.Apply(pump);
            pump.Settle();
            var samples = new float[Rate * 8];
            for (int i = 0; i < samples.Length; i += Block)
                pump.Render(samples.AsSpan(i, Math.Min(Block, samples.Length - i)), Rate);
            string name = $"{(char)('A' + k)}.wav";
            Wav.Write(Path.Combine(outDir, name), samples, Rate);
            answers.AppendLine($"| {name} | {state.Name} ({state.Id}) |");
            Console.WriteLine($"{name}  최대 {Peak(samples):F2}  RMS {Rms(samples):F3}");
        }
        File.WriteAllText(Path.Combine(root, "docs", "reports", "sound-kit-blind-answers.md"), answers.ToString());
        Console.WriteLine($"→ {outDir}, 정답 docs/reports/sound-kit-blind-answers.md");
        return 0;
    }

    case "levels":
        foreach (var d in Demos.All)
        {
            var s = Render(d);
            Console.WriteLine($"{d.File,-32} 최대 {Peak(s):F3}  RMS {Rms(s):F4}  0.8 넘는 샘플 {s.Count(x => Math.Abs(x) > 0.8f)}");
        }
        return 0;

    default:
        Console.Error.WriteLine("사용법: render [--out 폴더] [--all] | blind [--out 폴더] [--seed N] | levels");
        return 2;
}

static float[] Render(Demo d)
{
    var voices = d.Create();
    var clock = new Clock { Block = Block / (float)Rate };
    var mix = new float[(int)(d.Seconds * Rate)];
    var tmp = new float[Block];
    for (int i = 0; i < mix.Length; i += Block)
    {
        int n = Math.Min(Block, mix.Length - i);
        clock.T = i / (float)Rate;
        d.Script(voices, clock);
        foreach (var v in voices)
        {
            v.Render(tmp.AsSpan(0, n), Rate);
            for (int k = 0; k < n; k++) mix[i + k] += tmp[k];
        }
        if (voices.Length > 1)
            for (int k = 0; k < n; k++) mix[i + k] = Voice.Limit(mix[i + k]);
    }
    return mix;
}

static float Peak(float[] s) => s.Length == 0 ? 0 : s.Max(x => Math.Abs(x));
static float Rms(float[] s) => s.Length == 0 ? 0 : (float)Math.Sqrt(s.Average(x => (double)x * x));

static string FindRoot(string dir)
{
    for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "DeepLine.Audio.sln"))) return d.FullName;
    return dir;
}

static class Wav
{
    /// <summary>16비트 PCM 모노.</summary>
    public static void Write(string path, float[] samples, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        foreach (var x in samples) w.Write((short)Math.Round(Math.Clamp(x, -1f, 1f) * 32767));
    }
}
