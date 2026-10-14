using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;

namespace WYD.Server.Game;

/// <summary>
/// Para onde o /kingdom leva cada reino. Começa com o appsettings.json e, se o GM marcou um lugar no jogo
/// (/gm reino azul), usa o que está em data/kingdoms.txt ("azul 1757 1620", uma linha por reino).
/// </summary>
public sealed class KingdomPoints
{
    private readonly Dictionary<Kingdom, (short X, short Y)> _points = new();
    private readonly string _path;
    private readonly object _lock = new();

    public KingdomPoints(IOptions<ServerOptions> options, ILogger<KingdomPoints> logger)
    {
        var o = options.Value;
        _points[Kingdom.Blue] = ((short)o.KingdomBlueX, (short)o.KingdomBlueY);
        _points[Kingdom.Red] = ((short)o.KingdomRedX, (short)o.KingdomRedY);
        _points[Kingdom.Neutral] = ((short)o.KingdomNeutralX, (short)o.KingdomNeutralY);
        _path = PathResolver.Resolve(o.KingdomPointsPath);

        if (!File.Exists(_path)) return;
        foreach (var line in File.ReadAllLines(_path))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 3 && Parse(p[0]) is { } k
                && short.TryParse(p[1], CultureInfo.InvariantCulture, out var x) && short.TryParse(p[2], CultureInfo.InvariantCulture, out var y))
                _points[k] = (x, y);
        }
        logger.LogInformation("Pontos do /kingdom lidos de {Path}.", _path);
    }

    public (short X, short Y) Of(Kingdom k)
    {
        lock (_lock) return _points[Kingdoms.IsNeutral(k) ? Kingdom.Neutral : k];
    }

    /// <summary>Muda e grava o ponto de um reino.</summary>
    public void Set(Kingdom k, short x, short y)
    {
        lock (_lock)
        {
            _points[Kingdoms.IsNeutral(k) ? Kingdom.Neutral : k] = (x, y);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllLines(_path, _points.Select(p => $"{Name(p.Key)} {p.Value.X} {p.Value.Y}"));
        }
    }

    public static Kingdom? Parse(string text) => text.Trim().ToLowerInvariant() switch
    {
        "azul" or "blue" => Kingdom.Blue,
        "vermelho" or "red" => Kingdom.Red,
        "neutro" or "neutral" or "branco" => Kingdom.Neutral,
        _ => null,
    };

    private static string Name(Kingdom k) => k switch { Kingdom.Blue => "azul", Kingdom.Red => "vermelho", _ => "neutro" };
}
