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

            // Nos itens de skill, os 4 campos de requisito (FOR/INT/DES/CON nos outros itens) são os Especiais 1 a 4:
            // a 1ª árvore de cada classe pede Especial 2, a 2ª Especial 3 e a 3ª Especial 4.
            var s = c.CurrentScore;
            (int Need, int Have)[] specials =
                [(def.ReqStr, s.Special1), (def.ReqInt, s.Special2), (def.ReqDex, s.Special3), (def.ReqCon, s.Special4)];
            for (int i = 0; i < specials.Length; i++)
                if (specials[i].Have < specials[i].Need)
                    return $"{name} precisa de {specials[i].Need} no Especial {i + 1} (você tem {specials[i].Have}).";
        }

        int cost = skills?[skill]?.SkillPoint ?? 0;
        if (c.SkillBonus < cost) return $"{name} custa {cost} pontos de skill (você tem {c.SkillBonus}).";

        c.SkillBonus -= (short)cost;
        c.LearnedSkill |= 1u << (skill % PerClass);
        return null;
    }
}
