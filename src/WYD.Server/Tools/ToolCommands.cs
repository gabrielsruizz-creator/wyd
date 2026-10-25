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
                          (list.MatchesClient ? "(igual ao esperado pelo client 7.57)." : $"(o client 7.57 espera 0x{Data.ItemList.ClientChecksum:X}; rode o fix-lists)."));
        Console.WriteLine($"{"Índice",6}  {"Nome",-32} {"Modelo",6} {"Nível",5} {"Pos",6} {"Preço",10}");
        foreach (var i in items)
            Console.WriteLine($"{i.Index,6}  {i.Name,-32} {i.Mesh,6} {i.ReqLevel,5} {i.Pos,6} {i.Price,10}");
        if (filter is not null)
            Console.WriteLine($"{items.Count} item(ns) com \"{filter}\".");
        return 0;
    }

    /// <summary>
    /// fix-lists &lt;pasta do client&gt;: acerta o checksum da ItemList.bin e da SkillData.bin do client para o que o
    /// WYD.exe espera (as do patch 7580 não batem com o exe dele) e copia as duas para data/ do servidor. O original de
    /// cada uma fica em .original (só na primeira vez); a que já estava em data/ vira .bak.
    /// </summary>
    public static int FixListsCommand(string client)
    {
        if (!Directory.Exists(client))
        {
            Console.Error.WriteLine($"Pasta não encontrada: {client}");
            return 1;
        }
        int errors = 0;
        foreach (var (name, expected, compute, fix) in new (string, int, Func<byte[], int>, Func<byte[], int>)[]
        {
            ("ItemList.bin", Data.ItemList.ClientChecksum, f => Data.ItemList.ComputeChecksum(f.AsSpan(0, Math.Min(f.Length, Data.ItemList.DataSize))), Data.ItemList.FixChecksum),
            ("SkillData.bin", Data.SkillData.ClientChecksum, Data.SkillData.ComputeChecksum, Data.SkillData.FixChecksum),
        })
        {
            string? path = Directory.EnumerateFiles(client)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (path is null)
            {
                Console.Error.WriteLine($"{name}: não está em {client}.");
                errors++;
                continue;
            }
            var file = File.ReadAllBytes(path);
            int before = compute(file);
            if (before == expected)
                Console.WriteLine($"{name}: checksum 0x{before:X} já é o que o client espera.");
            else
            {
                int changed;
                try { changed = fix(file); }
                catch (InvalidDataException ex)
                {
                    Console.Error.WriteLine($"{name}: {ex.Message}");
                    errors++;
                    continue;
                }
                if (!File.Exists(path + ".original")) File.Copy(path, path + ".original");
                File.WriteAllBytes(path, file);
                Console.WriteLine($"{name}: checksum 0x{before:X} -> 0x{compute(file):X} ({changed} bytes em registros vazios). Original em {name}.original.");
            }
            Directory.CreateDirectory("data");
            string server = Path.Combine("data", name);
            if (File.Exists(server) && !File.ReadAllBytes(server).AsSpan().SequenceEqual(file))
                File.Copy(server, server + ".bak", overwrite: true);
            File.WriteAllBytes(server, file);
            Console.WriteLine($"  copiada para {server}.");
        }
        return errors == 0 ? 0 : 1;
    }

    /// <summary>
    /// launcher &lt;pasta do client&gt; &lt;ip&gt;: aponta o launcher para o servidor (primeira linha do Settings.txt =
    /// http://ip/). O servidor responde a lista de atualização na porta 80, e o launcher libera o jogo e as
    /// Configurações. O Settings.txt anterior vira Settings.txt.bak.
    /// </summary>
    public static int LauncherCommand(string client, string ip)
    {
        string? path = Directory.Exists(client) ? Directory.EnumerateFiles(client)
            .FirstOrDefault(f => Path.GetFileName(f).Equals("Settings.txt", StringComparison.OrdinalIgnoreCase)) : null;
        if (path is null)
        {
            Console.Error.WriteLine($"{client}: não achei o Settings.txt do client.");
            return 1;
        }
        if (!IPAddress.TryParse(ip, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            Console.Error.WriteLine($"IP inválido: {ip}");
            return 2;
        }
        var settings = File.ReadAllBytes(path);
        string before = ClientSettings.PatchAddress(settings), after = $"http://{ip}/";
        if (before != after)
        {
            File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllBytes(path, ClientSettings.SetPatchAddress(settings, after));
        }
        Console.WriteLine($"Launcher: atualização em {after} (antes: {before}). Com o servidor aberto, o launcher libera o jogo e as Configurações.");
        return 0;
    }

    /// <summary>
    /// client-config &lt;pasta do client&gt; [janela|tela-cheia] [resolução]: mostra ou troca o modo de tela e a
    /// resolução no Config.bin (o que a tela de Configurações do launcher grava). Sem o arquivo, parte dos valores que
    /// o WYD.exe usa sem ele.
    /// </summary>
    public static int ClientConfigCommand(string client, string[] options)
    {
        if (!Directory.Exists(client))
        {
            Console.Error.WriteLine($"Pasta não encontrada: {client}");
            return 1;
        }
        string path = Directory.EnumerateFiles(client)
            .FirstOrDefault(f => Path.GetFileName(f).Equals("Config.bin", StringComparison.OrdinalIgnoreCase))
            ?? Path.Combine(client, "Config.bin");
        bool exists = File.Exists(path);
        var values = ClientSettings.ReadConfig(exists ? File.ReadAllBytes(path) : null);
        // Sem Config.bin, a versão (que o launcher usa para saber quais patches já tem) vem do config.txt.
        if (!exists && Directory.EnumerateFiles(client).FirstOrDefault(f => Path.GetFileName(f).Equals("config.txt", StringComparison.OrdinalIgnoreCase)) is { } txt
            && System.Text.RegularExpressions.Regex.Match(File.ReadAllText(txt), @"\[VERSION\]\s*(\d+)") is { Success: true } m
            && short.TryParse(m.Groups[1].Value, out short version))
            values[ClientSettings.Version] = version;

        foreach (var option in options)
        {
            switch (option.ToLowerInvariant())
            {
                case "janela" or "window":
                    values[ClientSettings.Window] = 1;
                    break;
                case "tela-cheia" or "telacheia" or "fullscreen":
                    values[ClientSettings.Window] = 0;
                    break;
                default:
                    if (ClientSettings.ParseResolution(option) is not { } res)
                    {
                        Console.Error.WriteLine($"Opção desconhecida: {option}. Use janela, tela-cheia ou uma resolução: " +
                            string.Join(", ", ClientSettings.Resolutions.Where(r => r.Bits == 32).Select(r => $"{r.Width}x{r.Height}")) + ".");
                        return 2;
                    }
                    values[ClientSettings.Resolution] = res;
                    break;
            }
        }

        if (options.Length > 0)
        {
            if (exists) File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllBytes(path, ClientSettings.WriteConfig(values));
        }
        Console.WriteLine($"{path}{(exists || options.Length > 0 ? "" : " (não existe; valores padrão do jogo)")}:");
        Console.WriteLine($"  Tela: {(values[ClientSettings.Window] == 0 ? "cheia" : "janela")}, resolução {ClientSettings.Describe(values[ClientSettings.Resolution])}.");
        for (int i = 0; i < ClientSettings.Names.Length; i++)
            Console.WriteLine($"  [{ClientSettings.Names[i]}] {values[i]}");
        return 0;
    }

    /// <summary>
    /// fix-mesh-textures &lt;pasta do client&gt;: acha os modelos (.msa) com o nome da textura cortado (ver
    /// <see cref="MeshTextures"/>), copia a textura de verdade com o nome cortado e põe esse nome na
    /// MeshTextureList.bin. Parte sempre da lista original (MeshTextureList.bin.bak, guardada na primeira vez), o que
    /// também desfaz o jeito antigo da correção (nome cortado num registro livre).
    /// </summary>
    public static int FixMeshTexturesCommand(string client)
    {
        string? mesh = Directory.Exists(client) ? Directory.EnumerateDirectories(client)
            .FirstOrDefault(d => Path.GetFileName(d).Equals("mesh", StringComparison.OrdinalIgnoreCase)) : null;
        string? listPath = mesh is null ? null : Directory.EnumerateFiles(mesh)
            .FirstOrDefault(f => Path.GetFileName(f).Equals("MeshTextureList.bin", StringComparison.OrdinalIgnoreCase));
        if (mesh is null || listPath is null)
        {
            Console.Error.WriteLine($"{client}: não achei a pasta mesh com a MeshTextureList.bin (client 7.57).");
            return 1;
        }
        var files = Directory.EnumerateFiles(mesh).ToDictionary(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
        string? FileOf(string entry) => files.TryGetValue(entry["mesh\\".Length..], out var f) ? f : null;

        string original = listPath + ".bak";
        var list = File.ReadAllBytes(File.Exists(original) ? original : listPath);
        var models = files.Values.Where(f => f.EndsWith(".msa", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path.GetFileName(f), File.ReadAllBytes(f)));
        var fixes = MeshTextures.Plan(models, list, e => FileOf(e) is not null);
        if (fixes.Count == 0)
        {
            Console.WriteLine("Nenhum modelo com o nome da textura cortado.");
            return 0;
        }
        if (!MeshTextures.Apply(list, fixes))
        {
            Console.Error.WriteLine("A MeshTextureList.bin não tem registros livres suficientes; nada foi alterado.");
            return 1;
        }
        foreach (var fix in fixes)
        {
            string target = Path.Combine(mesh, fix.Missing["mesh\\".Length..]);
            if (!File.Exists(target)) File.Copy(FileOf(fix.Source)!, target);
            Console.WriteLine($"  {fix.Model}: o client procura {fix.Missing}; agora é uma cópia de {fix.Source}" +
                              (fix.Rename ? $" (no lugar dela, registro {fix.Index})." : " (registro novo)."));
        }
        if (!File.Exists(original)) File.Copy(listPath, original);
        File.WriteAllBytes(listPath, list);
        Console.WriteLine($"{fixes.Count} textura(s) corrigida(s) na MeshTextureList.bin (a original fica em MeshTextureList.bin.bak).");
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
        switch (ClientPatcher.Patch(exe, out string? version))
        {
            case ClientPatcher.Result.AlreadyPatched:
                Console.WriteLine($"Este WYD.exe {version} já está corrigido.");
                return 0;
            case ClientPatcher.Result.Unknown:
                Console.Error.WriteLine("Este não é um WYD.exe conhecido (7.57 ou 7.54), ou já foi modificado de outro jeito; nada foi alterado.");
                return 1;
        }

        string backup = exePath + ".bak";
        if (!File.Exists(backup))
            File.Copy(exePath, backup);
        File.WriteAllBytes(exePath, exe);
        Console.WriteLine($"WYD.exe {version} corrigido: armas +13 a +15 usam o efeito do +12" +
                          (version == "7.54" ? " e cada item ocupa 1 slot no inventário" : "") + $". Original guardado em {backup}.");
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
            Console.Error.WriteLine("Este não é um WYD.exe conhecido (7.57 ou 7.54), ou já foi modificado de outro jeito; nada foi publicado.");
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
