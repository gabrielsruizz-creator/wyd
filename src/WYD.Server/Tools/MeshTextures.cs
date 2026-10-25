using System.Text;

namespace WYD.Server.Tools;

/// <summary>
/// Texturas de modelos com nome cortado no client 7.57. O .msa guarda o nome da textura num campo de 11 bytes em
/// 0x20 (lido em 0x4C283C), e o client monta "mesh\nome.wys" (0x4C27DF) e procura esse nome exato na
/// mesh\MeshTextureList.bin (0x4BEFB4: 2048 registros de 0x108 bytes, o nome terminado em zero e o resto 0xCD).
/// Modelos com nome de mais de 10 letras saíram cortados: o aawswordtwo.msa (Hrotti, modelo 2899) diz
/// "ordtwo.tga", o client não acha "mesh\ordtwo.wys" e desenha a arma sem a textura (apagada, sem os brilhos).
/// <para>
/// O client desenha com a textura de número índice + deslocamento (0x4C1077): as armas novas têm logo depois da
/// textura a variação "_b" (aawswordtwo.wys em 1683, aawswordtwo_b.wys em 1684). Por isso a correção troca o nome no
/// próprio registro da textura de verdade (a única da lista cujo nome termina com o nome cortado), para o nome cortado,
/// e copia o arquivo com esse nome; os números não mudam. Se outro modelo usa o nome completo, o nome cortado vai
/// para um registro livre.
/// </para>
/// </summary>
public static class MeshTextures
{
    public const int RecordSize = 0x108, Records = 0x800;
    private const int NameOffset = 0x20, NameLength = 11;
    private const byte Filler = 0xCD;

    /// <summary>Rename: o registro <see cref="Index"/> passa a se chamar <see cref="Missing"/>; senão, registro novo.</summary>
    public sealed record Fix(string Model, string Missing, string Source, int Index, bool Rename);

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
        var all = models.Select(m => (m.Model, Texture: TextureOf(m.Msa))).ToList();
        var used = new HashSet<string>(all.Select(m => m.Texture).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        var fixes = new List<Fix>();
        foreach (var (model, wanted) in all)
        {
            if (wanted is null || known.Contains(wanted)) continue;
            string tail = wanted["mesh\\".Length..];
            var candidates = Enumerable.Range(0, names.Count)
                .Where(i => names[i].Length > tail.Length && names[i].EndsWith(tail, StringComparison.OrdinalIgnoreCase)
                            && names[i].StartsWith("mesh\\", StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count != 1 || !exists(names[candidates[0]])) continue;
            int index = candidates[0];
            fixes.Add(new Fix(model, wanted, names[index], index, Rename: !used.Contains(names[index])));
            known.Add(wanted);
        }
        return fixes;
    }

    /// <summary>Aplica as correções. Retorna false se faltarem registros livres (para as que não trocam o nome).</summary>
    public static bool Apply(byte[] list, IEnumerable<Fix> fixes)
    {
        foreach (var fix in fixes.Where(f => f.Rename)) Write(list, fix.Index, fix.Missing);
        return Add(list, fixes.Where(f => !f.Rename).Select(f => f.Missing));
    }

    /// <summary>Põe os nomes nos primeiros registros livres. Retorna false se não houver registros livres suficientes.</summary>
    public static bool Add(byte[] list, IEnumerable<string> names)
    {
        var free = new Queue<int>(Enumerable.Range(0, Math.Min(Records, list.Length / RecordSize))
            .Where(i => list[i * RecordSize] == 0));
        foreach (var name in names)
        {
            if (!free.TryDequeue(out int i)) return false;
            Write(list, i, name);
        }
        return true;
    }

    private static void Write(byte[] list, int index, string name)
    {
        var r = list.AsSpan(index * RecordSize, RecordSize);
        r.Fill(Filler);
        var bytes = Encoding.Latin1.GetBytes(name);
        bytes.CopyTo(r);
        r[bytes.Length] = 0;
    }
}
