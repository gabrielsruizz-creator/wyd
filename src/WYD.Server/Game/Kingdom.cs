using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Reino do jogador, pela capa que ele veste (Equip[15]). Os números são os que o client 7.54 devolve na função
/// que classifica as capas (0x564783): 7 Hekalotia (azul), 8 Akeronia (vermelho), 6 Adventurer (neutro, capas brancas).
/// </summary>
public enum Kingdom : byte
{
    None = 0,
    Neutral = 6,
    Blue = 7,
    Red = 8,
}

public static class Kingdoms
{
    public const int CapeSlot = 15;

    private static readonly HashSet<short> BlueCapes = [543, 545, 1766, 1767, 1768, 3191, 3194, 3197];
    private static readonly HashSet<short> RedCapes = [544, 546, 1769, 1770, 1771, 3192, 3195, 3198];
    private static readonly HashSet<short> WhiteCapes = [548, 549, 573, 3193, 3196, 3199];

    public static Kingdom OfCape(Item cape) =>
        BlueCapes.Contains(cape.Index) ? Kingdom.Blue
        : RedCapes.Contains(cape.Index) ? Kingdom.Red
        : WhiteCapes.Contains(cape.Index) ? Kingdom.Neutral
        : Kingdom.None;

    public static Kingdom Of(Character c) => OfCape(c.Equip[CapeSlot]);

    /// <summary>Sem capa ou com capa branca: pode evoluir com qualquer um dos reis.</summary>
    public static bool IsNeutral(Kingdom k) => k is Kingdom.None or Kingdom.Neutral;

    public static string Name(Kingdom k) => k switch
    {
        Kingdom.Blue => "Azul",
        Kingdom.Red => "Vermelho",
        _ => "Neutro",
    };

    /// <summary>Rei de cada reino (o clã do NPC é o número do reino).</summary>
    public static string KingName(Kingdom k) => k == Kingdom.Red ? "Rei Glantuar" : "Rei Harabard";

}
