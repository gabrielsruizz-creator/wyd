namespace WYD.Server.Config;

/// <summary>Caminhos relativos valem a partir da pasta atual ou, se não existirem lá, da pasta do executável.</summary>
public static class PathResolver
{
    public static string Resolve(string path)
    {
        if (Path.IsPathRooted(path) || File.Exists(path)) return path;
        string beside = Path.Combine(AppContext.BaseDirectory, path);
        return File.Exists(beside) ? beside : path;
    }

    public static string ResolveDirectory(string path)
    {
        if (Path.IsPathRooted(path) || Directory.Exists(path)) return path;
        string beside = Path.Combine(AppContext.BaseDirectory, path);
        return Directory.Exists(beside) ? beside : path;
    }
}
