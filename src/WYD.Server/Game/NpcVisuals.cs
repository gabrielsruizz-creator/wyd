using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;
using WYD.Server.Data;

namespace WYD.Server.Game;

/// <summary>
/// O que o client vê do equipamento dos NPCs (só o 0x364; o NPC continua igual no servidor).
/// <para>
/// Itens que não existem no ItemList.bin do client nunca vão para a tela: o client 7.54 fecha ao tentar desenhar
/// um (o Black Oracle e o Red Oracle dos castelos têm o item 13 no elmo, o Argus de Noatun tem o rosto 348, e os
/// dois estão vazios no ItemList).
/// </para>
/// <para>
/// O slot 12 é o da medalha de guilda: o client desenha ali só itens que vão nesse slot. Os arquivos de NPC mais novos
/// põem trajes e baús nele (a Curandeira tem o 4159, um traje no ItemList do Star Destiny), que o client 7.54 não
/// desenha; esses ficam escondidos.
/// </para>
/// data/npc-visual.txt ("Nome slot item", uma por linha; item 0 tira a peça) troca peças à vontade.
/// </summary>
public sealed class NpcVisuals
{
    private readonly Dictionary<string, Dictionary<int, short>> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly ItemList? _items;

    public NpcVisuals(IOptions<ServerOptions> options, ItemCatalog catalog, ILogger<NpcVisuals> logger)
        : this(Load(PathResolver.Resolve(options.Value.NpcVisualPath), logger), catalog.List) { }

    /// <summary>Regras e ItemList dados (para os testes).</summary>
    public static NpcVisuals With(IEnumerable<(string Npc, int Slot, short Item)> rules, ItemList? items = null) => new(rules, items);

    private NpcVisuals(IEnumerable<(string Npc, int Slot, short Item)> rules, ItemList? items)
    {
        _items = items;
        foreach (var (npc, slot, item) in rules)
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

    /// <summary>Slot da medalha de guilda.</summary>
    public const int MedalSlot = 12;

    /// <summary>O item que aparece na tela no slot do NPC (0 se ele não existir no ItemList do client).</summary>
    public short Visible(string npc, int slot, short item)
    {
        if (_rules.TryGetValue(npc, out var slots) && slots.TryGetValue(slot, out var replaced)) item = replaced;
        if (item <= 0 || _items is null) return item;
        var def = _items[item];
        if (def is null) return 0;
        return slot == MedalSlot && (def.Pos & (1 << MedalSlot)) == 0 ? (short)0 : item;
    }
}
