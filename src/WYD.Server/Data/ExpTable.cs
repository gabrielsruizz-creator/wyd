using System.Buffers.Binary;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;
using WYD.Server.Game;

namespace WYD.Server.Data;

/// <summary>
/// Experiência acumulada para passar de cada nível: Next[n] é quanto o nível n precisa para virar n+1.
/// As tabelas vêm do WYD.exe (comando extract-exptable) e ficam em data/, um número por linha.
/// </summary>
public sealed class ExpTable
{
    /// <summary>Começo da tabela de Mortal/Arch no client 7.54 (500, 1124, 1826, 2610...), usado para achá-la no WYD.exe.</summary>
    private static readonly uint[] Signature = [500, 1124, 1826, 2610];

    public ExpTable(IReadOnlyList<long> next) => Next = next;

    public IReadOnlyList<long> Next { get; }

    /// <summary>Maior nível alcançável com a tabela.</summary>
    public int MaxLevel => Next.Count;

    public static ExpTable Parse(string text) =>
        new(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => long.Parse(l, CultureInfo.InvariantCulture)).ToList());

    /// <summary>
    /// Procura as tabelas no WYD.exe. A de Mortal/Arch começa em 500, 1124, 1826, 2610 e não diminui até o
    /// nível 400; logo depois vem a de Celestial/Sub Celestial, que começa em 0 (a experiência no início de
    /// cada nível; aqui ela é guardada como as outras, com o quanto cada nível precisa para passar).
    /// </summary>
    public static (ExpTable Mortal, ExpTable? Celestial)? FindInClient(byte[] exe)
    {
        for (int o = 0; o + Signature.Length * 4 <= exe.Length; o += 4)
        {
            bool match = true;
            for (int i = 0; i < Signature.Length && match; i++)
                match = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(o + i * 4)) == Signature[i];
            if (!match) continue;

            var mortal = ReadRising(exe, o);
            var celestial = ReadRising(exe, o + mortal.Count * 4);
            return (new ExpTable(mortal),
                celestial.Count > 1 && celestial[0] == 0 ? new ExpTable(celestial.Skip(1).ToList()) : null);
        }
        return null;
    }

    /// <summary>Números de 32 bits a partir de <paramref name="offset"/> enquanto não diminuem.</summary>
    private static List<long> ReadRising(byte[] exe, int offset)
    {
        var values = new List<long>();
        for (int p = offset; p + 4 <= exe.Length; p += 4)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(p));
            if (values.Count > 0 && v < values[^1]) break;
            values.Add(v);
        }
        return values;
    }

    public string Serialize() => string.Join('\n', Next.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "\n";
}

/// <summary>As tabelas carregadas no servidor, se os arquivos existirem.</summary>
public sealed class ExpCatalog
{
    public ExpCatalog(IOptions<ServerOptions> options, ILogger<ExpCatalog> logger)
    {
        Table = Load(options.Value.ExpTablePath, logger);
        Celestial = Load(options.Value.CelestialExpTablePath, logger);
        if (Table is not null)
            logger.LogInformation("Tabelas de experiência: Mortal/Arch com {Mortal} níveis, Celestial com {Celestial}.",
                Table.MaxLevel, Celestial?.MaxLevel ?? 0);
    }

    public ExpCatalog(ExpTable? table, ExpTable? celestial = null)
    {
        Table = table;
        Celestial = celestial;
    }

    private static ExpTable? Load(string configured, ILogger logger)
    {
        string path = PathResolver.Resolve(configured);
        if (File.Exists(path)) return ExpTable.Parse(File.ReadAllText(path));
        logger.LogInformation("Sem {Path}: rode extract-exptable <WYD.exe> para os personagens subirem de nível.", path);
        return null;
    }

    /// <summary>Tabela de Mortal e Arch.</summary>
    public ExpTable? Table { get; }

    /// <summary>Tabela de Celestial e Sub Celestial.</summary>
    public ExpTable? Celestial { get; }

    public ExpTable? For(Evolution evolution) => evolution >= Evolution.Celestial ? Celestial : Table;
}
