using System.Buffers.Binary;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;

namespace WYD.Server.Data;

/// <summary>
/// Experiência acumulada para passar de cada nível: Next[n] é quanto o nível n precisa para virar n+1.
/// A tabela vem do WYD.exe (comando extract-exptable) e fica em data/ExpTable.txt, um número por linha.
/// </summary>
public sealed class ExpTable
{
    /// <summary>Começo da tabela no client 7.54 (500, 1124, 1826, 2610...), usado para achá-la no WYD.exe.</summary>
    private static readonly uint[] Signature = [500, 1124, 1826, 2610];

    public ExpTable(IReadOnlyList<long> next) => Next = next;

    public IReadOnlyList<long> Next { get; }

    /// <summary>Maior nível alcançável com a tabela.</summary>
    public int MaxLevel => Next.Count;

    public static ExpTable Parse(string text) =>
        new(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => long.Parse(l, CultureInfo.InvariantCulture)).ToList());

    /// <summary>Procura a tabela no WYD.exe: a sequência que começa em 500, 1124, 1826, 2610 e cresce até parar.</summary>
    public static ExpTable? FindInClient(byte[] exe)
    {
        for (int o = 0; o + Signature.Length * 4 <= exe.Length; o += 4)
        {
            bool match = true;
            for (int i = 0; i < Signature.Length && match; i++)
                match = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(o + i * 4)) == Signature[i];
            if (!match) continue;

            var values = new List<long>();
            uint previous = 0;
            for (int p = o; p + 4 <= exe.Length; p += 4)
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(p));
                if (v <= previous) break;
                values.Add(v);
                previous = v;
            }
            return new ExpTable(values);
        }
        return null;
    }

    public string Serialize() => string.Join('\n', Next.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "\n";
}

/// <summary>A tabela carregada no servidor, se data/ExpTable.txt existir.</summary>
public sealed class ExpCatalog
{
    public ExpCatalog(IOptions<ServerOptions> options, ILogger<ExpCatalog> logger)
    {
        string path = PathResolver.Resolve(options.Value.ExpTablePath);
        if (!File.Exists(path))
        {
            logger.LogInformation("Sem {Path}: os personagens não sobem de nível. Rode: extract-exptable <WYD.exe>", path);
            return;
        }
        Table = ExpTable.Parse(File.ReadAllText(path));
        logger.LogInformation("Tabela de experiência carregada: {Count} níveis.", Table.MaxLevel);
    }

    public ExpCatalog(ExpTable? table) => Table = table;

    public ExpTable? Table { get; }
}
