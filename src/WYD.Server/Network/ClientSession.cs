using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using WYD.Server.Config;
using WYD.Server.Game;
using WYD.Server.Protocol;

namespace WYD.Server.Network;

public enum SessionState
{
    /// <summary>Conectado, aguardando o login da conta.</summary>
    Connected,
    /// <summary>Conta autenticada, na tela de seleção de personagem.</summary>
    CharacterSelect,
    /// <summary>Personagem dentro do mundo.</summary>
    InGame,
}

/// <summary>Uma conexão de client: lê, decifra e despacha os pacotes, e cifra os que são enviados.</summary>
public sealed class ClientSession
{
    private readonly Socket _socket;
    private readonly KeyTable _keyTable;
    private readonly ServerOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private int _closed;

    public ClientSession(ushort id, Socket socket, KeyTable keyTable, ServerOptions options, ILogger logger)
    {
        Id = id;
        _socket = socket;
        _keyTable = keyTable;
        _options = options;
        _logger = logger;
        RemoteEndPoint = socket.RemoteEndPoint as IPEndPoint;
    }

    public ushort Id { get; }
    public IPEndPoint? RemoteEndPoint { get; }
    public SessionState State { get; set; } = SessionState.Connected;
    public Account? Account { get; set; }
    public List<Character> Characters { get; set; } = [];
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public event Action<ClientSession>? Closed;

    public async Task RunAsync(Func<ClientSession, PacketReader, Task> dispatch, CancellationToken serverToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverToken, _cts.Token);
        var token = linked.Token;
        var framer = new PacketFramer();
        var buffer = new byte[4096];

        try
        {
            while (!token.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                idle.CancelAfter(TimeSpan.FromSeconds(_options.IdleTimeoutSeconds));

                int read;
                try
                {
                    read = await _socket.ReceiveAsync(buffer, SocketFlags.None, idle.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    _logger.LogInformation("[{Id}] Desconectado por inatividade.", Id);
                    break;
                }

                if (read == 0) break;
                framer.Append(buffer.AsSpan(0, read));

                while (framer.TryRead() is { } packet)
                {
                    if (!PacketCipher.Decrypt(packet, _keyTable))
                    {
                        if (_options.StrictChecksum)
                            throw new ProtocolException("Checksum inválido.");
                        _logger.LogDebug("[{Id}] Checksum não confere no pacote {Type}.", Id, Opcodes.Name(PacketHeader.ReadType(packet)));
                    }

                    if (_options.LogPackets)
                        _logger.LogInformation("[{Id}] RECV {Type} ({Size} bytes)\n{Hex}", Id,
                            Opcodes.Name(PacketHeader.ReadType(packet)), packet.Length, HexDump.Format(packet));

                    await dispatch(this, new PacketReader(packet));
                    if (IsClosed) return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ProtocolException ex)
        {
            _logger.LogWarning("[{Id}] Erro de protocolo de {Remote}: {Message}", Id, RemoteEndPoint, ex.Message);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug("[{Id}] Socket: {Message}", Id, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Id}] Erro ao processar pacote de {Remote}.", Id, RemoteEndPoint);
        }
        finally
        {
            Close();
        }
    }

    /// <summary>Cifra e envia um pacote montado com <see cref="PacketWriter"/>.</summary>
    public async Task SendAsync(byte[] packet)
    {
        if (IsClosed) return;

        if (_options.LogPackets)
            _logger.LogInformation("[{Id}] SEND {Type} ({Size} bytes)\n{Hex}", Id,
                Opcodes.Name(PacketHeader.ReadType(packet)), packet.Length, HexDump.Format(packet));

        var data = (byte[])packet.Clone();
        PacketCipher.Encrypt(data, _keyTable, (byte)RandomNumberGenerator.GetInt32(256));

        await _sendLock.WaitAsync();
        try
        {
            int sent = 0;
            while (sent < data.Length)
                sent += await _socket.SendAsync(data.AsMemory(sent), SocketFlags.None);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            Close();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public Task SendMessageAsync(string text) => SendAsync(ServerPackets.MessagePanel(text, Id));

    /// <summary>Envia uma mensagem e fecha a conexão logo depois.</summary>
    public async Task KickAsync(string message)
    {
        await SendMessageAsync(message);
        Close();
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _cts.Cancel();
        try { _socket.Shutdown(SocketShutdown.Both); } catch { }
        _socket.Dispose();
        Closed?.Invoke(this);
    }
}
