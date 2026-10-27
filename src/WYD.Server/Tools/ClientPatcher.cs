using System.Buffers.Binary;

namespace WYD.Server.Tools;

/// <summary>
/// Correções do WYD.exe (7.57 e 7.54; endereços do 7.54 entre parênteses):
/// <list type="bullet">
/// <item>Efeito das armas +13 a +15. O client escolhe o efeito da arma como Grade * 13 + refinação (0x4FFFAB; 0x52482A);
/// acima de +12 a conta cai no efeito do Grade seguinte, por isso as armas +13 a +15 (ancient ou não) soltam o
/// efeito errado. A correção troca as quatro somas (mão direita e esquerda, nos dois caminhos) por uma chamada que
/// soma no máximo 12. O nome e a refinação do item continuam os de verdade.</item>
/// <item>Só no 7.54: cada item ocupa 1 slot no inventário (no 7.57 a ItemList já traz o efeito 33 zerado em todos os
/// itens). O tamanho vem do efeito 33 do item (0 a 7), que escolhe um dos 8
/// pares largura × altura da tabela em 0x5C88F4 (1×1 a 2×4; ícone, busca de slot livre e encaixe) e uma das 8
/// máscaras de 2 colunas × 4 linhas em 0x5C8934 (quais slots ficam ocupados, no inventário e no baú, 0x55D797).
/// A correção põe 1×1 nos 8 pares e só o primeiro quadrado nas 8 máscaras.</item>
/// </list>
/// Cada correção só é aplicada se os bytes forem os de um client conhecido; as que já estão aplicadas ficam.
/// </summary>
public static class ClientPatcher
{
    public enum Result { Patched, AlreadyPatched, Unknown }

    private enum State { Original, Patched, Unknown }

    /// <summary>
    /// Endereços de uma versão: onde o client soma a refinação ao efeito ("add al, byte ptr [edx + 0x18c|0x18d]", 6
    /// bytes), o código novo no fim da .text (sobra de alinhamento, só zeros), um para cada campo, e se a versão leva
    /// a correção do tamanho dos itens.
    /// </summary>
    private sealed record Profile(string Name, (uint Va, byte Field)[] Sites, uint CaveRight, uint CaveLeft, bool OneSlot);

    private static readonly Profile[] Profiles =
    [
        new("7.57", [(0x4FFFE6, 0x8C), (0x500085, 0x8D), (0x50010D, 0x8C), (0x500190, 0x8D)], 0x5DC590, 0x5DC5B0, OneSlot: false),
        new("7.54", [(0x524865, 0x8C), (0x524904, 0x8D), (0x52498C, 0x8C), (0x524A0F, 0x8D)], 0x5AC840, 0x5AC860, OneSlot: true),
    ];

    private const int MaxRefine = 12;

    /// <summary>Tabela {int largura, int altura}[8] do tamanho dos itens no inventário (na .data).</summary>
    private const uint GridTable = 0x5C88F4;
    private static readonly (int W, int H)[] OriginalGrid = [(1, 1), (1, 2), (1, 3), (1, 4), (2, 1), (2, 2), (2, 3), (2, 4)];

