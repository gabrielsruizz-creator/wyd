namespace WYD.Server.Tools;

/// <summary>
/// Traz o visual de outro client 7.54 (o Star Destiny) para o nosso: os arquivos novos ou diferentes das pastas de
/// modelos, cenário, interface, efeitos e música, mais o itemhelp.dat. Os mapas (Env\Field) ficam os nossos. O que é só daquele client (exe, DLL, launcher,
/// shaders, configuração) e as logos da tela de login ficam de fora. O arquivo substituído vai antes para a pasta de
/// backup, no mesmo caminho.
/// <list type="bullet">
/// <item>Textura (.wys): DDS com a assinatura "WS10 " no lugar de "DDS ". O Star troca por "Star " em algumas; o
/// client não confere, mas a cópia volta para "WS10 ".</item>
/// <item>Imagem da interface (.wyt): TGA com 4 bytes na frente ("WT10" no original; o Star põe qualquer coisa). A
/// cópia volta para "WT10".</item>
/// </list>
/// </summary>
public static class ClientImport
{
    public sealed record Report(int Added, int Replaced, int Unchanged, int Fixed, List<string> Changed);

    /// <summary>Pastas copiadas (com as subpastas).</summary>
    public static readonly string[] Folders = ["Mesh", "Env", "UI", "Effect", "Music"];

    /// <summary>Arquivos da pasta principal que são só dados do jogo.</summary>
    public static readonly string[] RootFiles = ["itemhelp.dat"];

    /// <summary>
    /// Ficam como estão no nosso client: as logos do servidor e cópias soltas que o exe não lê (ele usa
    /// Env/AttributeMap.dat e a ItemList da pasta principal).
    /// </summary>
    private static readonly HashSet<string> Kept = new(StringComparer.OrdinalIgnoreCase)
    {
        "UI/logo1.wyt", "UI/logo2.wyt", "Mesh/AttributeMap.dat", "Mesh/ItemList.bin",
    };

    private static readonly byte[] TextureSignature = "WS10 "u8.ToArray();
    private static readonly byte[] StarTextureSignature = "Star "u8.ToArray();
    private static readonly byte[] ImageSignature = "WT10"u8.ToArray();

    public static Report Run(string source, string target, string backup)
    {
        var existing = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .GroupBy(f => Relative(target, f), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int added = 0, replaced = 0, unchanged = 0, fixedCount = 0;
        var changed = new List<string>();
        foreach (var file in SourceFiles(source))
        {
            string rel = Relative(source, file);
            if (Kept.Contains(rel) || IsMap(rel)) continue;
            var bytes = File.ReadAllBytes(file);
            if (Normalize(rel, bytes)) fixedCount++;

            if (existing.TryGetValue(rel, out var current))
            {
                if (File.ReadAllBytes(current).AsSpan().SequenceEqual(bytes)) { unchanged++; continue; }
                var saved = Path.Combine(backup, Relative(target, current));
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(current, saved, overwrite: true);
                File.WriteAllBytes(current, bytes);
                replaced++;
            }
            else
            {
                var path = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
                added++;
            }
            changed.Add(rel);
        }
        return new Report(added, replaced, unchanged, fixedCount, changed);
    }

    private static IEnumerable<string> SourceFiles(string source)
    {
        foreach (var folder in Folders)
        {
            var dirs = Directory.EnumerateDirectories(source)
                .Where(d => Path.GetFileName(d).Equals(folder, StringComparison.OrdinalIgnoreCase));
            foreach (var dir in dirs)
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) yield return f;
        }
        foreach (var f in Directory.EnumerateFiles(source))
            if (RootFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)) yield return f;
    }

    /// <summary>
    /// Os mapas (Env\FieldXXYY.trn/.dat) ficam os do nosso client: os do Star têm o terreno codificado e o cdata.bin
    /// precisaria ser refeito (ver <see cref="FieldChecksums"/>, comando fix-cdata).
    /// </summary>
    private static bool IsMap(string rel) =>
        rel.StartsWith("Env/Field", StringComparison.OrdinalIgnoreCase) &&
        (rel.EndsWith(".trn", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));

    private static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>Volta as assinaturas para as do client original. Retorna true se mudou algo.</summary>
    public static bool Normalize(string rel, byte[] bytes)
    {
        string ext = Path.GetExtension(rel);
        if (ext.Equals(".wys", StringComparison.OrdinalIgnoreCase) && bytes.AsSpan().StartsWith(StarTextureSignature))
        {
            TextureSignature.CopyTo(bytes, 0);
            return true;
        }
        // TGA depois dos 4 bytes: sem paleta e tipo de imagem 1-3 ou 9-11.
        if (ext.Equals(".wyt", StringComparison.OrdinalIgnoreCase) && bytes.Length > 6 &&
            !bytes.AsSpan().StartsWith(ImageSignature) && bytes[5] <= 1 && bytes[6] is >= 1 and <= 3 or >= 9 and <= 11)
        {
            ImageSignature.CopyTo(bytes, 0);
            return true;
        }
        return false;
    }
}
