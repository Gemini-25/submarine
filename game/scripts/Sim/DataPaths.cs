using Godot;

namespace DeepLine.Game.Sim;

/// <summary>
/// data 폴더 찾기. 편집기와 저장소에서 실행하면 res://../data, 내보낸 빌드는 실행 파일 옆의 data.
/// 명령줄의 --data=경로가 있으면 그것을 쓴다.
/// </summary>
public static class DataPaths
{
    public static string DataDir(IReadOnlyDictionary<string, string> args)
    {
        if (args.TryGetValue("data", out var given) && given.Length > 0)
            return Path.GetFullPath(given);
        var candidates = new[]
        {
            Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "data"),
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "data"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(Path.Combine(full, "tuning.json"))) return full;
        }
        throw new DirectoryNotFoundException("data 폴더를 찾지 못했습니다. --data=경로 로 지정하세요. 찾아본 곳: " +
                                             string.Join(", ", candidates.Select(Path.GetFullPath)));
    }

    public static string ManualDir(string dataDir) => Path.Combine(dataDir, "manual");

    /// <summary>Godot 명령줄의 사용자 인수(-- 뒤)를 --key=value 또는 --flag 형태로 읽는다.</summary>
    public static Dictionary<string, string> UserArgs()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (!a.StartsWith("--", StringComparison.Ordinal)) continue;
            int eq = a.IndexOf('=');
            if (eq < 0) map[a[2..]] = "";
            else map[a[2..eq]] = a[(eq + 1)..];
        }
        return map;
    }
}
