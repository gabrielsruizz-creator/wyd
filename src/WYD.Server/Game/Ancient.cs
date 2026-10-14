using WYD.Server.Data;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>Efeito especial de um item ancient: cada um dos 4 itens iguais do grupo (Grade 5 a 8) tem um.</summary>
public enum AncientEffect : byte
{
    Exp = 5,         // Gema de Diamante
    Absorption = 6,  // Gema de Esmeralda
    Perforation = 7, // Gema de Coral
    Drop = 8,        // Gema de Garnet
}

/// <summary>
/// Itens ancient vêm em grupos de 4 cópias seguidas no ItemList, iguais a não ser pelo Grade (5, 6, 7 e 8).
/// Usar uma gema (Diamond_Gem 3386, Emerald_Gem 3387, Coral_Gem 3388, Granet_Gem 3389; uso 180 a 183 no
/// ItemList, ou seja, Grade + 175) num ancient troca o item pela cópia do grupo com o efeito da gema.
/// As pedras 2441 a 2444 (Diamond, Emerald, Coral, Garnet) são material de composição, não gemas.
/// Com o item +10 ou mais equipado, o efeito vale <see cref="BonusPercent"/>%.
/// </summary>
public static class Ancient
{
    public const short DiamondGem = 3386, EmeraldGem = 3387, CoralGem = 3388, GarnetGem = 3389;

    /// <summary>Uso (efeito 38) das gemas no ItemList: 180 a 183, o Grade do efeito + 175.</summary>
    public const short FirstGemUsable = 180, LastGemUsable = 183;

    /// <summary>Quanto cada ancient +10 equipado soma ao efeito dele.</summary>
    public const int BonusPercent = 10;

    /// <summary>Refinação mínima para o efeito valer.</summary>
    public const int MinRefine = 10;

    /// <summary>O efeito que a gema dá, pelo uso dela no ItemList (180 a 183).</summary>
    public static AncientEffect? GemEffect(short usable) =>
        usable is >= FirstGemUsable and <= LastGemUsable ? (AncientEffect)(usable - 175) : null;

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
