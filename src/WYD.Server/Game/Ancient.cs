using WYD.Server.Data;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>Efeito especial de um item ancient: cada um dos 4 itens iguais do grupo (Grade 5 a 8) tem um.</summary>
public enum AncientEffect : byte
{
    Exp = 5,         // Diamante
    Absorption = 6,  // Esmeralda
    Perforation = 7, // Coral
    Drop = 8,        // Garnet
}

/// <summary>
/// Itens ancient vêm em grupos de 4 cópias seguidas no ItemList, iguais a não ser pelo Grade (5, 6, 7 e 8).
/// Usar uma gema (Diamante 2441, Esmeralda 2442, Coral 2443, Garnet 2444) num ancient troca o item pela
/// cópia do grupo com o efeito da gema. Com o item +10 ou mais equipado, o efeito vale <see cref="BonusPercent"/>%.
/// </summary>
public static class Ancient
{
    public const short Diamond = 2441, Emerald = 2442, Coral = 2443, Garnet = 2444;

    /// <summary>Uso (efeito 38) das gemas no ItemList.</summary>
    public const short UsableGem = 9;

    /// <summary>Quanto cada ancient +10 equipado soma ao efeito dele.</summary>
    public const int BonusPercent = 10;

    /// <summary>Refinação mínima para o efeito valer.</summary>
    public const int MinRefine = 10;

    public static AncientEffect? GemEffect(short gem) => gem switch
    {
        Diamond => AncientEffect.Exp,
        Emerald => AncientEffect.Absorption,
        Coral => AncientEffect.Perforation,
        Garnet => AncientEffect.Drop,
        _ => null,
    };

    public static string Name(AncientEffect e) => e switch
    {
        AncientEffect.Exp => "Experiência",
        AncientEffect.Absorption => "Absorção",
        AncientEffect.Perforation => "Perfuração",
        _ => "Drop",
    };

    /// <summary>O efeito do item, se ele for ancient.</summary>
    public static AncientEffect? EffectOf(ItemDefinition? def) =>
        def is not null && def.Name.Contains("(Ancient)", StringComparison.OrdinalIgnoreCase) && def.Grade is >= 5 and <= 8
            ? (AncientEffect)def.Grade
            : null;

    /// <summary>O índice da cópia do grupo com o efeito pedido (null se o grupo não estiver completo no ItemList).</summary>
    public static short? Variant(short index, ItemList items, AncientEffect wanted)
    {
        if (EffectOf(items[index]) is not { } current) return null;
        int target = index - (int)current + (int)wanted;
        var def = items[target];
        return def is not null && EffectOf(def) == wanted && def.Mesh == items[index]!.Mesh ? (short)target : null;
    }
}
