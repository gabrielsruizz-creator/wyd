using System.Globalization;
using System.Text.RegularExpressions;

namespace WYD.Server.Data;

/// <summary>
/// Uma entrada do NPCGener.txt: onde um grupo de monstros (ou um NPC) nasce e por onde anda.
/// <code>
/// #	[   0]
/// 	MinuteGenerate:	-1      MaxNumMob:	100     MinGroup:	4     MaxGroup:	7
/// 	Leader:		Ciclope_Forte   Follower:	Ciclope_Forte   RouteType:	2   Formation:	0
/// 	StartX:		2640    StartY:	1726    StartRange:	5     StartWait:	10
/// 	DestX:		2640    DestY:	1726    DestRange:	10    DestWait:	10
/// </code>
/// Leader e Follower são nomes de arquivos da pasta npc.
/// </summary>
public sealed record SpawnRule
{
    public int Index { get; init; }
    public string Leader { get; init; } = "";
    public string Follower { get; init; } = "";
    public int MinuteGenerate { get; init; }
    public int MaxNumMob { get; init; } = 1;
    public int MinGroup { get; init; }
    public int MaxGroup { get; init; }
    public int RouteType { get; init; }
    public int Formation { get; init; }
    public short StartX { get; init; }
    public short StartY { get; init; }
    public int StartRange { get; init; }
    public int StartWait { get; init; }
    public short DestX { get; init; }
    public short DestY { get; init; }
    public int DestRange { get; init; }
    public int DestWait { get; init; }
}

public static partial class NpcGener
{
    [GeneratedRegex(@"^#\s*\[\s*(\d+)\s*\]")]
    private static partial Regex Header();

    [GeneratedRegex(@"^\s+(\w+):\s*(.*?)\s*$")]
    private static partial Regex Field();

    public static List<SpawnRule> Parse(string text)
    {
        var rules = new List<SpawnRule>();
        Dictionary<string, string>? fields = null;
        int index = 0;

        void Flush()
        {
            if (fields is null || !fields.TryGetValue("Leader", out var leader) || leader.Length == 0) return;
            int I(string key, int fallback = 0) =>
                fields.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;

            rules.Add(new SpawnRule
            {
                Index = index,
                Leader = leader,
                Follower = fields.GetValueOrDefault("Follower", leader),
                MinuteGenerate = I("MinuteGenerate"),
                MaxNumMob = I("MaxNumMob", 1),
                MinGroup = I("MinGroup"),
                MaxGroup = I("MaxGroup"),
                RouteType = I("RouteType"),
                Formation = I("Formation"),
                StartX = (short)I("StartX"),
                StartY = (short)I("StartY"),
                StartRange = I("StartRange"),
                StartWait = I("StartWait"),
                DestX = (short)I("DestX", I("StartX")),
                DestY = (short)I("DestY", I("StartY")),
                DestRange = I("DestRange"),
                DestWait = I("DestWait"),
            });
        }

        foreach (var line in text.Split('\n'))
        {
            if (Header().Match(line) is { Success: true } h)
            {
                Flush();
                fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                index = int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            else if (fields is not null && Field().Match(line) is { Success: true } f)
            {
                fields[f.Groups[1].Value] = f.Groups[2].Value;
            }
        }
        Flush();
        return rules;
    }

    public static List<SpawnRule> Load(string path) => Parse(File.ReadAllText(path));
}
