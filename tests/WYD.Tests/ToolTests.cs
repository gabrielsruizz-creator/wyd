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
    public void Exp_tables_are_found_in_the_client_and_round_trip()
    {
        var exe = new byte[400];
        new Random(1).NextBytes(exe);
        // Mortal (com valores repetidos no fim, como no client), depois Celestial começando em 0, depois outra coisa.
        uint[] values = [500, 1124, 1826, 2610, 4000, 4000, 9000, 0, 100, 300, 600, 5];
        for (int i = 0; i < values.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(100 + i * 4), values[i]);

        var (mortal, celestial) = WYD.Server.Data.ExpTable.FindInClient(exe)!.Value;
        Assert.Equal(new long[] { 500, 1124, 1826, 2610, 4000, 4000, 9000 }, mortal.Next);
        Assert.Equal(new long[] { 100, 300, 600 }, celestial!.Next);   // quanto cada nível precisa para passar
        Assert.Equal(mortal.Next, WYD.Server.Data.ExpTable.Parse(mortal.Serialize()).Next);
        Assert.Null(WYD.Server.Data.ExpTable.FindInClient(new byte[64]));
    }

    [Fact]
    public void Level_stops_at_the_evolution_cap_and_evolves()
    {
        var c = new WYD.Server.Game.Character { Exp = long.MaxValue };
        var big = new WYD.Server.Data.ExpTable(Enumerable.Range(1, 500).Select(i => (long)i).ToList());

        WYD.Server.Game.Leveling.Apply(c, big);
        Assert.Equal(399, c.BaseScore.Level);                       // Mortal: nível 400 na tela
        Assert.True(WYD.Server.Game.Leveling.CanEvolve(c));

        WYD.Server.Game.Leveling.Evolve(c, WYD.Server.Game.Evolution.Arch);
        Assert.Equal(WYD.Server.Game.Evolution.Arch, c.Evolution);
        Assert.Equal(1, c.Equip[0].Value2);                         // guardado no rosto
        Assert.Equal(0, c.BaseScore.Level);
        Assert.Equal(0, c.Exp);

        c.Evolution = WYD.Server.Game.Evolution.Celestial;
        c.Exp = long.MaxValue;
        WYD.Server.Game.Leveling.Apply(c, big);
        Assert.Equal(199, c.BaseScore.Level);                       // Celestial: nível 200 na tela

        c.Evolution = WYD.Server.Game.Evolution.SubCelestial;
        Assert.False(WYD.Server.Game.Leveling.CanEvolve(c));        // última evolução
    }

    [Fact]
    public void Points_go_to_attributes_and_specials()
    {
        var c = new WYD.Server.Game.Character { ScoreBonus = 2, SpecialBonus = 1 };
        c.BaseScore.MaxHp = 100;

        Assert.Null(WYD.Server.Game.Points.Apply(c, 0, 3));         // CON
        Assert.Equal(1, c.BaseScore.Con);
        Assert.Equal(100, c.BaseScore.MaxHp);                         // a CON entra na ficha atual
        Assert.Equal(100 + WYD.Server.Game.Points.HpPerCon, c.MaxHp);
        Assert.Null(WYD.Server.Game.Points.Apply(c, 0, 0));         // FOR
        Assert.Equal(1, c.BaseScore.Str);
        Assert.NotNull(WYD.Server.Game.Points.Apply(c, 0, 0));      // acabaram os pontos
        Assert.Equal(1, c.BaseScore.Str);

        Assert.Null(WYD.Server.Game.Points.Apply(c, 1, 2));         // especial 3
        Assert.Equal(1, c.BaseScore.Special3);
        Assert.Equal(0, c.SpecialBonus);
        Assert.NotNull(WYD.Server.Game.Points.Apply(c, 0, 9));
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

    [Fact]
    public void Set_level_moves_up_and_down()
    {
        var c = new WYD.Server.Game.Character { Class = WYD.Server.Game.CharacterClass.Foema };
        c.BaseScore.MaxHp = 60; c.BaseScore.MaxMp = 65;
        var table = new WYD.Server.Data.ExpTable([100, 200, 300, 400]);

        WYD.Server.Game.Leveling.SetLevel(c, 3, table);
        Assert.Equal(3, c.BaseScore.Level);
        Assert.Equal(300, c.Exp);                       // começo do nível: não sobe sozinho no próximo kill
        Assert.Equal(66, c.BaseScore.MaxHp);
        Assert.Equal(77, c.BaseScore.Mp);
        Assert.Equal(0, WYD.Server.Game.Leveling.Apply(c, table));

        WYD.Server.Game.Leveling.SetLevel(c, 0, table);
        Assert.Equal(0, c.Exp);
        Assert.Equal(60, c.BaseScore.MaxHp);
        Assert.Equal(0, c.ScoreBonus);
    }

    [Fact]
    public void Run_speed_comes_from_boots_and_mount()
    {
        var c = new WYD.Server.Game.Character();
        c.BaseScore.AttackRun = WYD.Server.Protocol.Score.DefaultAttackRun;
        Assert.Equal(2, WYD.Server.Game.Stats.RunSpeed(c, null));

        // Bota com efeito 29 (velocidade) +2.
        c.Equip[6] = new WYD.Server.Protocol.Item(1150) { Effect1 = 29, Value1 = 2 };
        Assert.Equal(4, WYD.Server.Game.Stats.RunSpeed(c, null));

        // Montaria morta (vida 0) não conta; viva vale a velocidade dela.
        c.Equip[14] = new WYD.Server.Protocol.Item(2370);
        Assert.Equal(4, WYD.Server.Game.Stats.RunSpeed(c, null));
        c.Equip[14] = WYD.Server.Game.Stats.NewMount(2370);
        Assert.Equal(6, WYD.Server.Game.Stats.RunSpeed(c, null));
        c.Equip[14] = WYD.Server.Game.Stats.NewMount(2360);          // porco
        Assert.Equal(4, WYD.Server.Game.Stats.RunSpeed(c, null));

        WYD.Server.Game.Stats.Recalculate(c, null);
        Assert.Equal(4, c.CurrentScore.AttackRun & 0x0F);
        Assert.Equal(2, c.BaseScore.AttackRun & 0x0F);                 // o base não muda
    }

    [Fact]
    public void Unused_mounts_get_life()
    {
        var items = new WYD.Server.Protocol.Item[] { new(2370), new(905), WYD.Server.Game.Stats.NewMount(2380) };
        items[2].Effect1 = 5;                                           // vida 5: já foi usada, não mexe
        Assert.True(WYD.Server.Game.Stats.FixNewMounts(items));
        Assert.Equal(WYD.Server.Game.Stats.DefaultMountHp, WYD.Server.Game.Stats.MountHp(items[0]));
        Assert.Equal(0, items[1].Effect1);
        Assert.Equal(5, WYD.Server.Game.Stats.MountHp(items[2]));
        Assert.False(WYD.Server.Game.Stats.FixNewMounts(items));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(19, 9)]      // só o último dígito vale até 229
    [InlineData(230, 10)]
    [InlineData(234, 11)]
    [InlineData(250, 15)]
    public void Refine_is_read_like_the_client(byte raw, int expected) =>
        Assert.Equal(expected, WYD.Server.Game.Stats.Refine(new WYD.Server.Protocol.Item(905) { Effect1 = 43, Value1 = raw }));

    [Fact]
    public void Equipment_adds_to_the_current_score_with_refine()
    {
        var items = ItemListTests.TestItems();
        var c = new WYD.Server.Game.Character();
        c.BaseScore.Damage = 10;
        c.BaseScore.MaxHp = 100;
        c.Equip[6] = new WYD.Server.Protocol.Item(905) { Effect1 = 43, Value1 = 9, Effect2 = 4, Value2 = 20 }; // dano 25, +9, +20 HP

        WYD.Server.Game.Stats.Recalculate(c, items);
        Assert.Equal(10 + 25 * 19 / 10, c.CurrentScore.Damage);       // +9 multiplica por 1,9
        Assert.Equal(100 + 20 * 19 / 10, c.MaxHp);
        Assert.Equal(10, c.BaseScore.Damage);                         // o base não muda

        c.Equip[6] = new WYD.Server.Protocol.Item(905) { Effect1 = 43, Value1 = 250 };   // +15
        WYD.Server.Game.Stats.Recalculate(c, items);
        Assert.Equal(10 + 25 * 370 / 100, c.CurrentScore.Damage);
    }

    [Fact]
    public void Teleport_list_is_read_and_found_by_position()
    {
        var points = WYD.Server.Game.TeleportTable.Parse([
            "# comentário",
            "2116 2099 2480 1700 500 Azran",
            "2090 2120 2200 2200",
            "lixo",
        ]).ToList();
        Assert.Equal(2, points.Count);
        Assert.Equal("Azran", points[0].Name);
        Assert.Equal(0, points[1].Price);

        var table = new WYD.Server.Game.TeleportTable(points, Path.GetTempFileName());
        Assert.Equal(2480, table.Find(2118, 2101)!.DestX);          // a 2 células do centro
        Assert.Null(table.Find(2130, 2099));
    }

    [Fact]
    public void Perforation_and_absorption_change_the_damage()
    {
        var attacker = new WYD.Server.Protocol.Score { Damage = 200 };
        var target = new WYD.Server.Protocol.Score { Ac = 400 };
        int plain = WYD.Server.Game.Combat.Damage(attacker, target, new Random(7));
        int pierced = WYD.Server.Game.Combat.Damage(attacker, target, new Random(7), perforation: 10);
        int absorbed = WYD.Server.Game.Combat.Damage(attacker, target, new Random(7), absorption: 10);
        Assert.Equal(plain + 400 * 10 / 100 / 4, pierced);         // 10% da defesa ignorada
        Assert.Equal(plain * 90 / 100, absorbed);                    // 10% a menos de dano
    }

    [Fact]
    public void Skill_data_round_trips_and_learning_spends_points()
    {
        var file = WYD.Server.Data.SkillData.Encode([
            new(0, 24, 3, 15, 4, 2),     // TK: Heaven's Dust, 24 pontos
            new(1, 48, 4, 20, 3, 0),
            new(24, 12, 1, 5, 2, 6),     // FM: Magic Arrow
        ]);
        var data = WYD.Server.Data.SkillData.Parse(file);
        Assert.Equal(24, data[0]!.SkillPoint);
        Assert.Equal(15, data[0]!.ManaSpent);
        Assert.Equal(12, data[24]!.SkillPoint);

        var tk = new WYD.Server.Game.Character { Class = WYD.Server.Game.CharacterClass.TransKnight, SkillBonus = 30 };
        Assert.StartsWith("skill 24 não é da sua classe", WYD.Server.Game.Skills.Learn(tk, 5024, null, data));
        Assert.Null(WYD.Server.Game.Skills.Learn(tk, 5000, null, data));
        Assert.Equal(6, tk.SkillBonus);
        Assert.True(WYD.Server.Game.Skills.Knows(tk, 0));
        Assert.Equal(1u, tk.LearnedSkill);
        Assert.StartsWith("Você já sabe", WYD.Server.Game.Skills.Learn(tk, 5000, null, data));
        Assert.StartsWith("skill 1 custa 48 pontos", WYD.Server.Game.Skills.Learn(tk, 5001, null, data));

        var fm = new WYD.Server.Game.Character { Class = WYD.Server.Game.CharacterClass.Foema, SkillBonus = 12 };
        Assert.Null(WYD.Server.Game.Skills.Learn(fm, 5024, null, data));
        Assert.Equal(1u, fm.LearnedSkill);                       // bit 0 da classe (24 % 24)
    }

    [Fact]
    public void Attributes_and_accessories_shape_the_current_score()
    {
        var items = ItemListTests.TestItems();
        var c = new WYD.Server.Game.Character();
        c.Equip[0] = new WYD.Server.Protocol.Item(1);                  // rosto de mortal
        c.BaseScore = new WYD.Server.Protocol.Score { Level = 99, Damage = 10, MaxHp = 1000, MaxMp = 500, Str = 300, Dex = 80, Con = 100, Int = 50, Special1 = 20 };
        c.Equip[8] = new WYD.Server.Protocol.Item(1208) { Effect1 = 45, Value1 = 10 };   // anel com +10% de HP

        WYD.Server.Game.Stats.Recalculate(c, items);
        var s = c.CurrentScore;
        Assert.Equal(10 + 300 / 3 + 80 / 8 + 20 + 99, s.Damage);
        Assert.Equal((1000 + 100 * WYD.Server.Game.Points.HpPerCon) * 110 / 100, s.MaxHp);
        Assert.Equal(500 + 50 * WYD.Server.Game.Points.MpPerInt, s.MaxMp);
    }

    [Fact]
    public void Skill_requirements_are_level_and_tree_specials()
    {
        var items = ItemListTests.TestItems();
        var ht = new WYD.Server.Game.Character { Class = WYD.Server.Game.CharacterClass.Huntress, SkillBonus = 500 };
        ht.BaseScore.Level = 59;                                            // nível 60 na tela
        ht.BaseScore.Int = 5;                                               // INT baixa não importa

        Assert.Equal("Immunity precisa de 90 no Especial 2 (você tem 0).", WYD.Server.Game.Skills.Learn(ht, 5076, items, null));
        ht.BaseScore.Special2 = 90;
        Assert.Null(WYD.Server.Game.Skills.Learn(ht, 5076, items, null));
        Assert.True(WYD.Server.Game.Skills.Knows(ht, 5076 - 5000));
    }
}
