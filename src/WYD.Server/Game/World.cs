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
public sealed class World(
    ICharacterRepository characters,
    Microsoft.Extensions.Options.IOptions<WYD.Server.Config.ServerOptions> options,
    WYD.Server.Data.ExpCatalog expTable,
    ILogger<World> logger)
{
    /// <summary>Distância máxima entre quem ataca e o alvo (ataques à distância e magias incluídos).</summary>
    public const int AttackRange = 12;

    /// <summary>Menor intervalo entre dois ataques do mesmo jogador.</summary>
    public static readonly TimeSpan MinAttackInterval = TimeSpan.FromMilliseconds(300);

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

    /// <summary>Outros jogadores que estão vendo este (para avisar de mudanças como o equipamento).</summary>
    public List<ClientSession> Viewers(ClientSession session)
    {
        lock (_lock)
            return _players.Values.Where(p => p != session && _visible.TryGetValue(p.Id, out var v) && v.Contains(session.Id)).ToList();
    }

    /// <summary>Um monstro/NPC que o jogador está vendo, ou null.</summary>
    public Mob? VisibleMob(ClientSession session, ushort mobId)
    {
        lock (_lock)
            return _visible.TryGetValue(session.Id, out var mine) && mine.Contains(mobId)
                   && _mobs.TryGetValue(mobId, out var mob) && mob.Alive
                ? mob
                : null;
    }

    /// <summary>
    /// Ataque do jogador (0x39D / 0x39E / 0x36C): calcula o dano em cada monstro alvo, devolve o pacote com os
    /// danos a todos que estão vendo e, se algum morrer, dá a experiência e tira o monstro do mapa até renascer.
    /// </summary>
    public async Task AttackAsync(ClientSession session, byte[] packet)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        var killed = new List<Mob>();
        long exp;
        int levels;
        lock (_lock)
        {
            if (!_players.ContainsKey(session.Id)) return;
            var now = DateTime.UtcNow;
            if (now - session.LastAttack < MinAttackInterval) return;
            session.LastAttack = now;

            var me = session.Character!;
            var mine = _visible[session.Id];
            var response = ServerPackets.Relay(packet, session.Id);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(ServerPackets.AttackLayout.Attacker), session.Id);

            int count = Math.Min(ServerPackets.AttackLayout.MaxTargets,
                (packet.Length - ServerPackets.AttackLayout.Targets) / ServerPackets.AttackLayout.TargetSize);
            var hit = new List<Mob>();
            for (int i = 0; i < count; i++)
            {
                int o = ServerPackets.AttackLayout.Targets + i * ServerPackets.AttackLayout.TargetSize;
                ushort targetId = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(o));
                short damage = 0;

                if (targetId >= Mob.FirstId && mine.Contains(targetId) && _mobs.TryGetValue(targetId, out var mob)
                    && mob.Alive && mob.Attackable && !hit.Contains(mob)
                    && Math.Abs(mob.X - me.PosX) <= AttackRange && Math.Abs(mob.Y - me.PosY) <= AttackRange)
                {
                    damage = (short)Combat.Damage(me.BaseScore, mob.Template.Score);
                    mob.Hp -= damage;
                    hit.Add(mob);
                    if (mob.Hp <= 0) killed.Add(mob);
                }
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(o + 2), damage);
            }

            foreach (var mob in killed)
            {
                me.Exp = Math.Min(int.MaxValue, me.Exp + Math.Max(0, mob.Template.Exp));
                me.Coin = (int)Math.Min(Handlers.ItemHandler.MaxCoin, (long)me.Coin + Math.Max(0, mob.Template.Coin));
            }
            exp = me.Exp;
            levels = killed.Count > 0 ? Leveling.Apply(me, expTable.Table) : 0;

            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(ServerPackets.AttackLayout.Hold), 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(ServerPackets.AttackLayout.Exp), (int)exp);

            // Quem vê o atacante ou algum dos alvos recebe o ataque com os danos.
            foreach (var player in _players.Values)
                if (player == session || InRange(player.Character!, me) || hit.Any(m => InRange(player.Character!, m)))
                    outgoing.Add((player, response));

            foreach (var mob in killed)
            {
                mob.Alive = false;
                mob.Hp = 0;
                mob.RespawnAt = now + TimeSpan.FromSeconds(Math.Max(1, options.Value.RespawnSeconds));
                GridRemove(mob);
                foreach (var player in _players.Values)
                {
                    if (!_visible[player.Id].Remove(mob.Id)) continue;
                    outgoing.Add((player, ServerPackets.MobKill(mob.Id, session.Id, (int)exp)));
                    outgoing.Add((player, ServerPackets.RemoveMob(mob.Id, ServerPackets.RemoveDead)));
                }
            }
        }

        await SendAllAsync(outgoing);
        if (killed.Count > 0)
        {
            var me = session.Character!;
            await session.SendAsync(ServerPackets.UpdateEtc(session.Id, me));   // exp, gold e pontos
            if (levels > 0)
            {
                await session.SendAsync(ServerPackets.UpdateScore(session.Id, me));
                await session.SendAsync(ServerPackets.UpdateHpMp(session.Id, me.BaseScore.Hp, me.BaseScore.Mp));
                logger.LogInformation("[{Id}] {Name} subiu para o nível {Level}.", session.Id, me.Name, me.BaseScore.Level + 1);
            }
            logger.LogInformation("[{Id}] {Name} matou {Mobs} (exp {Exp}, gold {Coin}).", session.Id, me.Name,
                string.Join(", ", killed.Select(m => m.Template.Name)), exp, me.Coin);
            try { await characters.SaveProgressAsync(me); }
            catch (Exception ex) { logger.LogError(ex, "Não foi possível salvar o progresso de {Name}.", me.Name); }
        }
    }

    /// <summary>Monstros mortos cujo tempo de renascer já passou.</summary>
    public List<Mob> MobsToRespawn(DateTime now)
    {
        lock (_lock)
            return _mobs.Values.Where(m => !m.Alive && m.RespawnAt <= now).ToList();
    }

    /// <summary>Traz um monstro morto de volta, com vida cheia, na posição indicada.</summary>
    public Task RespawnAsync(Mob mob, short x, short y)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            if (mob.Alive) return Task.CompletedTask;
            mob.Alive = true;
            mob.Hp = mob.Template.Score.MaxHp;
            mob.X = x;
            mob.Y = y;
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
