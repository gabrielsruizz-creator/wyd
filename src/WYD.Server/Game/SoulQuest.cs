using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// A Soul (Limite da Alma) do Mortal:
/// <list type="bullet">
/// <item>Odin: as 7 runas do elemento da classe e 2kk de gold viram a Pedra Secreta (TK Água, FM Sol, BM Terra, HT
/// Vento). O client manda o 0x28E ao clicar nele (NPC de tipo 7).</item>
/// <item>Kibita: Mortal nível 371 ou mais, ou Arch de qualquer nível, com a Pedra Secreta da classe ganha a Soul (bit 30
/// das skills) e o Manto do Herói do reino, e volta para a seleção de personagens. O client pergunta e manda o 0x28B
/// (tipo 10). O Celestial já nasce com a Soul.</item>
/// <item>Ehre: as 3 primeiras joias do inventário (Diamante, Esmeralda, Coral, Garnet), na ordem dos slots, escolhem a
/// combinação da Soul. Ela não tem tipo no arquivo; o client a recebe com o tipo da pergunta (13) e manda o 0x28B.</item>
/// </list>
/// </summary>
public static class SoulQuest
{
    public const string KibitaName = "Kibita", EhreName = "Ehre", OdinName = "Odin";

    /// <summary>Nível (o que aparece na tela) para a Kibita dar a Soul ao Mortal (o Arch pode em qualquer nível).</summary>
    public const int MinLevel = 371;

    public const int OdinPrice = 2_000_000;

    public const short WaterStone = 5334, LandStone = 5335, SunStone = 5336, WindStone = 5337;

    /// <summary>Tipo de NPC que o client recebe para a Ehre: o da pergunta, que manda o 0x28B.</summary>
    public const byte EhreClientMerchant = 13;

