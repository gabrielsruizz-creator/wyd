using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WYD.Server;
using WYD.Server.Database;
using WYD.Server.Game;
using WYD.Server.Handlers;
using WYD.Server.Network;
using WYD.Server.Protocol;
using WYD.Server.Security;

namespace WYD.Tests;

/// <summary>Sobe o servidor de verdade (TCP, porta aleatória) com repositórios em memória e conversa com ele como um client.</summary>
public sealed class ServerFlowTests : IAsyncLifetime
{
    private readonly KeyTable _table = ProtocolTests.RandomKeyTable(99);
    private readonly InMemoryStore _store = new();
    private IHost _host = null!;
    private IPEndPoint _endPoint = null!;

    public async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Server:BindAddress"] = "127.0.0.1",
            ["Server:Port"] = "0",
            ["Server:AutoRegister"] = "false",
            ["Server:StrictChecksum"] = "true",
            ["Server:StatusPort"] = "0",
            ["Server:RespawnSeconds"] = "1",
            ["Server:TeleportsPath"] = Path.Combine(Path.GetTempPath(), $"wyd-teleports-{Guid.NewGuid():N}.txt"),
            ["Server:KingdomPointsPath"] = Path.Combine(Path.GetTempPath(), $"wyd-kingdoms-{Guid.NewGuid():N}.txt"),
        });
        builder.Services.AddWydServer(builder.Configuration);
        builder.Services.AddSingleton(_table);
        builder.Services.AddSingleton<IAccountRepository>(_store);
        builder.Services.AddSingleton<ICharacterRepository>(_store);
        builder.Services.AddSingleton(new WYD.Server.Data.ItemCatalog(ItemListTests.TestItems()));
        builder.Services.AddSingleton(new WYD.Server.Data.ExpCatalog(new WYD.Server.Data.ExpTable([100, 200, 300, 400])));

        _host = builder.Build();
        await _host.StartAsync();
        _endPoint = await _host.Services.GetRequiredService<GameServer>().Started;

        await _store.CreateAsync("jogador", PasswordHasher.Hash("senha123"));
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Login_create_and_delete_character()
    {
        using var client = await FakeClient.ConnectAsync(_endPoint, _table);

        await client.SendAsync(LoginPacket("jogador", "senha123", 756));
        var loginOk = await client.ReceiveAsync();
        Assert.Equal(Opcodes.AccountLoginOk, loginOk.Type);
        Assert.Equal(ServerPackets.AccountLoginOkSize, loginOk.Length);
        await client.SendAsync(new SecondPasswordRequest("1234", false).Write());
        Assert.Equal(Opcodes.SecondPassword, (await client.ReceiveAsync()).Type);

        await client.SendAsync(new PacketWriter(Opcodes.CreateCharacter, CreateCharacterRequest.PacketSize)
            .WriteInt32(1).WriteString("Arqueira", 16).WriteInt32((int)CharacterClass.Huntress).ToArray());
        var created = await client.ReceiveAsync();
        Assert.Equal(Opcodes.CreateCharacterOk, created.Type);
        created.Skip(16);
        Assert.Equal("", created.ReadString(16));
        Assert.Equal("Arqueira", created.ReadString(16));

        // Nome repetido é recusado.
        await client.SendAsync(new PacketWriter(Opcodes.CreateCharacter, CreateCharacterRequest.PacketSize)
            .WriteInt32(2).WriteString("arqueira", 16).WriteInt32(0).ToArray());
        var duplicated = await client.ReceiveAsync();
        Assert.Equal(Opcodes.MessagePanel, duplicated.Type);
        Assert.Equal("Este nome já está em uso.", duplicated.ReadString(GameConstants.MessageLength));
        Assert.Equal(Opcodes.CreateCharacterFail, (await client.ReceiveAsync()).Type);

        // Apagar exige a senha da conta.
        await client.SendAsync(DeletePacket(1, "Arqueira", "errada"));
        Assert.Equal("Senha incorreta.", (await client.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        Assert.Equal(Opcodes.DeleteCharacterFail, (await client.ReceiveAsync()).Type);

        await client.SendAsync(DeletePacket(1, "Arqueira", "senha123"));
        var deleted = await client.ReceiveAsync();
        Assert.Equal(Opcodes.DeleteCharacterOk, deleted.Type);
        Assert.Empty(await _store.ListByAccountAsync(1));
    }

    [Fact]
    public async Task Enter_world_with_created_character()
    {
        using var client = await FakeClient.ConnectAsync(_endPoint, _table);
        await LoginAndUnlockAsync(client);

        // Slot vazio: o client recebe o sinal de falha.
        await client.SendAsync(new PacketWriter(Opcodes.CharacterLogin, CharacterLoginRequest.PacketSize).WriteInt32(0).Skip(20).ToArray());
        Assert.Equal(Opcodes.CharacterLoginFail, (await client.ReceiveAsync()).Type);

        await client.SendAsync(new PacketWriter(Opcodes.CreateCharacter, CreateCharacterRequest.PacketSize)
            .WriteInt32(0).WriteString("Cavaleiro", 16).WriteInt32(0).ToArray());
        Assert.Equal(Opcodes.CreateCharacterOk, (await client.ReceiveAsync()).Type);

        await client.SendAsync(new PacketWriter(Opcodes.CharacterLogin, CharacterLoginRequest.PacketSize).WriteInt32(0).Skip(20).ToArray());
        var world = await client.ReceiveAsync();
        Assert.Equal(Opcodes.CharacterLoginOk, world.Type);
        Assert.Equal(ServerPackets.CharacterLoginOkSize, world.Length);
        world.Skip(4);
        Assert.Equal("Cavaleiro", world.ReadString(16));
    }

    [Fact]
    public async Task Wrong_password_is_refused()
    {
        using var client = await FakeClient.ConnectAsync(_endPoint, _table);
        await client.SendAsync(LoginPacket("jogador", "errada", 756));
        var reply = await client.ReceiveAsync();
        Assert.Equal(Opcodes.MessagePanel, reply.Type);
        Assert.Equal("Senha incorreta.", reply.ReadString(GameConstants.MessageLength));
    }

    [Fact]
    public async Task Wrong_client_version_is_disconnected()
    {
        using var client = await FakeClient.ConnectAsync(_endPoint, _table);
        await client.SendAsync(LoginPacket("jogador", "senha123", 600));
        var reply = await client.ReceiveAsync();
        Assert.Contains("Versão do client inválida", reply.ReadString(GameConstants.MessageLength));
        Assert.True(await client.IsClosedAsync());
    }

    [Fact]
    public async Task Second_login_kicks_first_session()
    {
        using var first = await FakeClient.ConnectAsync(_endPoint, _table);
        await first.SendAsync(LoginPacket("jogador", "senha123", 756));
        Assert.Equal(Opcodes.AccountLoginOk, (await first.ReceiveAsync()).Type);

        using var second = await FakeClient.ConnectAsync(_endPoint, _table);
        await second.SendAsync(LoginPacket("jogador", "senha123", 756));
        Assert.Equal(Opcodes.AccountLoginOk, (await second.ReceiveAsync()).Type);

        Assert.Equal("Sua conta foi conectada em outro lugar.", (await first.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        Assert.True(await first.IsClosedAsync());
    }

    [Theory]
    [InlineData(ChecksumMode.CipherMinusPlain)]
    [InlineData(ChecksumMode.PlainMinusCipher)]
    public async Task Server_answers_with_the_clients_checksum_mode(ChecksumMode mode)
    {
        using var client = await FakeClient.ConnectAsync(_endPoint, _table);
        client.Mode = mode;
        await client.SendAsync(LoginPacket("jogador", "senha123", 756));
        Assert.Equal(Opcodes.AccountLoginOk, (await client.ReceiveAsync()).Type);
        await client.SendAsync(new SecondPasswordRequest("1234", false).Write());
        Assert.Equal(Opcodes.SecondPassword, (await client.ReceiveAsync()).Type);

        // Se o checksum de um pacote for ambíguo (os dois modos dão o mesmo valor), o servidor ainda
        // não sabe o modo. Por isso confere alguns pacotes seguidos.
        for (int i = 0; i < 5; i++)
        {
            await client.SendAsync(new PacketWriter(Opcodes.CreateCharacter, CreateCharacterRequest.PacketSize)
                .WriteInt32(9).WriteString("Teste", 16).WriteInt32(0).ToArray());
            await client.ReceiveAsync(); // mensagem de erro (slot inválido)
            await client.ReceiveAsync(); // sinal de falha
            if (client.LastReceivedMode is { } received)
            {
                Assert.Equal(mode, received);
                return;
            }
        }
        Assert.Fail("nenhuma resposta com checksum não ambíguo");
    }

    /// <summary>Loga e passa pela segunda senha, chegando à tela de personagens liberada.</summary>
    private static async Task LoginAndUnlockAsync(FakeClient client, string secondPassword = "1234")
    {
        await client.SendAsync(LoginPacket("jogador", "senha123", 754));
        Assert.Equal(Opcodes.AccountLoginOk, (await client.ReceiveAsync()).Type);
        await client.SendAsync(new SecondPasswordRequest(secondPassword, false).Write());
        Assert.Equal(Opcodes.SecondPassword, (await client.ReceiveAsync()).Type);
    }

    private static byte[] CreatePacket(int slot, string name, int cls = 0) =>
        new PacketWriter(Opcodes.CreateCharacter, CreateCharacterRequest.PacketSize)
            .WriteInt32(slot).WriteString(name, 16).WriteInt32(cls).ToArray();

    [Fact]
    public async Task Second_password_is_defined_on_first_use_and_then_checked()
    {
        using (var first = await FakeClient.ConnectAsync(_endPoint, _table))
        {
            await LoginAndUnlockAsync(first, "7002"); // conta sem segunda senha: 7002 passa a ser a senha
        }
        Assert.NotNull((await _store.FindByUsernameAsync("jogador"))!.SecondPasswordHash);

        using var client = await FakeClient.ConnectAsync(_endPoint, _table);
        await client.SendAsync(LoginPacket("jogador", "senha123", 754));
        Assert.Equal(Opcodes.AccountLoginOk, (await client.ReceiveAsync()).Type);

        // Sem a segunda senha, nada é liberado.
        await client.SendAsync(CreatePacket(0, "Bloqueado"));
        Assert.Equal(Opcodes.CreateCharacterFail, (await client.ReceiveAsync()).Type);

        await client.SendAsync(new SecondPasswordRequest("9999", false).Write());
        Assert.Equal(Opcodes.SecondPasswordFail, (await client.ReceiveAsync()).Type);

        await client.SendAsync(new SecondPasswordRequest("7002", false).Write());
        Assert.Equal(Opcodes.SecondPassword, (await client.ReceiveAsync()).Type);

        await client.SendAsync(CreatePacket(0, "Liberado"));
        Assert.Equal(Opcodes.CreateCharacterOk, (await client.ReceiveAsync()).Type);
    }

    [Fact]
    public async Task Second_password_can_be_changed_after_confirming_the_current_one()
    {
        using var client = await FakeClient.ConnectAsync(_endPoint, _table);
        await client.SendAsync(LoginPacket("jogador", "senha123", 754));
        Assert.Equal(Opcodes.AccountLoginOk, (await client.ReceiveAsync()).Type);

        // Trocar sem ter confirmado a atual é recusado.
        await client.SendAsync(new SecondPasswordRequest("5555", true).Write());
        Assert.Equal(Opcodes.SecondPasswordFail, (await client.ReceiveAsync()).Type);

        await client.SendAsync(new SecondPasswordRequest("1111", false).Write());
        Assert.Equal(Opcodes.SecondPassword, (await client.ReceiveAsync()).Type);
        await client.SendAsync(new SecondPasswordRequest("5555", true).Write());
        Assert.Equal(Opcodes.SecondPassword, (await client.ReceiveAsync()).Type);

        var account = (await _store.FindByUsernameAsync("jogador"))!;
        Assert.True(PasswordHasher.Verify("5555", account.SecondPasswordHash!));
    }

    [Fact]
    public async Task Too_many_wrong_second_passwords_disconnect()
    {
        await _store.SetSecondPasswordAsync(1, PasswordHasher.Hash("1234"));
        using var client = await FakeClient.ConnectAsync(_endPoint, _table);
        await client.SendAsync(LoginPacket("jogador", "senha123", 754));
        Assert.Equal(Opcodes.AccountLoginOk, (await client.ReceiveAsync()).Type);

        for (int i = 1; i < LoginHandler.MaxSecondPasswordFailures; i++)
        {
            await client.SendAsync(new SecondPasswordRequest("0000", false).Write());
            Assert.Equal(Opcodes.SecondPasswordFail, (await client.ReceiveAsync()).Type);
        }
        await client.SendAsync(new SecondPasswordRequest("0000", false).Write());
        Assert.Equal("Muitas tentativas de senha incorretas.", (await client.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        Assert.True(await client.IsClosedAsync());
    }

    /// <summary>Cria a conta (se preciso), loga, passa da segunda senha, cria um personagem e entra no mapa.</summary>
    private async Task<FakeClient> EnterWorldAsync(string account, string name)
    {
        if (await _store.FindByUsernameAsync(account) is null)
            await _store.CreateAsync(account, PasswordHasher.Hash("senha123"));

        var client = await FakeClient.ConnectAsync(_endPoint, _table);
        await client.SendAsync(LoginPacket(account, "senha123", 754));
        Assert.Equal(Opcodes.AccountLoginOk, (await client.ReceiveAsync()).Type);
        await client.SendAsync(new SecondPasswordRequest("1234", false).Write());
        Assert.Equal(Opcodes.SecondPassword, (await client.ReceiveAsync()).Type);
        await client.SendAsync(CreatePacket(0, name));
        Assert.Equal(Opcodes.CreateCharacterOk, (await client.ReceiveAsync()).Type);
        await client.SendAsync(new PacketWriter(Opcodes.CharacterLogin, CharacterLoginRequest.PacketSize).WriteInt32(0).Skip(20).ToArray());
        Assert.Equal(Opcodes.CharacterLoginOk, (await client.ReceiveAsync()).Type);
        return client;
    }

    private static byte[] MovePacket(ushort id, short x, short y, short destX, short destY) =>
        new PacketWriter(Opcodes.Move, ServerPackets.MoveSize, id)
            .WriteInt16(x).WriteInt16(y).WriteInt32(2).WriteInt32(0)
            .WriteInt16(destX).WriteInt16(destY).WriteString("1111", 24).ToArray();

    [Fact]
    public async Task Players_see_each_other_move_chat_and_leave()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        using var b = await EnterWorldAsync("jogadorb", "Bruno");

        // Os dois nasceram em (2100, 2100): cada um recebe o 0x364 do outro.
        var bornForA = await a.ReceiveAsync();
        Assert.Equal(Opcodes.CreateMob, bornForA.Type);
        Assert.Equal(ServerPackets.CreateMobSize, bornForA.Length);
        bornForA.Skip(4);
        ushort bId = bornForA.ReadUInt16();
        Assert.Equal("Bruno", bornForA.ReadString(12));
        var bornForB = await b.ReceiveAsync();
        Assert.Equal(Opcodes.CreateMob, bornForB.Type);
        bornForB.Skip(4);
        ushort aId = bornForB.ReadUInt16();

        // B anda um pouco: A recebe o mesmo movimento com o id de B.
        await b.SendAsync(MovePacket(bId, 2100, 2100, 2105, 2103));
        var move = await a.ReceiveAsync();
        Assert.Equal(Opcodes.Move, move.Type);
        Assert.Equal(bId, move.Id);
        move.Skip(12);
        Assert.Equal(2105, move.ReadInt16());

        // Chat de A chega a B com o id de A.
        await a.SendAsync(new PacketWriter(Opcodes.Chat, ServerPackets.ChatSize, aId).WriteString("oi Bruno", GameConstants.MessageLength).ToArray());
        var chat = await b.ReceiveAsync();
        Assert.Equal(Opcodes.Chat, chat.Type);
        Assert.Equal(aId, chat.Id);
        Assert.Equal("oi Bruno", chat.ReadString(GameConstants.MessageLength));

        // B se afasta para fora da visão: os dois somem um para o outro.
        await b.SendAsync(MovePacket(bId, 2105, 2103, 2140, 2103));
        var goneForA = await a.ReceiveAsync();
        Assert.Equal(Opcodes.RemoveMob, goneForA.Type);
        Assert.Equal(bId, goneForA.Id);
        Assert.Equal(Opcodes.RemoveMob, (await b.ReceiveAsync()).Type);

        // Volta: aparecem de novo.
        await b.SendAsync(MovePacket(bId, 2140, 2103, 2110, 2103));
        Assert.Equal(Opcodes.CreateMob, (await b.ReceiveAsync()).Type);
        Assert.Equal(Opcodes.CreateMob, (await a.ReceiveAsync()).Type);

        // B sai do jogo: A recebe o 0x165 e a posição de B fica salva.
        b.Dispose();
        var left = await a.ReceiveAsync();
        Assert.Equal(Opcodes.RemoveMob, left.Type);
        Assert.Equal(bId, left.Id);

        var bAccount = (await _store.FindByUsernameAsync("jogadorb"))!;
        for (int i = 0; i < 50 && (await _store.ListByAccountAsync(bAccount.Id))[0].PosX != 2110; i++)
            await Task.Delay(20);
        Assert.Equal(2110, (await _store.ListByAccountAsync(bAccount.Id))[0].PosX);
    }

    [Fact]
    public async Task Move_too_far_is_ignored()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        using var b = await EnterWorldAsync("jogadorb", "Bruno");
        Assert.Equal(Opcodes.CreateMob, (await a.ReceiveAsync()).Type);
        Assert.Equal(Opcodes.CreateMob, (await b.ReceiveAsync()).Type);

        // Teleporte de 500 células: recusado, ninguém some da tela.
        await b.SendAsync(MovePacket(0, 2100, 2100, 2600, 2100));
        await b.SendAsync(MovePacket(0, 2100, 2100, 2101, 2100));
        var move = await a.ReceiveAsync();
        Assert.Equal(Opcodes.Move, move.Type);
        move.Skip(12);
        Assert.Equal(2101, move.ReadInt16());
    }

    [Fact]
    public async Task Player_sees_monsters_near_and_loses_them_far_away()
    {
        // Pasta npc e regras de spawn de exemplo: 3 lobos perto de Armia, 1 NPC parado e 1 monstro longe.
        var npcDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(npcDir, "Lobo"), NpcDataTests.NpcFile("Lobo", 299, 18, 200));
            File.WriteAllBytes(Path.Combine(npcDir, "Ferreiro"), NpcDataTests.NpcFile("Ferreiro", 58, 1, 100, merchant: 1));
            File.WriteAllBytes(Path.Combine(npcDir, "Quebrado"), new byte[10]);
            var rules = new List<WYD.Server.Data.SpawnRule>
            {
                new() { Leader = "Lobo", Follower = "Lobo", MaxNumMob = 3, MinGroup = 2, MaxGroup = 2, StartX = 2105, StartY = 2105, StartRange = 2 },
                new() { Leader = "Ferreiro", MaxNumMob = 1, StartX = 2098, StartY = 2100 },
                new() { Leader = "Lobo", MaxNumMob = 1, StartX = 3000, StartY = 3000 },
                new() { Leader = "Quebrado", MaxNumMob = 1, StartX = 2100, StartY = 2100 },
                new() { Leader = "NaoExiste", MaxNumMob = 1, StartX = 2100, StartY = 2100 },
            };
            await _host.Services.GetRequiredService<MobService>().SpawnAllAsync(rules, npcDir);
            Assert.Equal(5, _host.Services.GetRequiredService<World>().MobCount);

            using var a = await EnterWorldAsync("jogadora", "Alice");
            var seen = new List<(ushort Id, string Name, short Model)>();
            for (int i = 0; i < 4; i++)
            {
                var p = await a.ReceiveAsync();
                Assert.Equal(Opcodes.CreateMob, p.Type);
                p.Skip(4);
                ushort id = p.ReadUInt16();
                string name = p.ReadString(12);
                p.Skip(4);
                seen.Add((id, name, p.ReadInt16()));
            }
            Assert.Equal(3, seen.Count(m => m.Name == "Lobo" && m.Model == 299));
            Assert.Contains(seen, m => m.Name == "Ferreiro" && m.Model == 58);
            Assert.All(seen, m => Assert.True(m.Id >= WYD.Server.Game.Mob.FirstId));

            // Andando para longe, os 4 somem da tela.
            await a.SendAsync(MovePacket(0, 2100, 2100, 2130, 2130));
            var gone = new HashSet<ushort>();
            for (int i = 0; i < 4; i++)
            {
                var p = await a.ReceiveAsync();
                Assert.Equal(Opcodes.RemoveMob, p.Type);
                gone.Add(p.Id);
            }
            Assert.Equal(seen.Select(m => m.Id).ToHashSet(), gone);
        }
        finally
        {
            Directory.Delete(npcDir, true);
        }
    }

    [Fact]
    public async Task Clicking_a_shop_npc_opens_its_item_list()
    {
        var npcDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // Uma loja com um item em cada aba (slots 0, 27 e 54 do inventário) e um item fora das abas (slot 10).
            File.WriteAllBytes(Path.Combine(npcDir, "Ferreiro"),
                NpcDataTests.NpcFile("Ferreiro", 58, 1, 100, merchant: 1, carry: [(0, 905), (27, 1208), (54, 3407), (10, 999)]));
            File.WriteAllBytes(Path.Combine(npcDir, "Lobo"), NpcDataTests.NpcFile("Lobo", 299, 18, 200));
            await _host.Services.GetRequiredService<MobService>().SpawnAllAsync(
            [
                new() { Leader = "Ferreiro", MaxNumMob = 1, StartX = 2102, StartY = 2100 },
                new() { Leader = "Lobo", MaxNumMob = 1, StartX = 2098, StartY = 2100 },
            ], npcDir);

            using var a = await EnterWorldAsync("jogadora", "Alice");
            var ids = new Dictionary<string, ushort>();
            for (int i = 0; i < 2; i++)
            {
                var p = await a.ReceiveAsync();
                p.Skip(4);
                ushort id = p.ReadUInt16();
                ids[p.ReadString(12)] = id;
            }

            // Pedir a loja de um monstro não faz nada; a do Ferreiro responde com a lista.
            await a.SendAsync(new PacketWriter(Opcodes.RequestShop, 16).WriteUInt16(ids["Lobo"]).Skip(2).ToArray());
            await a.SendAsync(new PacketWriter(Opcodes.RequestShop, 16).WriteUInt16(ids["Ferreiro"]).Skip(2).ToArray());

            var shop = await a.ReceiveAsync();
            Assert.Equal(Opcodes.ShopList, shop.Type);
            Assert.Equal(ServerPackets.ShopListSize, shop.Length);
            Assert.Equal(1, shop.ReadInt32());          // tipo 1: loja de itens
            var list = Enumerable.Range(0, 27).Select(_ => { short idx = shop.ReadInt16(); shop.Skip(6); return idx; }).ToArray();
            Assert.Equal(905, list[0]);                 // aba 1 <- Carry[0]
            Assert.Equal(1208, list[9]);                // aba 2 <- Carry[27]
            Assert.Equal(3407, list[18]);               // aba 3 <- Carry[54]
            Assert.DoesNotContain((short)999, list);    // Carry[10] não aparece na loja
        }
        finally
        {
            Directory.Delete(npcDir, true);
        }
    }

    private static byte[] CommandPacket(string command, string text) =>
        new PacketWriter(Opcodes.Command, ServerPackets.CommandSize)
            .WriteString(command, 16).WriteString(text, ServerPackets.CommandSize - 28).ToArray();

    [Fact]
    public async Task Gm_creates_item_with_slash_command()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");

        // Sem ser GM: recusado.
        await a.SendAsync(CommandPacket("gm", "item 905"));
        Assert.Equal("Comando só para GMs.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

        var account = (await _store.FindByUsernameAsync("jogadora"))!;
        account.AccessLevel = 1; // o mesmo objeto que a sessão usa (no servidor real vale a partir do próximo login)

        await a.SendAsync(CommandPacket("gm", "item 905 43 9"));
        var update = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateItem, update.Type);
        Assert.Equal(ServerPackets.UpdateItemSize, update.Length);
        Assert.Equal(1, update.ReadInt16());        // inventário
        Assert.Equal(0, update.ReadInt16());        // primeiro slot livre
        Assert.Equal(905, update.ReadInt16());
        Assert.Equal((43, 9), (update.ReadByte(), update.ReadByte()));
        Assert.Equal("Espada Longa criado no inventário.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

        // O segundo vai para o slot seguinte, e o inventário fica salvo.
        await a.SendAsync(CommandPacket("gm", "item 1208"));
        var second = await a.ReceiveAsync();
        second.Skip(2);
        Assert.Equal(1, second.ReadInt16());
        await a.ReceiveAsync();
        var saved = (await _store.ListByAccountAsync(account.Id))[0].Inventory;
        Assert.Equal(new short[] { 905, 1208 }, saved.Take(2).Select(i => i.Index));

        // Argumentos inválidos.
        await a.SendAsync(CommandPacket("gm", "item abc"));
        Assert.StartsWith("Uso: /gm item", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        await a.SendAsync(CommandPacket("gm", "item 7000"));
        Assert.StartsWith("Índice inválido", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        await a.SendAsync(CommandPacket("gm", "item 777"));
        Assert.Equal("O item 777 não existe no ItemList.bin.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
    }

    [Fact]
    public async Task Slash_name_whispers_to_another_player()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        using var b = await EnterWorldAsync("jogadorb", "Bruno");
        Assert.Equal(Opcodes.CreateMob, (await a.ReceiveAsync()).Type);
        Assert.Equal(Opcodes.CreateMob, (await b.ReceiveAsync()).Type);

        await a.SendAsync(CommandPacket("bruno", "oi, tudo bem?"));
        var whisper = await b.ReceiveAsync();
        Assert.Equal(Opcodes.Command, whisper.Type);
        Assert.Equal("Alice", whisper.ReadString(16));
        Assert.Equal("oi, tudo bem?", whisper.ReadString(96));

        await a.SendAsync(CommandPacket("Fulano", "alô"));
        Assert.Equal("Fulano não está online.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
    }

    private static byte[] AttackPacket(ushort attacker, params ushort[] targets)
    {
        ushort type = targets.Length switch { 1 => Opcodes.AttackOne, 2 => Opcodes.AttackTwo, _ => Opcodes.Attack };
        int size = targets.Length switch { 1 => 48, 2 => 52, _ => 96 };
        var w = new PacketWriter(type, size, attacker);
        w.WriteUInt16(attacker).Skip(2)
         .WriteInt16(2100).WriteInt16(2100).WriteInt16(2101).WriteInt16(2100)
         .WriteInt16(-1).WriteInt16(-1)                    // skill, ?
         .WriteByte(0xFF).WriteByte(0).WriteByte(0).WriteByte(0)
         .Skip(12);                                        // Hold, Exp, ?
        foreach (var t in targets) w.WriteUInt16(t).WriteInt16(-1);
        w.Skip(size - w.Position);
        return w.ToArray();
    }

    [Fact]
    public async Task Killing_a_monster_gives_exp_gold_levels_and_it_respawns()
    {
        var npcDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // Monstro com 1 de vida (morre no primeiro golpe) e um NPC de loja ao lado.
            var weak = NpcDataTests.NpcFile("Gremlin", 202, 4, 1);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(weak.AsSpan(0x20), 280); // Exp
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(weak.AsSpan(0x1C), 50);  // Gold
            File.WriteAllBytes(Path.Combine(npcDir, "Gremlin"), weak);
            File.WriteAllBytes(Path.Combine(npcDir, "Ferreiro"), NpcDataTests.NpcFile("Ferreiro", 58, 1, 100, merchant: 1));
            await _host.Services.GetRequiredService<MobService>().SpawnAllAsync(
            [
                new() { Leader = "Gremlin", MaxNumMob = 1, StartX = 2101, StartY = 2100 },
                new() { Leader = "Ferreiro", MaxNumMob = 1, StartX = 2099, StartY = 2100 },
            ], npcDir);

            using var a = await EnterWorldAsync("jogadora", "Alice");
            var ids = new Dictionary<string, ushort>();
            for (int i = 0; i < 2; i++)
            {
                var p = await a.ReceiveAsync();
                p.Skip(4);
                ushort id = p.ReadUInt16();
                ids[p.ReadString(12)] = id;
            }
            var account = (await _store.FindByUsernameAsync("jogadora"))!;
            const ushort me = 1; // cada teste sobe um servidor novo: a primeira conexão recebe o id 1

            // NPC de loja não leva dano.
            await a.SendAsync(AttackPacket(me, ids["Ferreiro"]));
            var onNpc = await a.ReceiveAsync();
            Assert.Equal(Opcodes.AttackOne, onNpc.Type);
            onNpc.Skip(0x2C - 12 + 2);
            Assert.Equal(0, onNpc.ReadInt16());

            await Task.Delay(World.MinAttackInterval + TimeSpan.FromMilliseconds(50));
            await a.SendAsync(AttackPacket(me, ids["Gremlin"]));
            var hit = await a.ReceiveAsync();
            Assert.Equal(Opcodes.AttackOne, hit.Type);
            Assert.Equal(48, hit.Length);
            hit.Skip(0x24 - 12);
            Assert.Equal(280, hit.ReadInt32());          // experiência nova de quem atacou
            hit.Skip(4);
            Assert.Equal(ids["Gremlin"], hit.ReadUInt16());
            Assert.True(hit.ReadInt16() >= 1);           // dano calculado pelo servidor

            var kill = await a.ReceiveAsync();
            Assert.Equal(Opcodes.MobKill, kill.Type);
            Assert.Equal(ServerPackets.MobKillSize, kill.Length);
            kill.Skip(4);
            Assert.Equal(ids["Gremlin"], kill.ReadUInt16());
            kill.Skip(2);
            Assert.Equal(280, kill.ReadInt32());

            var removed = await a.ReceiveAsync();
            Assert.Equal(Opcodes.RemoveMob, removed.Type);
            Assert.Equal(ids["Gremlin"], removed.Id);
            Assert.Equal(ServerPackets.RemoveDead, removed.ReadInt32());

            // Exp, gold e pontos novos; com 280 de exp e a tabela 100/200/300 sobe dois níveis.
            var etc = await a.ReceiveAsync();
            Assert.Equal(Opcodes.UpdateEtc, etc.Type);
            Assert.Equal(36, etc.Length);
            etc.Skip(4);
            Assert.Equal(280, etc.ReadInt32());
            etc.Skip(4);
            Assert.Equal(2 * Leveling.ScorePointsPerLevel, etc.ReadInt16());
            etc.Skip(6);
            Assert.Equal(50, etc.ReadInt32());

            var score = await a.ReceiveAsync();
            Assert.Equal(Opcodes.UpdateScore, score.Type);
            Assert.Equal(92, score.Length);
            Assert.Equal(2, score.ReadInt16());          // nível (0 = nível 1 na tela)
            var hpMp = await a.ReceiveAsync();
            Assert.Equal(Opcodes.UpdateHpMp, hpMp.Type);

            // O progresso ficou salvo e o monstro renasce (RespawnSeconds = 1 no teste).
            var saved = (await _store.ListByAccountAsync(account.Id))[0];
            Assert.Equal(280, saved.Exp);
            Assert.Equal(50, saved.Coin);
            Assert.Equal(2, saved.BaseScore.Level);
            Assert.Equal(saved.MaxHp, saved.BaseScore.Hp);                 // cheia no máximo atual (com a CON)
            var back = await a.ReceiveAsync();
            Assert.Equal(Opcodes.CreateMob, back.Type);
            back.Skip(4);
            Assert.Equal(ids["Gremlin"], back.ReadUInt16());
        }
        finally
        {
            Directory.Delete(npcDir, true);
        }
    }

    private static async Task<PacketReader> ReceiveTypeAsync(FakeClient client, ushort type)
    {
        while (true)
        {
            var p = await client.ReceiveAsync();
            if (p.Type == type) return p;
        }
    }

    [Fact]
    public async Task Monster_hits_back_kills_and_player_revives_in_town()
    {
        var npcDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // Monstro forte (não morre com um golpe e mata com um) parado ao lado de quem entra.
            var ogre = NpcDataTests.NpcFile("Ogro", 202, 40, 100_000);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(ogre.AsSpan(0x34), 30_000); // Damage
            File.WriteAllBytes(Path.Combine(npcDir, "Ogro"), ogre);
            await _host.Services.GetRequiredService<MobService>().SpawnAllAsync(
                [new() { Leader = "Ogro", MaxNumMob = 1, StartX = 2101, StartY = 2100 }], npcDir);

            using var a = await EnterWorldAsync("jogadora", "Alice");
            var created = await ReceiveTypeAsync(a, Opcodes.CreateMob);
            created.Skip(4);
            ushort ogreId = created.ReadUInt16();
            const ushort me = 1;

            await a.SendAsync(AttackPacket(me, ogreId));
            Assert.Equal(me, (await ReceiveTypeAsync(a, Opcodes.AttackOne)).Id);

            // O ogro revida: ataque com ele como atacante e eu como alvo, depois a vida nova e a morte.
            var hit = await ReceiveTypeAsync(a, Opcodes.AttackOne);
            Assert.Equal(ogreId, hit.Id);
            Assert.Equal(ServerPackets.AttackOneSize, hit.Length);
            Assert.Equal(ogreId, hit.ReadUInt16());
            hit.Skip(0x2C - 0x0E);
            Assert.Equal(me, hit.ReadUInt16());
            Assert.True(hit.ReadInt16() > 0);

            var hp = await a.ReceiveAsync();
            Assert.Equal(Opcodes.UpdateHpMp, hp.Type);
            Assert.Equal(0, hp.ReadInt32());
            var death = await a.ReceiveAsync();
            Assert.Equal(Opcodes.MobKill, death.Type);
            death.Skip(4);
            Assert.Equal(me, death.ReadUInt16());
            Assert.Equal(ogreId, death.ReadUInt16());

            // Morto não anda; ao pedir para renascer volta para a cidade com a vida cheia.
            await a.SendAsync(new PacketWriter(Opcodes.Revive, PacketHeader.Size, me).ToArray());
            var teleport = await a.ReceiveAsync();
            Assert.Equal(Opcodes.Move, teleport.Type);
            teleport.Skip(8);
            Assert.Equal(ServerPackets.MoveTeleport, teleport.ReadInt32());
            Assert.Equal(2100, teleport.ReadInt16());
            Assert.Equal(Opcodes.UpdateScore, (await a.ReceiveAsync()).Type);
            var full = await a.ReceiveAsync();
            Assert.Equal(Opcodes.UpdateHpMp, full.Type);
            Assert.True(full.ReadInt32() > 0);
        }
        finally
        {
            Directory.Delete(npcDir, true);
        }
    }

    [Fact]
    public async Task Help_lists_commands_in_chat()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        (await _store.FindByUsernameAsync("jogadora"))!.AccessLevel = 1;
        await a.SendAsync(CommandPacket("gm", "ajuda"));
        var lines = new List<string>();
        for (int i = 0; i < CommandHandler.GmCommands.Length; i++)
        {
            var p = await a.ReceiveAsync();
            Assert.Equal(Opcodes.Command, p.Type);
            Assert.Equal("Sistema", p.ReadString(16));
            lines.Add(p.ReadString(96));
        }
        Assert.Contains(lines, l => l.StartsWith("/gm item"));
        Assert.Contains(lines, l => l.StartsWith("/gm nivel"));
    }

    [Fact]
    public async Task Points_and_evolution_through_the_game()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        (await _store.FindByUsernameAsync("jogadora"))!.AccessLevel = 1;
        var me = await CharacterOf("jogadora");
        short str = me.BaseScore.Str;

        // Sem pontos: recusa e devolve os pontos do servidor.
        await a.SendAsync(new PacketWriter(Opcodes.ApplyBonus, 20, 1).WriteInt16(0).WriteInt16(0).Skip(4).ToArray());
        Assert.Equal("Sem pontos de atributo.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        Assert.Equal(Opcodes.UpdateEtc, (await a.ReceiveAsync()).Type);

        await a.SendAsync(CommandPacket("gm", "pontos 3"));
        Assert.Equal(Opcodes.UpdateEtc, (await a.ReceiveAsync()).Type);
        await a.ReceiveAsync();

        await a.SendAsync(new PacketWriter(Opcodes.ApplyBonus, 20, 1).WriteInt16(0).WriteInt16(0).Skip(4).ToArray());
        var score = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateScore, score.Type);
        score.Skip(0x10);                                           // Level, Ac, Damage, Merchant, AttackRun, MaxHp, MaxMp, Hp, Mp
        Assert.Equal(str + 1, score.ReadInt16());                   // FOR na ficha
        var etc = await a.ReceiveAsync();
        etc.Skip(12);
        Assert.Equal(2, etc.ReadInt16());                           // sobraram 2 pontos de atributo

        await a.SendAsync(CommandPacket("gm", "evoluir sub"));
        for (int i = 0; i < 5; i++) await a.ReceiveAsync();
        Assert.Equal(Evolution.SubCelestial, me.Evolution);
        await a.SendAsync(CommandPacket("gm", "nivel 201"));
        Assert.StartsWith("Uso: /gm nivel <1 a 200>", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
    }

    private static byte[] NpcClickPacket(ushort npc) =>
        new PacketWriter(Opcodes.NpcClick, 20, 1).WriteInt32(npc).WriteInt32(0).ToArray();

    [Fact]
    public async Task Evolution_happens_at_the_king_of_your_kingdom()
    {
        var npcDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // Rei azul (clã 7, Merchant 15) ao lado de quem entra.
            var king = NpcDataTests.NpcFile("Rei_Harabard", 304, 1, 100_000, merchant: 0x6F);
            king[0x10] = (byte)Kingdom.Blue;
            File.WriteAllBytes(Path.Combine(npcDir, "Rei_Harabard"), king);
            await _host.Services.GetRequiredService<MobService>().SpawnAllAsync(
                [new() { Leader = "Rei_Harabard", MaxNumMob = 1, StartX = 2102, StartY = 2100 }], npcDir);

            using var a = await EnterWorldAsync("jogadora", "Alice");
            var created = await ReceiveTypeAsync(a, Opcodes.CreateMob);
            created.Skip(4);
            ushort kingId = created.ReadUInt16();
            (await _store.FindByUsernameAsync("jogadora"))!.AccessLevel = 1;
            var me = await CharacterOf("jogadora");

            // Antes do nível máximo: só o aviso.
            await a.SendAsync(NpcClickPacket(kingId));
            Assert.Equal("Volte quando chegar ao nível 400 de Mortal para virar Arch.",
                (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

            await a.SendAsync(CommandPacket("gm", "nivel 400"));
            for (int i = 0; i < 4; i++) await a.ReceiveAsync();

            // Capa vermelha: este rei não evolui.
            me.Equip[Kingdoms.CapeSlot] = new Item(3192);
            await a.SendAsync(NpcClickPacket(kingId));
            Assert.Equal("Você é do reino Vermelho. Evolua com o Rei Glantuar.",
                (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

            // Capa azul: evolui para Arch, com o rosto atualizado.
            me.Equip[Kingdoms.CapeSlot] = new Item(3191);
            await a.SendAsync(NpcClickPacket(kingId));
            var face = await a.ReceiveAsync();
            Assert.Equal(Opcodes.UpdateItem, face.Type);
            face.Skip(4 + 2 + 3);
            Assert.Equal((byte)Evolution.Arch, face.ReadByte());
            for (int i = 0; i < 3; i++) await a.ReceiveAsync();
            Assert.Equal("Parabéns! Agora você é Arch (nível 1).", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
            Assert.Equal(Evolution.Arch, me.Evolution);

            // /kingdom leva para a frente do Rei Harabard.
            await a.SendAsync(CommandPacket("kingdom", ""));
            var teleport = await ReceiveTypeAsync(a, Opcodes.Move);
            Assert.Equal((1760, 1725), (teleport.ReadInt16(), teleport.ReadInt16()));   // entrada do reino
        }
        finally
        {
            Directory.Delete(npcDir, true);
        }
    }

    [Fact]
    public async Task Mount_runs_fast_once_equipped()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        (await _store.FindByUsernameAsync("jogadora"))!.AccessLevel = 1;
        var me = await CharacterOf("jogadora");

        // /gm item de uma montaria cria ela viva (vida 100, nível 1).
        await a.SendAsync(CommandPacket("gm", "item 2370"));
        var created = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateItem, created.Type);
        await a.ReceiveAsync();
        Assert.Equal(Stats.DefaultMountHp, Stats.MountHp(me.Inventory[0]));

        // Equipar no slot 14: a ficha volta com a velocidade da montaria (6).
        await a.SendAsync(new PacketWriter(Opcodes.MoveItem, 20, 1).WriteByte(0).WriteByte(14).WriteByte(1).WriteByte(0).Skip(4).ToArray());
        Assert.Equal(Opcodes.MoveItem, (await a.ReceiveAsync()).Type);
        var score = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateScore, score.Type);
        score.Skip(7);
        Assert.Equal(6, score.ReadByte() & 0x0F);                   // AttackRun
    }

    [Fact]
    public async Task City_teleport_charges_and_moves()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        (await _store.FindByUsernameAsync("jogadora"))!.AccessLevel = 1;
        var me = await CharacterOf("jogadora");
        var request = new PacketWriter(Opcodes.Teleport, 16, 1).WriteInt32(0).ToArray();

        // Sem destino cadastrado aqui (o de Armia fica em 2117 2102): o GM recebe a dica.
        await a.SendAsync(request);
        Assert.StartsWith("Teleporte sem destino em 2100 2100", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

        await a.SendAsync(CommandPacket("gm", "tp add 2480 1700 500 Azran"));
        Assert.StartsWith("Teleporte em 2100 2100 -> 2480 1700", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

        // Sem gold: recusado. Com gold: cobra e leva.
        await a.SendAsync(request);
        Assert.Equal("O teleporte custa 500 de gold.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        me.Coin = 1000;
        await a.SendAsync(request);
        var coin = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateCoin, coin.Type);
        Assert.Equal(500, coin.ReadInt32());
        var move = await ReceiveTypeAsync(a, Opcodes.Move);
        move.Skip(8);
        Assert.Equal(ServerPackets.MoveTeleport, move.ReadInt32());
        Assert.Equal((2480, 1700), (move.ReadInt16(), move.ReadInt16()));
        Assert.Equal((2480, 1700), (me.PosX, me.PosY));
    }

    [Fact]
    public async Task Armia_circle_goes_to_Noatun_for_700_gold()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        (await _store.FindByUsernameAsync("jogadora"))!.AccessLevel = 1;
        var me = await CharacterOf("jogadora");
        await a.SendAsync(CommandPacket("gm", "ir 2117 2102"));
        await ReceiveTypeAsync(a, Opcodes.Move);

        me.Coin = 1000;
        await a.SendAsync(new PacketWriter(Opcodes.Teleport, 16, 1).WriteInt32(0).ToArray());
        Assert.Equal(300, (await ReceiveTypeAsync(a, Opcodes.UpdateCoin)).ReadInt32());
        var move = await ReceiveTypeAsync(a, Opcodes.Move);
        move.Skip(12);
        Assert.Equal((1045, 1725), (move.ReadInt16(), move.ReadInt16()));
    }

    [Fact]
    public async Task Add_command_spends_many_points_at_once()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        var me = await CharacterOf("jogadora");
        short str = me.BaseScore.Str, con = me.BaseScore.Con;
        me.ScoreBonus = 10;

        await a.SendAsync(CommandPacket("add", "for 3 con 20"));
        Assert.Equal(Opcodes.UpdateScore, (await a.ReceiveAsync()).Type);
        Assert.Equal(Opcodes.UpdateEtc, (await a.ReceiveAsync()).Type);
        Assert.Equal("+3 FOR, +7 CON. Sem pontos de atributo. Restam 0 de atributo e 0 especiais.",
            (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        Assert.Equal(str + 3, me.BaseScore.Str);
        Assert.Equal(con + 7, me.BaseScore.Con);

        await a.SendAsync(CommandPacket("add", "sorte 5"));
        Assert.StartsWith("Não entendi", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
    }

    [Fact]
    public async Task Gem_changes_the_ancient_effect()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        var c = await CharacterOf("jogadora");
        c.Inventory[0] = new Item(3388);                                  // Gema de Coral: perfuração
        c.Inventory[3] = new Item(2443);                                  // pedra Coral: é de composição
        c.Inventory[1] = new Item(2451) { Effect1 = 43, Value1 = 230 };   // Club ancient (experiência) +10
        c.Inventory[2] = new Item(905);

        byte[] Use(int srcPos, int destType, int destPos) =>
            new PacketWriter(Opcodes.UseItem, 36).WriteInt32(1).WriteInt32(srcPos).WriteInt32(destType).WriteInt32(destPos).Skip(8).ToArray();

        // Em item que não é ancient: recusado e a gema fica.
        await a.SendAsync(Use(0, 1, 2));
        Assert.Equal("A gema só funciona em itens ancient.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        Assert.Equal(3388, c.Inventory[0].Index);

        // A pedra não é gema.
        await a.SendAsync(Use(3, 1, 1));
        Assert.Equal("Coral ainda não tem função neste servidor.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

        await a.SendAsync(Use(0, 1, 1));
        Assert.Equal(Opcodes.UpdateItem, (await a.ReceiveAsync()).Type);          // a gema sumiu
        var changed = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateItem, changed.Type);
        changed.Skip(4);
        Assert.Equal(2453, changed.ReadInt16());                                    // a cópia de Grade 7 (perfuração)
        Assert.Equal("Efeito do ancient trocado para Perfuração.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        Assert.True(c.Inventory[0].IsEmpty);
        Assert.Equal(230, c.Inventory[1].Value1);                                   // refinação mantida

        // Equipado e +10: a perfuração vale 10%.
        c.Equip[6] = c.Inventory[1];
        Stats.Recalculate(c, ItemListTests.TestItems());
        Assert.Equal(Ancient.BonusPercent, c.ItemBonus.Perforation);
    }

    [Fact]
    public async Task Skill_master_shows_its_list_and_teaches()
    {
        var npcDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // Mestre de skill de TK (Merchant 19, Score.Merchant 3) ao lado de quem entra.
            var master = NpcDataTests.NpcFile("Cap.Cavaleiros", 200, 1, 1000, carry: [(0, 5000), (1, 5001)]);
            master[0x11] = 19;
            master[0x38] = 3;
            File.WriteAllBytes(Path.Combine(npcDir, "Cap.Cavaleiros"), master);
            await _host.Services.GetRequiredService<MobService>().SpawnAllAsync(
                [new() { Leader = "Cap.Cavaleiros", MaxNumMob = 1, StartX = 2102, StartY = 2100 }], npcDir);

            using var a = await EnterWorldAsync("jogador2", "Cavaleiro");
            var created = await ReceiveTypeAsync(a, Opcodes.CreateMob);
            created.Skip(4);
            ushort masterId = created.ReadUInt16();

            await a.SendAsync(new PacketWriter(Opcodes.RequestShop, 16, 1).WriteUInt16(masterId).Skip(2).ToArray());
            var list = await a.ReceiveAsync();
            Assert.Equal(Opcodes.ShopList, list.Type);

            // Tipo 2 do 0x277: aprender a skill do item 5000.
            await a.SendAsync(new PacketWriter(Opcodes.ApplyBonus, 20, 1).WriteInt16(2).WriteInt16(5000).WriteInt16((short)masterId).Skip(2).ToArray());
            var etc = await a.ReceiveAsync();
            Assert.Equal(Opcodes.UpdateEtc, etc.Type);
            etc.Skip(8);
            Assert.Equal(1, etc.ReadInt32());                                  // LearnedSkill com o bit 0
            Assert.StartsWith("Você aprendeu", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

            // Skill de outra classe: recusada.
            await a.SendAsync(new PacketWriter(Opcodes.ApplyBonus, 20, 1).WriteInt16(2).WriteInt16(5024).WriteInt16((short)masterId).Skip(2).ToArray());
            Assert.Contains("não é da sua classe", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        }
        finally
        {
            Directory.Delete(npcDir, true);
        }
    }

    [Fact]
    public async Task Crash_right_after_teleport_keeps_the_old_position()
    {
        var a = await EnterWorldAsync("jogadora", "Alice");
        (await _store.FindByUsernameAsync("jogadora"))!.AccessLevel = 1;
        var me = await CharacterOf("jogadora");

        await a.SendAsync(CommandPacket("gm", "ir 1056 1730"));
        await ReceiveTypeAsync(a, Opcodes.Move);
        Assert.Equal((1056, 1730), (me.PosX, me.PosY));

        a.Dispose();                                        // o client caiu sem andar no lugar novo
        for (int i = 0; i < 50 && me.PosX != 2100; i++) await Task.Delay(20);
        Assert.Equal((2100, 2100), (me.PosX, me.PosY));
    }

    [Fact]
    public async Task Gm_commands_change_level_gold_and_position()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        using var b = await EnterWorldAsync("jogadorb", "Bruno");
        Assert.Equal(Opcodes.CreateMob, (await a.ReceiveAsync()).Type);
        Assert.Equal(Opcodes.CreateMob, (await b.ReceiveAsync()).Type);

        // Quem não é GM não usa.
        await b.SendAsync(CommandPacket("gm", "gold 5"));
        Assert.Equal("Comando só para GMs.", (await b.ReceiveAsync()).ReadString(GameConstants.MessageLength));

        (await _store.FindByUsernameAsync("jogadora"))!.AccessLevel = 1;

        await a.SendAsync(CommandPacket("gm", "nivel 3"));     // tabela do teste: 100/200/300/400
        var score = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateScore, score.Type);
        Assert.Equal(2, score.ReadInt16());
        var etc = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateEtc, etc.Type);
        etc.Skip(4);
        Assert.Equal(200, etc.ReadInt32());
        Assert.Equal(Opcodes.UpdateHpMp, (await a.ReceiveAsync()).Type);
        Assert.Equal("Agora você está no nível 3.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

        await a.SendAsync(CommandPacket("gm", "gold 123456"));
        var coin = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateCoin, coin.Type);
        Assert.Equal(123456, coin.ReadInt32());
        await a.ReceiveAsync();
        Assert.Equal(123456, (await CharacterOf("jogadora")).Coin);

        // Teleporte para longe: Bruno deixa de vê-la.
        await a.SendAsync(CommandPacket("gm", "ir 2500 2600"));
        Assert.Equal(Opcodes.RemoveMob, (await a.ReceiveAsync()).Type);
        var teleport = await a.ReceiveAsync();
        Assert.Equal(Opcodes.Move, teleport.Type);
        Assert.Equal((2500, 2600), (teleport.ReadInt16(), teleport.ReadInt16()));
        Assert.Equal(Opcodes.RemoveMob, (await b.ReceiveAsync()).Type);

        // Trazer o Bruno: os dois voltam a se ver.
        await a.SendAsync(CommandPacket("gm", "trazer bruno"));
        var moved = await b.ReceiveAsync();
        Assert.Equal(Opcodes.Move, moved.Type);
        Assert.Equal((2500, 2600), (moved.ReadInt16(), moved.ReadInt16()));
        Assert.Equal(Opcodes.CreateMob, (await a.ReceiveAsync()).Type);

        await a.SendAsync(CommandPacket("gm", "kick ninguem"));
        Assert.Equal("ninguem não está online.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
    }

    private async Task<Character> CharacterOf(string account) =>
        (await _store.ListByAccountAsync((await _store.FindByUsernameAsync(account))!.Id))[0];

    private static byte[] UsePacket(int type, int slot) =>
        new PacketWriter(Opcodes.UseItem, 36).WriteInt32(type).WriteInt32(slot).Skip(16).ToArray();

    private static byte[] MoveItemPacket(byte destType, byte destPos, byte srcType, byte srcPos) =>
        new PacketWriter(Opcodes.MoveItem, 20).WriteByte(destType).WriteByte(destPos).WriteByte(srcType).WriteByte(srcPos).Skip(4).ToArray();

    [Fact]
    public async Task Gold_bar_and_potion_work()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        var c = await CharacterOf("jogadora");
        c.Inventory[3] = new Item(4011);
        c.Inventory[4] = new Item(400) { Effect1 = ItemHandler.EfAmount, Value1 = 2 }; // 2 poções empilhadas
        c.Inventory[5] = new Item(5000);
        c.BaseScore.Hp = 10;

        await a.SendAsync(UsePacket(1, 3));
        var slot = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateItem, slot.Type);
        slot.Skip(4);
        Assert.Equal(0, slot.ReadInt16());                     // a barra sumiu
        var coin = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateCoin, coin.Type);
        Assert.Equal(1_000_000_000, coin.ReadInt32());
        Assert.Equal(1_000_000_000, c.Coin);

        await a.SendAsync(UsePacket(1, 4));
        var potion = await a.ReceiveAsync();
        potion.Skip(4);
        Assert.Equal(400, potion.ReadInt16());                 // sobrou 1 poção
        Assert.Equal((ItemHandler.EfAmount, 1), (potion.ReadByte(), potion.ReadByte()));
        var hp = await a.ReceiveAsync();
        Assert.Equal(Opcodes.UpdateHpMp, hp.Type);
        Assert.Equal(60, hp.ReadInt32());

        await a.SendAsync(UsePacket(1, 5));
        Assert.Equal("Pedra Sem Uso ainda não tem função neste servidor.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
    }

    [Fact]
    public async Task Equip_items_respecting_slot_and_level()
    {
        using var a = await EnterWorldAsync("jogadora", "Alice");
        var c = await CharacterOf("jogadora");
        c.Inventory[0] = new Item(905);   // espada (Pos 64 = slot 6)
        c.Inventory[1] = new Item(1300);  // espada de nível 99

        // Espada no slot do elmo: recusado.
        await a.SendAsync(MoveItemPacket(0, 1, 1, 0));
        Assert.Equal("Espada Longa não pode ser equipado aí.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

        // No slot da arma: o servidor devolve o pedido e o item muda de lugar.
        await a.SendAsync(MoveItemPacket(0, 6, 1, 0));
        var ok = await a.ReceiveAsync();
        Assert.Equal(Opcodes.MoveItem, ok.Type);
        Assert.Equal((0, 6, 1, 0), (ok.ReadByte(), ok.ReadByte(), ok.ReadByte(), ok.ReadByte()));
        Assert.Equal(905, c.Equip[6].Index);
        Assert.True(c.Inventory[0].IsEmpty);
        Assert.Equal(Opcodes.UpdateScore, (await a.ReceiveAsync()).Type);   // ficha com a velocidade nova

        // Nível insuficiente e rosto: recusados.
        await a.SendAsync(MoveItemPacket(0, 6, 1, 1));
        Assert.Equal("Espada Lendaria precisa do nível 99.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));
        await a.SendAsync(MoveItemPacket(1, 10, 0, 0));
        Assert.Equal("O rosto do personagem não pode ser trocado.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

        // Desequipar para o inventário.
        await a.SendAsync(MoveItemPacket(1, 7, 0, 6));
        Assert.Equal(Opcodes.MoveItem, (await a.ReceiveAsync()).Type);
        Assert.Equal(905, c.Inventory[7].Index);
    }

    [Fact]
    public async Task Buy_and_sell_at_a_shop()
    {
        var npcDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(npcDir, "Ferreiro"),
                NpcDataTests.NpcFile("Ferreiro", 58, 1, 100, merchant: 1, carry: [(0, 905), (27, 1208)]));
            await _host.Services.GetRequiredService<MobService>().SpawnAllAsync(
                [new() { Leader = "Ferreiro", MaxNumMob = 1, StartX = 2102, StartY = 2100 }], npcDir);

            using var a = await EnterWorldAsync("jogadora", "Alice");
            var created = await a.ReceiveAsync();
            created.Skip(4);
            ushort npc = created.ReadUInt16();
            var c = await CharacterOf("jogadora");

            // Sem gold: recusado.
            await a.SendAsync(new PacketWriter(Opcodes.Buy, 24).WriteUInt16(npc).WriteInt16(0).WriteInt16(2).Skip(6).ToArray());
            Assert.Equal("Gold insuficiente: Espada Longa custa 3500.", (await a.ReceiveAsync()).ReadString(GameConstants.MessageLength));

            c.Coin = 10_000;
            await a.SendAsync(new PacketWriter(Opcodes.Buy, 24).WriteUInt16(npc).WriteInt16(0).WriteInt16(2).Skip(6).ToArray());
            var buy = await a.ReceiveAsync();
            Assert.Equal(Opcodes.Buy, buy.Type);
            Assert.Equal((npc, (short)0, (short)2), (buy.ReadUInt16(), buy.ReadInt16(), buy.ReadInt16()));
            buy.Skip(2);
            Assert.Equal(6_500, buy.ReadInt32());                       // gold total depois da compra
            Assert.Equal(Opcodes.UpdateItem, (await a.ReceiveAsync()).Type);
            Assert.Equal(6_500, (await a.ReceiveAsync()).ReadInt32());  // 0x3AF
            Assert.Equal(905, c.Inventory[2].Index);

            // Slot que não é da loja: ignorado.
            await a.SendAsync(new PacketWriter(Opcodes.Buy, 24).WriteUInt16(npc).WriteInt16(10).WriteInt16(3).Skip(6).ToArray());

            // Vender devolve 1/4 do preço.
            await a.SendAsync(new PacketWriter(Opcodes.Sell, 20).WriteUInt16(npc).WriteInt16(1).WriteInt16(2).Skip(2).ToArray());
            Assert.Equal(Opcodes.Sell, (await a.ReceiveAsync()).Type);
            var emptied = await a.ReceiveAsync();
            emptied.Skip(4);
            Assert.Equal(0, emptied.ReadInt16());
            Assert.Equal(6_500 + 3_500 / 4, (await a.ReceiveAsync()).ReadInt32());
            Assert.True(c.Inventory[2].IsEmpty);
        }
        finally
        {
            Directory.Delete(npcDir, true);
        }
    }

    private static byte[] LoginPacket(string username, string password, int version) =>
        new AccountLoginRequest(username, password, version).Write();

    private static byte[] DeletePacket(int slot, string name, string password) =>
        new PacketWriter(Opcodes.DeleteCharacter, DeleteCharacterRequest.PacketSize)
            .WriteInt32(slot).WriteString(name, 16).WriteString(password, 12)
            .ToArray();
}

/// <summary>Client mínimo: envia o INITCODE, cifra o que manda e decifra o que recebe.</summary>
internal sealed class FakeClient : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly KeyTable _table;
    private bool _initSent;

    /// <summary>Forma de checksum que este client usa ao enviar.</summary>
    public ChecksumMode Mode { get; set; } = ChecksumMode.CipherMinusPlain;

    /// <summary>Forma de checksum do último pacote recebido do servidor.</summary>
    public ChecksumMode? LastReceivedMode { get; private set; }

    private FakeClient(TcpClient tcp, KeyTable table)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _table = table;
    }

    public static async Task<FakeClient> ConnectAsync(IPEndPoint endPoint, KeyTable table)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(endPoint);
        return new FakeClient(tcp, table);
    }

    public async Task SendAsync(byte[] packet)
    {
        var data = (byte[])packet.Clone();
        PacketCipher.Encrypt(data, _table, (byte)Random.Shared.Next(256), Mode);
        if (!_initSent)
        {
            var init = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(init, PacketHeader.InitCode);
            data = [.. init, .. data];
            _initSent = true;
        }
        await _stream.WriteAsync(data);
    }

    public async Task<PacketReader> ReceiveAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sizeBytes = new byte[2];
        await _stream.ReadExactlyAsync(sizeBytes, timeout.Token);
        int size = BinaryPrimitives.ReadUInt16LittleEndian(sizeBytes);

        var packet = new byte[size];
        sizeBytes.CopyTo(packet, 0);
        await _stream.ReadExactlyAsync(packet.AsMemory(2), timeout.Token);

        Assert.True(PacketCipher.Decrypt(packet, _table, out var mode), "checksum do servidor não confere");
        LastReceivedMode = mode;
        return new PacketReader(packet);
    }

    public async Task<bool> IsClosedAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            return await _stream.ReadAsync(new byte[1], timeout.Token) == 0;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public void Dispose() => _tcp.Dispose();
}

