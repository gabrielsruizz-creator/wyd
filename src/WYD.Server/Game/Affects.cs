using WYD.Server.Data;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Tipos de efeito das skills (AffectType da SkillData.bin, e o TickType das auras sem AffectType). O client usa o
/// número para o ícone; os nomes vêm das skills que usam cada um.
/// </summary>
public enum AffectType : byte
{
    Slow = 1,             // Toque Sagrado, Tempestade de Gelo, Lança de Gelo (nos inimigos)
    Haste = 2,            // Haste
    Pursuit = 3,          // Perseguição (Double Swing): diminui a imunidade dos inimigos
    PowerUp = 4,          // Power Up
    Freeze = 7,           // Freeze Blade
    MagicWeapon = 9,      // Magic Weapon
    Waste = 10,           // Waste
    MagicShield = 11,     // Magic Shield
    Assault = 13,         // Assault
    Growth = 14,          // Growth
    SkillAmplify = 15,    // Skill Amplify
    Transform = 16,       // Werewolf, Werebear, Astaroth, Titan, Evilator
    AuraOfLife = 17,      // Aura Of Life (TickType)
    ManaControl = 18,     // Mana Control
    Immunity = 19,        // Immunity
    Concentration = 20,   // Concentration
    TransferArmor = 21,   // Transfer Armor Class
    LightningStorm = 22,  // Lightning Storm (TickType)
    ElementalStream = 23, // Elemental Stream (TickType)
    Rescue = 24,          // Rescue
    ElementalProtect = 25,// Elemental Protector
    Dodge = 26,           // Dodge
    EnchantFrost = 27,    // Enchant Frost
    Hide = 28,            // Hide In Shadow
    WeaponMastery = 29,   // Weapon Mastery, Soul Of Limits
    HunterVision = 30,    // Hunter's Vision
    CoinArmor = 31,       // Coin Armor
    Cancellation = 32,    // Cancellation
    CriticalArmor = 36,   // Critical Armor, Venom
    AnotherSoul = 37,     // Another Soul
    SpiritChange = 38,    // Spirit Change, Improved Range
}

/// <summary>Um efeito ativo num personagem: quanto vale e até quando.</summary>
public sealed record Affect(AffectType Type, int Amount, int Skill, DateTime Expires);

/// <summary>O que os efeitos ativos somam aos atributos.</summary>
public readonly record struct AffectBonus(int Damage, int Ac, int MagicPercent, int RunSpeed, int Dodge, int RegenHp, int ManaToHpPercent,
    int AttackSpeed = 0, bool SpiritChange = false);

/// <summary>
/// Efeitos (buffs) das skills. O client guarda até 16, cada um num short: tipo no byte de cima e o tempo que falta
/// no de baixo, em unidades de 8 segundos (0x5359B6). Vai no 0x336 (0x2A) para o próprio jogador, que mostra os
/// ícones, e no 0x364 (0x42) para quem o vê.
/// </summary>
public static class Affects
{
    public const int Max = 16;

    /// <summary>Quanto da mana máxima o Controle de Mana passa para a vida máxima, enquanto dura.</summary>
    public const int ManaControlPercent = 30;
    public const int SecondsPerUnit = 8;

    /// <summary>Vida máxima que a Troca de Espírito soma além da mana transferida.</summary>
    public const int SpiritChangeHp = 750;

    /// <summary>
    /// Quanto da mana máxima a Troca de Espírito (efeito 38) passa para a vida máxima, como o client (0x55C65B):
    /// mana / 4 (evoluído) ou / 8 (Mortal), mais (Especial 3 + nível) / 4; o Celestial soma 400 ao nível (o código
    /// oficial reconhece o Celestial pelo bit 30 das skills, a Soul).
    /// </summary>
    public static int SpiritChangeMoved(int maxMp, int special3, int level, bool celestial, bool evolved)
    {
        if (celestial) level += 400;
        return (evolved ? maxMp / 4 : maxMp / 8) + (special3 + level) / 4;
    }

    /// <summary>
    /// O efeito que a skill dá, ou null se ela não dá nenhum. A duração é o AffectTime (em unidades de 8 s) mais
    /// um a cada 10 pontos do especial da árvore; a força é o AffectValue mais 1/50 dele por ponto do especial.
    /// </summary>
    public static Affect? FromSkill(Character caster, SkillDefinition spell, int skill, DateTime now)
    {
        int type = spell.AffectType != 0 ? spell.AffectType : spell.TickType;
        if (type is <= 0 or > 255 || !Enum.IsDefined((AffectType)type)) return null;
        int special = SkillUse.TreeSpecial(caster.CurrentScore, skill);
        int units = Math.Clamp(Math.Max(1, spell.AffectTime) + special / 10, 1, 255);
        int baseValue = spell.AffectType != 0 ? spell.AffectValue : spell.TickValue;
        int amount = type == (int)AffectType.Transform ? baseValue : baseValue + baseValue * special / 50;
        if (skill == Skills.SoulOfLimits) return new Affect((AffectType)type, amount, skill, now + Soul.Duration);
        return new Affect((AffectType)type, amount, skill, now.AddSeconds(units * SecondsPerUnit));
    }

    /// <summary>Põe o efeito (o do mesmo tipo é trocado; cheio, o que acaba primeiro sai).</summary>
    public static void Apply(Character c, Affect affect) => Apply(c.Affects, affect);

