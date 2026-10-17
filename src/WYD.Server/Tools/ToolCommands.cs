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
            count = ServerList.ReplaceAddresses(plain, newIp);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (count == 0)
        {
            Console.WriteLine("Nenhum endereço encontrado para trocar.");
            return 1;
        }

        var encoded = codec.Encode(plain);
        string backup = path + ".bak";
        if (!File.Exists(backup)) File.Copy(path, backup);
        File.WriteAllBytes(path, encoded);

        PrintStrings($"{count} endereço(s) apontado(s) para {newIp}. Backup do original em {backup}.", ServerList.Strings(plain));
        return 0;
    }

    /// <summary>find-keytable &lt;WYD.exe ou dump&gt; [--port 8281] [--status "texto"]</summary>
    public static async Task<int> FindKeyTableAsync(string clientFile, int port, string? statusBody = null)
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

        // O client só tenta conectar se a página de status disser que o canal existe.
        using var statusCts = new CancellationTokenSource();
        string body = statusBody?.Replace("\\n", "\n") ?? StatusHttpServer.BuildBody(11, 0);
        var status = new StatusHttpServer(80, () => body, Console.WriteLine);
        if (status.Start() is { } statusListener)
            _ = status.RunAsync(statusListener, statusCts.Token);

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
            statusCts.Cancel();
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

    /// <summary>items &lt;ItemList.bin&gt; [texto]: lista os itens, ou só os que têm o texto no nome.</summary>
    public static int ItemsCommand(string path, string? filter)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Arquivo não encontrado: {path}");
            return 1;
        }

        Data.ItemList list;
        try
        {
            list = Data.ItemList.Load(path, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "ExtraItem.bin"));
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var items = list.All.Where(i => filter is null || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"{path}: {list.All.Count()} itens, checksum 0x{list.Checksum:X} " +
                          (list.MatchesClient ? "(igual ao esperado pelo client 7.54)." : $"(o client 7.54 espera 0x{Data.ItemList.ClientChecksum:X})."));
        Console.WriteLine($"{"Índice",6}  {"Nome",-32} {"Modelo",6} {"Nível",5} {"Pos",6} {"Preço",10}");
        foreach (var i in items)
            Console.WriteLine($"{i.Index,6}  {i.Name,-32} {i.Mesh,6} {i.ReqLevel,5} {i.Pos,6} {i.Price,10}");
        if (filter is not null)
            Console.WriteLine($"{items.Count} item(ns) com \"{filter}\".");
        return 0;
    }

    /// <summary>
    /// import-itemlist &lt;ItemList de outro client&gt; &lt;saída&gt;: decifra (chave descoberta sozinha) e grava no
    /// formato do client 7.54, com o checksum que ele exige. A saída que já existir vira .bak.
    /// </summary>
    public static int ImportItemListCommand(string source, string destination)
    {
        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"Arquivo não encontrado: {source}");
            return 1;
        }
        var result = ItemListImport.Convert(File.ReadAllBytes(source));
        if (result is null)
        {
            Console.Error.WriteLine($"{source}: não consegui decifrar (não parece uma ItemList de 6500 itens de 140 bytes).");
            return 1;
        }
        if (File.Exists(destination)) File.Copy(destination, destination + ".bak", overwrite: true);
        File.WriteAllBytes(destination, result.File);
        var list = Data.ItemList.Load(destination);
        Console.WriteLine($"{destination}: {list.All.Count()} itens (chave de {result.KeyLength} byte(s)); " +
                          $"checksum 0x{list.Checksum:X} {(list.MatchesClient ? "aceito pelo client 7.54" : "DIFERENTE do client")}, " +
                          $"{result.AdjustedBytes} bytes acertados em itens vazios.");
        return list.MatchesClient ? 0 : 1;
    }

    /// <summary>
    /// import-client &lt;pasta do outro client&gt; &lt;pasta do client&gt;: copia o visual (ver <see cref="ClientImport"/>),
    /// converte a ItemList para o client e para data/ItemList.bin do servidor e tira o Itemname.bin (os nomes passam a
    /// vir da ItemList). Tudo o que for substituído vai para a pasta backup-import-... dentro do client.
    /// </summary>
    public static int ImportClientCommand(string source, string target)
    {
        foreach (var dir in new[] { source, target })
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"Pasta não encontrada: {dir}");
                return 1;
            }
        if (Path.GetFullPath(source).TrimEnd('\\', '/').Equals(Path.GetFullPath(target).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("As duas pastas são a mesma.");
            return 1;
        }

        string backup = Path.Combine(target, $"backup-import-{DateTime.Now:yyyyMMdd-HHmmss}");
        var report = ClientImport.Run(source, target, backup);
        Console.WriteLine($"Arquivos: {report.Added} novos, {report.Replaced} substituídos, {report.Unchanged} já iguais; " +
                          $"{report.Fixed} assinaturas de textura/imagem corrigidas.");

        string? itemList = Directory.EnumerateFiles(source)
            .FirstOrDefault(f => Path.GetFileName(f).Equals("ItemList.bin", StringComparison.OrdinalIgnoreCase));
        if (itemList is not null)
        {
            var result = ItemListImport.Convert(File.ReadAllBytes(itemList));
            if (result is null)
                Console.Error.WriteLine($"{itemList}: não consegui decifrar a ItemList; ela não foi trocada.");
            else
            {
                string clientList = Path.Combine(target, "ItemList.bin");
                if (File.Exists(clientList))
                {
                    Directory.CreateDirectory(backup);
                    File.Copy(clientList, Path.Combine(backup, "ItemList.bin"), overwrite: true);
                }
                File.WriteAllBytes(clientList, result.File);
                Directory.CreateDirectory("data");
                if (File.Exists("data/ItemList.bin")) File.Copy("data/ItemList.bin", "data/ItemList.bin.bak", overwrite: true);
                File.WriteAllBytes("data/ItemList.bin", result.File);
                Console.WriteLine($"ItemList: {result.Items} itens, gravada no client e em data/ItemList.bin (a antiga do servidor ficou em data/ItemList.bin.bak).");
            }
        }

        string? itemName = Directory.EnumerateFiles(target)
            .FirstOrDefault(f => Path.GetFileName(f).Equals("Itemname.bin", StringComparison.OrdinalIgnoreCase));
        if (itemName is not null && itemList is not null)
        {
            Directory.CreateDirectory(backup);
            File.Move(itemName, Path.Combine(backup, Path.GetFileName(itemName)), overwrite: true);
            Console.WriteLine("Itemname.bin tirado do client: os nomes dos itens vêm da ItemList nova.");
        }
        if (Directory.Exists(backup)) Console.WriteLine($"O que foi substituído está em {backup}.");
        return FixCDataCommand(target);
    }

    /// <summary>
    /// fix-cdata &lt;pasta do client&gt;: recalcula no cdata.bin o valor esperado de cada mapa da pasta Env (ver
    /// <see cref="FieldChecksums"/>), para o client aceitar mapas novos ou alterados. O anterior vira cdata.bin.bak.
    /// </summary>
    public static int FixCDataCommand(string client)
    {
        string? path = Directory.Exists(client) ? Directory.EnumerateFiles(client)
            .FirstOrDefault(f => Path.GetFileName(f).Equals("cdata.bin", StringComparison.OrdinalIgnoreCase)) : null;
        string? env = Directory.Exists(client) ? Directory.EnumerateDirectories(client)
            .FirstOrDefault(d => Path.GetFileName(d).Equals("Env", StringComparison.OrdinalIgnoreCase)) : null;
        if (path is null || env is null)
        {
            Console.Error.WriteLine($"{client}: não achei o cdata.bin e a pasta Env do client.");
            return 1;
        }
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != FieldChecksums.FileSize)
        {
            Console.Error.WriteLine($"{path} tem {bytes.Length} bytes; o do client 7.54 tem {FieldChecksums.FileSize}.");
            return 1;
        }
        var table = FieldChecksums.Read(bytes);

        var result = FieldChecksums.Recompute(table, env);
        Console.WriteLine($"Mapas: {result.Files} arquivos; dos {result.Known} que já estavam no cdata.bin, {result.Matched} batem com a conta.");
        if (!result.Trusted)
        {
            Console.Error.WriteLine("A conta não bate com os mapas originais; o cdata.bin não foi mudado.");
            return 1;
        }
        if (result.ChangedFiles.Count == 0)
        {
            Console.WriteLine("O cdata.bin já aceita todos os mapas.");
            return 0;
        }
        File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllBytes(path, FieldChecksums.Write(result.Table));
        Console.WriteLine($"cdata.bin atualizado para {result.ChangedFiles.Count} mapa(s) novos ou alterados: " +
                          string.Join(", ", result.ChangedFiles) + $". O anterior ficou em {Path.GetFileName(path)}.bak.");
        return 0;
    }

    /// <summary>extract-exptable &lt;WYD.exe&gt;: copia a tabela de experiência do client para data/ExpTable.txt.</summary>
    public static int ExtractExpTableCommand(string exePath)
    {
        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"Arquivo não encontrado: {exePath}");
            return 1;
        }

        var tables = Data.ExpTable.FindInClient(File.ReadAllBytes(exePath));
        if (tables is not { } found)
        {
            Console.Error.WriteLine("Tabela de experiência não encontrada neste arquivo (o executável pode estar protegido ou ser de outra versão).");
            return 1;
        }

        Directory.CreateDirectory("data");
        File.WriteAllText("data/ExpTable.txt", found.Mortal.Serialize());
        Console.WriteLine($"Mortal/Arch: {found.Mortal.MaxLevel} níveis salvos em data/ExpTable.txt.");
        if (found.Celestial is { } celestial)
        {
            File.WriteAllText("data/ExpTableCelestial.txt", celestial.Serialize());
            Console.WriteLine($"Celestial/Sub Celestial: {celestial.MaxLevel} níveis salvos em data/ExpTableCelestial.txt.");
        }
        else
        {
            Console.WriteLine("A tabela de Celestial não foi encontrada logo depois da de Mortal.");
        }
        return 0;
    }

    /// <summary>patch-client &lt;WYD.exe&gt;: corrige o efeito das armas +13 a +15 e põe cada item em 1 slot (guarda o original em WYD.exe.bak).</summary>
    public static int PatchClientCommand(string exePath)
    {
        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"Arquivo não encontrado: {exePath}");
            return 1;
        }

        var exe = File.ReadAllBytes(exePath);
        switch (ClientPatcher.Patch(exe))
        {
            case ClientPatcher.Result.AlreadyPatched:
                Console.WriteLine("Este WYD.exe já está corrigido.");
                return 0;
            case ClientPatcher.Result.Unknown:
                Console.Error.WriteLine("Este não é o WYD.exe 7.54 conhecido (ou já foi modificado de outro jeito); nada foi alterado.");
                return 1;
        }

        string backup = exePath + ".bak";
        if (!File.Exists(backup))
            File.Copy(exePath, backup);
        File.WriteAllBytes(exePath, exe);
        Console.WriteLine($"WYD.exe corrigido: armas +13 a +15 usam o efeito do +12 e cada item ocupa 1 slot no inventário. Original guardado em {backup}.");
        return 0;
    }

    /// <summary>
    /// publish-client &lt;WYD.exe&gt;: põe uma cópia corrigida do WYD.exe em data/patch e uma versão nova no
    /// update1.htm, para o WYDLauncher de cada jogador baixar na próxima vez que abrir.
    /// </summary>
    public static int PublishClientCommand(string exePath, string folder = "data/patch")
    {
        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"Arquivo não encontrado: {exePath}");
            return 1;
        }

        var exe = File.ReadAllBytes(exePath);
        if (ClientPatcher.Patch(exe) == ClientPatcher.Result.Unknown)
        {
            Console.Error.WriteLine("Este não é o WYD.exe 7.54 conhecido (ou já foi modificado de outro jeito); nada foi publicado.");
            return 1;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "WYD.EXE"), exe);
        string listPath = Path.Combine(folder, LauncherPatch.ListName);
        var (list, version) = LauncherPatch.Publish(File.Exists(listPath) ? File.ReadAllText(listPath) : null, "WYD.EXE");
        File.WriteAllText(listPath, list);

        Console.WriteLine($"WYD.EXE corrigido publicado em {folder} como versão {version}.");
        Console.WriteLine("Na pasta do jogo de cada jogador, a primeira linha do Settings.txt do launcher deve ser");
        Console.WriteLine("  http://IP-DO-SERVIDOR/");
        Console.WriteLine($"(com o servidor aberto; usa a porta 80, a mesma do status). O launcher baixa o WYD.EXE e o config.txt passa a {version}.");
        return 0;
    }

    private static void PrintStrings(string title, List<(int Offset, string Text)> strings)
    {
        Console.WriteLine(title);
        foreach (var (offset, text) in strings)
            Console.WriteLine($"  [{offset,5}] {text}");
    }
}
