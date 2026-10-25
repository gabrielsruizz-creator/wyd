using System.Text;

namespace WYD.Server.Tools;

/// <summary>
/// Texturas de modelos com nome cortado no client 7.57. O .msa guarda o nome da textura num campo de 11 bytes em
/// 0x20 (lido em 0x4C283C), e o client monta "mesh\nome.wys" (0x4C27DF) e procura esse nome exato na
/// mesh\MeshTextureList.bin (0x4BEFB4: 2048 registros de 0x108 bytes, o nome terminado em zero e o resto 0xCD).
/// Modelos com nome de mais de 10 letras saíram cortados: o aawswordtwo.msa (Hrotti, modelo 2899) diz
/// "ordtwo.tga", o client não acha "mesh\ordtwo.wys" e desenha a arma sem a textura (apagada, sem os brilhos).
/// A correção põe na lista, num registro livre, o nome cortado apontando para uma cópia da textura de verdade (a
/// única da lista cujo nome termina com o nome cortado).
/// </summary>
public static class MeshTextures
{
    public const int RecordSize = 0x108, Records = 0x800;
    private const int NameOffset = 0x20, NameLength = 11;
    private const byte Filler = 0xCD;

    public sealed record Fix(string Model, string Missing, string Source);

    public static string? TextureOf(byte[] msa)
    {
        if (msa.Length < NameOffset + NameLength) return null;
        var field = msa.AsSpan(NameOffset, NameLength);
        int end = field.IndexOf((byte)0);
        string name = Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
        int dot = name.IndexOf('.');
        if (dot >= 0) name = name[..dot];
        return name.Length == 0 ? null : $"mesh\\{name}.wys";
    }

    public static List<string> Names(byte[] list)
    {
        var names = new List<string>(Records);
        for (int i = 0; i < Records && (i + 1) * RecordSize <= list.Length; i++)
        {
            var r = list.AsSpan(i * RecordSize, RecordSize);
            int end = r.IndexOf((byte)0);
            names.Add(Encoding.Latin1.GetString(end < 0 ? r : r[..end]));
        }
        return names;
    }

    /// <summary>
    /// O que falta para os modelos: para cada textura que o client não acharia, a textura da lista que termina com o
    /// mesmo nome (se for uma só). <paramref name="exists"/> diz se o arquivo da textura de verdade existe.
    /// </summary>
    public static List<Fix> Plan(IEnumerable<(string Model, byte[] Msa)> models, byte[] list, Func<string, bool> exists)
    {
        var names = Names(list);
        var known = new HashSet<string>(names.Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
        var fixes = new List<Fix>();
        foreach (var (model, msa) in models)
        {
            if (TextureOf(msa) is not { } wanted || known.Contains(wanted)) continue;
            string tail = wanted["mesh\\".Length..];
            var candidates = names.Where(n => n.Length > tail.Length && n.EndsWith(tail, StringComparison.OrdinalIgnoreCase)
                                             && n.StartsWith("mesh\\", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            if (candidates.Count != 1 || !exists(candidates[0])) continue;
            fixes.Add(new Fix(model, wanted, candidates[0]));
            known.Add(wanted);
        }
        return fixes;
    }

    /// <summary>Põe os nomes nos primeiros registros livres. Retorna false se não houver registros livres suficientes.</summary>
    public static bool Add(byte[] list, IEnumerable<string> names)
    {
        var free = new Queue<int>(Enumerable.Range(0, Math.Min(Records, list.Length / RecordSize))
            .Where(i => list[i * RecordSize] == 0));
        foreach (var name in names)
        {
            if (!free.TryDequeue(out int i)) return false;
            var r = list.AsSpan(i * RecordSize, RecordSize);
            r.Fill(Filler);
            var bytes = Encoding.Latin1.GetBytes(name);
            bytes.CopyTo(r);
            r[bytes.Length] = 0;
        }
        return true;
    }
}
