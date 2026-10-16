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
    private ChecksumMode _checksumMode = ChecksumMode.CipherMinusPlain;
    private bool _checksumModeKnown;
    private string _closeReason = "conexão encerrada pelo servidor";

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

    /// <summary>A segunda senha foi conferida nesta conexão (libera criar, apagar e entrar no mundo).</summary>
    public bool SecondPasswordVerified { get; set; }
    public int SecondPasswordFailures { get; set; }

    /// <summary>Hora do último ataque aceito (para limitar a velocidade dos ataques).</summary>
    public DateTime LastAttack { get; set; }

    /// <summary>
    /// Onde o jogador estava antes do último teleporte, até ele andar no lugar novo. Se o client cair antes disso
    /// (destino que o client não consegue carregar), a posição salva volta a ser esta.
    /// </summary>
    public (short X, short Y, DateTime At)? TeleportedFrom { get; set; }

    /// <summary>Já avisou que o servidor está sem a tabela de experiência.</summary>
    public bool WarnedNoExpTable { get; set; }

    /// <summary>Já avisou que o servidor está sem a SkillData.bin.</summary>
    public bool WarnedNoSkillData { get; set; }

    /// <summary>Personagem em jogo (depois do 0x213).</summary>
    public Character? Character { get; set; }
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
                    _closeReason = "inatividade";
                    break;
                }

                if (read == 0)
                {
                    _closeReason = "o client fechou a conexão";
                    break;
                }
                framer.Append(buffer.AsSpan(0, read));

                while (framer.TryRead() is { } packet)
                {
                    if (PacketCipher.Decrypt(packet, _keyTable, out var mode))
                    {
                        // Responde com o checksum calculado do mesmo jeito que o client calcula o dele.
                        if (mode is { } detected && !_checksumModeKnown)
                        {
                            _checksumMode = detected;
                            _checksumModeKnown = true;
                            _logger.LogInformation("[{Id}] Checksum do client: {Mode}.", Id, detected);
                        }
                    }
                    else
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
            _closeReason = $"erro de protocolo: {ex.Message}";
        }
        catch (SocketException ex)
        {
            _closeReason = $"erro de rede: {ex.Message}";
        }
        catch (Exception ex)
        {
            _closeReason = "erro no servidor";
            _logger.LogError(ex, "[{Id}] Erro ao processar pacote de {Remote}.", Id, RemoteEndPoint);
        }
        finally
        {
            _logger.LogInformation("[{Id}] Desconectado: {Reason}.", Id, _closeReason);
            Close();
        }
    }

    /// <summary>Cifra e envia um pacote montado com <see cref="PacketWriter"/>.</summary>
    public async Task SendAsync(byte[] packet)
    {
        if (IsClosed) return;

        var data = (byte[])packet.Clone();
        PacketHeader.WriteTick(data, PacketHeader.ServerTick);

        if (_options.LogPackets)
            _logger.LogInformation("[{Id}] SEND {Type} ({Size} bytes)\n{Hex}", Id,
                Opcodes.Name(PacketHeader.ReadType(data)), data.Length, HexDump.Format(data));

        PacketCipher.Encrypt(data, _keyTable, (byte)RandomNumberGenerator.GetInt32(256), _checksumMode);

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

    /// <summary>
    /// Mensagem na faixa do alto da tela (4 s). O id do cabeçalho tem de ser 0: com outro id o client 7.54 ignora
    /// o 0x101 (0x499438).
    /// </summary>
    public Task SendMessageAsync(string text) => SendAsync(ServerPackets.MessagePanel(text));

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
