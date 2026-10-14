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

    public static int RunSpeed(Character c, ItemList? items)
    {
        var mount = c.Equip[MountSlot];
        if (IsMount(mount.Index) && MountHp(mount) > 0)
            return mount.Index <= LastMount ? MountSpeed[mount.Index - FirstMount] : TimedMountSpeed;

        int speed = c.BaseScore.AttackRun & 0x0F;
        for (int slot = 1; slot < c.Equip.Length; slot++)
        {
            var item = c.Equip[slot];
            if (item.IsEmpty || slot == MountSlot) continue;
            speed += Effect(item, items, RunSpeedEffect);
        }
        return Math.Clamp(speed, 0, MaxRunSpeed);
    }

    /// <summary>Soma do efeito no ItemList.bin e nos efeitos do próprio item.</summary>
    public static int Effect(Item item, ItemList? items, int effect)
    {
        int total = 0;
        if (items?[item.Index] is { } def)
            foreach (var e in def.Effects)
                if (e.Effect == effect) total += e.Value;
        if (item.Effect1 == effect) total += item.Value1;
        if (item.Effect2 == effect) total += item.Value2;
        if (item.Effect3 == effect) total += item.Value3;
        return total;
    }

    /// <summary>Atualiza os atributos calculados do personagem (chamar ao entrar e ao trocar equipamento).</summary>
    public static void Recalculate(Character c, ItemList? items) => c.RunSpeed = (byte)RunSpeed(c, items);
}