    /// <summary>Máscaras byte[2 colunas × 4 linhas][8] dos slots que cada tamanho ocupa (na .data).</summary>
    private const uint ShapeTable = 0x5C8934;
    private static readonly byte[] OriginalShapes = Convert.FromHexString(
        "0100000000000000" + "0100010000000000" + "0100010001000000" + "0100010001000100" +
        "0101000000000000" + "0101010100000000" + "0101010101010000" + "0101010101010101");
    private static byte[] OneSlotShapes =>
        Enumerable.Range(0, 8).SelectMany(_ => new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }).ToArray();

    /// <summary>Trocas de dados na .data: endereço, bytes do client conhecido, bytes corrigidos.</summary>
    private static (uint Va, byte[] Original, byte[] Patched)[] DataPatches =>
    [
        (GridTable, Grid(OriginalGrid), OneSlotGrid),
        (ShapeTable, OriginalShapes, OneSlotShapes),
    ];

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

    private static uint CaveOf(Profile p, byte field) => field == 0x8C ? p.CaveRight : p.CaveLeft;

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

    /// <summary>
    /// Na abertura o client 7.57 procura uma janela "With Your Destiny" (FindWindowA em 0x54D759) e, se achar, fecha:
    /// só um client por vez. A troca vira o "je" logo depois em "jmp", e o client abre sempre.
    /// </summary>
    private static readonly byte[] SingleInstanceCheck = Convert.FromHexString("6894A260006A00FF1520D35D0085C0");

    /// <summary>Deixa abrir mais de um client no mesmo PC (só o 7.57).</summary>
    public static Result AllowMultipleClients(byte[] exe)
    {
        int at = exe.AsSpan().IndexOf(SingleInstanceCheck);
        if (at < 0) return Result.Unknown;
        int jump = at + SingleInstanceCheck.Length;   // depois do test eax, eax
        if (exe[jump] == 0xEB) return Result.AlreadyPatched;
        if (exe[jump] != 0x74) return Result.Unknown;
        exe[jump] = 0xEB;                         // je +7 -> jmp +7
        return Result.Patched;
    }

    /// <summary>Aplica as correções no conteúdo do WYD.exe (da primeira versão conhecida que bater).</summary>
    public static Result Patch(byte[] exe) => Patch(exe, out _);

    public static Result Patch(byte[] exe, out string? version)
    {
        version = null;
        if (!TrySection(exe, ".text", out var text) || !TrySection(exe, ".data", out var data)) return Result.Unknown;

        foreach (var profile in Profiles)
        {
            var weapon = WeaponState(exe, text, profile);
            if (weapon == State.Unknown) continue;
            var tables = (profile.OneSlot ? DataPatches : []).Select(p =>
                (p.Va, p.Patched, State: data.Matches(exe, p.Va, p.Patched) ? State.Patched
                    : data.Matches(exe, p.Va, p.Original) ? State.Original
                    : State.Unknown)).ToList();
            if (tables.Any(t => t.State == State.Unknown)) return Result.Unknown;
            version = profile.Name;
            if (weapon == State.Patched && tables.All(t => t.State == State.Patched)) return Result.AlreadyPatched;

            if (weapon == State.Original) PatchWeapon(exe, text, profile);
            foreach (var t in tables.Where(t => t.State == State.Original))
                t.Patched.CopyTo(exe, data.Offset(t.Va));
            return Result.Patched;
        }
        return Result.Unknown;
    }

    private static State WeaponState(byte[] exe, Section text, Profile p)
    {
        if (p.Sites.All(s => IsCall(exe, text, s.Va, CaveOf(p, s.Field))) &&
            text.Matches(exe, p.CaveRight, Cave(0x8C)) && text.Matches(exe, p.CaveLeft, Cave(0x8D)))
            return State.Patched;
        if (!p.Sites.All(s => text.Matches(exe, s.Va, Original(s.Field)))) return State.Unknown;

        // O espaço do código novo precisa existir e estar livre.
        uint caveEnd = p.CaveLeft + (uint)Cave(0x8D).Length;
        if (caveEnd > text.Va + text.RawSize) return State.Unknown;
        foreach (var va in new[] { p.CaveRight, p.CaveLeft })
            if (exe.AsSpan(text.Offset(va), Cave(0x8C).Length).ContainsAnyExcept((byte)0))
                return State.Unknown;
        return State.Original;
    }

    private static void PatchWeapon(byte[] exe, Section text, Profile p)
    {
        Cave(0x8C).CopyTo(exe, text.Offset(p.CaveRight));
        Cave(0x8D).CopyTo(exe, text.Offset(p.CaveLeft));
        foreach (var (va, field) in p.Sites)
        {
            int o = text.Offset(va);
            exe[o] = 0xE8; // call rel32
            BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(o + 1), (int)(CaveOf(p, field) - (va + 5)));
            exe[o + 5] = 0x90; // nop
        }

        // O código novo fica além do VirtualSize da .text; aumenta para o loader mapear como código.
        uint needed = p.CaveLeft + (uint)Cave(0x8D).Length - text.Va;
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
