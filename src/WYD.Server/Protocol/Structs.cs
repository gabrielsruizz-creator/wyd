using System.Buffers.Binary;
using WYD.Server.Network;

namespace WYD.Server.Protocol;

public static class GameConstants
{
    public const int NameLength = 16;
    public const int AccountNameLength = 16;
    public const int AccountPasswordLength = 12;
    public const int MessageLength = 96; // o client 7.54 corta o texto em 0x6A (conferido no WYD.exe)
    public const int CharactersPerAccount = 4;
    public const int MaxEquip = 16;
    public const int MaxCarry = 64;
    public const int MaxCargo = 128;
}

/// <summary>STRUCT_ITEM: 8 bytes (índice + 3 pares efeito/valor).</summary>
public struct Item
{
    public const int Size = 8;

    public short Index;
    public byte Effect1, Value1, Effect2, Value2, Effect3, Value3;

    public Item(short index) : this() => Index = index;

    public readonly bool IsEmpty => Index <= 0;

    public readonly void Write(PacketWriter w) =>
        w.WriteInt16(Index)
         .WriteByte(Effect1).WriteByte(Value1)
         .WriteByte(Effect2).WriteByte(Value2)
         .WriteByte(Effect3).WriteByte(Value3);

    public readonly void WriteTo(Span<byte> dest)
    {
        BinaryPrimitives.WriteInt16LittleEndian(dest, Index);
        dest[2] = Effect1; dest[3] = Value1;
        dest[4] = Effect2; dest[5] = Value2;
        dest[6] = Effect3; dest[7] = Value3;
    }

    public static Item ReadFrom(ReadOnlySpan<byte> src) => new()
    {
        Index = BinaryPrimitives.ReadInt16LittleEndian(src),
        Effect1 = src[2], Value1 = src[3],
        Effect2 = src[4], Value2 = src[5],
        Effect3 = src[6], Value3 = src[7],
    };

    /// <summary>Serializa uma lista de itens para guardar no banco (BLOB).</summary>
    public static byte[] ToBlob(Item[] items)
    {
        var blob = new byte[items.Length * Size];
        for (int i = 0; i < items.Length; i++)
            items[i].WriteTo(blob.AsSpan(i * Size));
        return blob;
    }

    public static Item[] FromBlob(byte[]? blob, int count)
    {
        var items = new Item[count];
        if (blob is null) return items;
        for (int i = 0; i < count && (i + 1) * Size <= blob.Length; i++)
            items[i] = ReadFrom(blob.AsSpan(i * Size));
        return items;
    }
}

/// <summary>
/// STRUCT_SCORE do client 7.54: 28 bytes, com os campos em short.
/// Conferido no WYD.exe (cópia de 0x1C bytes por personagem e leituras word/byte nesses offsets).
/// <code>
///  0 short Level      2 short Ac        4 short Damage
///  6 byte  Merchant   7 byte  AttackRun
///  8 short MaxHp     10 short MaxMp    12 short Hp       14 short Mp
/// 16 short Str       18 short Int      20 short Dex      22 short Con
/// 24 byte  Special[4]
/// </code>
/// Na memória os valores são int; ao montar o pacote são limitados ao intervalo de um short.
/// </summary>
public struct Score
{
    public const int Size = 28;

    public int Level;
    public int Ac;
    public int Damage;
    public byte Merchant;
    public byte AttackRun;
    public int MaxHp, MaxMp, Hp, Mp;
    public short Str, Int, Dex, Con;
    public byte Special1, Special2, Special3, Special4;

    private static short Clamp(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    public readonly void Write(PacketWriter w) =>
        w.WriteInt16(Clamp(Level)).WriteInt16(Clamp(Ac)).WriteInt16(Clamp(Damage))
         .WriteByte(Merchant).WriteByte(AttackRun)
         .WriteInt16(Clamp(MaxHp)).WriteInt16(Clamp(MaxMp)).WriteInt16(Clamp(Hp)).WriteInt16(Clamp(Mp))
         .WriteInt16(Str).WriteInt16(Int).WriteInt16(Dex).WriteInt16(Con)
         .WriteByte(Special1).WriteByte(Special2).WriteByte(Special3).WriteByte(Special4);
}