    public static void Apply(List<Affect> affects, Affect affect)
    {
        affects.RemoveAll(a => a.Type == affect.Type);
        if (affects.Count >= Max)
            affects.Remove(affects.MinBy(a => a.Expires)!);
        affects.Add(affect);
    }

    /// <summary>
    /// Rostos das transformações da BM (AffectValue do efeito 16): 1 Lobisomem (Wolf_Master 22), 2 Homem-Urso
    /// (Bear_Master 23), 3 Astaroth (24), 4 Titã (25), 5 Éden (Evilator 32). O client escolhe o corpo e os golpes pelo
    /// efeito 18 do rosto (0x4DC21F), por isso a transformação troca o rosto que ele recebe.
    /// </summary>
    public static readonly short[] TransformFaces = [0, 22, 23, 24, 25, 32];

    /// <summary>O rosto da fera enquanto a transformação dura (null sem transformação).</summary>
    public static short? TransformFace(Character c)
    {
        var now = DateTime.UtcNow;
        var t = c.Affects.FirstOrDefault(a => a.Type == AffectType.Transform && a.Expires > now);
        return t is not null && t.Amount > 0 && t.Amount < TransformFaces.Length ? TransformFaces[t.Amount] : null;
    }

    /// <summary>Tira os que acabaram. Retorna true se algum saiu.</summary>
    public static bool Expire(Character c, DateTime now) => c.Affects.RemoveAll(a => a.Expires <= now) > 0;

    /// <summary>Os 16 shorts que o client espera: (tipo &lt;&lt; 8) | unidades de 8 s que faltam.</summary>
    public static short[] ToClient(Character c, DateTime now) => ToClient(c.Affects, now);

    public static short[] ToClient(List<Affect> affects, DateTime now)
    {
        var shorts = new short[Max];
        int i = 0;
        foreach (var a in affects.Where(a => a.Expires > now).Take(Max))
        {
            int units = Math.Clamp((int)Math.Ceiling((a.Expires - now).TotalSeconds / SecondsPerUnit), 1, 255);
            shorts[i++] = (short)(((int)a.Type << 8) | units);
        }
        return shorts;
    }

    /// <summary>Bytes de cada efeito gravado: byte tipo, short skill, int força, int segundos que faltam, byte livre.</summary>
    public const int StoredSize = 12;

    /// <summary>
    /// Os efeitos ativos para gravar no banco, com o tempo que falta (o relógio para enquanto o personagem está fora
    /// do jogo e volta a correr no próximo login).
    /// </summary>
    public static byte[] Serialize(Character c, DateTime now)
    {
        var active = c.Affects.Where(a => a.Expires > now).Take(Max).ToList();
        var data = new byte[active.Count * StoredSize];
        for (int i = 0; i < active.Count; i++)
        {
            var span = data.AsSpan(i * StoredSize);
            span[0] = (byte)active[i].Type;
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[1..], (short)active[i].Skill);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[3..], active[i].Amount);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[7..],
                (int)Math.Ceiling((active[i].Expires - now).TotalSeconds));
        }
        return data;
    }

    /// <summary>Volta os efeitos gravados, contando o tempo que faltava a partir de agora.</summary>
    public static List<Affect> Deserialize(byte[]? data, DateTime now)
    {
        var list = new List<Affect>();
        if (data is null) return list;
        for (int o = 0; o + StoredSize <= data.Length && list.Count < Max; o += StoredSize)
        {
            var span = data.AsSpan(o);
            int seconds = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span[7..]);
            if (seconds <= 0 || !Enum.IsDefined((AffectType)span[0])) continue;
            list.Add(new Affect((AffectType)span[0], System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span[3..]),
                System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span[1..]), now.AddSeconds(seconds)));
        }
        return list;
    }

    public static AffectBonus Bonus(Character c)
    {
        int damage = 0, ac = 0, magic = 0, speed = 0, dodge = 0, regen = 0;
        int manaToHp = 0, attackSpeed = 0;
        bool spiritChange = false;
        foreach (var a in c.Affects)
        {
            switch (a.Type)
            {
                case AffectType.WeaponMastery when a.Skill == Skills.SoulOfLimits:
                    break;                                   // Soul: o bônus vem da combinação (ver Soul)
                case AffectType.PowerUp or AffectType.MagicWeapon or AffectType.Assault or AffectType.Growth
                    or AffectType.WeaponMastery:
                    damage += a.Amount * 5;
                    break;
                case AffectType.MagicShield or AffectType.TransferArmor:
                    ac += a.Amount * 5;
                    break;
                case AffectType.SkillAmplify:
                    magic += a.Amount * 2;
                    break;
                case AffectType.Haste:
                    speed += 1;
                    break;
                case AffectType.Dodge:
                    dodge += 10 * Math.Max(1, a.Amount);
                    break;
                case AffectType.AuraOfLife:
                    regen += a.Amount;
                    break;
                case AffectType.ManaControl:
                    manaToHp = ManaControlPercent;
                    break;
                // Como no client (0x55AEE8 e 0x55B110): lentidão tira 30 da velocidade de ataque, congelar
                // tira valor/10 + 10.
                case AffectType.SpiritChange:
                    spiritChange = true;
                    break;
                case AffectType.Slow:
                    attackSpeed -= 30;
                    break;
                case AffectType.Freeze:
                    attackSpeed -= a.Amount / 10 + 10;
                    break;
            }
        }
        return new AffectBonus(damage, ac, magic, speed, Math.Min(dodge, 50), regen, manaToHp, attackSpeed, spiritChange);
    }
}
