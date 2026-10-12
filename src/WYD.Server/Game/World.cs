using Microsoft.Extensions.Logging;
using WYD.Server.Database;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Jogadores dentro do mapa e quem vê quem. Cada jogador enxerga os outros num quadrado de
/// <see cref="ViewRange"/> células em volta dele (o client do WYD desenha 33x33).
/// Quando alguém entra no campo de visão recebe o 0x364 do outro; quando sai, o 0x165.
/// </summary>
public sealed class World(ICharacterRepository characters, ILogger<World> logger)
{
    public const int ViewRange = 16;

    /// <summary>Maior distância aceita entre a posição atual e o destino de um movimento.</summary>
    public const int MaxMoveDistance = 40;

    private readonly object _lock = new();
    private readonly Dictionary<ushort, ClientSession> _players = new();
    private readonly Dictionary<ushort, HashSet<ushort>> _visible = new();

    public int Count
    {
        get { lock (_lock) return _players.Count; }
    }

    private static bool InRange(Character a, Character b) =>
        Math.Abs(a.PosX - b.PosX) <= ViewRange && Math.Abs(a.PosY - b.PosY) <= ViewRange;

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
            if (_players.TryGetValue(mobId, out var other) && other != session
                && _visible.TryGetValue(session.Id, out var mine) && mine.Contains(mobId))
                packet = ServerPackets.CreateMob(mobId, other.Character!);
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
