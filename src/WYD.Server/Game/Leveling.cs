using WYD.Server.Data;

namespace WYD.Server.Game;

/// <summary>Subida de nível: pontos e vida/mana a mais por nível, e vida/mana cheias ao subir.</summary>
public static class Leveling
{
    /// <summary>Pontos de atributo, especiais e de skill ganhos por nível.</summary>
    public const short ScorePointsPerLevel = 5, SpecialPointsPerLevel = 1, SkillPointsPerLevel = 1;

    /// <summary>Vida e mana a mais por nível, por classe (TK, FM, BM, HT).</summary>
    private static readonly (int Hp, int Mp)[] GrowthPerClass = [(5, 1), (2, 4), (3, 3), (4, 2)];

    /// <summary>Sobe quantos níveis a experiência permitir. Retorna quantos subiu.</summary>
    public static int Apply(Character c, ExpTable? table)
    {
        if (table is null) return 0;
        var (hp, mp) = GrowthPerClass[Math.Clamp((int)c.Class, 0, GrowthPerClass.Length - 1)];
        int gained = 0;

        while (c.BaseScore.Level < table.MaxLevel && c.Exp >= table.Next[c.BaseScore.Level])
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