/// <summary>Repositórios em memória para os testes não dependerem do MySQL.</summary>
internal sealed class InMemoryStore : IAccountRepository, ICharacterRepository
{
    private readonly ConcurrentDictionary<string, Account> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Character> _characters = [];
    private int _nextId;

    public Task<Account?> FindByUsernameAsync(string username) =>
        Task.FromResult(_accounts.TryGetValue(username, out var a) ? a : null);

    public Task<Account> CreateAsync(string username, string passwordHash)
    {
        var account = new Account { Id = Interlocked.Increment(ref _nextId), Username = username, PasswordHash = passwordHash };
        _accounts[username] = account;
        return Task.FromResult(account);
    }

    public Task UpdateLastLoginAsync(int accountId, string? ip) => Task.CompletedTask;

    public Task SetAccessLevelAsync(int accountId, int level)
    {
        foreach (var a in _accounts.Values.Where(a => a.Id == accountId))
            a.AccessLevel = level;
        return Task.CompletedTask;
    }

    public Task SaveInventoryAsync(int characterId, Item[] inventory)
    {
        lock (_characters)
            foreach (var c in _characters.Where(c => c.Id == characterId))
                c.Inventory = (Item[])inventory.Clone();
        return Task.CompletedTask;
    }

    public Task SaveExpAsync(int characterId, long exp)
    {
        lock (_characters)
            foreach (var c in _characters.Where(c => c.Id == characterId))
                c.Exp = exp;
        return Task.CompletedTask;
    }

