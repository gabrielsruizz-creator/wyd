namespace WYD.Server.Game;

/// <summary>
/// Soul (Limite da Alma, skill 102, efeito 29). Enquanto o buff dura, os atributos da combinação escolhida sobem
/// uma porcentagem deles mesmos, como no código oficial que ficou no WYD.exe (0x55BEE6): o Celestial ganha 80% + 60%
/// nas combinações duplas e 120% nas simples; o Mortal e o Arch com a Soul, 40% + 30% e 60%. (O código oficial
/// separa pelo rosto evoluído, porque lá só o Celestial tinha a Soul; aqui o Arch também pode ter, e vale a evolução.)
/// </summary>
public static class Soul
{
    public const int Combinations = 10;

    /// <summary>Quanto o buff dura e o tempo até poder usar de novo.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(400);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    private enum Stat { Str, Int, Dex, Con }

    /// <summary>As 10 combinações, na ordem do client: o atributo principal e o secundário (nenhum nas simples).</summary>
    private static readonly (Stat Main, Stat? Second)[] Table =
    [
        (Stat.Str, Stat.Con), (Stat.Int, Stat.Con), (Stat.Dex, Stat.Con), (Stat.Str, Stat.Dex), (Stat.Int, Stat.Dex),
        (Stat.Con, Stat.Dex), (Stat.Str, null), (Stat.Int, null), (Stat.Con, null), (Stat.Dex, null),
    ];

    private static readonly string[] Names = ["FOR", "INT", "DES", "CON"];

    /// <summary>Descrição da combinação, por exemplo "FOR +40% e CON +30%".</summary>
    public static string Describe(int combination, bool celestial)
    {
        var (main, second) = Table[Math.Clamp(combination, 1, Combinations) - 1];
        var (big, small, single) = Percents(celestial);
        return second is { } s
            ? $"{Names[(int)main]} +{big}% e {Names[(int)s]} +{small}%"
            : $"{Names[(int)main]} +{single}%";
    }

    private static (int Big, int Small, int Single) Percents(bool celestial) => celestial ? (80, 60, 120) : (40, 30, 60);

    /// <summary>Soma à ficha o bônus da combinação (1 a 10; fora disso vale a 1, como no client).</summary>
    public static void Apply(ref Protocol.Score s, int combination, bool celestial)
    {
        var (main, second) = Table[(combination is >= 1 and <= Combinations ? combination : 1) - 1];
        var (big, small, single) = Percents(celestial);
        Add(ref s, main, second is null ? single : big);
        if (second is { } other) Add(ref s, other, small);
    }

    private static void Add(ref Protocol.Score s, Stat stat, int percent)
    {
        static short Up(short v, int p) => (short)Math.Clamp(v + v * p / 100, 0, short.MaxValue);
        switch (stat)
        {
            case Stat.Str: s.Str = Up(s.Str, percent); break;
            case Stat.Int: s.Int = Up(s.Int, percent); break;
            case Stat.Dex: s.Dex = Up(s.Dex, percent); break;
            default: s.Con = Up(s.Con, percent); break;
        }
    }

    /// <summary>O buff da Soul está ativo (o efeito 29 lançado pela skill 102; a Maestria em Armas usa o mesmo efeito).</summary>
    public static bool Active(Character c) =>
        Skills.Knows(c, Skills.SoulOfLimits) && c.Affects.Any(a => a.Skill == Skills.SoulOfLimits && a.Expires > DateTime.UtcNow);
}
