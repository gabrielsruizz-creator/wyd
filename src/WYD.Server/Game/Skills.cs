using WYD.Server.Protocol;
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

    /// <summary>
    /// Skills de Sephira: 96 a 103 da SkillData, iguais para todas as classes, nos bits 24 a 31 do LearnedSkill
    /// (97 Conjure Cannon, 98 Thorn Wall, 99 Resurrect, 100 Concentration, 101 Spectral Force, 102 Soul).
    /// </summary>
    public const int FirstSephira = 96, LastSephira = 103;

    /// <summary>Canhão Guardião: usa o canhão do castelo de Noatun, só na guerra de castelos.</summary>
    public const int ConjureCannon = 97;

    /// <summary>Muro de Espinhos: convoca plantas com espinhos (ver <see cref="ThornWall"/>).</summary>
    public const int ThornWallSkill = 98;

    /// <summary>Ressurreição: renasce no lugar onde morreu.</summary>
    public const int Resurrect = 99;

    /// <summary>Concentração (passiva): o ataque acerta sem se importar com a esquiva do alvo.</summary>
    public const int Concentration = 100;

    /// <summary>Força Espectral (passiva): +1 de alcance em todas as armas e skills (o client já soma, 0x490525).</summary>
    public const int SpectralForce = 101;

    /// <summary>Soul (Limite da Alma): bit 30. O client trata rosto evoluído + esse bit como Celestial.</summary>
    public const int SoulOfLimits = 102;

    /// <summary>Uso (efeito 38) dos livros de Sephira no ItemList: 32 a 36 ensinam as skills 97 a 101 (itens 667 a 671).</summary>
    public const short FirstSephiraUsable = 32, LastSephiraUsable = 36;

    public static int? SephiraOfUsable(short usable) =>
        usable is >= FirstSephiraUsable and <= LastSephiraUsable ? usable - FirstSephiraUsable + 97 : null;

    /// <summary>Bit do LearnedSkill da skill: n % 24 nas da classe, 24 a 31 nas de Sephira.</summary>
    public static int Bit(int skill) => skill >= FirstSephira ? skill - FirstSephira + 24 : skill % PerClass;

    public static bool Knows(Character c, int skill) => (c.LearnedSkill & (1u << Bit(skill))) != 0;

    public static void Teach(Character c, int skill) => c.LearnedSkill |= 1u << Bit(skill);

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
        Teach(c, skill);
        return null;
    }
}

/// <summary>
/// Usar uma skill: o client manda o ataque (0x39D/0x39E/0x36C) com o número da skill em 0x18. O efeito vem da
/// SkillData.bin: InstanceType 1 = dano físico, 2 a 5 = dano mágico de elemento, 6 = cura; InstanceValue é a força.
/// As fórmulas são deste servidor: a força da skill soma os especiais da árvore dela (e INT/magia nas mágicas).
/// </summary>
public static class SkillUse
{
    public const int Physical = 1, FirstMagic = 2, LastMagic = 5, Heal = 6;

    /// <summary>A skill pode ser usada: é da classe do personagem e já foi aprendida.</summary>
    /// <summary>
    /// Golpe normal à distância: com arma de tipo 101, 102 ou 103 (efeito 21: arcos e afins) o client manda o
    /// ataque comum com a "skill" 151, 152 ou 153 (0x45F030), não com -1.
    /// </summary>
    public static bool IsRangedNormal(int skill) => skill is >= 151 and <= 153;

    public static bool CanUse(Character c, int skill) =>
        (skill >= 0 && skill < Skills.PerClass * 4 && skill / Skills.PerClass == (int)c.Class
         || skill is >= Skills.FirstSephira and <= Skills.LastSephira)
        && Skills.Knows(c, skill);

    /// <summary>Especial da árvore da skill: 1ª árvore = Especial 2, 2ª = Especial 3, 3ª = Especial 4.</summary>
    public static int TreeSpecial(Score s, int skill) => ((skill % Skills.PerClass) / 8) switch
    {
        0 => s.Special2,
        1 => s.Special3,
        _ => s.Special4,
    };

    /// <summary>Mana gasta, menos a economia de mana dos itens (efeito 44, no máximo metade).</summary>
    public static int ManaCost(Data.SkillDefinition spell, int saveMana) =>
        Math.Max(0, spell.ManaSpent * (100 - Math.Clamp(saveMana, 0, 50)) / 100);

    /// <summary>Skill de efeito (buff): não dá dano nem cura, só põe um efeito.</summary>
    public static bool IsBuff(Data.SkillDefinition? spell) =>
        spell is not null && !IsDamage(spell) && spell.InstanceType != Heal && (spell.AffectType != 0 || spell.TickType != 0);

    /// <summary>Alvo da skill: 0 = em si mesmo, 2 = num aliado (ou em si).</summary>
    public const int TargetSelf = 0, TargetAlly = 2;

    public static bool IsDamage(Data.SkillDefinition? spell) =>
        spell is null || spell.InstanceType is Physical or >= FirstMagic and <= LastMagic;

    /// <summary>Dano da skill num alvo.</summary>
    public static int Damage(Character caster, Data.SkillDefinition spell, int skill, Score target, Random? random = null)
    {
        random ??= Random.Shared;
        var s = caster.CurrentScore;
        int special = TreeSpecial(s, skill);
        var b = caster.ItemBonus;
        if (spell.InstanceType == Physical)
            return Math.Min(short.MaxValue,
                Combat.Damage(s, target, random, perforation: b.Perforation) + spell.InstanceValue + special);

        int power = (spell.InstanceValue + special + s.Int / 4) * (100 + b.Magic + Affects.Bonus(caster).MagicPercent) / 100;
        int rolled = power * random.Next(90, 111) / 100;
        return Math.Clamp(rolled - target.Ac / 8, 1, short.MaxValue);
    }

    /// <summary>Quanto a skill de cura recupera.</summary>
    public static int HealAmount(Character caster, Data.SkillDefinition spell, int skill)
    {
        var s = caster.CurrentScore;
        return (spell.InstanceValue + TreeSpecial(s, skill) + s.Int / 4)
            * (100 + caster.ItemBonus.Magic + Affects.Bonus(caster).MagicPercent) / 100;
    }
}
