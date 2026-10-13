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
        });
        builder.Services.AddWydServer(builder.Configuration);
        builder.Services.AddSingleton(_table);
        builder.Services.AddSingleton<IAccountRepository>(_store);
        builder.Services.AddSingleton<ICharacterRepository>(_store);

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
