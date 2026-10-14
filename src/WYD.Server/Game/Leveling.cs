using WYD.Server.Data;

namespace WYD.Server.Game;

/// <summary>Subida de nível: pontos e vida/mana a mais por nível, e vida/mana cheias ao subir.</summary>
public static class Leveling
{
    /// <summary>Pontos de atributo, especiais e de skill ganhos por nível.</summary>
    public const short ScorePointsPerLevel = 5, SpecialPointsPerLevel = 1, SkillPointsPerLevel = 1;

    /// <summary>Vida e mana a mais por nível, por classe (TK, FM, BM, HT).</summary>
    private static readonly (int Hp, int Mp)[] GrowthPerClass = [(5, 1), (2, 4), (3, 3), (4, 2)];

    /// <summary>Nível máximo (0 = nível 1 na tela) sem a tabela de experiência.</summary>
    public const int DefaultMaxLevel = 399;

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
        if (table is not null)
            c.Exp = level == 0 ? 0 : table.Next[level - 1];
        c.BaseScore.Hp = c.BaseScore.MaxHp;
        c.BaseScore.Mp = c.BaseScore.MaxMp;
    }

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
