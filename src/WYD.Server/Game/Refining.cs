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
/// </summary>
public static class Refining
{
    public const short OriharukonPowder = 412, LaktoreriumPowder = 413;
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

    public static string Name(RefineMaterial m) => m == RefineMaterial.Oriharukon ? "Oriharukon" : "Laktorerium";

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

    /// <summary>Chance (%) de o material subir o item mais um, ou 0 se não dá.</summary>
    public static int Chance(Item item, ItemList items, RefineMaterial m)
    {
        int grade = Grade(item, items), refine = Stats.Refine(item);
        if (grade == 0 || refine >= MaxRefine(m)) return 0;
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
        return random.Next(100) < chance
            ? (WithRefine(item, refine + 1, 0), true)
            : (WithRefine(item, refine, Fails(item) + 1), false);
    }
}
