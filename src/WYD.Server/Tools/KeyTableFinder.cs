using System.Buffers.Binary;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Tools;

/// <summary>
/// Acha a KeyTable dentro do executável do client (ou de um dump de memória dele).
/// Testa cada posição do arquivo como início de uma tabela de 512 bytes e fica com a que
/// decifra um pacote de login capturado para algo válido (opcode 0x20D, conta legível, versão plausível).
/// </summary>
public static class KeyTableFinder
{
    public sealed record Match(int Offset, byte[] Table, string Username, int ClientVersion);

    /// <summary>
    /// Procura tabelas que decifram todos os pacotes de login. Um pacote só usa parte da tabela, então
    /// trechos deslocados podem servir por coincidência; cada pacote a mais (com outro KeyWord) elimina esses falsos.
    /// </summary>
    public static List<Match> Search(byte[] file, IReadOnlyList<byte[]> logins)
    {
        var matches = new List<Match>();
        if (logins.Count == 0 || logins.Any(l => l.Length < MinLoginSize))
            return matches;

        var first = logins[0];
        byte keyWord = first[PacketHeader.KeyWordOffset];
        byte c4 = first[4], c5 = first[5];

        for (int offset = 0; offset + KeyTable.Length <= file.Length; offset++)
        {
            // Filtro rápido: decifra só os 2 bytes do opcode, que precisam dar 0x20D.
            int position = file[offset + keyWord * 2];
            int k0 = file[offset + (position & 0xFF) * 2 + 1];
            if ((byte)(c4 - (k0 << 1)) != (Opcodes.AccountLogin & 0xFF)) continue;
            int k1 = file[offset + ((position + 1) & 0xFF) * 2 + 1];
            if ((byte)(c5 + (k1 >> 3)) != (Opcodes.AccountLogin >> 8)) continue;

            var table = file.AsSpan(offset, KeyTable.Length).ToArray();
            if (matches.Any(m => m.Table.AsSpan().SequenceEqual(table))) continue;
            if (TryDecryptAll(table, logins) is { } login)
                matches.Add(new Match(offset, table, login.Username, login.ClientVersion));
        }

        return matches;
    }

    /// <summary>Mantém só os candidatos que também decifram o pacote novo.</summary>
    public static List<Match> Filter(List<Match> candidates, byte[] login) =>
        candidates.Where(m => TryDecryptAll(m.Table, [login]) is not null).ToList();

    /// <summary>Cabeçalho + conta[16] + senha[12]: o mínimo para reconhecer um login.</summary>
    private const int MinLoginSize = PacketHeader.Size + 12 + 16;

    /// <summary>
    /// Decifra todos os pacotes com a tabela. Não exige o tamanho exato de 116 bytes, para funcionar mesmo
    /// que o layout do client seja um pouco diferente do documentado: basta o opcode, o checksum e uma conta legível.
    /// </summary>
    private static AccountLoginRequest? TryDecryptAll(byte[] table, IReadOnlyList<byte[]> logins)
    {
        var keyTable = new KeyTable(table);
        AccountLoginRequest? result = null;
        foreach (var encrypted in logins)
        {
            var packet = (byte[])encrypted.Clone();
            if (!PacketCipher.Decrypt(packet, keyTable) || PacketHeader.ReadType(packet) != Opcodes.AccountLogin) return null;

            // Conta[16] depois da senha[12] (7.57) ou logo depois do cabeçalho (7.54).
            var r = new PacketReader(packet);
            string accountFirst = r.ReadString(16);
            r = new PacketReader(packet);
            r.Skip(12);
            string accountSecond = r.ReadString(16);
            string? username =
                Handlers.LoginHandler.UsernamePattern().IsMatch(accountSecond) ? accountSecond :
                Handlers.LoginHandler.UsernamePattern().IsMatch(accountFirst) ? accountFirst : null;
            if (username is null) return null;

            int version = packet.Length >= AccountLoginRequest.PacketSize ? AccountLoginRequest.Read(new PacketReader(packet)).ClientVersion : 0;
            result = new AccountLoginRequest(username, "", version);
        }
        return result;
    }

    /// <summary>Separa o primeiro pacote dos bytes recebidos, pulando o INITCODE se ele estiver lá.</summary>
    public static byte[]? ExtractFirstPacket(byte[] raw)
    {
        int start = raw.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(raw) == PacketHeader.InitCode ? 4 : 0;
        if (raw.Length < start + PacketHeader.Size) return null;
        int size = PacketHeader.ReadSize(raw.AsSpan(start));
        if (size < PacketHeader.Size || raw.Length < start + size) return null;
        return raw.AsSpan(start, size).ToArray();
    }
}
