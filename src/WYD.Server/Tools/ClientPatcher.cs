using System.Buffers.Binary;

namespace WYD.Server.Tools;

/// <summary>
/// Correções do WYD.exe 7.54:
/// <list type="bullet">
/// <item>Efeito das armas +13 a +15. O client escolhe o efeito da arma como Grade * 13 + refinação (0x52482A);
/// acima de +12 a conta cai no efeito do Grade seguinte, por isso as armas +13 a +15 (ancient ou não) soltam o
/// efeito errado. A correção troca as quatro somas (mão direita e esquerda, nos dois caminhos) por uma chamada que
/// soma no máximo 12. O nome e a refinação do item continuam os de verdade.</item>
/// <item>Cada item ocupa 1 slot no inventário. O tamanho vem do efeito 33 do item, que escolhe um dos 8 pares
/// largura × altura da tabela em 0x5C88F4 (1×1 a 2×4); o client usa a mesma tabela no ícone, na busca de slot livre
/// e no encaixe. A correção põe 1×1 nos 8.</item>
/// </list>
/// Cada correção só é aplicada se os bytes forem os do client 7.54 conhecido; as que já estão aplicadas ficam.
/// </summary>
public static class ClientPatcher
{
    public enum Result { Patched, AlreadyPatched, Unknown }

    private enum State { Original, Patched, Unknown }

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

    /// <summary>Tabela {int largura, int altura}[8] do tamanho dos itens no inventário (na .data).</summary>
    private const uint GridTable = 0x5C88F4;
    private static readonly (int W, int H)[] OriginalGrid = [(1, 1), (1, 2), (1, 3), (1, 4), (2, 1), (2, 2), (2, 3), (2, 4)];

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

    private static byte[] Grid((int W, int H)[] sizes)
    {
        var bytes = new byte[sizes.Length * 8];
        for (int i = 0; i < sizes.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 8), sizes[i].W);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 8 + 4), sizes[i].H);
        }
        return bytes;
    }

    private static byte[] OneSlotGrid => Grid(Enumerable.Repeat((1, 1), OriginalGrid.Length).ToArray());

    /// <summary>Aplica as correções no conteúdo do WYD.exe.</summary>
    public static Result Patch(byte[] exe)
    {
        if (!TrySection(exe, ".text", out var text) || !TrySection(exe, ".data", out var data)) return Result.Unknown;

        var weapon = WeaponState(exe, text);
        var grid = data.Matches(exe, GridTable, OneSlotGrid) ? State.Patched
            : data.Matches(exe, GridTable, Grid(OriginalGrid)) ? State.Original
            : State.Unknown;
        if (weapon == State.Unknown || grid == State.Unknown) return Result.Unknown;
        if (weapon == State.Patched && grid == State.Patched) return Result.AlreadyPatched;

        if (weapon == State.Original) PatchWeapon(exe, text);
        if (grid == State.Original) OneSlotGrid.CopyTo(exe, data.Offset(GridTable));
        return Result.Patched;
    }

    private static State WeaponState(byte[] exe, Section text)
    {
        if (Sites.All(s => IsCall(exe, text, s.Va, CaveOf(s.Field))) &&
            text.Matches(exe, CaveRight, Cave(0x8C)) && text.Matches(exe, CaveLeft, Cave(0x8D)))
            return State.Patched;
        if (!Sites.All(s => text.Matches(exe, s.Va, Original(s.Field)))) return State.Unknown;

        // O espaço do código novo precisa existir e estar livre.
        uint caveEnd = CaveLeft + (uint)Cave(0x8D).Length;
        if (caveEnd > text.Va + text.RawSize) return State.Unknown;
        foreach (var va in new[] { CaveRight, CaveLeft })
            if (exe.AsSpan(text.Offset(va), Cave(0x8C).Length).ContainsAnyExcept((byte)0))
                return State.Unknown;
        return State.Original;
    }

    private static void PatchWeapon(byte[] exe, Section text)
    {
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
        uint needed = CaveLeft + (uint)Cave(0x8D).Length - text.Va;
        if (text.VirtualSize < needed)
            BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(text.HeaderOffset + 8), needed);
    }

    private static bool IsCall(byte[] exe, Section text, uint va, uint target)
    {
        if (!text.Contains(va, 6) || text.Offset(va) + 6 > exe.Length) return false;
        int o = text.Offset(va);
        return exe[o] == 0xE8 && exe[o + 5] == 0x90 &&
               BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(o + 1)) == (int)(target - (va + 5));
    }

    private readonly record struct Section(int HeaderOffset, uint Va, uint VirtualSize, uint RawSize, uint RawPointer)
    {
        public int Offset(uint va) => (int)(va - Va + RawPointer);

        public bool Contains(uint va, int length) => va >= Va && va + length <= Va + RawSize;

        public bool Matches(byte[] exe, uint va, byte[] bytes) =>
            Contains(va, bytes.Length) && Offset(va) + bytes.Length <= exe.Length &&
            exe.AsSpan(Offset(va), bytes.Length).SequenceEqual(bytes);
    }

    /// <summary>Acha a seção pelo cabeçalho PE (endereços já somados ao ImageBase).</summary>
    private static bool TrySection(byte[] exe, string name, out Section section)
    {
        section = default;
        if (exe.Length < 0x40 || exe[0] != 'M' || exe[1] != 'Z') return false;
        int pe = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x3C));
        if (pe <= 0 || pe + 0x78 > exe.Length || BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(pe)) != 0x4550)
            return false;
        int sections = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe + 6));
        int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe + 20));
        uint imageBase = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(pe + 24 + 28));
        int table = pe + 24 + optionalSize;
        var wanted = System.Text.Encoding.ASCII.GetBytes(name.PadRight(8, '\0'));
        for (int i = 0; i < sections; i++)
        {
            int h = table + i * 40;
            if (h + 40 > exe.Length) return false;
            if (!exe.AsSpan(h, 8).SequenceEqual(wanted)) continue;
            section = new Section(h,
                imageBase + BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(h + 12)),
                BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(h + 8)),
                BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(h + 16)),
                BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(h + 20)));
            return true;
        }
        return false;
    }
}
