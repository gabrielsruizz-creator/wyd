using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Eternal Stone no Oracle do reino (BlueOracle no castelo azul, RedOracle no vermelho): Unicorn's Spirit (1740),
/// Phoenix's Spirit (1741) e 10 Sapphires (697) no inventário viram a Eternal Stone (1742). Neutros (capa branca
/// ou sem capa) podem usar qualquer um dos dois.
/// </summary>
public static class Oracle
{
    public const short UnicornSpirit = 1740, PhoenixSpirit = 1741, Sapphire = 697;
    public const int Sapphires = 10;

    /// <summary>
    /// Tipo de NPC que o client recebe para o Oracle. Com o tipo original (14) o client 7.54 só abre a pergunta com
    /// 20 safiras, os dois Spirits lado a lado no inventário e nível 300; com o 13 ele sempre pergunta e manda o
    /// 0x28B, e quem confere a receita é o servidor.
    /// </summary>
    public const byte ClientMerchant = 13;

    /// <summary>NPC que falta em alguns pacotes de dados (arquivo vazio) e é criado a partir de outro igual.</summary>
    public static readonly IReadOnlyDictionary<string, string> TemplateFallbacks =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["BlueOracle"] = "RedOracle" };

    public static Kingdom? KingdomOf(string npcName) =>
        npcName.Equals("BlueOracle", StringComparison.OrdinalIgnoreCase) ? Kingdom.Blue
        : npcName.Equals("RedOracle", StringComparison.OrdinalIgnoreCase) ? Kingdom.Red
        : null;

    private static int Count(Character c, int basicSlots, short index) =>
        c.Inventory.Take(basicSlots).Count(i => i.Index == index);

    /// <summary>O que falta para criar (null se tem tudo).</summary>
    public static string? Missing(Character c, int basicSlots)
    {
        var missing = new List<string>();
        if (Count(c, basicSlots, UnicornSpirit) == 0) missing.Add("Unicorn's Spirit");
        if (Count(c, basicSlots, PhoenixSpirit) == 0) missing.Add("Phoenix's Spirit");
        int sapphires = Count(c, basicSlots, Sapphire);
        if (sapphires < Sapphires) missing.Add($"{Sapphires - sapphires} Sapphire(s) (tem {sapphires} de {Sapphires})");
        return missing.Count == 0 ? null : string.Join(", ", missing);
    }

    /// <summary>
    /// Gasta os dois Spirits e as 10 safiras e põe a Eternal Stone no inventário. Retorna os slots do inventário
    /// que mudaram, ou null se faltar algo.
    /// </summary>
    public static List<int>? Create(Character c, int basicSlots)
    {
        if (Missing(c, basicSlots) is not null) return null;
        var changed = new List<int>();
        void Take(short index, int amount)
        {
            for (int slot = 0; slot < basicSlots && amount > 0; slot++)
            {
                if (c.Inventory[slot].Index != index) continue;
                c.Inventory[slot] = default;
                changed.Add(slot);
                amount--;
            }
        }
        Take(UnicornSpirit, 1);
        Take(PhoenixSpirit, 1);
        Take(Sapphire, Sapphires);
        int free = changed.Min();                       // sempre há: os itens saíram
        c.Inventory[free] = new Item(Sefirot.EternalStone);
        return changed;
    }
}
