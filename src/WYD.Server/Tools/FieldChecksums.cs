using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace WYD.Server.Tools;

/// <summary>
/// O cdata.bin do client guarda o valor esperado de cada pedaço de mapa (Env\FieldXXYY): int[64][32], as linhas
/// 0-31 para o terreno (.trn, linha YY) e as 32-63 para os objetos (.dat, linha YY + 32), na coluna XX. Ao entrar
/// num pedaço, o client soma o arquivo e, se não der o valor da tabela, trata o mapa como adulterado
/// (.trn em 0x53DDFC, .dat em 0x54F4FC). O próprio cdata.bin tem uma soma de conferência (0x53C462), comparada com
/// <see cref="TableSum"/>; o cdata.bin original não dá esse valor (a conferência só roda com o client já no mapa, e
/// o arquivo é lido antes), então a soma é só mantida igual à do arquivo original.
/// <list type="bullet">
/// <item>.trn: 1 byte de tamanho, o nome, 1 byte XX, 1 byte YY e 0xC000 bytes de terreno; vale a soma dos bytes
/// (com sinal) + XX × YY.</item>
/// <item>.dat: objetos de 28 bytes (36 para os tipos 501-506, 511-518 e 520-599, que trazem 8 bytes a mais), até
/// 4096; cada um soma o tipo, as partes inteiras dos floats em 4, 8 e 12 e o int em 24; vale a soma + XX × YY.</item>
/// </list>
/// Para usar mapas de outro client (o Star Destiny troca a conta pela DLL), os valores são recalculados com a conta
/// do nosso client e a soma de conferência é mantida acertando um pedaço que não existe.
/// </summary>
public static partial class FieldChecksums
{
    public const int Rows = 64, Columns = 32, FileSize = Rows * Columns * 4;
    public const uint TableSum = 0x23C16ED4;

    private const int TerrainBytes = 0xC000;
    private const int MaxObjects = 0x1000;

    /// <summary>Known: pedaços que já tinham valor na tabela; Matched: desses, os que bateram com a conta.</summary>
    public sealed record Result(int Files, int Known, int Matched, int[] Table, List<string> ChangedFiles)
    {
        /// <summary>A conta é a do client: a maior parte dos mapas que ele já aceitava bate.</summary>
        public bool Trusted => Known > 0 && Matched * 2 >= Known;
    }

    [GeneratedRegex(@"^Field(\d{2})(\d{2})\.(trn|dat)$", RegexOptions.IgnoreCase)]
    private static partial Regex FieldName();

    public static uint Sum(int[] table)
    {
        uint sum = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                sum = unchecked(sum + (uint)(c * 16 + r * 8) + (uint)table[r * Columns + c] * 4);
        return sum;
    }

    /// <summary>Valor do .trn e a posição que o próprio arquivo diz (XX, YY).</summary>
    public static (int X, int Y, int Value)? Terrain(byte[] file)
    {
        if (file.Length < 1) return null;
        int nameLength = file[0];
        int start = 1 + nameLength + 2;
        if (file.Length < start + TerrainBytes) return null;
        int x = file[1 + nameLength], y = file[2 + nameLength];
        int sum = 0;
        foreach (var b in file.AsSpan(start, TerrainBytes)) sum += (sbyte)b;
        return (x, y, unchecked(sum + x * y));
    }

    public static int Objects(byte[] file, int x, int y)
    {
        int sum = 0, offset = 0;
        for (int n = 0; n < MaxObjects && offset + 28 <= file.Length; n++)
        {
            var r = file.AsSpan(offset);
            int type = BinaryPrimitives.ReadInt32LittleEndian(r);
            unchecked
            {
                sum += type + Truncate(BinaryPrimitives.ReadSingleLittleEndian(r[4..]))
                            + Truncate(BinaryPrimitives.ReadSingleLittleEndian(r[8..]))
                            + Truncate(BinaryPrimitives.ReadSingleLittleEndian(r[12..]))
                            + BinaryPrimitives.ReadInt32LittleEndian(r[24..]);
            }
            offset += 28 + (HasExtra(type) ? 8 : 0);
        }
        return unchecked(sum + x * y);
    }

    /// <summary>Tipos de objeto que trazem 8 bytes a mais (0x54C957 a 0x54E281).</summary>
    public static bool HasExtra(int type) =>
        type is >= 501 and <= 506 or >= 511 and <= 518 or >= 520 and <= 599;

    /// <summary>Parte inteira como o _ftol do client (fora do intervalo de um int, 0x80000000).</summary>
    private static int Truncate(float f) =>
        float.IsNaN(f) || f >= 2147483648f || f < -2147483648f ? int.MinValue : (int)f;

    /// <summary>
    /// Recalcula a tabela para os mapas da pasta Env. Os pedaços cujo valor já bate contam como conferidos (o client
    /// original aceita esses arquivos, então a conta está certa); os outros recebem o valor novo.
    /// </summary>
    public static Result Recompute(int[] table, string envFolder)
    {
        var result = (int[])table.Clone();
        var used = new bool[Rows * Columns];
        int files = 0, known = 0, matched = 0;
        var changed = new List<string>();
        foreach (var path in Directory.EnumerateFiles(envFolder))
        {
            var m = FieldName().Match(Path.GetFileName(path));
            if (!m.Success) continue;
            int x = int.Parse(m.Groups[1].Value), y = int.Parse(m.Groups[2].Value);
            if (x >= Columns || y >= Columns) continue;
            var bytes = File.ReadAllBytes(path);
            bool terrain = m.Groups[3].Value.Equals("trn", StringComparison.OrdinalIgnoreCase);
            int value;
            if (terrain)
            {
                if (Terrain(bytes) is not { } t || t.X != x || t.Y != y) continue;
                value = t.Value;
            }
            else value = Objects(bytes, x, y);

            int cell = (terrain ? y : y + Columns) * Columns + x;
            used[cell] = true;
            files++;
            if (result[cell] != 0) known++;
            if (result[cell] == value) matched++;
            else
            {
                result[cell] = value;
                changed.Add(Path.GetFileName(path));
            }
        }

        // A soma de conferência fica igual à do arquivo original acertando o último pedaço sem arquivo.
        uint target = Sum(table);
        int spare = Array.FindLastIndex(used, u => !u);
        result[spare] = 0;
        uint missing = unchecked(target - Sum(result));
        result[spare] = unchecked((int)(missing / 4));
        return new Result(files, known, matched, result, changed);
    }

    public static int[] Read(byte[] file)
    {
        var table = new int[Rows * Columns];
        for (int i = 0; i < table.Length; i++) table[i] = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(i * 4));
        return table;
    }

    public static byte[] Write(int[] table)
    {
        var file = new byte[FileSize];
        for (int i = 0; i < table.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(i * 4), table[i]);
        return file;
    }
}
