using WYD.Server.Data;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Evocação da BM (skills 56 a 63, InstanceType 11 na SkillData; o InstanceValue de 1 a 8 é a criatura). A criatura
/// nasce ao lado de quem invocou, segue o dono, ataca o monstro que o dono atacou por último (ou o que está atacando
/// o dono) e dá a experiência e o drop ao dono. Uma invocação nova troca a anterior; ela some em
/// <see cref="Duration"/>, se morrer, se o dono sair ou se ficar longe demais.
/// <para>
/// Os atributos são deste servidor (os do oficial não estão nos arquivos do client): nível do dono; vida
/// (100 + 15 × nível) × (2 + n) / 4; dano 10 × n + nível / 2 + especial da árvore (Evocação = Especial 3); defesa
/// 5 × n + nível / 4.
/// </para>
/// </summary>
public static class Summons
{
    /// <summary>InstanceType da SkillData das invocações.</summary>
    public const int InstanceType = 11;

    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);

    /// <summary>Distância do dono a partir da qual a criatura vai atrás dele (sem alvo) ou some (longe demais).</summary>
    public const int FollowDistance = 2, LeashDistance = 20;

    /// <summary>Tempo entre dois golpes da criatura.</summary>
    public static readonly TimeSpan AttackInterval = TimeSpan.FromMilliseconds(1500);

    /// <summary>Nome (até 12 letras) e rosto (modelo do ItemList) de cada criatura, de 1 a 8.</summary>
    public static readonly (string Name, short Face)[] Creatures =
    [
        ("", 0),
        ("Condor", 206),
        ("Javali", 216),          // Wild_Boar
        ("Lobo", 226),            // Wolf
        ("Urso", 227),            // Bear
        ("Grande Tigre", 244),    // Trample_Tiger
        ("Gorila", 245),          // Sturning_Gorilla
        ("Dragao Negro", 307),    // Black_Dragon
        ("Succubus", 396),
    ];

    public static bool IsSummon(SkillDefinition? spell) =>
        spell is { InstanceType: InstanceType } && spell.InstanceValue is >= 1 and < 9;

    public static NpcTemplate Template(Character owner, SkillDefinition spell, int skill)
    {
        int n = spell.InstanceValue, level = owner.BaseScore.Level;
        int maxHp = Math.Min(Score.MaxHpMp, (100 + 15 * level) * (2 + n) / 4);
        var score = new Score
        {
            Level = level,
            Ac = 5 * n + level / 4,
            Damage = 10 * n + level / 2 + SkillUse.TreeSpecial(owner.CurrentScore, skill),
            AttackRun = 4,
            MaxHp = maxHp,
            Hp = maxHp,
        };
        var equip = new Item[GameConstants.MaxEquip];
        equip[0] = new Item(Creatures[n].Face);
        return new NpcTemplate(Creatures[n].Name, 0, 0, 0, 0, 0, 0, score, equip, new Item[GameConstants.MaxCarry]);
    }
}
