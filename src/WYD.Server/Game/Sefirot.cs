using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Sefirot no mestre de skill: com a Eternal Stone (1742) equipada no slot 10, as 8 pedras (Kocuna, Veean, Keced,
/// Gebra, Tipalet, Nechark, Yesord e Marcute, itens 1744 a 1751) no inventário e 30 milhões de gold, o mestre troca
/// tudo pelo Sefirot da classe dele (1760 TK, 1761 FM, 1762 BM, 1763 HT).
/// </summary>
public static class Sefirot
{
    public const short EternalStone = 1742;
    public const int EternalStoneSlot = 10;
    public const short FirstStone = 1744, LastStone = 1751;
    public const short FirstSefirot = 1760;
    public const int Price = 30_000_000;

    public static readonly string[] StoneNames = ["Kocuna", "Veean", "Keced", "Gebra", "Tipalet", "Nechark", "Yesord", "Marcute"];

    public static bool HasEternalStone(Character c) => c.Equip[EternalStoneSlot].Index == EternalStone;

    /// <summary>A classe do mestre, pelo primeiro item de skill que ele ensina (5000 + 24 × classe).</summary>
    public static CharacterClass? MasterClass(IEnumerable<Item> carry)
    {
        var first = carry.FirstOrDefault(i => i.Index is >= Skills.FirstSkillItem and < Skills.FirstSkillItem + Skills.PerClass * 4);
        return first.IsEmpty ? null : (CharacterClass)((first.Index - Skills.FirstSkillItem) / Skills.PerClass);
    }

    public static string ClassName(CharacterClass c) => c switch
    {
        CharacterClass.TransKnight => "Transknight",
        CharacterClass.Foema => "Foema",
        CharacterClass.BeastMaster => "Beastmaster",
        _ => "Hunter",
    };

    /// <summary>O que falta para criar (null se tem tudo).</summary>
    public static string? Missing(Character c, int basicSlots)
    {
        var missing = new List<string>();
        if (!HasEternalStone(c)) missing.Add("Eternal Stone no slot 10");
        for (short s = FirstStone; s <= LastStone; s++)
        {
            short stone = s;
            if (Array.FindIndex(c.Inventory, 0, basicSlots, i => i.Index == stone) < 0) missing.Add(StoneNames[s - FirstStone]);
        }
        if (c.Coin < Price) missing.Add($"{Price:N0} de gold");
        return missing.Count == 0 ? null : string.Join(", ", missing);
    }

    /// <summary>
    /// Gasta as pedras, o gold e a Eternal Stone e põe o Sefirot no inventário. Retorna os slots do inventário que
    /// mudaram, ou null se faltar algo.
    /// </summary>
    public static List<int>? Create(Character c, CharacterClass cls, int basicSlots)
    {
        if (Missing(c, basicSlots) is not null) return null;
        var changed = new List<int>();
        for (short s = FirstStone; s <= LastStone; s++)
        {
            short stone = s;
            int slot = Array.FindIndex(c.Inventory, 0, basicSlots, i => i.Index == stone);
            c.Inventory[slot] = default;
            changed.Add(slot);
        }
        c.Coin -= Price;
        c.Equip[EternalStoneSlot] = default;
        int free = Bags.FirstFree(c);                                              // sempre há: as pedras saíram
        c.Inventory[free] = new Item((short)(FirstSefirot + (int)cls));
        if (!changed.Contains(free)) changed.Add(free);
        return changed;
    }
}
