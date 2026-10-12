namespace WYD.Server.Network;

/// <summary>
/// Tabela de 512 bytes usada pelo client para cifrar os pacotes (g_pKeyTable no servidor original).
/// Ela faz parte do client do jogo, por isso não vem neste repositório: coloque o arquivo em data/KeyTable.bin.
/// Bytes pares guardam o incremento inicial de cada KeyWord; bytes ímpares, a chave de cada posição.
/// </summary>
public sealed class KeyTable
{
    public const int Length = 512;

    private readonly byte[] _table;

    public KeyTable(byte[] table)
    {
        if (table.Length != Length)
            throw new ArgumentException($"A KeyTable precisa ter {Length} bytes (recebido: {table.Length}).", nameof(table));
        _table = (byte[])table.Clone();
    }

    public static KeyTable Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"KeyTable não encontrada em '{Path.GetFullPath(path)}'. Veja data/README.md para saber como obtê-la.", path);
        return new KeyTable(File.ReadAllBytes(path));
    }

    /// <summary>Incremento inicial para um KeyWord (byte par).</summary>
    public byte Increment(int keyWord) => _table[(keyWord & 0xFF) * 2];

    /// <summary>Chave de uma posição do fluxo (byte ímpar).</summary>
    public byte Key(int position) => _table[(position & 0xFF) * 2 + 1];
}
