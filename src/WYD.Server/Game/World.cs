using Microsoft.Extensions.Logging;
using WYD.Server.Database;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Jogadores e monstros no mapa, e quem vê quem. Cada jogador enxerga num quadrado de
/// <see cref="ViewRange"/> células em volta dele (o client do WYD desenha 33x33).
/// Quando algo entra no campo de visão o jogador recebe o 0x364; quando sai, o 0x165.
/// Os monstros ficam numa grade de <see cref="CellSize"/> células para a busca por proximidade ser rápida.
/// </summary>
public sealed class World(ICharacterRepository characters, ILogger<World> logger)
{
    public const int ViewRange = 16;

    /// <summary>Maior distância aceita entre a posição atual e o destino de um movimento.</summary>
    public const int MaxMoveDistance = 40;

    private const int CellSize = 32;

    private readonly object _lock = new();
    private readonly Dictionary<ushort, ClientSession> _players = new();
    private readonly Dictionary<ushort, HashSet<ushort>> _visible = new();
    private readonly Dictionary<ushort, Mob> _mobs = new();
    private readonly Dictionary<(int, int), List<Mob>> _grid = new();

    public int Count
    {
        get { lock (_lock) return _players.Count; }
    }

    public int MobCount
    {
        get { lock (_lock) return _mobs.Count; }
    }

    private static bool InRange(Character a, Character b) =>
        Math.Abs(a.PosX - b.PosX) <= ViewRange && Math.Abs(a.PosY - b.PosY) <= ViewRange;

    private static bool InRange(Character a, Mob m) =>
        Math.Abs(a.PosX - m.X) <= ViewRange && Math.Abs(a.PosY - m.Y) <= ViewRange;

    private static (int, int) Cell(int x, int y) => (x / CellSize, y / CellSize);

