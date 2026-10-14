using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;

namespace WYD.Server.Game;

/// <summary>Um círculo de teleporte: de (X, Y) para (DestX, DestY), cobrando Price de gold.</summary>
public sealed record TeleportPoint(short X, short Y, short DestX, short DestY, int Price, string Name);

/// <summary>
/// Os círculos de teleporte. O client só sabe que a célula é de teleporte (flag 0x10 no mapa dele) e manda o
/// 0x290; para onde vai é decisão do servidor. A lista fica em data/teleports.txt, uma linha por círculo:
/// "x y destX destY preço nome", e o GM monta ela no jogo com /gm tp add.
/// </summary>
public sealed class TeleportTable
{
    /// <summary>Distância máxima entre o jogador e o centro do círculo.</summary>
    public const int Radius = 4;

    private readonly List<TeleportPoint> _points = [];
    private readonly string _path;
    private readonly object _lock = new();

    public TeleportTable(IOptions<ServerOptions> options, ILogger<TeleportTable> logger)
    {
        _path = PathResolver.Resolve(options.Value.TeleportsPath);
        if (!File.Exists(_path))
        {
            logger.LogInformation("Sem {Path}: monte os teleportes no jogo com /gm tp add <x> <y> [preço] [nome].", _path);
            return;
        }
        _points.AddRange(Parse(File.ReadAllLines(_path)));
        logger.LogInformation("{Count} teleporte(s) lido(s) de {Path}.", _points.Count, _path);
    }

    public TeleportTable(IEnumerable<TeleportPoint> points, string path)
    {
        _points.AddRange(points);
        _path = path;
    }

    public static IEnumerable<TeleportPoint> Parse(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var p = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 4) continue;
            if (!short.TryParse(p[0], CultureInfo.InvariantCulture, out var x) || !short.TryParse(p[1], CultureInfo.InvariantCulture, out var y)
                || !short.TryParse(p[2], CultureInfo.InvariantCulture, out var dx) || !short.TryParse(p[3], CultureInfo.InvariantCulture, out var dy))
                continue;
            int price = p.Length > 4 && int.TryParse(p[4], CultureInfo.InvariantCulture, out var v) ? v : 0;
            yield return new TeleportPoint(x, y, dx, dy, Math.Max(0, price), p.Length > 5 ? p[5] : "");
        }
    }

    public int Count
    {
        get { lock (_lock) return _points.Count; }
    }

    /// <summary>O círculo mais perto de (x, y), se estiver a até <see cref="Radius"/> células.</summary>
    public TeleportPoint? Find(int x, int y)
    {
        lock (_lock)
            return _points
                .Where(p => Math.Abs(p.X - x) <= Radius && Math.Abs(p.Y - y) <= Radius)
                .MinBy(p => Math.Max(Math.Abs(p.X - x), Math.Abs(p.Y - y)));
    }

    public List<TeleportPoint> Near(int x, int y, int range)
    {
        lock (_lock)
            return _points.Where(p => Math.Abs(p.X - x) <= range && Math.Abs(p.Y - y) <= range).ToList();
    }

    /// <summary>Adiciona (ou troca o que já existe no mesmo lugar) e grava o arquivo.</summary>
    public void Add(TeleportPoint point)
    {
        lock (_lock)
        {
            _points.RemoveAll(p => p.X == point.X && p.Y == point.Y);
            _points.Add(point);
            Save();
        }
    }

    /// <summary>Remove o círculo mais perto de (x, y). Retorna o removido.</summary>
    public TeleportPoint? Remove(int x, int y)
    {
        lock (_lock)
        {
            var found = Find(x, y);
            if (found is null) return null;
            _points.Remove(found);
            Save();
            return found;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllLines(_path,
            new[] { "# x y destinoX destinoY preço nome   (criado pelo /gm tp add)" }
                .Concat(_points.Select(p => $"{p.X} {p.Y} {p.DestX} {p.DestY} {p.Price} {p.Name}".TrimEnd())));
    }
}
