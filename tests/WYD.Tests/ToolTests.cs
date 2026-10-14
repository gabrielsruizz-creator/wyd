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
        var packet = new AccountLoginRequest(username, "senha123", version).Write();
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
    public void Points_ips_and_status_urls_to_new_address()
    {
        var (encoded, _) = FakeServerList(xor: false, period: 64);
        var codec = ServerList.Detect(encoded);
        var plain = codec.Decode(encoded);

        Assert.Equal(4, ServerList.ReplaceAddresses(plain, "127.0.0.1"));
        var reread = codec.Decode(codec.Encode(plain));
        var texts = ServerList.Strings(reread).Select(s => s.Text).ToList();

        Assert.Equal(["http://127.0.0.1/status", "127.0.0.1", "127.0.0.1", "127.0.0.1"], texts);
        Assert.Equal(0, reread[64 + "127.0.0.1".Length]); // terminado em zero
        Assert.Equal(0, reread["http://127.0.0.1/status".Length]); // sobra do endereço antigo apagada

        // Rodar de novo não muda nada, e uma porta diferente de 80 entra no endereço.
        Assert.Equal(4, ServerList.ReplaceAddresses(plain, "127.0.0.1"));
        ServerList.ReplaceAddresses(plain, "10.0.0.2", statusPort: 8080);
        Assert.Contains(ServerList.Strings(plain), s => s.Text == "http://10.0.0.2:8080/status");
    }

    [Fact]
    public async Task Status_server_answers_any_path()
    {
        var logs = new List<string>();
        var server = new StatusHttpServer(0, () => StatusHttpServer.BuildBody(3, 5), logs.Add);
        var listener = server.Start()!;
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource();
        var run = server.RunAsync(listener, cts.Token);

        using var http = new HttpClient();
        var body = await http.GetStringAsync($"http://127.0.0.1:{port}/wyd_br/servers/serv01.asp");

        Assert.Equal("5\n5\n5\n", body);
        Assert.Contains(logs, l => l.Contains("GET /wyd_br/servers/serv01.asp"));
        cts.Cancel();
        await run;
    }

    [Fact]
    public void Plain_text_server_list_is_read_as_is()
    {
        var (_, plain) = FakeServerList(xor: false, period: 1);
        var codec = ServerList.Detect(plain);
        Assert.Equal(plain, codec.Decode(plain));
    }

    [Fact]
    public void Exp_table_is_found_in_the_client_and_round_trips()
    {
        var exe = new byte[400];
        new Random(1).NextBytes(exe);
        uint[] values = [500, 1124, 1826, 2610, 4000, 9000];
        for (int i = 0; i < values.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(100 + i * 4), values[i]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(100 + values.Length * 4), 0); // fim

        var table = WYD.Server.Data.ExpTable.FindInClient(exe)!;
        Assert.Equal(values.Select(v => (long)v), table.Next);
        Assert.Equal(table.Next, WYD.Server.Data.ExpTable.Parse(table.Serialize()).Next);
        Assert.Null(WYD.Server.Data.ExpTable.FindInClient(new byte[64]));
    }

    [Fact]
    public void Leveling_gives_points_life_and_heals()
    {
        var c = new WYD.Server.Game.Character { Class = WYD.Server.Game.CharacterClass.TransKnight, Exp = 150 };
        c.BaseScore.MaxHp = 80; c.BaseScore.Hp = 10;
        var table = new WYD.Server.Data.ExpTable([100, 200]);

        Assert.Equal(1, WYD.Server.Game.Leveling.Apply(c, table));
        Assert.Equal(1, c.BaseScore.Level);
        Assert.Equal(85, c.BaseScore.MaxHp);
        Assert.Equal(85, c.BaseScore.Hp);
        Assert.Equal(WYD.Server.Game.Leveling.ScorePointsPerLevel, c.ScoreBonus);

        c.Exp = 10_000;                                  // no topo da tabela não passa do último nível
        Assert.Equal(1, WYD.Server.Game.Leveling.Apply(c, table));
        Assert.Equal(0, WYD.Server.Game.Leveling.Apply(c, table));
        Assert.Equal(0, WYD.Server.Game.Leveling.Apply(c, null));
    }
}