    /// <summary>Monstros num raio de <see cref="ViewRange"/> do ponto (chamar com o lock).</summary>
    private IEnumerable<Mob> MobsNear(int x, int y)
    {
        var (cx0, cy0) = Cell(Math.Max(0, x - ViewRange), Math.Max(0, y - ViewRange));
        var (cx1, cy1) = Cell(x + ViewRange, y + ViewRange);
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cy = cy0; cy <= cy1; cy++)
                if (_grid.TryGetValue((cx, cy), out var list))
                    foreach (var mob in list)
                        if (Math.Abs(mob.X - x) <= ViewRange && Math.Abs(mob.Y - y) <= ViewRange)
                            yield return mob;
    }

    /// <summary>Coloca um monstro no mapa (normalmente na partida do servidor) e mostra a quem estiver perto.</summary>
    public Task AddMobAsync(Mob mob)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            _mobs[mob.Id] = mob;
            GridAdd(mob);
            foreach (var player in _players.Values)
            {
                if (!InRange(player.Character!, mob)) continue;
                _visible[player.Id].Add(mob.Id);
                outgoing.Add((player, mob.CreatePacket()));
            }
        }
        return SendAllAsync(outgoing);
    }

    /// <summary>Monstros que algum jogador está vendo (os outros não precisam andar).</summary>
    public List<Mob> WatchedMobs()
    {
        lock (_lock)
            return _players.Values.SelectMany(p => MobsNear(p.Character!.PosX, p.Character.PosY)).Distinct().ToList();
    }

    /// <summary>Move um monstro: quem já via recebe o 0x366; quem passa a ver ou deixa de ver recebe 0x364/0x165.</summary>
    public Task MoveMobAsync(Mob mob, short destX, short destY)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            var move = ServerPackets.Move(mob.Id, mob.X, mob.Y, destX, destY, mob.Speed);
            GridRemove(mob);
            mob.X = destX;
            mob.Y = destY;
            GridAdd(mob);

            foreach (var player in _players.Values)
            {
                var mine = _visible[player.Id];
                bool near = InRange(player.Character!, mob);
                bool seen = mine.Contains(mob.Id);
                if (near && seen) outgoing.Add((player, move));
                else if (near)
                {
                    mine.Add(mob.Id);
                    outgoing.Add((player, mob.CreatePacket()));
                }
                else if (seen)
                {
                    mine.Remove(mob.Id);
                    outgoing.Add((player, ServerPackets.RemoveMob(mob.Id)));
                }
            }
        }
        return SendAllAsync(outgoing);
    }

    private void GridAdd(Mob mob)
    {
        var cell = Cell(mob.X, mob.Y);
        if (!_grid.TryGetValue(cell, out var list)) _grid[cell] = list = [];
        list.Add(mob);
    }

    private void GridRemove(Mob mob)
    {
        if (_grid.TryGetValue(Cell(mob.X, mob.Y), out var list)) list.Remove(mob);
    }

    /// <summary>Atualiza os monstros que o jogador vê depois de ele entrar ou andar (chamar com o lock).</summary>
    private void RefreshMobs(ClientSession session, List<(ClientSession To, byte[] Packet)> outgoing)
    {
        var me = session.Character!;
        var mine = _visible[session.Id];
        var near = MobsNear(me.PosX, me.PosY).ToList();

        foreach (var id in mine.Where(id => id >= Mob.FirstId && !near.Any(m => m.Id == id)).ToList())
        {
            mine.Remove(id);
            outgoing.Add((session, ServerPackets.RemoveMob(id)));
        }
        foreach (var mob in near)
            if (mine.Add(mob.Id))
                outgoing.Add((session, mob.CreatePacket()));
    }

    /// <summary>O jogador entrou no mapa: ele e quem estiver perto passam a se ver.</summary>
    public Task EnterAsync(ClientSession session)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            var me = session.Character!;
            _players[session.Id] = session;
            var mine = _visible[session.Id] = [];

            foreach (var other in _players.Values)
            {
                if (other == session || !InRange(me, other.Character!)) continue;
                mine.Add(other.Id);
                _visible[other.Id].Add(session.Id);
                outgoing.Add((session, ServerPackets.CreateMob(other.Id, other.Character!)));
                outgoing.Add((other, ServerPackets.CreateMob(session.Id, me)));
            }
            RefreshMobs(session, outgoing);
        }
        session.Closed += OnClosed;
        return SendAllAsync(outgoing);
    }

    /// <summary>
    /// Movimento (0x366/0x367): guarda o destino como a nova posição, repassa o pacote a quem vê o jogador
    /// e atualiza quem entra e sai do campo de visão. Retorna false se o destino for inválido.
    /// </summary>
    public async Task<bool> MoveAsync(ClientSession session, byte[] packet, short destX, short destY)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            var me = session.Character!;
            if (!_players.ContainsKey(session.Id)) return false;
            if (destX is < 0 or > 4095 || destY is < 0 or > 4095) return false;
            if (Math.Abs(destX - me.PosX) > MaxMoveDistance || Math.Abs(destY - me.PosY) > MaxMoveDistance) return false;

            me.PosX = destX;
            me.PosY = destY;

            var mine = _visible[session.Id];
            var relay = ServerPackets.Relay(packet, session.Id);
            foreach (var other in _players.Values)
            {
                if (other == session) continue;
                bool near = InRange(me, other.Character!);
                bool seen = mine.Contains(other.Id);

                if (near && seen)
                {
                    outgoing.Add((other, relay));
                }
                else if (near)
                {
                    mine.Add(other.Id);
                    _visible[other.Id].Add(session.Id);
                    outgoing.Add((session, ServerPackets.CreateMob(other.Id, other.Character!)));
                    outgoing.Add((other, ServerPackets.CreateMob(session.Id, me)));
                }
                else if (seen)
                {
                    mine.Remove(other.Id);
                    _visible[other.Id].Remove(session.Id);
                    outgoing.Add((session, ServerPackets.RemoveMob(other.Id)));
                    outgoing.Add((other, ServerPackets.RemoveMob(session.Id)));
                }
            }
            RefreshMobs(session, outgoing);
        }
        await SendAllAsync(outgoing);
        return true;
    }

    /// <summary>0x369: o client recebeu algo de um id que não conhece e pede os dados dele.</summary>
    public Task RequestMobAsync(ClientSession session, ushort mobId)
    {
        byte[]? packet = null;
        lock (_lock)
        {
            if (_visible.TryGetValue(session.Id, out var mine) && mine.Contains(mobId))
            {
                if (_players.TryGetValue(mobId, out var other) && other != session)
                    packet = ServerPackets.CreateMob(mobId, other.Character!);
                else if (_mobs.TryGetValue(mobId, out var mob))
                    packet = mob.CreatePacket();
            }
        }
        return packet is null ? Task.CompletedTask : session.SendAsync(packet);
    }

    /// <summary>Chat (0x333): repassa a quem vê o jogador. O próprio client já mostra a mensagem de quem falou.</summary>
    public Task ChatAsync(ClientSession session, byte[] packet)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            if (!_visible.TryGetValue(session.Id, out var mine)) return Task.CompletedTask;
            var relay = ServerPackets.Relay(packet, session.Id);
            foreach (var id in mine)
                if (_players.TryGetValue(id, out var other))
                    outgoing.Add((other, relay));
        }
        return SendAllAsync(outgoing);
    }

    /// <summary>Grava a posição de todos (usado ao desligar o servidor).</summary>
    public async Task SaveAllAsync()
    {
        List<Character> online;
        lock (_lock) online = _players.Values.Select(s => s.Character!).ToList();
        foreach (var c in online)
            await SaveAsync(c);
    }

    private void OnClosed(ClientSession session)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            if (!_players.Remove(session.Id)) return;
            if (_visible.Remove(session.Id, out var mine))
            {
                foreach (var id in mine)
                {
                    if (!_players.TryGetValue(id, out var other)) continue;
                    _visible[id].Remove(session.Id);
                    outgoing.Add((other, ServerPackets.RemoveMob(session.Id)));
                }
            }
        }

        _ = Task.Run(async () =>
        {
            await SendAllAsync(outgoing);
            await SaveAsync(session.Character!);
        });
    }

    private async Task SaveAsync(Character c)
    {
        try
        {
            await characters.SavePositionAsync(c.Id, c.PosX, c.PosY);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Não foi possível salvar a posição de {Name}.", c.Name);
        }
    }

    private static async Task SendAllAsync(List<(ClientSession To, byte[] Packet)> outgoing)
    {
        foreach (var (to, packet) in outgoing)
            await to.SendAsync(packet);
    }
}
