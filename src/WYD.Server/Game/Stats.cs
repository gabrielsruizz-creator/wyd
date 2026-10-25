using WYD.Server.Data;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Atributos que dependem do equipamento, calculados como o client 7.54 faz (função em 0x55A392):
/// velocidade = base (AttackRun &amp; 0xF) + efeito 29 dos itens (botas); com uma montaria viva no slot 14,
/// vale a velocidade da montaria; no máximo 6.
/// </summary>
public static class Stats
{
    public const int RunSpeedEffect = 29;
    public const int MaxRunSpeed = 6;
    public const int MountSlot = 14;

    /// <summary>Montarias adultas: 2360 (Porco) a 2389; os cavalos com prazo: 3980 a 3998.</summary>
    public const short FirstMount = 2360, LastMount = 2389, FirstTimedMount = 3980, LastTimedMount = 3998;

    /// <summary>Velocidade de cada montaria 2360..2389 (tabela do WYD.exe em 0x5CA4D0, 5º campo de cada linha).</summary>
    private static readonly byte[] MountSpeed =
    [
        4, 4, 5, 5, 4, 5, 6, 6, 6, 6,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
    ];

    /// <summary>Os cavalos com prazo (3980..3998) correm na velocidade 6 (tabela em 0x5CA7A0).</summary>
    private const byte TimedMountSpeed = 6;

    /// <summary>Vida e nível padrão de uma montaria criada sem efeitos (pelo /gm item).</summary>
    public const short DefaultMountHp = 100;
    public const byte DefaultMountLevel = 1;

    public static bool IsMount(short index) =>
        index is >= FirstMount and <= LastMount or >= FirstTimedMount and <= LastTimedMount;

    /// <summary>A vida da montaria fica nos bytes 2-3 do item (Effect1 e Value1 juntos, como short).</summary>
    public static short MountHp(Item mount) => (short)(mount.Effect1 | mount.Value1 << 8);

    /// <summary>O nível da montaria fica no byte 4 do item (Effect2).</summary>
    public static byte MountLevel(Item mount) => mount.Effect2;

    /// <summary>Uma montaria nova (do /gm item, sem efeitos): viva, nível 1.</summary>
    public static Item NewMount(short index) => new(index)
    {
        Effect1 = (byte)(DefaultMountHp & 0xFF),
        Value1 = (byte)(DefaultMountHp >> 8),
        Effect2 = DefaultMountLevel,
    };

