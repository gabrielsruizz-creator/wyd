using System.Buffers.Binary;
using System.Text;

namespace WYD.Server.Data;

/// <summary>Um efeito fixo do item (tipo + valor), como dano, defesa, nível de refino.</summary>
public readonly record struct ItemEffect(short Effect, short Value);

/// <summary>
/// STRUCT_ITEMLIST: definição de um item (140 bytes), igual no client e no servidor.
/// <code>
///   0 char  Name[64]          64 short Mesh            66 short Texture         68 short VisualEffect
///  70 short ReqLevel          72 short ReqStr          74 short ReqInt          76 short ReqDex
///  78 short ReqCon            80 {short Effect, Value}[12]
/// 128 int   Price            132 short Unique         134 short Pos (máscara de onde equipa)
/// 136 short Extreme          138 short Grade
/// </code>
/// </summary>
public sealed record ItemDefinition(
    short Index, string Name, short Mesh, short Texture, short VisualEffect,
    short ReqLevel, short ReqStr, short ReqInt, short ReqDex, short ReqCon,
    ItemEffect[] Effects, int Price, short Unique, short Pos, short Extreme, short Grade)
{
    public const int Size = 140;

    public bool IsEmpty => string.IsNullOrEmpty(Name);

    public static ItemDefinition Read(short index, ReadOnlySpan<byte> span)
    {
        var d = span.ToArray();
        short S(int o) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o));
        var name = d.AsSpan(0, 64);
        int end = name.IndexOf((byte)0);
        var effects = new ItemEffect[12];
        for (int i = 0; i < 12; i++)
            effects[i] = new ItemEffect(S(80 + i * 4), S(82 + i * 4));

        return new ItemDefinition(index, Encoding.Latin1.GetString(end >= 0 ? name[..end] : name),
            S(64), S(66), S(68), S(70), S(72), S(74), S(76), S(78), effects,
            BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(128)), S(132), S(134), S(136), S(138));
    }
}

/// <summary>
/// Lê o ItemList.bin do client 7.57 (formato conferido no WYD.exe, igual ao do 7.54): 6500 itens de 140 bytes com
/// cada byte em XOR 0x5A, seguidos de um int. O client só aceita o arquivo se o checksum dos bytes codificados
/// der <see cref="ClientChecksum"/> (0x53A3CB no exe). O ExtraItem.bin opcional traz registros de 142 bytes
/// (short índice + item em claro) que substituem itens da lista.
/// </summary>
public sealed class ItemList
{
    public const int MaxItems = 6500;
    public const int DataSize = MaxItems * ItemDefinition.Size;
    public const int ClientChecksum = 0x381AF9B;
    public const byte XorKey = 0x5A;

    private readonly ItemDefinition[] _items;

    private ItemList(ItemDefinition[] items, int checksum)
    {
        _items = items;
        Checksum = checksum;
    }

    /// <summary>Checksum calculado do arquivo; diferente de <see cref="ClientChecksum"/> quer dizer que o client recusaria.</summary>
    public int Checksum { get; }
    public bool MatchesClient => Checksum == ClientChecksum;

    public ItemDefinition? this[int index] =>
        index is > 0 and < MaxItems && !_items[index].IsEmpty ? _items[index] : null;

    public IEnumerable<ItemDefinition> All => _items.Where(i => !i.IsEmpty);

    public static ItemList Load(string path, string? extraPath = null)
    {
        var file = File.ReadAllBytes(path);
        if (file.Length < DataSize)
            throw new InvalidDataException($"{path} tem {file.Length} bytes; o esperado são pelo menos {DataSize}.");

        var data = file.AsSpan(0, DataSize).ToArray();
        int checksum = ComputeChecksum(data);
        for (int i = 0; i < data.Length; i++) data[i] ^= XorKey;

        if (extraPath is not null && File.Exists(extraPath))
        {
            var extra = File.ReadAllBytes(extraPath);
            for (int o = 0; o + 2 + ItemDefinition.Size <= extra.Length; o += 2 + ItemDefinition.Size)
            {
                short index = BinaryPrimitives.ReadInt16LittleEndian(extra.AsSpan(o));
                if (index is > 0 and < MaxItems)
                    extra.AsSpan(o + 2, ItemDefinition.Size).CopyTo(data.AsSpan(index * ItemDefinition.Size));
            }
        }

        var items = new ItemDefinition[MaxItems];
        for (short i = 0; i < MaxItems; i++)
            items[i] = ItemDefinition.Read(i, data.AsSpan(i * ItemDefinition.Size, ItemDefinition.Size));
        return new ItemList(items, checksum);
    }

    /// <summary>Ver <see cref="DataChecksum"/>.</summary>
    public static int ComputeChecksum(ReadOnlySpan<byte> data) => DataChecksum.Compute(data);

    /// <summary>
    /// Acerta o checksum do arquivo para o do client (ver <see cref="DataChecksum.Fix"/>) e regrava o int do fim.
    /// Retorna quantos bytes mudaram.
    /// </summary>
    public static int FixChecksum(byte[] file)
    {
        if (file.Length < DataSize + 4) throw new InvalidDataException($"tem {file.Length} bytes; o esperado são {DataSize + 4}.");
        int changed = DataChecksum.Fix(file.AsSpan(0, DataSize), ItemDefinition.Size, ClientChecksum, XorKey);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(DataSize), ComputeChecksum(file.AsSpan(0, DataSize)));
        return changed;
    }

    /// <summary>Gera um ItemList.bin (usado nos testes).</summary>
    public static byte[] Encode(ReadOnlySpan<byte> plain)
    {
        var data = new byte[DataSize + 4];
        plain.CopyTo(data);
        for (int i = 0; i < DataSize; i++) data[i] ^= XorKey;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(DataSize), ComputeChecksum(data.AsSpan(0, DataSize)));
        return data;
    }
}