    public Task SaveItemsAsync(Character character) => Task.CompletedTask; // os objetos já são os mesmos da sessão

    public Task SaveProgressAsync(Character character) => Task.CompletedTask;

    public Task SaveCargoAsync(Account account) => Task.CompletedTask;

    public Task SetSecondPasswordAsync(int accountId, string? hash)
    {
        foreach (var a in _accounts.Values.Where(a => a.Id == accountId))
            a.SecondPasswordHash = hash;
        return Task.CompletedTask;
    }

    public Task<List<Character>> ListByAccountAsync(int accountId)
    {
        lock (_characters) return Task.FromResult(_characters.Where(c => c.AccountId == accountId).OrderBy(c => c.Slot).ToList());
    }

    public Task<bool> NameExistsAsync(string name)
    {
        lock (_characters) return Task.FromResult(_characters.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<bool> CreateAsync(Character character)
    {
        lock (_characters)
        {
            if (_characters.Any(c => c.Name.Equals(character.Name, StringComparison.OrdinalIgnoreCase)
                                     || (c.AccountId == character.AccountId && c.Slot == character.Slot)))
                return Task.FromResult(false);
            character.Id = Interlocked.Increment(ref _nextId);
            _characters.Add(character);
            return Task.FromResult(true);
        }
    }

    public Task SavePositionAsync(int characterId, short x, short y)
    {
        lock (_characters)
            foreach (var c in _characters.Where(c => c.Id == characterId))
                (c.PosX, c.PosY) = (x, y);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(int accountId, int slot)
    {
        lock (_characters) return Task.FromResult(_characters.RemoveAll(c => c.AccountId == accountId && c.Slot == slot) > 0);
    }
}