    public static bool IsKibita(Mob npc) => npc.Template.Name.Equals(KibitaName, StringComparison.OrdinalIgnoreCase);
    public static bool IsOdin(Mob npc) => npc.Template.Name.Equals(OdinName, StringComparison.OrdinalIgnoreCase);
    public static bool IsEhre(string npcName) => npcName.StartsWith(EhreName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pedra Secreta da classe.</summary>
    public static short SecretStone(CharacterClass cls) => cls switch
    {
        CharacterClass.TransKnight => WaterStone,
        CharacterClass.Foema => SunStone,
        CharacterClass.BeastMaster => LandStone,
        _ => WindStone,
    };

    public static string StoneName(short stone) => stone switch
    {
        WaterStone => "Pedra Secreta da Água",
        LandStone => "Pedra Secreta da Terra",
        SunStone => "Pedra Secreta do Sol",
        _ => "Pedra Secreta do Vento",
    };

    /// <summary>Manto do Herói do reino (Hero Hekalotia, Akelonia ou Adventurer), sem refinação.</summary>
    public static short HeroCape(Kingdom k) => k switch
    {
        Kingdom.Blue => 3194,
        Kingdom.Red => 3195,
        _ => 3196,
    };

    /// <summary>As 7 runas de cada Pedra Secreta (itens 5110 a 5133).</summary>
    public static readonly IReadOnlyDictionary<short, short[]> Runes = new Dictionary<short, short[]>
    {
        // Vento: Eihwaz, Hagalaz, Ing, Isa, Laguz, Othel, Perthro
        [WindStone] = [5122, 5119, 5132, 5120, 5130, 5133, 5123],
        // Água: Berkano, Ehwaz, Jara, Raidho, Sowilo, Uraz, Wunjo
        [WaterStone] = [5126, 5127, 5121, 5114, 5125, 5111, 5118],
        // Terra: Dagaz, Fehu, Kenaz, Naudhiz, Sowilo, Thurisaz, Raidho
        [LandStone] = [5131, 5113, 5115, 5116, 5125, 5112, 5114],
        // Sol: Ansuz, Elhaz, Gebo, Mannaz, Raidho, Sowilo, Tiwaz
        [SunStone] = [5110, 5124, 5117, 5129, 5114, 5125, 5128],
    };

    /// <summary>Quantas unidades tem o item (efeito 61 nos empilhados; 1 nos outros).</summary>
    private static int Amount(Item item)
    {
        if (item.IsEmpty) return 0;
        int amount = item.Effect1 == 61 ? item.Value1 : item.Effect2 == 61 ? item.Value2 : item.Effect3 == 61 ? item.Value3 : 1;
        return Math.Max(1, amount);
    }

    /// <summary>Gasta uma unidade do slot (os empilhados diminuem).</summary>
    private static void TakeOne(Character c, int slot)
    {
        ref var item = ref c.Inventory[slot];
        if (item.Effect1 == 61 && item.Value1 > 1) { item.Value1--; return; }
        if (item.Effect2 == 61 && item.Value2 > 1) { item.Value2--; return; }
        if (item.Effect3 == 61 && item.Value3 > 1) { item.Value3--; return; }
        item = default;
    }

    /// <summary>
    /// Odin: gasta as 7 runas e o gold e põe a Pedra Secreta no inventário. Retorna a recusa (com o que falta), ou null
    /// e os slots que mudaram.
    /// </summary>
    public static (string? Refusal, List<int> Changed) MakeStone(Character c, int basicSlots)
    {
        short stone = SecretStone(c.Class);
        var runes = Runes[stone];
        var slots = new List<int>();
        var missing = new List<string>();
        foreach (var rune in runes)
        {
            int slot = Enumerable.Range(0, basicSlots).FirstOrDefault(s => c.Inventory[s].Index == rune && !slots.Contains(s), -1);
            if (slot < 0) missing.Add($"runa {rune}");
            else slots.Add(slot);
        }
        if (c.Coin < OdinPrice) missing.Add($"{OdinPrice - c.Coin:N0} de gold");
        if (missing.Count > 0) return ($"Para a {StoneName(stone)} falta: {string.Join(", ", missing)}.", []);

        foreach (var slot in slots) TakeOne(c, slot);
        c.Coin -= OdinPrice;
        int free = Array.FindIndex(c.Inventory, 0, basicSlots, i => i.IsEmpty);
        if (free < 0) free = slots.Min();                // só falta se as runas eram empilhadas e nenhuma acabou
        c.Inventory[free] = new Item(stone);
        return (null, slots.Append(free).Distinct().ToList());
    }

    /// <summary>
    /// Kibita: confere e dá a Soul e o Manto do Herói (o manto antigo vai para o inventário). Retorna a recusa, ou null
    /// e os slots do inventário que mudaram.
    /// </summary>
    public static (string? Refusal, List<int> Changed) GiveSoul(Character c, int basicSlots)
    {
        if (c.Evolution >= Evolution.Celestial || Skills.Knows(c, Skills.SoulOfLimits))
            return ("Você já tem a Soul. Configure-a com a Ehre.", []);
        if (c.Evolution == Evolution.Mortal && c.BaseScore.Level + 1 < MinLevel)
            return ($"O Mortal recebe a Soul a partir do nível {MinLevel}.", []);
        short stone = SecretStone(c.Class);
        int stoneSlot = Enumerable.Range(0, basicSlots).FirstOrDefault(s => c.Inventory[s].Index == stone, -1);
        if (stoneSlot < 0) return ($"Traga a {StoneName(stone)} (faça com o Odin).", []);

        var oldCape = c.Equip[Kingdoms.CapeSlot];
        var changed = new List<int> { stoneSlot };
        TakeOne(c, stoneSlot);
        if (!oldCape.IsEmpty)
        {
            int free = Array.FindIndex(c.Inventory, 0, basicSlots, i => i.IsEmpty);
            if (free < 0)
            {
                c.Inventory[stoneSlot] = c.Inventory[stoneSlot].IsEmpty ? new Item(stone) : c.Inventory[stoneSlot];
                return ("Deixe um espaço livre no inventário para o seu manto atual.", []);
            }
            c.Inventory[free] = oldCape;
            changed.Add(free);
        }
        c.Equip[Kingdoms.CapeSlot] = new Item(HeroCape(Kingdoms.OfCape(oldCape)));
        Skills.Teach(c, Skills.SoulOfLimits);
        return (null, changed);
    }

    /// <summary>Joias da Ehre: D = Diamante, E = Esmeralda, C = Coral, G = Garnet (pedras 2441-2444 ou gemas 3386-3389).</summary>
    private static char? Jewel(short index) => index switch
    {
        2441 or Ancient.DiamondGem => 'D',
        2442 or Ancient.EmeraldGem => 'E',
        2443 or Ancient.CoralGem => 'C',
        2444 or Ancient.GarnetGem => 'G',
        _ => null,
    };

    /// <summary>As receitas da Ehre, na ordem das combinações da Soul (1 a 10).</summary>
    public static readonly string[] Recipes = ["DDD", "EEE", "CCC", "GGG", "DEC", "DCG", "ECG", "EDC", "CDC", "GDC"];

    /// <summary>
    /// Ehre: as 3 primeiras joias do inventário escolhem a combinação. Retorna a recusa, ou null, a combinação e os slots
    /// gastos.
    /// </summary>
    public static (string? Refusal, int Combo, List<int> Changed) Configure(Character c, int basicSlots)
    {
        if (!Skills.Knows(c, Skills.SoulOfLimits)) return ("Você ainda não tem a Soul (fale com a Kibita).", 0, []);
        // Cada unidade conta (3 Diamantes empilhados num slot são "DDD").
        var jewels = Enumerable.Range(0, basicSlots)
            .Where(s => Jewel(c.Inventory[s].Index) is not null)
            .SelectMany(s => Enumerable.Repeat((Slot: s, Jewel: Jewel(c.Inventory[s].Index)), Amount(c.Inventory[s])))
            .Take(3).ToList();
        string recipe = new(jewels.Select(x => x.Jewel!.Value).ToArray());
        int combo = Array.IndexOf(Recipes, recipe) + 1;
        if (jewels.Count < 3 || combo == 0)
            return ("Coloque 3 joias no inventário, na ordem da combinação (Diamante, Esmeralda, Coral ou Garnet).", 0, []);

        foreach (var (slot, _) in jewels) TakeOne(c, slot);
        c.SoulCombo = (byte)combo;
        return (null, combo, jewels.Select(x => x.Slot).Distinct().ToList());
    }
}
