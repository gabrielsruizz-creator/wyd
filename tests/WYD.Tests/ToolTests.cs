using System.Buffers.Binary;
using System.Text;
using WYD.Server.Network;
using WYD.Server.Protocol;
using WYD.Server.Tools;

namespace WYD.Tests;

public class ToolTests
{
    private static byte[] EncryptedLogin(KeyTable table, string username, int version, byte keyWord = 77)
    {
        var packet = new PacketWriter(Opcodes.AccountLogin, AccountLoginRequest.PacketSize)
            .WriteString("senha123", 12).WriteString(username, 16).Skip(52).WriteInt32(version).Skip(4 + 16)
            .ToArray();
        PacketCipher.Encrypt(packet, table, keyWord);
        return packet;
    }

    [Fact]
    public void Finds_key_table_inside_a_file()
    {
        var rng = new Random(5);
        var tableBytes = new byte[KeyTable.Length];
        rng.NextBytes(tableBytes);

        var exe = new byte[300_000];
        rng.NextBytes(exe);
        tableBytes.CopyTo(exe, 123_457);

        var login = EncryptedLogin(new KeyTable(tableBytes), "jogador", 754);
        var raw = new byte[4 + login.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, PacketHeader.InitCode);
        login.CopyTo(raw, 4);

        var packet = KeyTableFinder.ExtractFirstPacket(raw)!;
        var match = Assert.Single(KeyTableFinder.Search(exe, [packet]));

        Assert.Equal(123_457, match.Offset);
        Assert.Equal(tableBytes, match.Table);
        Assert.Equal("jogador", match.Username);
        Assert.Equal(754, match.ClientVersion);
    }

    [Fact]
    public void Returns_nothing_when_table_is_absent()
    {
        var rng = new Random(6);
        var tableBytes = new byte[KeyTable.Length];
        rng.NextBytes(tableBytes);
        var exe = new byte[100_000];
        rng.NextBytes(exe);

        Assert.Empty(KeyTableFinder.Search(exe, [EncryptedLogin(new KeyTable(tableBytes), "jogador", 754)]));
    }

    [Fact]
    public void Shifted_false_match_is_removed_by_a_second_login()
    {
        var rng = new Random(8);
        var tableBytes = new byte[KeyTable.Length];
        rng.NextBytes(tableBytes);

        // Força a coincidência: com KeyWord 77, a janela deslocada 10 entradas para a frente começa
        // na mesma chave (incremento 10 + deslocamento 10 = incremento 20 da tabela verdadeira).
        const int shift = 10;
        tableBytes[77 * 2] = 20;
        tableBytes[(77 + shift) * 2] = 20 - shift;

        var exe = new byte[50_000];
        rng.NextBytes(exe);
        tableBytes.CopyTo(exe, 9_000);
        var table = new KeyTable(tableBytes);

        var first = EncryptedLogin(table, "jogador", 754, keyWord: 77);
        var candidates = KeyTableFinder.Search(exe, [first]);
        Assert.Contains(candidates, m => m.Offset == 9_000);
        Assert.Contains(candidates, m => m.Offset == 9_000 + shift * 2);

        var second = EncryptedLogin(table, "jogador", 754, keyWord: 5);
        var remaining = Assert.Single(KeyTableFinder.Filter(candidates, second));
        Assert.Equal(9_000, remaining.Offset);
        Assert.Single(KeyTableFinder.Search(exe, [first, second]));
    }

    /// <summary>Serverlist sintético: 10 grupos x 11 servidores x 64 bytes, codificado com chave que se repete.</summary>
    private static (byte[] Encoded, byte[] Plain) FakeServerList(bool xor, int period)
    {
        var plain = new byte[10 * 11 * 64];
        void Put(int group, int server, string text) =>
            Encoding.ASCII.GetBytes(text).CopyTo(plain, (group * 11 + server) * 64);

        Put(0, 0, "http://wyd.exemplo.com/status");
        Put(0, 1, "200.100.50.25");
        Put(0, 2, "200.100.50.25");
        Put(1, 1, "10.0.0.5");

        var key = Enumerable.Range(0, period).Select(i => (byte)(i * 37 + 11)).ToArray();
        var encoded = plain.Select((b, i) => xor ? (byte)(b ^ key[i % period]) : (byte)(b + key[i % period])).ToArray();
        return (encoded, plain);
    }

    [Theory]
    [InlineData(false, 64)]
    [InlineData(true, 64)]
    [InlineData(false, 5)]
    [InlineData(true, 1)]
    public void Decodes_server_list(bool xor, int period)
    {
        var (encoded, plain) = FakeServerList(xor, period);
        var codec = ServerList.Detect(encoded);

        Assert.Equal(plain, codec.Decode(encoded));
        Assert.Contains(ServerList.Strings(codec.Decode(encoded)), s => s.Text == "200.100.50.25");
    }

    [Fact]
    public void Replaces_ips_and_keeps_other_text()
    {
        var (encoded, _) = FakeServerList(xor: false, period: 64);
        var codec = ServerList.Detect(encoded);
        var plain = codec.Decode(encoded);

        Assert.Equal(3, ServerList.ReplaceIps(plain, "127.0.0.1"));
        var reread = codec.Decode(codec.Encode(plain));
        var texts = ServerList.Strings(reread).Select(s => s.Text).ToList();

        Assert.Equal(["http://wyd.exemplo.com/status", "127.0.0.1", "127.0.0.1", "127.0.0.1"], texts);
        Assert.Equal(0, reread[64 + "127.0.0.1".Length]); // terminado em zero
    }

    [Fact]
    public void Plain_text_server_list_is_read_as_is()
    {
        var (_, plain) = FakeServerList(xor: false, period: 1);
        var codec = ServerList.Detect(plain);
        Assert.Equal(plain, codec.Decode(plain));
    }
}
