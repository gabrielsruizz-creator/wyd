using System.Buffers.Binary;
using System.Text;
using WYD.Server.Protocol;

namespace WYD.Server.Data;

/// <summary>
/// Um arquivo da pasta npc do servidor: a STRUCT_MOB de 816 bytes do servidor 7.56 (os bytes 0xCC são
/// sobras de memória do programa original e são ignorados). Layout conferido com o ItemList.bin do 7.54:
/// o Equip[0] de "Lobo" é 299 (Wolf), o de "Javali" é 216 (Wild_Boar).
/// <code>
/// 0x000 char Name[16]     0x010 byte Clan        0x011 byte Merchant      0x012 ushort Guild
/// 0x014 byte Class        0x01C int  Coin        0x020 long Exp           0x028 short SPX, SPY
/// 0x02C SCORE BaseScore (48 bytes: int Level, Ac, Damage; byte Merchant, AttackRun, Direction, ChaosRate;
///       int MaxHp, MaxMp, Hp, Mp; short Str, Int, Dex, Con; ushort Special[4])
/// 0x05C SCORE CurrentScore   0x08C ITEM Equip[16]   0x10C ITEM Carry[64] (itens que pode dropar)
/// </code>
/// </summary>
public sealed record NpcTemplate(
    string Name, byte Clan, byte Merchant, ushort Guild, byte Class, int Coin, long Exp,
    Score Score, Item[] Equip, Item[] Carry)
{
    public const int FileSize = 816;

    /// <summary>Índice do modelo (Equip[0]) no ItemList.bin.</summary>
    public short Model => Equip[0].Index;

    public static NpcTemplate Parse(string fileName, byte[] d)
    {
        if (d.Length != FileSize)
            throw new InvalidDataException($"{fileName}: {d.Length} bytes (esperado {FileSize}).");

        int I32(int o) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o));
        short I16(int o) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o));

        var nameBytes = d.AsSpan(0, 16);
        int end = nameBytes.IndexOf((byte)0);
        string name = Encoding.Latin1.GetString(end >= 0 ? nameBytes[..end] : nameBytes);

        const int s = 0x2C; // BaseScore
        var score = new Score
        {
            Level = I32(s),
            Ac = I32(s + 4),
            Damage = I32(s + 8),
            Merchant = d[s + 12],
            AttackRun = d[s + 13],
            MaxHp = I32(s + 16),
            MaxMp = I32(s + 20),
            Hp = I32(s + 16),
            Mp = I32(s + 20),
            Str = I16(s + 32),
            Int = I16(s + 34),
            Dex = I16(s + 36),
            Con = I16(s + 38),
        };

        return new NpcTemplate(
            string.IsNullOrEmpty(name) ? fileName : name,
            d[0x10], d[0x11], BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(0x12)), d[0x14],
            I32(0x1C), BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(0x20)), score,
            Item.FromBlob(d.AsSpan(0x8C, GameConstants.MaxEquip * Item.Size).ToArray(), GameConstants.MaxEquip),
            Item.FromBlob(d.AsSpan(0x10C, GameConstants.MaxCarry * Item.Size).ToArray(), GameConstants.MaxCarry));
    }
}
