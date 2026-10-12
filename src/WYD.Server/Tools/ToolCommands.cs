using System.Net;
using System.Net.Sockets;
using WYD.Server.Network;

namespace WYD.Server.Tools;

/// <summary>Comandos que não precisam do banco: serverlist e find-keytable.</summary>
public static class ToolCommands
{
    public const string CapturesDirectory = "data/capturas";
    public const string KeyTableOutputPath = "data/KeyTable.bin";

    /// <summary>serverlist &lt;arquivo&gt; [novo-ip]</summary>
    public static int ServerListCommand(string path, string? newIp)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Arquivo não encontrado: {path}");
            return 1;
        }

        var data = File.ReadAllBytes(path);
        var codec = ServerList.Detect(data);
        var plain = codec.Decode(data);
        var strings = ServerList.Strings(plain);

        Console.WriteLine($"{path}: {data.Length} bytes, chave de {codec.Period} byte(s) por {codec.Method}, {codec.Score:P1} legível.");
        if (codec.Score < 0.9 || strings.Count == 0)
        {
            Console.WriteLine("Não consegui decodificar este arquivo. Mande para o desenvolvedor a saída de:");
            Console.WriteLine($"  Format-Hex \"{path}\" | Select-Object -First 40");
            return 1;
        }

        PrintStrings("Endereços encontrados:", strings);
        if (newIp is null) return 0;

        if (!IPAddress.TryParse(newIp, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            Console.Error.WriteLine($"IP inválido: {newIp}");
            return 2;
        }

        int count;
        try
        {
            count = ServerList.ReplaceIps(plain, newIp);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (count == 0)
        {
            Console.WriteLine("Nenhum IP encontrado para trocar.");
            return 1;
        }

        var encoded = codec.Encode(plain);
        string backup = path + ".bak";
        if (!File.Exists(backup)) File.Copy(path, backup);
        File.WriteAllBytes(path, encoded);

        PrintStrings($"{count} IP(s) trocado(s) por {newIp}. Backup do original em {backup}.", ServerList.Strings(plain));
        return 0;
    }

    /// <summary>find-keytable &lt;WYD.exe ou dump&gt; [--port 8281]</summary>
    public static async Task<int> FindKeyTableAsync(string clientFile, int port)
    {
        if (!File.Exists(clientFile))
        {
            Console.Error.WriteLine($"Arquivo não encontrado: {clientFile}");
            return 1;
        }

        var file = await File.ReadAllBytesAsync(clientFile);
        Directory.CreateDirectory(CapturesDirectory);

        // Logins capturados em execuções anteriores (por exemplo, antes de tentar com um dump de memória).
        var logins = Directory.GetFiles(CapturesDirectory, "login-*.bin").Order()
            .Select(f => KeyTableFinder.ExtractFirstPacket(File.ReadAllBytes(f)))
            .OfType<byte[]>()
            .ToList();

        List<KeyTableFinder.Match>? candidates = null;
        if (logins.Count > 0)
        {
            Console.WriteLine($"Usando {logins.Count} login(s) já capturado(s) em {CapturesDirectory}. Procurando em {clientFile}...");
            candidates = KeyTableFinder.Search(file, logins);
            if (candidates.Count == 0) return NotFound(clientFile, logins[^1], fromOldCaptures: true);
        }

        TcpListener? listener = null;
        try
        {
            for (int attempt = 1; candidates is null || candidates.Count > 1; attempt++)
            {
                if (attempt > 6)
                {
                    Console.Error.WriteLine("Ainda há mais de uma tabela possível depois de várias tentativas. Mande este log para o desenvolvedor.");
                    return 1;
                }

                if (candidates is not null)
                    Console.WriteLine($"Achei {candidates.Count} tabelas possíveis. Para saber qual é a certa, tente logar de novo no client.");

                listener ??= StartListener(port);
                if (listener is null) return 1;

                var raw = await CaptureLoginAsync(listener);
                if (raw is null) return 1;

                string capturePath = Path.Combine(CapturesDirectory, $"login-{DateTime.Now:yyyyMMdd-HHmmss-fff}.bin");
                await File.WriteAllBytesAsync(capturePath, raw);
                var login = KeyTableFinder.ExtractFirstPacket(raw)!;

                if (candidates is null)
                {
                    Console.WriteLine($"Login capturado. Procurando a KeyTable em {clientFile}...");
                    candidates = KeyTableFinder.Search(file, [login]);
                    if (candidates.Count == 0) return NotFound(clientFile, login, fromOldCaptures: false);
                }
                else
                {
                    candidates = KeyTableFinder.Filter(candidates, login);
                    if (candidates.Count == 0)
                    {
                        Console.Error.WriteLine($"Os logins capturados não batem entre si. Apague a pasta {CapturesDirectory} e rode de novo.");
                        return 1;
                    }
                }
            }
        }
        finally
        {
            listener?.Stop();
        }

        var match = candidates[0];
        Console.WriteLine($"KeyTable encontrada na posição 0x{match.Offset:X} (conta '{match.Username}', versão do client {match.ClientVersion}).");
        await File.WriteAllBytesAsync(KeyTableOutputPath, match.Table);
        Console.WriteLine($"Salva em {KeyTableOutputPath}. Agora é só iniciar o servidor.");
        return 0;
    }

    private static int NotFound(string clientFile, byte[] login, bool fromOldCaptures)
    {
        Console.WriteLine($"""
            KeyTable não encontrada em {clientFile}.
            O executável provavelmente está protegido (compactado). Tente com um dump de memória do client:
              1. Abra o client e deixe na tela de login.
              2. No Gerenciador de Tarefas, aba Detalhes, clique com o botão direito no processo do WYD
                 e escolha "Criar arquivo de despejo". Ele mostra onde salvou o arquivo .DMP.
              3. Rode de novo apontando para o .DMP (o login já capturado será reaproveitado):
                 dotnet run --project src\WYD.Server -- find-keytable "<caminho do .DMP>"
            """);
        if (fromOldCaptures)
            Console.WriteLine($"Se você trocou de client, apague a pasta {CapturesDirectory} antes, para capturar de novo.");
        Console.WriteLine("Se também não achar no .DMP, mande para o desenvolvedor o pacote capturado:");
        Console.WriteLine(HexDump.Format(login));
        return 1;
    }

    private static TcpListener? StartListener(int port)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        try
        {
            listener.Start();
            return listener;
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"Não consegui usar a porta {port} ({ex.Message}). Feche o servidor WYD se ele estiver aberto.");
            return null;
        }
    }

    /// <summary>Espera um client conectar e mandar o primeiro pacote; depois fecha a conexão.</summary>
    private static async Task<byte[]?> CaptureLoginAsync(TcpListener listener)
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Console.WriteLine($"Esperando o client na porta {port}. No jogo, escolha o servidor e tente logar com a sua conta...");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            Console.WriteLine($"Client conectado de {client.Client.RemoteEndPoint}.");

            var stream = client.GetStream();
            var buffer = new MemoryStream();
            var chunk = new byte[4096];
            while (true)
            {
                var raw = buffer.ToArray();
                if (KeyTableFinder.ExtractFirstPacket(raw) is not null) return raw;

                int read = await stream.ReadAsync(chunk, timeout.Token);
                if (read == 0) break;
                buffer.Write(chunk, 0, read);
            }

            Console.Error.WriteLine("O client desconectou antes de mandar o login. Bytes recebidos:");
            Console.Error.WriteLine(HexDump.Format(buffer.ToArray()));
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Nenhum client conectou em 10 minutos. Confira se o serverlist aponta para este PC (comando serverlist).");
        }
        return null;
    }

    private static void PrintStrings(string title, List<(int Offset, string Text)> strings)
    {
        Console.WriteLine(title);
        foreach (var (offset, text) in strings)
            Console.WriteLine($"  [{offset,5}] {text}");
    }
}
