using WYD.Server.Data;

namespace WYD.Server.Game;

/// <summary>Subida de nível: pontos e vida/mana a mais por nível, e vida/mana cheias ao subir.</summary>
public static class Leveling
{
    /// <summary>Pontos de atributo, especiais e de skill ganhos por nível.</summary>
    public const short ScorePointsPerLevel = 5, SpecialPointsPerLevel = 1, SkillPointsPerLevel = 1;

    /// <summary>Vida e mana a mais por nível, por classe (TK, FM, BM, HT).</summary>
    private static readonly (int Hp, int Mp)[] GrowthPerClass = [(5, 1), (2, 4), (3, 3), (4, 2)];

    /// <summary>Chegou ao nível máximo da evolução e ainda pode evoluir.</summary>
    public static bool CanEvolve(Character c) =>
        c.Evolution < Evolution.SubCelestial && c.BaseScore.Level >= Evolutions.MaxLevel(c.Evolution);

    /// <summary>
    /// Passa para a evolução <paramref name="to"/> no nível 1, com a experiência zerada e vida/mana cheias.
    /// Atributos e pontos ganhos até aqui continuam.
    /// </summary>
    public static void Evolve(Character c, Evolution to)
    {
        c.Evolution = to;
        c.BaseScore.Level = 0;
        c.Exp = 0;
        c.BaseScore.Hp = c.BaseScore.MaxHp;
        c.BaseScore.Mp = c.BaseScore.MaxMp;
    }

    /// <summary>
    /// Leva o personagem direto ao nível <paramref name="level"/> (0 = nível 1 na tela), somando ou tirando os
    /// pontos e a vida/mana de cada nível, com a experiência do começo do nível. Vida e mana ficam cheias.
    /// </summary>
    public static void SetLevel(Character c, int level, ExpTable? table)
    {
        var (hp, mp) = GrowthPerClass[Math.Clamp((int)c.Class, 0, GrowthPerClass.Length - 1)];
        int delta = level - c.BaseScore.Level;
        c.BaseScore.Level = level;
        c.BaseScore.MaxHp = Math.Max(1, c.BaseScore.MaxHp + hp * delta);
        c.BaseScore.MaxMp = Math.Max(1, c.BaseScore.MaxMp + mp * delta);
        c.ScoreBonus = (short)Math.Clamp(c.ScoreBonus + ScorePointsPerLevel * delta, 0, short.MaxValue);
        c.SpecialBonus = (short)Math.Clamp(c.SpecialBonus + SpecialPointsPerLevel * delta, 0, short.MaxValue);
        c.SkillBonus = (short)Math.Clamp(c.SkillBonus + SkillPointsPerLevel * delta, 0, short.MaxValue);
        if (table is not null && table.MaxLevel > 0)
            c.Exp = level == 0 ? 0 : table.Next[Math.Min(level, table.MaxLevel) - 1];
        c.BaseScore.Hp = c.BaseScore.MaxHp;
        c.BaseScore.Mp = c.BaseScore.MaxMp;
    }

    /// <summary>Sobe quantos níveis a experiência permitir. Retorna quantos subiu.</summary>
    public static int Apply(Character c, ExpTable? table)
    {
        if (table is null) return 0;
        var (hp, mp) = GrowthPerClass[Math.Clamp((int)c.Class, 0, GrowthPerClass.Length - 1)];
        int gained = 0;

        int cap = Math.Min(Evolutions.MaxLevel(c.Evolution), table.MaxLevel);
        while (c.BaseScore.Level < cap && c.Exp >= table.Next[c.BaseScore.Level])
        {
            c.BaseScore.Level++;
            c.BaseScore.MaxHp += hp;
            c.BaseScore.MaxMp += mp;
            c.ScoreBonus += ScorePointsPerLevel;
            c.SpecialBonus += SpecialPointsPerLevel;
            c.SkillBonus += SkillPointsPerLevel;
            gained++;
        }

        if (gained > 0)
        {
            c.BaseScore.Hp = c.BaseScore.MaxHp;
            c.BaseScore.Mp = c.BaseScore.MaxMp;
        }
        return gained;
    }
}
