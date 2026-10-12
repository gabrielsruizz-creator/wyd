using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;
using WYD.Server.Game;
using WYD.Server.Handlers;

namespace WYD.Server.Network;

/// <summary>Aceita conexões TCP e cria uma <see cref="ClientSession"/> para cada client.</summary>
public sealed class GameServer : BackgroundService
{
    private readonly ServerOptions _options;
    private readonly KeyTable _keyTable;
    private readonly SessionManager _sessions;
    private readonly PacketDispatcher _dispatcher;
    private readonly World _world;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GameServer> _logger;
    private readonly TaskCompletionSource<IPEndPoint> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public GameServer(
        IOptions<ServerOptions> options,
        KeyTable keyTable,
        SessionManager sessions,
        PacketDispatcher dispatcher,
        World world,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _keyTable = keyTable;
        _sessions = sessions;
        _dispatcher = dispatcher;
        _world = world;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<GameServer>();
    }

    /// <summary>Completa quando o servidor começa a escutar (usado nos testes com porta 0).</summary>
    public Task<IPEndPoint> Started => _started.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endPoint = new IPEndPoint(IPAddress.Parse(_options.BindAddress), _options.Port);
        using var listener = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        try
        {
            listener.Bind(endPoint);
            listener.Listen(128);
        }
        catch (Exception ex)
        {
            _started.TrySetException(ex);
            throw;
        }

        var bound = (IPEndPoint)listener.LocalEndPoint!;
        _logger.LogInformation("Servidor WYD escutando em {EndPoint} (versões aceitas: {Versions}).",
            bound, string.Join(", ", _options.ClientVersions));
        _started.TrySetResult(bound);

        var sessionLogger = _loggerFactory.CreateLogger<ClientSession>();

        while (!stoppingToken.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await listener.AcceptAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            socket.NoDelay = true;
            ushort id = _sessions.ReserveId();
            if (id == 0)
            {
                _logger.LogWarning("Servidor cheio, recusando {Remote}.", socket.RemoteEndPoint);
                socket.Dispose();
                continue;
            }

            var session = new ClientSession(id, socket, _keyTable, _options, sessionLogger);
            _sessions.Add(session);
            _logger.LogInformation("[{Id}] Conexão de {Remote}. Online: {Count}.", id, session.RemoteEndPoint, _sessions.Count);

            _ = session.RunAsync(_dispatcher.DispatchAsync, stoppingToken);
        }

        // Desligando: grava a posição de quem está no mapa antes de derrubar as conexões.
        await _world.SaveAllAsync();
        foreach (var session in _sessions.All.ToList())
            session.Close();
    }
}
