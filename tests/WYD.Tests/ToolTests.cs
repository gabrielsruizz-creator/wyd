using System.Buffers.Binary;
using System.Text;
using WYD.Server.Game;
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

    [Fact]
    public void Npc_items_missing_from_the_item_list_are_not_shown()
    {
        var visuals = WYD.Server.Game.NpcVisuals.With([("Rei_Glantuar", 6, (short)905)], ItemListTests.TestItems());
        Assert.Equal(0, visuals.Visible("BlackOracle", 1, 13));            // item 13 não existe: o client fecharia
        Assert.Equal(905, visuals.Visible("Lobo", 6, 905));                // existe: vai igual
        Assert.Equal(905, visuals.Visible("Rei_Glantuar", 6, 999));        // regra do arquivo
        Assert.Equal(0, visuals.Visible("Lobo", 0, 0));
        Assert.Equal(508, visuals.Visible("Guarda", 12, 508));            // medalha no slot da medalha
        Assert.Equal(0, visuals.Visible("Curandeira", 12, 4159));         // traje no slot da medalha: escondido
        Assert.Equal(4159, visuals.Visible("Curandeira", 13, 4159));      // no slot dele vai
        Assert.Equal(13, WYD.Server.Game.NpcVisuals.With([]).Visible("X", 1, 13));   // sem ItemList não dá para saber
    }

    private sealed class FixedRandom(int value) : Random
    {
        public override int Next(int maxValue) => value;
    }

    [Fact]
    public void Refining_uses_the_client_chances_and_fail_bonus()
    {
        var items = ItemListTests.TestItems();
        var armor = new Item(1210);                                                     // grau 5

        Assert.Equal(100, Refining.Chance(armor, items, RefineMaterial.Laktorerium));     // +0 -> +1
        var plus5 = Refining.WithRefine(armor, 5, 0);
        Assert.Equal(5, Stats.Refine(plus5));
        Assert.Equal(56, Refining.Chance(plus5, items, RefineMaterial.Laktorerium));      // tabela do client, grau 5
        Assert.Equal(40, Refining.Chance(plus5, items, RefineMaterial.Oriharukon));

        // Falhou: continua +5, guarda a falha nas dezenas e a próxima chance sobe 3%.
        var (failed, ok) = Refining.Try(plus5, 56, new FixedRandom(99));
        Assert.False(ok);
        Assert.Equal(15, Stats.RawSanc(failed));
        Assert.Equal(5, Stats.Refine(failed));
        Assert.Equal(59, Refining.Chance(failed, items, RefineMaterial.Laktorerium));

        // Passou: +6 e as falhas zeram.
        var (done, success) = Refining.Try(failed, 59, new FixedRandom(0));
        Assert.True(success);
        Assert.Equal(6, Stats.RawSanc(done));

        // Limites: Oriharukon até +6, Laktorerium até +9; item sem grau não refina.
        Assert.Equal(0, Refining.Chance(done, items, RefineMaterial.Oriharukon));
        Assert.Equal(0, Refining.Chance(Refining.WithRefine(armor, 9, 0), items, RefineMaterial.Laktorerium));
        Assert.Equal(0, Refining.Chance(new Item(1208), items, RefineMaterial.Laktorerium));
    }

    [Fact]
    public void Laktorerium_takes_a_plus_10_to_plus_11()
    {
        var items = ItemListTests.TestItems();
        var plus10 = new Item(1210) { Effect1 = Stats.EfSanc, Value1 = 232 };            // +10 com gema de coral
        const RefineMaterial lac = RefineMaterial.Laktorerium;

        Assert.Equal(Refining.HighChance, Refining.Chance(plus10, items, lac, Refining.LaktoreriumPowder));
        Assert.Equal(100, Refining.Chance(plus10, items, lac, Refining.LaktoreriumHundred));
        Assert.Null(Refining.Refusal(10, lac, Refining.LaktoreriumPowder));

        // Falhou: o +10 fica igual (sem contar falha).
        var (same, failed) = Refining.Try(plus10, Refining.HighChance, new FixedRandom(50));
        Assert.False(failed);
        Assert.Equal(232, Stats.RawSanc(same));

        // Passou: +11 e a gema continua.
        var (plus11, ok) = Refining.Try(plus10, 100, new FixedRandom(0));
        Assert.True(ok);
        Assert.Equal(11, Stats.Refine(plus11));
        Assert.Equal(AncientEffect.Perforation, Ancient.GemOf(plus11));

        // O +9 não passa para +10 com Laktorerium; a Laktorerium 100 é só para o +10; o +11 já não sobe.
        Assert.Equal(0, Refining.Chance(Refining.WithRefine(new Item(1210), 9, 0), items, lac, Refining.LaktoreriumPowder));
        Assert.Equal("O Laktorerium não passa do +9 para o +10.", Refining.Refusal(9, lac, Refining.LaktoreriumPowder));
        Assert.Equal(0, Refining.Chance(Refining.WithRefine(new Item(1210), 5, 0), items, lac, Refining.LaktoreriumHundred));
        Assert.Equal("A Laktorerium 100 só funciona em itens +10.", Refining.Refusal(5, lac, Refining.LaktoreriumHundred));
        Assert.Equal(0, Refining.Chance(plus11, items, lac, Refining.LaktoreriumPowder));
        Assert.Equal("O Laktorerium refina só até +11.", Refining.Refusal(11, lac, Refining.LaktoreriumPowder));
    }

    /// <summary>Um "WYD.exe" com o cabeçalho PE e a .text do client 7.54 e as quatro somas no lugar.</summary>
    private static byte[] FakeClient()
    {
        var exe = new byte[0x1D5000];
        exe[0] = (byte)'M';
        exe[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(0x3C), 0x80);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(0x80), 0x4550);
        BinaryPrimitives.WriteUInt16LittleEndian(exe.AsSpan(0x80 + 6), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(exe.AsSpan(0x80 + 20), 0xE0);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(0x80 + 24 + 28), 0x400000);
        int h = 0x80 + 24 + 0xE0;
        ".text"u8.CopyTo(exe.AsSpan(h));
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(h + 8), 0x1AB83E);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(h + 12), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(h + 16), 0x1AC000);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(h + 20), 0x1000);
        foreach (var (va, field) in new[] { (0x524865, 0x8C), (0x524904, 0x8D), (0x52498C, 0x8C), (0x524A0F, 0x8D) })
            new byte[] { 0x02, 0x82, (byte)field, 0x01, 0x00, 0x00 }.CopyTo(exe, va - 0x400000);

        // .data com a tabela de tamanho dos itens no inventário (1x1 a 2x4).
        int d = h + 40;
        ".data"u8.CopyTo(exe.AsSpan(d));
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(d + 8), 0xE09025);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(d + 12), 0x1BB000);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(d + 16), 0x1A000);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(d + 20), 0x1BB000);
        int[] grid = [1, 1, 1, 2, 1, 3, 1, 4, 2, 1, 2, 2, 2, 3, 2, 4];
        for (int i = 0; i < grid.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(GridOffset + i * 4), grid[i]);
        OriginalShapes.CopyTo(exe, ShapeOffset);
        return exe;
    }

    /// <summary>Posição no arquivo da tabela de tamanho dos itens (0x5C88F4 na .data).</summary>
    private const int GridOffset = 0x5C88F4 - 0x5BB000 + 0x1BB000;

    /// <summary>Posição no arquivo das máscaras de slots ocupados (0x5C8934 na .data), 2 colunas × 4 linhas.</summary>
    private const int ShapeOffset = 0x5C8934 - 0x5BB000 + 0x1BB000;

    private static readonly byte[] OriginalShapes = Convert.FromHexString(
        "0100000000000000" + "0100010000000000" + "0100010001000000" + "0100010001000100" +
        "0101000000000000" + "0101010100000000" + "0101010101010000" + "0101010101010101");

    private static int[] GridOf(byte[] exe) =>
        Enumerable.Range(0, 16).Select(i => BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(GridOffset + i * 4))).ToArray();

    [Fact]
    public void Patches_client_weapon_effect_to_cap_refine_at_12()
    {
        var exe = FakeClient();
        Assert.Equal(ClientPatcher.Result.Patched, ClientPatcher.Patch(exe));

        // Cada soma vira "call" para o código novo + nop.
        int site = 0x524865 - 0x400000;
        Assert.Equal(0xE8, exe[site]);
        Assert.Equal(0x90, exe[site + 5]);
        Assert.Equal(0x5AC840 - (0x524865 + 5), BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(site + 1)));
        int left = 0x524A0F - 0x400000;
        Assert.Equal(0x5AC860 - (0x524A0F + 5), BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(left + 1)));

        // O código novo lê o mesmo campo e limita em 12; a .text passa a cobri-lo.
        Assert.Equal(new byte[] { 0x51, 0x0F, 0xB6, 0x8A, 0x8C, 0x01, 0x00, 0x00, 0x83, 0xF9, 12 },
            exe.AsSpan(0x5AC840 - 0x400000, 11).ToArray());
        Assert.Equal(0x8D, exe[0x5AC860 - 0x400000 + 4]);
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(0x80 + 24 + 0xE0 + 8)) >= 0x5AC860 + 22 - 0x401000);

        // Todo item ocupa 1 slot: os 8 tamanhos viram 1x1 e as 8 máscaras marcam só o primeiro quadrado.
        Assert.All(GridOf(exe), v => Assert.Equal(1, v));
        for (int i = 0; i < 8; i++)
            Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }, exe.AsSpan(ShapeOffset + i * 8, 8).ToArray());

        Assert.Equal(ClientPatcher.Result.AlreadyPatched, ClientPatcher.Patch(exe));
    }

    [Fact]
    public void Client_with_only_the_weapon_fix_gets_the_one_slot_inventory()
    {
        var exe = FakeClient();
        ClientPatcher.Patch(exe);
        int[] original = [1, 1, 1, 2, 1, 3, 1, 4, 2, 1, 2, 2, 2, 3, 2, 4];
        for (int i = 0; i < original.Length; i++)                               // como o WYD.exe corrigido antes
            BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(GridOffset + i * 4), original[i]);
        var weaponFixed = exe.AsSpan(0x1000, 0x1AC000).ToArray();

        Assert.Equal(ClientPatcher.Result.Patched, ClientPatcher.Patch(exe));
        Assert.All(GridOf(exe), v => Assert.Equal(1, v));
        Assert.Equal(weaponFixed, exe.AsSpan(0x1000, 0x1AC000).ToArray());       // a .text não mudou de novo

        // Client com a tabela de tamanhos já corrigida, mas não as máscaras (o WYD.exe da versão anterior).
        OriginalShapes.CopyTo(exe, ShapeOffset);
        Assert.Equal(ClientPatcher.Result.Patched, ClientPatcher.Patch(exe));
        Assert.Equal(1, exe[ShapeOffset + 8 * 7]);
        Assert.Equal(0, exe[ShapeOffset + 8 * 7 + 1]);
        Assert.Equal(ClientPatcher.Result.AlreadyPatched, ClientPatcher.Patch(exe));
    }

    [Fact]
    public void Does_not_patch_an_unknown_client()
    {
        var exe = FakeClient();
        exe[0x52498C - 0x400000] = 0x03;
        var before = (byte[])exe.Clone();
        Assert.Equal(ClientPatcher.Result.Unknown, ClientPatcher.Patch(exe));
        Assert.Equal(before, exe);

        // Tabela de tamanho diferente da conhecida: também não mexe em nada.
        var other = FakeClient();
        BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(GridOffset), 3);
        var otherBefore = (byte[])other.Clone();
        Assert.Equal(ClientPatcher.Result.Unknown, ClientPatcher.Patch(other));
        Assert.Equal(otherBefore, other);
        Assert.Equal(ClientPatcher.Result.Unknown, ClientPatcher.Patch(new byte[100]));
    }

    [Fact]
    public void Launcher_list_gets_a_new_version_for_each_publish()
    {
        var (first, v1) = LauncherPatch.Publish(null, "WYD.EXE");
        Assert.Equal(7542, v1);
        Assert.Equal("[WYD]\n7000 notice.txt\n7542 WYD.EXE\n", first);

        // Publicar de novo troca a linha antiga; as outras entradas e o NOTICE ficam.
        var (second, v2) = LauncherPatch.Publish(
            "[WYD]\r\n7000 notice.txt\r\n7542 WYD.EXE serverlist.bin\r\n\r\nNOTICE 598 368 http://x/n.htm\r\n", "wyd.exe");
        Assert.Equal(7543, v2);
        Assert.Equal("[WYD]\n7000 notice.txt\n7542 serverlist.bin\n7543 wyd.exe\nNOTICE 598 368 http://x/n.htm\n", second);
    }

    [Fact]
    public async Task Status_server_hands_patch_files_to_the_launcher()
    {
        string folder = Path.Combine(Path.GetTempPath(), "wyd-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "WYD.EXE"), [1, 2, 3, 4]);
            var logs = new List<string>();
            var server = new StatusHttpServer(0, () => "9\n", logs.Add, folder);
            var listener = server.Start()!;
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            using var cts = new CancellationTokenSource();
            var run = server.RunAsync(listener, cts.Token);

            using var http = new HttpClient();
            string root = $"http://127.0.0.1:{port}";
            // Sem update1.htm na pasta, a lista vazia; o launcher pede sem a "/" do começo, mas o nome é o que conta.
            Assert.Equal(LauncherPatch.DefaultList, await http.GetStringAsync($"{root}/patch/update1.htm"));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await http.GetByteArrayAsync($"{root}/patch/wyd.exe"));
            Assert.Equal("9\n", await http.GetStringAsync($"{root}/serv00.asp"));
            Assert.Equal("9\n", await http.GetStringAsync($"{root}/..%5CWYD.EXE"));
            Assert.Contains(logs, l => l.Contains("baixou WYD.EXE (4 bytes)"));
            cts.Cancel();
            await run;
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Attack_speed_goes_in_the_high_bits_of_attack_run_like_the_client()
    {
        Assert.Equal(0, Stats.AttackSpeed(0x02, 0, 0));
        Assert.Equal(2, Stats.AttackSpeed(0x02, 0, 100));          // DES 100: 20%
        Assert.Equal(4, Stats.AttackSpeed(0x22, 10, 50));          // 20 + 10 + 10 = 40%
        Assert.Equal(15, Stats.AttackSpeed(0x02, 700, 0));         // 700%: o client limita em 150
        Assert.Equal(0, Stats.AttackSpeed(0x02, -30, 20));         // lentidão não deixa negativo

        var c = new WYD.Server.Game.Character { Name = "Teste" };
        c.BaseScore.AttackRun = 0x02;
        c.BaseScore.Dex = 50;
        c.ItemBonus = new ItemBonus(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, AttackSpeed: 700);
        Assert.Equal(0xF2, c.CurrentScore.AttackRun);              // velocidade 15 em cima, corrida 2 embaixo
    }

    [Fact]
    public void Critical_chance_is_four_per_thousand_per_point_and_goes_to_the_client()
    {
        Assert.False(Combat.IsCritical(0, new FixedRandom(0)));
        Assert.True(Combat.IsCritical(25, new FixedRandom(99)));      // 25 = 10%: 0 a 99 de 1000
        Assert.False(Combat.IsCritical(25, new FixedRandom(100)));

        var c = new WYD.Server.Game.Character { Name = "Teste" };
        c.ItemBonus = new ItemBonus(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Critical: 30, SaveMana: 12);
        var score = new PacketReader(ServerPackets.UpdateScore(1, c));
        score.Skip(0x28 - 12);
        Assert.Equal(30, score.ReadByte());
        Assert.Equal(12, score.ReadByte());
    }

    [Fact]
    public void Spirit_change_moves_max_mana_into_max_life_like_the_client()
    {
        // Mortal: mana / 8 + (Especial 3 + nível) / 4; evoluído: mana / 4 + ...; o Celestial soma 400 ao nível.
        Assert.Equal(800 / 8 + (40 + 100) / 4, Affects.SpiritChangeMoved(800, 40, 100, celestial: false, evolved: false));
        Assert.Equal(800 / 4 + (40 + 100) / 4, Affects.SpiritChangeMoved(800, 40, 100, celestial: false, evolved: true));
        Assert.Equal(800 / 4 + (40 + 500) / 4, Affects.SpiritChangeMoved(800, 40, 100, celestial: true, evolved: true));

        var c = new WYD.Server.Game.Character { Name = "Teste" };
        c.BaseScore.MaxHp = 1000;
        c.BaseScore.MaxMp = 800;
        c.BaseScore.Level = 100;
        c.ItemBonus = new ItemBonus(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var before = c.CurrentScore;
        c.Affects.Add(new Affect(AffectType.SpiritChange, 0, 87, DateTime.UtcNow.AddMinutes(1)));
        var after = c.CurrentScore;
        int moved = before.MaxMp / 8 + (before.Special3 + before.Level) / 4;
        Assert.Equal(before.MaxHp + moved + Affects.SpiritChangeHp, after.MaxHp);
        Assert.Equal(before.MaxMp - moved, after.MaxMp);
    }

    [Fact]
    public void Sephira_skills_use_the_high_bits_and_the_soul_raises_the_chosen_stats()
    {
        var c = new WYD.Server.Game.Character { Name = "Teste" };
        Assert.Equal(25, Skills.Bit(97));                              // Conjure Cannon
        Assert.Equal(30, Skills.Bit(Skills.SoulOfLimits));
        Assert.Equal(1, Skills.Bit(25));                               // skill de classe: n % 24
        Assert.Equal(97, Skills.SephiraOfUsable(32));                  // item 667
        Assert.Equal(101, Skills.SephiraOfUsable(36));                 // item 671
        Assert.Null(Skills.SephiraOfUsable(37));

        Skills.Teach(c, 100);                                          // Concentration
        Assert.True(Skills.Knows(c, 100));
        Assert.True(SkillUse.CanUse(c, 100));                          // qualquer classe
        Assert.False(SkillUse.CanUse(c, 101));

        // Soul do Mortal na combinação 1 (FOR +40%, CON +30%) enquanto o buff dura.
        c.BaseScore.Str = 100;
        c.BaseScore.Con = 50;
        c.BaseScore.Int = 70;
        c.ItemBonus = new ItemBonus(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Skills.Teach(c, Skills.SoulOfLimits);
        Assert.Equal(100, c.CurrentScore.Str);                         // sem o buff, nada muda
        c.Affects.Add(new Affect(AffectType.WeaponMastery, 0, Skills.SoulOfLimits, DateTime.UtcNow + Soul.Duration));
        Assert.Equal(140, c.CurrentScore.Str);
        Assert.Equal(65, c.CurrentScore.Con);
        Assert.Equal(70, c.CurrentScore.Int);
        Assert.Equal(0, Affects.Bonus(c).Damage);                      // não soma o dano da Maestria em Armas

        c.SoulCombo = 8;                                               // INT +60%
        Assert.Equal(112, c.CurrentScore.Int);
        Assert.Equal("INT +120%", Soul.Describe(8, celestial: true));
        Assert.Equal("FOR +40% e CON +30%", Soul.Describe(1, celestial: false));
    }

    [Fact]
    public void Odin_kibita_and_ehre_make_the_soul_of_the_mortal()
    {
        var c = new WYD.Server.Game.Character { Name = "Teste", Class = CharacterClass.Huntress, Coin = 3_000_000 };
        c.Equip[0] = new Item(31);                                     // rosto de HT Mortal
        c.Equip[Kingdoms.CapeSlot] = new Item(545);                    // capa azul
        c.BaseScore.Level = 370;                                       // nível 371 na tela

        // Odin: falta runa.
        foreach (var (rune, slot) in SoulQuest.Runes[SoulQuest.WindStone].Skip(1).Select((r, k) => (r, k)))
            c.Inventory[slot] = new Item(rune);
        var (refusal, _) = SoulQuest.MakeStone(c, 63);
        Assert.Contains("5122", refusal);
        c.Inventory[10] = new Item(5122);
        var (ok, changed) = SoulQuest.MakeStone(c, 63);
        Assert.Null(ok);
        Assert.Equal(1_000_000, c.Coin);
        Assert.Contains(c.Inventory, i => i.Index == SoulQuest.WindStone);
        Assert.DoesNotContain(c.Inventory, i => i.Index == 5122);

        // Kibita: Soul, Manto do Herói azul e a capa antiga no inventário.
        var (given, _) = SoulQuest.GiveSoul(c, 63);
        Assert.Null(given);
        Assert.True(Skills.Knows(c, Skills.SoulOfLimits));
        Assert.Equal(3194, c.Equip[Kingdoms.CapeSlot].Index);
        Assert.Contains(c.Inventory, i => i.Index == 545);
        Assert.DoesNotContain(c.Inventory, i => i.Index == SoulQuest.WindStone);
        Assert.NotNull(SoulQuest.GiveSoul(c, 63).Refusal);              // já tem

        // Arch: qualquer nível, com a pedra.
        var arch = new WYD.Server.Game.Character { Name = "Arch", Class = CharacterClass.Huntress, Evolution = Evolution.Arch };
        arch.Inventory[0] = new Item(SoulQuest.WindStone);
        Assert.Null(SoulQuest.GiveSoul(arch, 63).Refusal);
        Assert.True(Skills.Knows(arch, Skills.SoulOfLimits));
        Assert.Equal(3196, arch.Equip[Kingdoms.CapeSlot].Index);       // sem capa: o manto neutro

        // Mortal abaixo do 371 não recebe; Celestial já nasce com ela.
        var low = new WYD.Server.Game.Character { Name = "Baixo", Class = CharacterClass.Huntress };
        low.Inventory[0] = new Item(SoulQuest.WindStone);
        Assert.Contains("371", SoulQuest.GiveSoul(low, 63).Refusal);
        var cel = new WYD.Server.Game.Character { Name = "Cel" };
        Leveling.Evolve(cel, Evolution.Celestial);
        Assert.True(Skills.Knows(cel, Skills.SoulOfLimits));

        // Ehre: Esmeralda, Diamante, Coral na janela = INT (combinação 8).
        c.Inventory[40] = new Item(2442);
        c.Inventory[41] = new Item(2441);
        c.Inventory[42] = new Item(2443);
        var (set, combo, used) = SoulQuest.Configure(c, [40, 41, 42]);
        Assert.Null(set);
        Assert.Equal(8, combo);
        Assert.Equal(8, c.SoulCombo);
        Assert.Equal(3, used.Count);
        Assert.True(c.Inventory[40].IsEmpty && c.Inventory[41].IsEmpty && c.Inventory[42].IsEmpty);

        // Receita que não existe, ou item que não é joia: nada é gasto.
        c.Inventory[43] = new Item(2444);
        c.Inventory[44] = new Item(2444);
        c.Inventory[45] = new Item(2442);
        Assert.NotNull(SoulQuest.Configure(c, [43, 44, 45]).Refusal);
        Assert.NotNull(SoulQuest.Configure(c, [43, 44, 10]).Refusal);
        Assert.Equal(2444, c.Inventory[43].Index);

        // Três Diamantes empilhados num slot = Força e Cons (1); um slot com 1 só não vale por 3.
        c.Inventory[50] = new Item(2441) { Effect1 = 61, Value1 = 5 };
        Assert.Equal(1, SoulQuest.Configure(c, [50, 50, 50]).Combo);
        Assert.Equal(2, c.Inventory[50].Value1);
    }

    [Fact]
    public void Thorn_wall_stands_across_the_caster_direction()
    {
        // Para o norte: muro na horizontal; longe demais: no lugar de quem lança.
        Assert.Equal([(99, 90), (100, 90), (101, 90)], ThornWall.Cells(100, 95, 100, 90).Select(c => ((int)c.X, (int)c.Y)));
        Assert.Equal([(99, 100), (100, 100), (101, 100)], ThornWall.Cells(100, 100, 100, 120).Select(c => ((int)c.X, (int)c.Y)));
        // Na diagonal: a outra diagonal.
        Assert.Equal([(106, 104), (105, 105), (104, 106)], ThornWall.Cells(100, 100, 105, 105).Select(c => ((int)c.X, (int)c.Y)));

        var caster = new WYD.Server.Game.Character { Name = "Alice" };
        caster.BaseScore.Level = 100;
        var t = ThornWall.Template(caster);
        Assert.Equal(2300, t.Score.MaxHp);
        Assert.Equal(ThornWall.Model, t.Model);
        Assert.Equal(0, t.Exp);
    }

    [Fact]
    public void Import_itemlist_finds_a_repeating_key_and_writes_a_list_the_client_accepts()
    {
        // 1000 itens com nome e efeitos, cifrados como no Star Destiny: XOR com um texto de 512 bytes que se repete.
        var plain = new byte[WYD.Server.Data.ItemList.DataSize];
        for (int i = 1; i <= 1000; i++)
        {
            System.Text.Encoding.Latin1.GetBytes($"Item_{i}").CopyTo(plain, i * 140);
            plain[i * 140 + 80] = 2;                                   // efeito 2 (dano)
            plain[i * 140 + 82] = (byte)(i % 100);
        }
        var key = new byte[512];
        new Random(7).NextBytes(key);
        var star = plain.Select((b, i) => (byte)(b ^ key[i % 512])).ToArray();

        var result = WYD.Server.Tools.ItemListImport.Convert(star)!;
        Assert.Equal(512, result.KeyLength);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, result.File);
            var list = WYD.Server.Data.ItemList.Load(path);
            Assert.True(list.MatchesClient);                           // o client 7.54 aceita
            Assert.Equal(1000, list.All.Count());                      // os itens vazios ajustados continuam vazios
            Assert.Equal("Item_777", list[777]!.Name);
            Assert.Equal(77, list[777]!.Effects[0].Value);
        }
        finally
        {
            File.Delete(path);
        }

        // Uma lista já no formato do 7.54 passa direto (chave de 1 byte).
        Assert.Equal(1, WYD.Server.Tools.ItemListImport.Convert(WYD.Server.Data.ItemList.Encode(plain))!.KeyLength);
    }

    [Fact]
    public void Import_client_copies_new_and_changed_assets_and_fixes_signatures()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string star = Path.Combine(root, "star"), ours = Path.Combine(root, "ours"), backup = Path.Combine(root, "bak");
            void Put(string dir, string rel, byte[] data)
            {
                var path = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }
            byte[] tga = [0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0x80, 0, 24, 0];
            byte[] Sig(string sig, byte[] rest) => System.Text.Encoding.ASCII.GetBytes(sig).Concat(rest).ToArray();

            Put(star, "Mesh/novo.msh", [1, 2, 3]);                                         // novo
            Put(star, "mesh/igual.msh", [9]); Put(ours, "Mesh/igual.msh", [9]);            // igual (sem diferença de maiúsculas)
            Put(star, "Mesh/mudou.wys", Sig("Star ", [7, 7])); Put(ours, "Mesh/mudou.wys", Sig("WS10 ", [1]));
            Put(star, "UI/m0123.wyt", Sig("star", tga[4..]));                               // imagem nova com outra assinatura
            Put(star, "UI/logo1.wyt", [5]); Put(ours, "UI/logo1.wyt", [6]);                 // logo do servidor fica
            Put(star, "itemhelp.dat", [4]);
            Put(star, "Star.exe", [0]); Put(star, "Shader/skinmesh1.bin", [0]);           // só do Star: não vem

            var report = WYD.Server.Tools.ClientImport.Run(star, ours, backup);

            Assert.Equal(3, report.Added);
            Assert.Equal(1, report.Replaced);
            Assert.Equal(1, report.Unchanged);
            Assert.Equal(2, report.Fixed);
            Assert.Equal(Sig("WS10 ", [7, 7]), File.ReadAllBytes(Path.Combine(ours, "Mesh/mudou.wys")));
            Assert.Equal(Sig("WS10 ", [1]), File.ReadAllBytes(Path.Combine(backup, "Mesh/mudou.wys")));
            Assert.Equal("WT10"u8.ToArray(), File.ReadAllBytes(Path.Combine(ours, "UI/m0123.wyt"))[..4]);
            Assert.Equal([6], File.ReadAllBytes(Path.Combine(ours, "UI/logo1.wyt")));
            Assert.True(File.Exists(Path.Combine(ours, "itemhelp.dat")));
            Assert.False(File.Exists(Path.Combine(ours, "Star.exe")));
            Assert.False(Directory.Exists(Path.Combine(ours, "Shader")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
