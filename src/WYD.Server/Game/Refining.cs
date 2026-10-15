using WYD.Server.Data;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>Material de refinação: o uso (efeito 38) no ItemList é 4 para o Oriharukon e 5 para o Laktorerium.</summary>
public enum RefineMaterial
{
    Oriharukon = 0,
    Laktorerium = 1,
}

/// <summary>
/// Refinação com Pó de Oriharukon (412) e Pó de Laktorerium (413), com as chances do client 7.54 (função 0x558A00
/// e tabelas em 0x5C80EC, 0x5C820C e 0x5C832C). Abaixo do +10 o efeito 43 guarda a refinação nas unidades e as
/// falhas nas dezenas: cada falha soma à chance seguinte (<see cref="FailBonus"/>), e o sucesso zera as falhas.
/// O Oriharukon leva até +6 e o Laktorerium até +9; a chance depende do grau do item (efeito 87, de 1 a 6).
/// O Laktorerium também tenta o +10 → +11 com <see cref="HighChance"/>% (coluna 10 da tabela do client; a 9, do
/// +9 → +10, é 0), e o Laktorerium 100 (4141) passa o +10 direto para +11. Do +10 em diante não há contagem de
/// falhas (o efeito 43 guarda 230 + 4 × (refino − 10) + gema), e a falha deixa o item como está.
/// </summary>
public static class Refining
{
    public const short OriharukonPowder = 412, LaktoreriumPowder = 413;

    /// <summary>Laktorerium 100: mesmo uso do Laktorerium (5), mas passa o +10 direto para +11.</summary>
    public const short LaktoreriumHundred = 4141;

    /// <summary>Chance (%) do Laktorerium no +10 → +11, igual em todos os graus no client.</summary>
    public const int HighChance = 3;
    public const short UsableOriharukon = 4, UsableLaktorerium = 5;

    /// <summary>Grau do item (efeito 87 no ItemList): escolhe a linha da tabela.</summary>
    public const int EfGrade = 87;

    /// <summary>Até onde cada material leva.</summary>
    public static int MaxRefine(RefineMaterial m) => m == RefineMaterial.Oriharukon ? 6 : 9;

    /// <summary>Chance (%) de passar de +n para +n+1, por grau (1 a 6), com Oriharukon.</summary>
    private static readonly int[][] Oriharukon =
    [
        [100, 100, 100, 100, 100, 100],
        [100, 100, 100, 100, 100, 74],
        [100, 100, 100, 100, 100, 68],
        [100, 100, 100, 90, 80, 53],
        [100, 100, 100, 85, 70, 40],
        [100, 100, 100, 85, 70, 40],
    ];

    /// <summary>Chance (%) de passar de +n para +n+1, por grau (1 a 6), com Laktorerium.</summary>
    private static readonly int[][] Laktorerium =
    [
        [100, 100, 100, 100, 100, 100, 90, 59, 41],
        [100, 100, 100, 100, 100, 100, 80, 51, 36],
        [100, 100, 100, 100, 100, 100, 70, 47, 30],
        [100, 100, 100, 100, 100, 80, 40, 22, 10],
        [100, 100, 100, 100, 100, 56, 35, 18, 5],
        [100, 100, 100, 100, 100, 56, 25, 18, 5],
    ];

    /// <summary>Quanto cada falha anterior soma à chance, pela refinação atual.</summary>
    private static readonly int[] FailBonus = [5, 5, 5, 4, 4, 3, 3, 2, 1];

    /// <summary>Falhas guardadas no item no máximo (o client só lê até o valor 210).</summary>
    public const int MaxFails = 20;

    public static RefineMaterial? MaterialOf(short usable) => usable switch
    {
        UsableOriharukon => RefineMaterial.Oriharukon,
        UsableLaktorerium => RefineMaterial.Laktorerium,
        _ => null,
    };

    public static string Name(RefineMaterial m, short powder = 0) =>
        powder == LaktoreriumHundred ? "Laktorerium 100" : m == RefineMaterial.Oriharukon ? "Oriharukon" : "Laktorerium";

    /// <summary>Grau do item (1 a 6), ou 0 se não é refinável.</summary>
    public static int Grade(Item item, ItemList items)
    {
        // Os mesmos que o client recusa: montarias e filhotes, joias e cavalos com prazo.
        if (item.Index is >= 2330 and < 2390 or >= 3200 and < 3300 or >= 3980 and < 4000) return 0;
        var def = items[item.Index];
        int grade = def?.Effects.Where(e => e.Effect == EfGrade).Sum(e => e.Value) ?? 0;
        return grade is >= 1 and <= 6 ? grade : 0;
    }

