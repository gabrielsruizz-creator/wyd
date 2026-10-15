namespace WYD.Server.Tools;

/// <summary>
/// Atualização pelo WYDLauncher. O launcher pega o endereço na primeira linha do Settings.txt (terminado em "/";
/// só porta 80) e baixa "endereço + update1.htm" (até 8 KB), que precisa começar com "[WYD]". Cada linha seguinte é
/// "versão arquivo [arquivo...]": os arquivos de versão maior que o [VERSION] do config.txt são baixados de
/// "endereço + arquivo" para a pasta do jogo (um .zip é extraído e apagado) e o config.txt passa a ter a versão
/// nova. A primeira linha também é a versão mínima: client mais velho que ela recebe "baixe do site" e para.
/// </summary>
public static class LauncherPatch
{
    public const string ListName = "update1.htm";

    /// <summary>A versão que vem no config.txt do client 7.54.</summary>
    public const int ClientVersion = 7541;

    /// <summary>A primeira linha da lista original: só a versão mínima (ninguém tem client abaixo de 7000).</summary>
    private const string MinimumLine = "7000 notice.txt";

    public static string DefaultList => $"[WYD]\n{MinimumLine}\n";

    /// <summary>
    /// Põe o arquivo na lista com uma versão acima de todas (e do client), tirando a entrada antiga dele, para que
    /// todo launcher com versão menor baixe a cópia nova uma vez.
    /// </summary>
    public static (string List, int Version) Publish(string? current, string file)
    {
        var lines = (current ?? DefaultList).Replace("\r", "").Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0 && l != "[WYD]").ToList();
        if (lines.Count == 0 || !char.IsAsciiDigit(lines[0][0]))
            lines.Insert(0, MinimumLine);

        int version = ClientVersion;
        for (int i = 0; i < lines.Count; i++)
        {
            var parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!int.TryParse(parts[0], out int v)) continue;
            version = Math.Max(version, v);
            // A linha da versão mínima fica, mesmo que cite o arquivo.
            if (i == 0) continue;
            var kept = parts.Skip(1).Where(p => !p.Equals(file, StringComparison.OrdinalIgnoreCase)).ToList();
            lines[i] = kept.Count > 0 ? $"{parts[0]} {string.Join(' ', kept)}" : "";
        }
        version++;
        lines.RemoveAll(l => l.Length == 0);
        // As linhas com versão vêm antes das outras (NOTICE...), na ordem em que o launcher baixa.
        int insertAt = lines.FindLastIndex(l => char.IsAsciiDigit(l[0])) + 1;
        lines.Insert(insertAt, $"{version} {file}");
        return ("[WYD]\n" + string.Join('\n', lines) + "\n", version);
    }

    /// <summary>
    /// O arquivo da pasta de patch pedido no caminho HTTP (só o último pedaço conta; nomes sem diferença de
    /// maiúsculas). Null se não existir ou se o nome não for um arquivo simples.
    /// </summary>
    public static string? Find(string folder, string requestPath)
    {
        string path = requestPath.Split('?', '#')[0];
        string name = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['\\', ':', '/']) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !Directory.Exists(folder))
            return null;
        return Directory.EnumerateFiles(folder)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Se o caminho pede a lista de atualizações.</summary>
    public static bool IsList(string requestPath) =>
        requestPath.Split('?', '#')[0].EndsWith("/" + ListName, StringComparison.OrdinalIgnoreCase) ||
        requestPath.Equals(ListName, StringComparison.OrdinalIgnoreCase);
}
