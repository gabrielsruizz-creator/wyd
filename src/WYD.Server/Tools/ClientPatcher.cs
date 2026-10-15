using System.Buffers.Binary;

namespace WYD.Server.Tools;

/// <summary>
/// Correção do WYD.exe 7.54 para o efeito das armas +13 a +15. O client escolhe o efeito da arma como
/// Grade * 13 + refinação (0x52482A); acima de +12 a conta cai no efeito do Grade seguinte, por isso as armas
/// +13 a +15 (ancient ou não) soltam o efeito errado. A correção troca as quatro somas (mão direita e esquerda, nos
/// dois caminhos) por uma chamada que soma no máximo 12. O nome e a refinação do item continuam os de verdade.
/// </summary>
public static class ClientPatcher
{
    public enum Result { Patched, AlreadyPatched, Unknown }

    /// <summary>Onde o client soma a refinação ao efeito: "add al, byte ptr [edx + 0x18c|0x18d]" (6 bytes).</summary>
    private static readonly (uint Va, byte Field)[] Sites =
    [
        (0x524865, 0x8C),
        (0x524904, 0x8D),
        (0x52498C, 0x8C),
        (0x524A0F, 0x8D),
    ];

    /// <summary>Código novo no fim da .text (sobra de alinhamento, só zeros): um para cada campo.</summary>
    private const uint CaveRight = 0x5AC840;
    private const uint CaveLeft = 0x5AC860;
    private const int MaxRefine = 12;

    private static byte[] Original(byte field) => [0x02, 0x82, field, 0x01, 0x00, 0x00];

    /// <summary>add al, min(byte [edx + 0x18c|0x18d], 12), sem mexer nos outros registradores.</summary>
    private static byte[] Cave(byte field) =>
    [
        0x51,                                     // push ecx
        0x0F, 0xB6, 0x8A, field, 0x01, 0x00, 0x00, // movzx ecx, byte ptr [edx + 0x18c|0x18d]
        0x83, 0xF9, MaxRefine,                    // cmp ecx, 12
        0x76, 0x05,                               // jbe +5
        0xB9, MaxRefine, 0x00, 0x00, 0x00,        // mov ecx, 12
        0x00, 0xC8,                               // add al, cl
        0x59,                                     // pop ecx
        0xC3,                                     // ret
    ];

    private static uint CaveOf(byte field) => field == 0x8C ? CaveRight : CaveLeft;

    /// <summary>Aplica a correção no conteúdo do WYD.exe. Só mexe se os bytes forem os do client 7.54 conhecido.</summary>
    public static Result Patch(byte[] exe)
    {
        if (!TryText(exe, out var text)) return Result.Unknown;

        if (Sites.All(s => IsCall(exe, text, s.Va, CaveOf(s.Field))) && CavesWritten(exe, text))
            return Result.AlreadyPatched;
        if (!Sites.All(s => text.Matches(exe, s.Va, Original(s.Field))))
            return Result.Unknown;

        uint caveEnd = CaveLeft + (uint)Cave(0x8D).Length;
        if (caveEnd > text.Va + text.RawSize) return Result.Unknown;
        foreach (var va in new[] { CaveRight, CaveLeft })
            if (exe.AsSpan(text.Offset(va), Cave(0x8C).Length).ContainsAnyExcept((byte)0))
                return Result.Unknown;

        Cave(0x8C).CopyTo(exe, text.Offset(CaveRight));
        Cave(0x8D).CopyTo(exe, text.Offset(CaveLeft));
        foreach (var (va, field) in Sites)
        {
            int o = text.Offset(va);
            exe[o] = 0xE8; // call rel32
            BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(o + 1), (int)(CaveOf(field) - (va + 5)));
            exe[o + 5] = 0x90; // nop
        }

        // O código novo fica além do VirtualSize da .text; aumenta para o loader mapear como código.
        uint needed = caveEnd - text.Va;
        if (text.VirtualSize < needed)
            BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(text.HeaderOffset + 8), needed);
        return Result.Patched;
    }

    private static bool IsCall(byte[] exe, TextSection text, uint va, uint target)
    {
        if (!text.Contains(va, 6) || text.Offset(va) + 6 > exe.Length) return false;
        int o = text.Offset(va);
        return exe[o] == 0xE8 && exe[o + 5] == 0x90 &&
               BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(o + 1)) == (int)(target - (va + 5));
    }

    private static bool CavesWritten(byte[] exe, TextSection text) =>
        text.Matches(exe, CaveRight, Cave(0x8C)) && text.Matches(exe, CaveLeft, Cave(0x8D));

    private readonly record struct TextSection(int HeaderOffset, uint Va, uint VirtualSize, uint RawSize, uint RawPointer)
    {
        public int Offset(uint va) => (int)(va - Va + RawPointer);

        public bool Contains(uint va, int length) => va >= Va && va + length <= Va + RawSize;

        public bool Matches(byte[] exe, uint va, byte[] bytes) =>
            Contains(va, bytes.Length) && Offset(va) + bytes.Length <= exe.Length &&
            exe.AsSpan(Offset(va), bytes.Length).SequenceEqual(bytes);
    }

    /// <summary>Acha a seção .text pelo cabeçalho PE (endereços já somados ao ImageBase).</summary>
    private static bool TryText(byte[] exe, out TextSection text)
    {
        text = default;
        if (exe.Length < 0x40 || exe[0] != 'M' || exe[1] != 'Z') return false;
        int pe = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x3C));
        if (pe <= 0 || pe + 0x78 > exe.Length || BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(pe)) != 0x4550)
            return false;
        int sections = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe + 6));
        int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe + 20));
        uint imageBase = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(pe + 24 + 28));
        int table = pe + 24 + optionalSize;
        for (int i = 0; i < sections; i++)
        {
            int h = table + i * 40;
            if (h + 40 > exe.Length) return false;
            if (!exe.AsSpan(h, 8).StartsWith(".text"u8)) continue;
            text = new TextSection(h,
                imageBase + BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(h + 12)),
                BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(h + 8)),
                BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(h + 16)),
                BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(h + 20)));
            return true;
        }
        return false;
    }
}