    /// <summary>Falhas guardadas no item (dezenas do efeito 43, abaixo do +10).</summary>
    public static int Fails(Item item)
    {
        int raw = Stats.RawSanc(item);
        return raw is > 0 and <= 210 ? raw / 10 : 0;
    }

    /// <summary>
    /// O pó (<paramref name="powder"/>, o índice do item) consegue tentar subir esta refinação? O Laktorerium pula
    /// só o +9 (o client dá 0% para o +10).
    /// </summary>
    public static bool CanTry(int refine, RefineMaterial m, short powder) =>
        powder == LaktoreriumHundred ? refine == 10
        : m == RefineMaterial.Oriharukon ? refine < MaxRefine(m)
        : refine < MaxRefine(m) || refine == 10;

    /// <summary>Por que o pó não sobe o item (null se pode tentar).</summary>
    public static string? Refusal(int refine, RefineMaterial m, short powder) =>
        CanTry(refine, m, powder) ? null
        : powder == LaktoreriumHundred ? "A Laktorerium 100 só funciona em itens +10."
        : m == RefineMaterial.Oriharukon ? $"O Oriharukon refina só até +{MaxRefine(m)}."
        : refine == 9 ? "O Laktorerium não passa do +9 para o +10."
        : "O Laktorerium refina só até +11.";

    /// <summary>Chance (%) de o pó subir o item mais um, ou 0 se não dá.</summary>
    public static int Chance(Item item, ItemList items, RefineMaterial m, short powder = 0)
    {
        int grade = Grade(item, items), refine = Stats.Refine(item);
        if (grade == 0 || !CanTry(refine, m, powder)) return 0;
        if (refine == 10) return powder == LaktoreriumHundred ? 100 : HighChance;
        int rate = (m == RefineMaterial.Oriharukon ? Oriharukon : Laktorerium)[grade - 1][refine];
        return Math.Min(100, rate + FailBonus[refine] * Fails(item));
    }

    /// <summary>O item tem onde guardar a refinação (o efeito 43 ou um efeito livre)?</summary>
    public static bool CanStore(Item item) =>
        item.Effect1 is Stats.EfSanc or 0 || item.Effect2 is Stats.EfSanc or 0 || item.Effect3 is Stats.EfSanc or 0;

    /// <summary>O item com a refinação e as falhas trocadas (escreve no efeito 43, ou no primeiro efeito livre).</summary>
    public static Item WithRefine(Item item, int refine, int fails)
    {
        byte raw = (byte)(refine + 10 * Math.Clamp(fails, 0, MaxFails));
        if (item.Effect1 == Stats.EfSanc) item.Value1 = raw;
        else if (item.Effect2 == Stats.EfSanc) item.Value2 = raw;
        else if (item.Effect3 == Stats.EfSanc) item.Value3 = raw;
        else if (item.Effect1 == 0) (item.Effect1, item.Value1) = ((byte)Stats.EfSanc, raw);
        else if (item.Effect2 == 0) (item.Effect2, item.Value2) = ((byte)Stats.EfSanc, raw);
        else if (item.Effect3 == 0) (item.Effect3, item.Value3) = ((byte)Stats.EfSanc, raw);
        return item;
    }

    /// <summary>Uma tentativa: sobe um (e zera as falhas) ou guarda mais uma falha.</summary>
    public static (Item Item, bool Success) Try(Item item, int chance, Random random)
    {
        int refine = Stats.Refine(item);
        bool success = random.Next(100) < chance;
        if (refine >= Ancient.MinRefine)
            return success ? (WithHighRefine(item, refine + 1), true) : (item, false);
        return success
            ? (WithRefine(item, refine + 1, 0), true)
            : (WithRefine(item, refine, Fails(item) + 1), false);
    }

    /// <summary>O item +10 ou mais com outra refinação, mantendo a gema guardada nos 2 bits de baixo.</summary>
    public static Item WithHighRefine(Item item, int refine)
    {
        int raw = Stats.RawSanc(item);
        int gem = raw >= Ancient.RefinedBase ? (raw - Ancient.RefinedBase) % 4 : 0;
        byte value = (byte)(Ancient.RefinedBase + 4 * (refine - Ancient.MinRefine) + gem);
        if (item.Effect1 == Stats.EfSanc) item.Value1 = value;
        else if (item.Effect2 == Stats.EfSanc) item.Value2 = value;
        else if (item.Effect3 == Stats.EfSanc) item.Value3 = value;
        return item;
    }
}
