using WYD.Server.Data;

namespace WYD.Server.Game;

/// <summary>
/// Aprender skills nos mestres. A skill n é o item 5000 + n; cada classe tem 24 (TK 0-23, FM 24-47, BM 48-71,
/// HT 72-95) e a aprendida vira o bit (n % 24) do LearnedSkill, que o client recebe no 0x337.
/// </summary>
public static class Skills
{
    public const short FirstSkillItem = 5000;
    public const int PerClass = 24;

    /// <summary>Mestres de skill: Merchant 19 no arquivo do NPC (o client vê 3 em Score.Merchant e abre a lista com 0x27B).</summary>
    public const byte MasterMerchant = 19;

    public static int SkillOf(short item) => item - FirstSkillItem;

    public static bool Knows(Character c, int skill) => (c.LearnedSkill & (1u << (skill % PerClass))) != 0;

    /// <summary>Aprende a skill do item. Retorna a recusa, ou null se aprendeu.</summary>
    public static string? Learn(Character c, short item, ItemList? items, SkillData? skills)
    {
        int skill = SkillOf(item);
        if (skill < 0 || skill >= PerClass * 4) return "Isso não é uma skill.";
        var def = items?[item];
        string name = def?.Name.Replace('_', ' ') ?? $"skill {skill}";

        if (skill / PerClass != (int)c.Class) return $"{name} não é da sua classe.";
        if (Knows(c, skill)) return $"Você já sabe {name}.";

        if (def is not null)
        {
            if (def.ReqLevel > c.BaseScore.Level + 1) return $"{name} precisa do nível {def.ReqLevel}.";
            var s = c.CurrentScore;
            if (s.Str < def.ReqStr || s.Int < def.ReqInt || s.Dex < def.ReqDex || s.Con < def.ReqCon)
                return $"{name} precisa de FOR {def.ReqStr}, INT {def.ReqInt}, DES {def.ReqDex}, CON {def.ReqCon}.";
        }

        int cost = skills?[skill]?.SkillPoint ?? 0;
        if (c.SkillBonus < cost) return $"{name} custa {cost} pontos de skill (você tem {c.SkillBonus}).";

        c.SkillBonus -= (short)cost;
        c.LearnedSkill |= 1u << (skill % PerClass);
        return null;
    }
}
