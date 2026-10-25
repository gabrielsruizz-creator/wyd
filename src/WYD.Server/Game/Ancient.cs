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
/// Gemas (Diamond_Gem 3386, Emerald_Gem 3387, Coral_Gem 3388, Granet_Gem 3389; uso 180 a 183 no ItemList, ou
/// seja, Grade + 175) dão a um item um destes efeitos, cada um valendo <see cref="BonusPercent"/>% equipado:
/// <list type="bullet">
/// <item>Ancient: os itens vêm em grupos de 4 cópias seguidas no ItemList, iguais a não ser pelo Grade (5 a 8);
/// a gema troca o item pela cópia do grupo com o efeito dela.</item>
/// <item>Item +10 ou mais (arma, armadura, qualquer um): a gema fica nos 2 bits de baixo do valor da refinação
/// (efeito 43): valor = 230 + 4 × (refino − 10) + gema. O client lê esses bits como o Grade (0x419297:
/// (valor − 230) % 4 + 5) e muda o brilho do item.</item>
/// </list>
/// As pedras 2441 a 2444 (Diamond, Emerald, Coral, Garnet) são material de composição, não gemas.
/// </summary>
public static class Ancient
{
    public const short DiamondGem = 3386, EmeraldGem = 3387, CoralGem = 3388, GarnetGem = 3389;

    /// <summary>Uso (efeito 38) das gemas no ItemList: 180 a 183, o Grade do efeito + 175.</summary>
    public const short FirstGemUsable = 180, LastGemUsable = 183;

    /// <summary>Quanto cada ancient +10 equipado soma ao efeito dele.</summary>
    public const int BonusPercent = 10;

    /// <summary>Refinação a partir da qual qualquer item guarda uma gema.</summary>
    public const int MinRefine = 10;

    /// <summary>Valor do efeito 43 do +10 sem gema (o client tira 230 e divide por 4).</summary>
    public const int RefinedBase = 230;

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

    /// <summary>
    /// O efeito do item, se ele for ancient: um grupo de 4 itens seguidos com o mesmo nome e o mesmo modelo e Grade 5
    /// a 8 (o nome nem sempre traz "(Ancient)").
    /// </summary>
    public static AncientEffect? EffectOf(ItemList? items, int index)
    {
        if (items?[index] is not { Grade: >= 5 and <= 8 } def) return null;
        int first = index - (def.Grade - (int)AncientEffect.Exp);
        for (int i = 0; i < 4; i++)
            if (items[first + i] is not { } copy || copy.Grade != (int)AncientEffect.Exp + i || copy.Name != def.Name || copy.Mesh != def.Mesh)
                return null;
        return (AncientEffect)def.Grade;
    }

    /// <summary>Gema guardada num item +10 ou mais (sem gema os bits valem 0: Experiência, como no client).</summary>
    public static AncientEffect? GemOf(Item item)
    {
        if (Stats.Refine(item) < MinRefine) return null;
        int raw = Stats.RawSanc(item);
        return (AncientEffect)((raw - RefinedBase) % 4 + (int)AncientEffect.Exp);
    }

    /// <summary>O item +10 ou mais com a gema trocada (a refinação continua a mesma).</summary>
    public static Item WithGem(Item item, AncientEffect gem)
    {
        int refine = Stats.Refine(item);
        if (refine < MinRefine) return item;
        byte raw = (byte)(RefinedBase + 4 * (refine - MinRefine) + ((int)gem - (int)AncientEffect.Exp));
        return Stats.WithRawSanc(item, raw, create: false);
    }

    /// <summary>O efeito que o item equipado dá: o do ancient, ou a gema de um item +10 ou mais.</summary>
    public static AncientEffect? EquippedEffect(Item item, ItemList? items) =>
        item.IsEmpty ? null : EffectOf(items, item.Index) ?? GemOf(item);

    /// <summary>O índice da cópia do grupo com o efeito pedido (null se o grupo não estiver completo no ItemList).</summary>
    public static short? Variant(short index, ItemList? items, AncientEffect wanted)
    {
        if (EffectOf(items, index) is not { } current) return null;
        return (short)(index - (int)current + (int)wanted);
    }
}
