using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;

namespace WYD.Server.Game;

/// <summary>
/// Troca de aparência de NPCs na tela (só o 0x364; o NPC continua igual no servidor). Os reis derrubam o client
/// 7.54 testado ao aparecer; eles são os únicos NPCs com o conjunto Orange King (rosto 304, armaduras 160-163), a
/// arma Heaven of Garve (999) e as capas Elite do reino (3191/3192), então por padrão aparecem com as peças dos
/// guardas, que o client desenha bem. data/npc-visual.txt
/// ("Nome slot item", uma por linha; item 0 tira a peça) soma ou troca essas regras.
/// </summary>
public sealed class NpcVisuals
{
    public static readonly IReadOnlyDictionary<string, Dictionary<int, short>> Defaults =
        new Dictionary<string, Dictionary<int, short>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rei_Harabard"] = KingLook(),
            ["Rei_Glantuar"] = KingLook(),
        };

    private static Dictionary<int, short> KingLook() => new()
    {
        [0] = 305,                                   // Orange_Guard (no lugar de Orange_King)
        [2] = 165, [3] = 166, [4] = 167, [5] = 168,  // Blue_Guard (no lugar do conjunto Orange King)
        [6] = 997,                                   // Heaven_Of_Shard (no lugar de Heaven_Of_Garve)
        [15] = 734,                                  // Cape_Of_CruSader dos guardas (no lugar das capas Elite do reino)
    };

    private readonly Dictionary<string, Dictionary<int, short>> _rules;

    public NpcVisuals(IOptions<ServerOptions> options, ILogger<NpcVisuals> logger)
        : this(Load(PathResolver.Resolve(options.Value.NpcVisualPath), logger), 0) { }

    /// <summary>As regras padrão mais as <paramref name="extra"/> (para os testes).</summary>
    public static NpcVisuals With(IEnumerable<(string Npc, int Slot, short Item)> extra) => new(extra, 0);

    private NpcVisuals(IEnumerable<(string Npc, int Slot, short Item)> extra, int _)
    {
        _rules = Defaults.ToDictionary(kv => kv.Key, kv => new Dictionary<int, short>(kv.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var (npc, slot, item) in extra)
        {
            if (!_rules.TryGetValue(npc, out var slots)) _rules[npc] = slots = [];
            slots[slot] = item;
        }
    }

    private static List<(string, int, short)> Load(string path, ILogger logger)
    {
        var list = new List<(string, int, short)>();
        if (!File.Exists(path)) return list;
        foreach (var raw in File.ReadAllLines(path))
        {
            var p = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 3 && !p[0].StartsWith('#')
                && int.TryParse(p[1], CultureInfo.InvariantCulture, out int slot) && slot is >= 0 and < 16
                && short.TryParse(p[2], CultureInfo.InvariantCulture, out short item))
                list.Add((p[0], slot, item));
        }
        logger.LogInformation("{Count} troca(s) de aparência de NPC lidas de {Path}.", list.Count, path);
        return list;
    }

    /// <summary>O item que aparece na tela no slot do NPC.</summary>
    public short Visible(string npc, int slot, short item) =>
        _rules.TryGetValue(npc, out var slots) && slots.TryGetValue(slot, out var replaced) ? replaced : item;
}
