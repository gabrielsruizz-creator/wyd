using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;

namespace WYD.Server.Network;

/// <summary>
/// Responde as páginas de status (serv00.asp, serv01.asp…) que o client consulta na tela de servidores.
/// Sem resposta, todos os canais aparecem "Inexistente." e o client se recusa a conectar.
/// O corpo é uma lista de números, um por canal, com a quantidade de jogadores (-1 = canal desligado).
/// </summary>
public sealed class StatusHttpServer
{
    private readonly int _port;
    private readonly Func<string> _body;
    private readonly Action<string> _log;

    public StatusHttpServer(int port, Func<string> body, Action<string> log)
    {
        _port = port;
        _body = body;
        _log = log;
    }

    /// <summary>Corpo padrão: o número de jogadores online repetido para cada canal, um por linha.</summary>
    public static string BuildBody(int channels, int online) =>
        string.Concat(Enumerable.Repeat($"{online}\n", channels));

    /// <summary>Tenta abrir a porta. Retorna null (e explica no log) se ela estiver ocupada.</summary>
    public TcpListener? Start()
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        try
        {
            listener.Start();
            _log($"Status dos canais (HTTP) na porta {_port}.");
            return listener;
        }
        catch (SocketException ex)
        {
            _log($"Não consegui abrir a porta {_port} para o status dos canais ({ex.Message}). " +
                 "Feche o programa que está usando essa porta (IIS, Skype, outro servidor web) ou mude Server:StatusPort.");
            return null;
        }
    }

    public async Task RunAsync(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = HandleAsync(client, token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var stream = client.GetStream();

                // Lê só até o fim dos cabeçalhos; o pedido é sempre um GET simples.
                var request = new StringBuilder();
                var buffer = new byte[2048];
                while (!request.ToString().Contains("\r\n\r\n") && request.Length < 16384)
                {
                    int read = await stream.ReadAsync(buffer, timeout.Token);
                    if (read == 0) break;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                string firstLine = request.ToString().Split("\r\n")[0];
                string body = _body();
                _log($"Status pedido por {client.Client.RemoteEndPoint}: {firstLine}");

                var bodyBytes = Encoding.ASCII.GetBytes(body);
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/html\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "Cache-Control: no-cache\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(header, timeout.Token);
                await stream.WriteAsync(bodyBytes, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) { }
        }
    }
}

/// <summary>Liga o <see cref="StatusHttpServer"/> junto com o servidor do jogo.</summary>
public sealed class StatusService(IOptions<ServerOptions> options, SessionManager sessions, ILogger<StatusService> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (o.StatusPort <= 0) return Task.CompletedTask;

        var server = new StatusHttpServer(
            o.StatusPort,
            () => string.IsNullOrEmpty(o.StatusBody)
                ? StatusHttpServer.BuildBody(o.StatusChannels, sessions.Count)
                : o.StatusBody.Replace("{online}", sessions.Count.ToString()).Replace("\\n", "\n"),
            message => logger.LogInformation("{Message}", message));

        var listener = server.Start();
        return listener is null ? Task.CompletedTask : server.RunAsync(listener, stoppingToken);
    }
}