    /// <summary>
    /// Montarias com vida e nível zerados nunca foram usadas (vieram do /gm item antigo): ganham vida e nível 1.
    /// Retorna se mudou alguma.
    /// </summary>
    public static bool FixNewMounts(Item[] items)
    {
        bool changed = false;
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (!IsMount(it.Index) || MountHp(it) != 0 || MountLevel(it) != 0) continue;
            items[i] = NewMount(it.Index);
            changed = true;
        }
        return changed;
    }

    // ------------------------------------------------------------------ efeitos dos itens

    public const int EfDamage = 2, EfAc = 3, EfHp = 4, EfMp = 5, EfStr = 7, EfInt = 8, EfDex = 9, EfCon = 10,
        EfSpecial1 = 11, EfSpecial2 = 12, EfSpecial3 = 13, EfSpecial4 = 14, EfMagic = 60, EfDamage2 = 73,
        EfSpecialAll = 74, EfSanc = 43, EfRegenHp = 47, EfRegenMp = 48,
        EfAttackSpeed = 26, EfCritical = 42, EfSaveMana = 44, EfHpPercent = 45, EfMpPercent = 46,
        EfResist1 = 49, EfResist2 = 50, EfResist3 = 51, EfResist4 = 52, EfAcAdd = 53, EfResistAll = 54, EfMagicAdd = 68;

    /// <summary>Efeitos que a refinação não multiplica (como no client, em 0x557507).</summary>
    private static readonly HashSet<int> NotRefined =
        [1, 17, 18, 21, 22, 23, 24, 25, 27, 33, 38, 47, 48, 78, 84, 86, 87, 88, 112, 113];

    /// <summary>Multiplicador (em %) de +11 a +15.</summary>
    private static readonly int[] HighRefine = [220, 250, 280, 320, 370];

    /// <summary>
    /// Refinação do item (+0 a +15), lida do efeito 43 como o client (0x55845F): de 0 a 229 vale o último
    /// dígito (+0 a +9); de 230 em diante, (valor - 220 - 10) / 4 + 10, ou seja 230 = +10, 234 = +11 ... 250 = +15.
    /// </summary>
    public static int Refine(Item item)
    {
        if (IsMount(item.Index)) return 0;
        int raw = RawSanc(item);
        int s = raw >= 230 ? raw - 220 : raw % 10;
        if (s is >= 10 and <= 35) s = (s - 10) / 4 + 10;
        return s;
    }

    /// <summary>
    /// Tipos de efeito que guardam a refinação: o 43 e, como no client (0x5385AA, 0x538476), de 115 a 126. De 116 a
    /// 125 são as cores da pintura (ver <see cref="Dyes"/>): o valor é a refinação de sempre.
    /// </summary>
    public static bool IsSancEffect(int effect) => effect is EfSanc or >= 115 and <= 126;

    /// <summary>Qual dos 3 efeitos do item guarda a refinação (1 a 3), ou 0 se nenhum.</summary>
    public static int SancSlot(Item item) =>
        IsSancEffect(item.Effect1) ? 1 : IsSancEffect(item.Effect2) ? 2 : IsSancEffect(item.Effect3) ? 3 : 0;

    /// <summary>Valor guardado no efeito da refinação (refinação; do +10 em diante também a gema).</summary>
    public static int RawSanc(Item item) => SancSlot(item) switch
    {
        1 => item.Value1,
        2 => item.Value2,
        3 => item.Value3,
        _ => 0,
    };

    /// <summary>
    /// O item com outro valor no efeito da refinação (mantendo o tipo, que pode ser uma cor). Sem esse efeito, usa o
    /// primeiro efeito livre com o 43, se <paramref name="create"/>.
    /// </summary>
    public static Item WithRawSanc(Item item, byte raw, bool create = true)
    {
        switch (SancSlot(item))
        {
            case 1: item.Value1 = raw; break;
            case 2: item.Value2 = raw; break;
            case 3: item.Value3 = raw; break;
            default:
                if (!create) break;
                if (item.Effect1 == 0) (item.Effect1, item.Value1) = (EfSanc, raw);
                else if (item.Effect2 == 0) (item.Effect2, item.Value2) = (EfSanc, raw);
                else if (item.Effect3 == 0) (item.Effect3, item.Value3) = (EfSanc, raw);
                break;
        }
        return item;
    }

    /// <summary>Um efeito de um item, com a refinação aplicada (como o client em 0x556F5B).</summary>
    public static int Ability(Item item, ItemList? items, int effect)
    {
        if (item.Index <= 0 || IsMount(item.Index)) return 0;
        var def = items?[item.Index];
        int value = 0;
        if (def is not null)
            foreach (var e in def.Effects)
                if (e.Effect == effect) value += e.Value;
        if (!IsSancEffect(effect))
        {
            if (item.Effect1 == effect) value += (sbyte)item.Value1;
            if (item.Effect2 == effect) value += (sbyte)item.Value2;
            if (item.Effect3 == effect) value += (sbyte)item.Value3;
        }

        int sanc = item.Index <= 40 ? 0 : Refine(item);
        if (sanc >= 9 && def is not null && (def.Pos & 0xF00) != 0) sanc++;
        if (sanc > 0 && !NotRefined.Contains(effect))
            value = sanc <= 10
                ? value * (sanc + 10) / 10
                : value * HighRefine[Math.Min(sanc, 15) - 11] / 100;

        if (effect == RunSpeedEffect)
        {
            if (value >= 3) value = 2;
            if (value > 0 && sanc >= 9) value++;
        }
        if (effect is EfRegenHp or EfRegenMp) value *= sanc;
        return value;
    }

    /// <summary>Soma de um efeito em todo o equipamento (como o client em 0x558FF0).</summary>
    public static int EquipAbility(Character c, ItemList? items, int effect)
    {
        int total = 0;
        for (int slot = 1; slot < c.Equip.Length; slot++)
        {
            if (effect == EfDamage && slot is 6 or 7) continue;    // as duas armas entram juntas, abaixo
            if (effect == EfMagic && slot == 7) continue;
            total += Ability(c.Equip[slot], items, effect);
        }

        if (effect == EfDamage)
        {
            // A arma mais forte conta inteira e a outra 30% (50% se forem do mesmo tipo).
            var (left, right) = (c.Equip[6], c.Equip[7]);
            int a = Ability(left, items, EfDamage) + Ability(left, items, EfDamage2);
            int b = Ability(right, items, EfDamage) + Ability(right, items, EfDamage2);
            int typeA = items?[left.Index]?.Unique ?? 0, typeB = items?[right.Index]?.Unique ?? 0;
            if (typeA != 0 && typeB != 0 && !(typeA == 47 && typeB == 45))
            {
                int percent = typeA == typeB ? 50 : 30;
                total += Math.Max(a, b) + Math.Min(a, b) * percent / 100;
            }
            else total += typeA == 47 && typeB == 45 ? a : Math.Max(a, b);
        }
        return total;
    }

    public static int RunSpeed(Character c, ItemList? items)
    {
        var mount = c.Equip[MountSlot];
        if (IsMount(mount.Index) && MountHp(mount) > 0)
            return mount.Index <= LastMount ? MountSpeed[mount.Index - FirstMount] : TimedMountSpeed;

        int speed = (c.BaseScore.AttackRun & 0x0F) + EquipAbility(c, items, RunSpeedEffect);
        return Math.Clamp(speed, 0, MaxRunSpeed);
    }

    /// <summary>Velocidade de ataque máxima em % (o client limita em 150 antes de dividir por 10).</summary>
    public const int MaxAttackSpeed = 150;

    /// <summary>
    /// Velocidade de ataque que vai nos 4 bits de cima de AttackRun (0 a 15), como o client 7.54 (0x55A70A a
    /// 0x55CA3B): 10 × os 4 bits de cima da base + efeito 26 dos itens (e transformação, efeitos) + DES / 5, entre 0
    /// e 150, dividido por 10. O client usa o valor da ficha que o servidor manda (0x336) para a animação.
    /// </summary>
    public static int AttackSpeed(byte baseAttackRun, int bonus, int dex) =>
        Math.Clamp((baseAttackRun >> 4) * 10 + bonus + dex / 5, 0, MaxAttackSpeed) / 10;

    /// <summary>Atualiza os atributos calculados do personagem (chamar ao entrar e ao trocar equipamento).</summary>
    public static void Recalculate(Character c, ItemList? items)
    {
        c.RunSpeed = (byte)RunSpeed(c, items);
        int specialAll = EquipAbility(c, items, EfSpecialAll);
        c.ItemBonus = new ItemBonus(
            Damage: EquipAbility(c, items, EfDamage),
            Ac: EquipAbility(c, items, EfAc) + EquipAbility(c, items, EfAcAdd),
            MaxHp: EquipAbility(c, items, EfHp),
            MaxMp: EquipAbility(c, items, EfMp),
            Str: EquipAbility(c, items, EfStr),
            Int: EquipAbility(c, items, EfInt),
            Dex: EquipAbility(c, items, EfDex),
            Con: EquipAbility(c, items, EfCon),
            Special1: EquipAbility(c, items, EfSpecial1) + specialAll,
            Special2: EquipAbility(c, items, EfSpecial2) + specialAll,
            Special3: EquipAbility(c, items, EfSpecial3) + specialAll,
            Special4: EquipAbility(c, items, EfSpecial4) + specialAll,
            Perforation: AncientPercent(c, items, AncientEffect.Perforation),
            Absorption: AncientPercent(c, items, AncientEffect.Absorption),
            ExpBonus: AncientPercent(c, items, AncientEffect.Exp),
            DropBonus: AncientPercent(c, items, AncientEffect.Drop),
            HpPercent: EquipAbility(c, items, EfHpPercent),
            MpPercent: EquipAbility(c, items, EfMpPercent),
            RegenHp: EquipAbility(c, items, EfRegenHp),
            RegenMp: EquipAbility(c, items, EfRegenMp),
            Magic: EquipAbility(c, items, EfMagic) + EquipAbility(c, items, EfMagicAdd),
            Critical: EquipAbility(c, items, EfCritical),
            AttackSpeed: EquipAbility(c, items, EfAttackSpeed),
            SaveMana: EquipAbility(c, items, EfSaveMana),
            ResistAll: EquipAbility(c, items, EfResistAll));
    }

    /// <summary>Soma (em %) do efeito das gemas no equipamento: ancients e itens +10 ou mais.</summary>
    public static int AncientPercent(Character c, ItemList? items, AncientEffect effect)
    {
        int total = 0;
        for (int slot = 1; slot < c.Equip.Length; slot++)
            if (Ancient.EquippedEffect(c.Equip[slot], items) == effect)
                total += Ancient.BonusPercent;
        return total;
    }
}

/// <summary>O que o equipamento soma aos atributos base.</summary>
public readonly record struct ItemBonus(
    int Damage, int Ac, int MaxHp, int MaxMp, int Str, int Int, int Dex, int Con,
    int Special1, int Special2, int Special3, int Special4,
    int Perforation = 0, int Absorption = 0, int ExpBonus = 0, int DropBonus = 0,
    int HpPercent = 0, int MpPercent = 0, int RegenHp = 0, int RegenMp = 0, int Magic = 0, int Critical = 0,
    int AttackSpeed = 0, int SaveMana = 0, int ResistAll = 0);
