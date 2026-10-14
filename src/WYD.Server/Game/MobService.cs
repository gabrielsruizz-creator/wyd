using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;
using WYD.Server.Data;

namespace WYD.Server.Game;

/// <summary>
/// Cria os monstros e NPCs do NPCGener.txt na partida do servidor e faz andar os que estão perto de jogadores.
/// Cada entrada nasce como um grupo: o líder mais MinGroup..MaxGroup seguidores (no máximo MaxNumMob),
/// espalhados num raio de StartRange em volta de (StartX, StartY).
/// </summary>
public sealed class MobService(IOptions<ServerOptions> options, World world, ILogger<MobService> logger) : BackgroundService
{
    private readonly ServerOptions _options = options.Value;

    /// <summary>Menor intervalo entre dois passeios do mesmo monstro.</summary>
    private const int MinWaitSeconds = 4;

    /// <summary>Distância máxima de cada passeio (a rota do client tem até 23 passos).</summary>
    private const int MaxStep = 12;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string generPath = PathResolver.Resolve(_options.NpcGenerPath);
        string npcPath = PathResolver.ResolveDirectory(_options.NpcPath);
        if (File.Exists(generPath) && Directory.Exists(npcPath))
            await SpawnAllAsync(NpcGener.Load(generPath), npcPath);
        else
            logger.LogInformation("Sem monstros: coloque o NPCGener.txt em {Gener} e a pasta npc em {Npc}.", generPath, npcPath);

        // O ciclo roda mesmo sem o NPCGener, para os monstros criados depois (renascer e andar).

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            foreach (var mob in world.MobsToRespawn(now))
            {
                var (x, y) = Around(mob.Spawn.StartX, mob.Spawn.StartY, mob.Spawn.StartRange);
                mob.NextMove = now + Wait(mob);
                await world.RespawnAsync(mob, x, y);
            }

            var busy = await world.MobActionsAsync(now);   // perseguir e bater em quem os atacou
            await world.RegenerateAsync(now);

            foreach (var mob in world.WatchedMobs())
            {
                if (!mob.Wanders || mob.NextMove > now || busy.Contains(mob)) continue;
                var (x, y) = PickDestination(mob);
                mob.NextMove = now + Wait(mob);
                if (x != mob.X || y != mob.Y)
                    await world.MoveMobAsync(mob, x, y);
            }
        }
    }

    public async Task SpawnAllAsync(IReadOnlyList<SpawnRule> rules, string npcPath)
    {
        var templates = new Dictionary<string, NpcTemplate?>(StringComparer.OrdinalIgnoreCase);
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        ushort nextId = Mob.FirstId;
        int limit = Math.Min(_options.MaxMobs, Mob.LastId - Mob.FirstId + 1);
        int spawned = 0;

        NpcTemplate? Template(string name)
        {
            if (templates.TryGetValue(name, out var cached)) return cached;
            NpcTemplate? template = null;
            string file = Path.Combine(npcPath, name);
            try
            {
                if (File.Exists(file)) template = NpcTemplate.Parse(name, File.ReadAllBytes(file));
            }
            catch (InvalidDataException ex)
            {
                logger.LogDebug("{Message}", ex.Message);
            }
            if (template is null) missing.Add(name);
            return templates[name] = template;
        }

        foreach (var rule in rules)
        {
            var leader = Template(rule.Leader);
            if (leader is null) continue;
            var follower = Template(rule.Follower) ?? leader;

            int followers = rule.MaxGroup > rule.MinGroup ? Random.Shared.Next(rule.MinGroup, rule.MaxGroup + 1) : rule.MinGroup;
            int count = Math.Max(1, Math.Min(1 + Math.Max(0, followers), Math.Max(1, rule.MaxNumMob)));

            for (int i = 0; i < count && spawned < limit; i++)
            {
                var (x, y) = Around(rule.StartX, rule.StartY, i == 0 ? 0 : rule.StartRange);
                var mob = new Mob(nextId++, i == 0 ? leader : follower, rule, x, y)
                {
                    NextMove = DateTime.UtcNow + TimeSpan.FromSeconds(Random.Shared.Next(MinWaitSeconds, MinWaitSeconds * 3)),
                };
                await world.AddMobAsync(mob);
                spawned++;
            }
        }

        logger.LogInformation("{Count} monstros e NPCs criados a partir de {Rules} spawns.", spawned, rules.Count);
        if (spawned >= limit)
            logger.LogWarning("Limite de {Limit} monstros atingido; os spawns restantes foram ignorados.", limit);
        if (missing.Count > 0)
            logger.LogWarning("{Count} arquivos de npc não encontrados ou com tamanho diferente de {Size} bytes (ignorados): {Names}",
                missing.Count, NpcTemplate.FileSize, string.Join(", ", missing));
    }

    private static (short X, short Y) Around(int x, int y, int range)
    {
        if (range > 0)
        {
            x += Random.Shared.Next(-range, range + 1);
            y += Random.Shared.Next(-range, range + 1);
        }
        return ((short)Math.Clamp(x, 0, 4095), (short)Math.Clamp(y, 0, 4095));
    }

    /// <summary>Um ponto aleatório perto de onde o monstro está, sem sair da área do spawn.</summary>
    private static (short X, short Y) PickDestination(Mob mob)
    {
        var s = mob.Spawn;
        int range = Math.Max(s.StartRange, s.DestRange);
        var (x, y) = Around(mob.X, mob.Y, Math.Min(range, MaxStep));
        x = (short)Math.Clamp((int)x, s.StartX - range, s.StartX + range);
        y = (short)Math.Clamp((int)y, s.StartY - range, s.StartY + range);
        return (x, y);
    }

    private static TimeSpan Wait(Mob mob)
    {
        int wait = Math.Max(MinWaitSeconds, mob.Spawn.StartWait);
        return TimeSpan.FromSeconds(wait + Random.Shared.Next(0, wait + 1));
    }
}
