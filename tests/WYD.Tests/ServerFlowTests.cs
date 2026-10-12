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
        await client.SendAsync(LoginPacket("jogador", "senha123", 754));
        Assert.Equal(Opcodes.AccountLoginOk, (await client.ReceiveAsync()).Type);

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

        // O login é o primeiro pacote; se o checksum dele for ambíguo (os dois modos dão o mesmo valor),
        // o servidor ainda não sabe o modo. Por isso confere alguns pacotes seguidos.
        Assert.Equal(Opcodes.AccountLoginOk, (await client.ReceiveAsync()).Type);
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
            _characters.Add(character);
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteAsync(int accountId, int slot)
    {
        lock (_characters) return Task.FromResult(_characters.RemoveAll(c => c.AccountId == accountId && c.Slot == slot) > 0);
    }
}
