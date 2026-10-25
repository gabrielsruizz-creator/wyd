using WYD.Server.Data;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Pintura dos itens: a Tintura (3397 a 3406) com Pó de Oriharukon vira o Feijão Mágico da mesma cor (3407 a 3416),
/// que arrastado num elmo, peitoral, calça, luva, bota ou escudo muda a cor do brilho da refinação; o Removedor de
/// Tintura (3417) volta à cor normal.
/// <para>
/// A cor fica no tipo do efeito da refinação: 43 é a normal e de 116 a 125 são as 10 cores (o valor continua sendo
/// a refinação). O client lê esses tipos como refinação (0x5385AA), passa a cor para a peça (0x4C5768) e desenha o
/// brilho com a textura da cor (0x4CB771: bl, re, gr, si, dk, vi, or, pi, ye, db), só em peça refinada (+1 ou
/// mais). As cores seguem a ordem dos itens: Blue, Red, Green, Silver, Black, Purple, Brown, Violet, Yellowish e
/// Indigo. Ao segurar o Feijão o client acende os slots de elmo, peitoral, calça, luva, bota e escudo (0x41D3F6).
/// </para>
/// </summary>
public static class Dyes
{
    public const short FirstTint = 3397, FirstBean = 3407, Remover = 3417;
    public const int Colors = 10;

    /// <summary>Uso (efeito 38) do Feijão Mágico e do Removedor no ItemList.</summary>
    public const short UsableBean = 186;

    /// <summary>Tipo do efeito da refinação na primeira cor (Blue).</summary>
    public const byte FirstColor = 116;

    /// <summary>O Feijão Mágico que a Tintura vira com o Oriharukon (null se não é Tintura).</summary>
    public static short? BeanOf(short tint) =>
        tint is >= FirstTint and < FirstTint + Colors ? (short)(tint - FirstTint + FirstBean) : null;

    /// <summary>O tipo do efeito da refinação que o item deixa (43 no Removedor; null se não é de pintura).</summary>
    public static byte? ColorOf(short bean) =>
        bean == Remover ? (byte)Stats.EfSanc
        : bean is >= FirstBean and < FirstBean + Colors ? (byte)(bean - FirstBean + FirstColor)
        : null;

    /// <summary>Peças que pegam a pintura: elmo, peitoral, calça, luva, bota e escudo (não armas).</summary>
    public static bool CanPaint(ItemDefinition def) =>
        def.Pos is 2 or 4 or 8 or 16 or 32 || (def.Pos == 128 && !def.Effects.Any(e => e.Effect == 21 && e.Value > 0));

    /// <summary>
    /// O item com a cor trocada para <paramref name="color"/> (tipo do efeito da refinação), ou null se nada muda:
    /// já tem essa cor, ou não tem onde guardar (o item sem refinação usa o primeiro efeito livre, com valor 0).
    /// </summary>
    public static Item? Paint(Item item, byte color)
    {
        switch (Stats.SancSlot(item))
        {
            case 1: if (item.Effect1 == color) return null; item.Effect1 = color; return item;
            case 2: if (item.Effect2 == color) return null; item.Effect2 = color; return item;
            case 3: if (item.Effect3 == color) return null; item.Effect3 = color; return item;
        }
        if (color == Stats.EfSanc) return null;   // sem cor para tirar
        if (item.Effect1 == 0) (item.Effect1, item.Value1) = (color, 0);
        else if (item.Effect2 == 0) (item.Effect2, item.Value2) = (color, 0);
        else if (item.Effect3 == 0) (item.Effect3, item.Value3) = (color, 0);
        else return null;
        return item;
    }
}
