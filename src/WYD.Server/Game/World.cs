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
public sealed partial class World(
    ICharacterRepository characters,
    Microsoft.Extensions.Options.IOptions<WYD.Server.Config.ServerOptions> options,
    WYD.Server.Data.ExpCatalog expTable,
    WYD.Server.Data.SkillCatalog skillCatalog,
    ILogger<World> logger)
{
    /// <summary>Distância máxima entre quem ataca e o alvo (ataques à distância e magias incluídos).</summary>
    public const int AttackRange = 12;

    /// <summary>
    /// Menor intervalo entre dois ataques do mesmo jogador. Baixo para não descartar golpes de quem tem velocidade
    /// de ataque alta (o client ataca mais rápido que 300 ms com a velocidade no máximo).
    /// </summary>
    public static readonly TimeSpan MinAttackInterval = TimeSpan.FromMilliseconds(100);

    public const int ViewRange = 16;

    /// <summary>Maior distância aceita entre a posição atual e o destino de um movimento.</summary>
    public const int MaxMoveDistance = 40;

    private const int CellSize = 32;

    /// <summary>Distância em que um monstro consegue bater (corpo a corpo).</summary>
    public const int MobAttackRange = 2;

    /// <summary>Intervalo entre dois golpes do mesmo monstro.</summary>
    public static readonly TimeSpan MobAttackInterval = TimeSpan.FromSeconds(2);

    /// <summary>Quantas células um monstro anda de cada vez quando persegue alguém.</summary>
    public const int ChaseStep = 6;

    private static bool IsDead(Character c) => c.BaseScore.Hp <= 0;

    /// <summary>A cada quanto tempo os vivos recuperam vida e mana (1/20 do máximo).</summary>
    public static readonly TimeSpan RegenInterval = TimeSpan.FromSeconds(6);

    private DateTime _nextRegen;

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
            (mob.X, mob.Y) = FreeCellLocked(mob.X, mob.Y, mob);
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

    /// <summary>Até quantas células em volta se procura um lugar livre quando o pedido já está ocupado.</summary>
    public const int FreeCellRadius = 4;

    /// <summary>Alguém (monstro vivo ou jogador, fora <paramref name="except"/>) está exatamente nessa célula (chamar com o lock).</summary>
    private bool OccupiedLocked(int x, int y, object? except) =>
        (_grid.TryGetValue(Cell(x, y), out var list) && list.Any(m => m != except && m.Alive && m.X == x && m.Y == y))
        || _players.Values.Any(p => p != except && p.Character!.PosX == x && p.Character.PosY == y);

    /// <summary>
    /// A célula pedida, ou a livre mais perto dela: como no servidor oficial, cada célula tem no máximo um
    /// monstro/NPC/jogador (o NPCGener traz cada rei duas vezes na mesma célula, e só perto deles o client cai).
    /// Chamar com o lock.
    /// </summary>
    private (short X, short Y) FreeCellLocked(short x, short y, object? except)
    {
        for (int r = 0; r <= FreeCellRadius; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int cx = x + dx, cy = y + dy;
                    if (cx is < 0 or >= MapSize || cy is < 0 or >= MapSize) continue;
                    if (!OccupiedLocked(cx, cy, except)) return ((short)cx, (short)cy);
                }
        return (x, y);
    }

    /// <summary>
    /// Tira do mapa (até o servidor reiniciar) os NPCs/monstros escolhidos e avisa quem os via. Ferramenta do GM
    /// para descobrir qual NPC derruba o client.
    /// </summary>
    public async Task<List<Mob>> RemoveMobsAsync(Func<Mob, bool> which)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        List<Mob> removed;
        lock (_lock)
        {
            removed = _mobs.Values.Where(m => m.Alive && which(m)).ToList();
            foreach (var mob in removed)
            {
                mob.Alive = false;
                mob.Target = 0;
                mob.RespawnAt = DateTime.MaxValue;
                GridRemove(mob);
                foreach (var player in _players.Values)
                    if (_visible[player.Id].Remove(mob.Id))
                        outgoing.Add((player, ServerPackets.RemoveMob(mob.Id)));
            }
        }
        await SendAllAsync(outgoing);
        return removed;
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
            if (!_players.ContainsKey(session.Id) || IsDead(me)) return false;
            if (destX is < 0 or >= MapSize || destY is < 0 or >= MapSize) return false;
            if (Math.Abs(destX - me.PosX) > MaxMoveDistance || Math.Abs(destY - me.PosY) > MaxMoveDistance) return false;

            me.PosX = destX;
            me.PosY = destY;
            session.TeleportedFrom = null;      // andou no lugar novo: o client carregou bem

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

    /// <summary>NPCs/monstros vivos que o jogador vê a até <paramref name="range"/> células.</summary>
    public List<Mob> NearbyMobs(ClientSession session, int range)
    {
        lock (_lock)
        {
            if (!_visible.TryGetValue(session.Id, out var mine)) return [];
            var me = session.Character!;
            return mine.Where(id => id >= Mob.FirstId && _mobs.ContainsKey(id)).Select(id => _mobs[id])
                .Where(m => m.Alive && Math.Abs(m.X - me.PosX) <= range && Math.Abs(m.Y - me.PosY) <= range)
                .ToList();
        }
    }

    /// <summary>Se há um monstro/NPC vivo que <paramref name="match"/> aceita a até <paramref name="distance"/> do personagem.</summary>
    public bool HasMobNear(Character c, Func<Mob, bool> match, int distance)
    {
        lock (_lock)
            return MobsNear(c.PosX, c.PosY).Any(m => m.Alive && match(m) &&
                Math.Abs(m.X - c.PosX) <= distance && Math.Abs(m.Y - c.PosY) <= distance);
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
    public async Task AttackAsync(ClientSession session, byte[] packet, int? auraDamage = null)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        var killed = new List<Mob>();
        var stored = new List<(int Slot, Item Item)>();
        var lost = new List<Item>();
        long exp;
        int levels;
        bool castSpell = false, noExpTable = false, noSkillData = false;
        short requested = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(ServerPackets.AttackLayout.Skill));
        if (auraDamage is null && requested == Skills.Resurrect)
        {
            await ResurrectAsync(session);
            return;
        }
        if (auraDamage is null && requested == Skills.ConjureCannon)
        {
            await session.SendMessageAsync("O Canhão Guardião só pode ser usado na guerra de castelos.");
            return;
        }
        lock (_lock)
        {
            if (!_players.ContainsKey(session.Id) || IsDead(session.Character!)) return;
            var now = DateTime.UtcNow;
            if (auraDamage is null && now - session.LastAttack < MinAttackInterval) return;

            var me = session.Character!;

            // Skill: número em 0x18 (-1 = ataque normal). Precisa ter aprendido e ter mana.
            short skill = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(ServerPackets.AttackLayout.Skill));
            WYD.Server.Data.SkillDefinition? spell = null;
            if (skill >= 0 && skillCatalog.Data is null && !session.WarnedNoSkillData)
                noSkillData = session.WarnedNoSkillData = true;
            if (auraDamage is null && skill >= 0 && !SkillUse.IsRangedNormal(skill))
            {
                if (!SkillUse.CanUse(me, skill)) return;
                if (skill == Skills.SoulOfLimits)
                {
                    if (now < me.SoulReadyAt) return;            // espera de 15 minutos entre um uso e outro
                    me.SoulReadyAt = now + Soul.Cooldown;
                }
                spell = skillCatalog.Data?[skill];
                int mana = spell is null ? 0 : SkillUse.ManaCost(spell, me.ItemBonus.SaveMana);
                if (me.BaseScore.Mp < mana) return;
                me.BaseScore.Mp -= mana;
                castSpell = mana > 0;
            }
            if (auraDamage is null) session.LastAttack = now;
            if (spell is not null && skill == Skills.ThornWallSkill)
                SummonThornsLocked(session,
                    System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(ServerPackets.AttackLayout.TargetX)),
                    System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(ServerPackets.AttackLayout.TargetY)),
                    now, outgoing);
            // Força Espectral: +1 de alcance, como o client.
            int attackRange = AttackRange + (Skills.Knows(me, Skills.SpectralForce) ? 1 : 0);

            var mine = _visible[session.Id];
            var response = ServerPackets.Relay(packet, session.Id);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(ServerPackets.AttackLayout.Attacker), session.Id);

            int count = Math.Min(ServerPackets.AttackLayout.MaxTargets,
                (packet.Length - ServerPackets.AttackLayout.Targets) / ServerPackets.AttackLayout.TargetSize);
            var hit = new List<Mob>();
            var buffed = new List<ClientSession>();
            for (int i = 0; i < count; i++)
            {
                int o = ServerPackets.AttackLayout.Targets + i * ServerPackets.AttackLayout.TargetSize;
                ushort targetId = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(o));
                short damage = 0;

                if (SkillUse.IsBuff(spell))
                {
                    // Buff: em si mesmo, ou num jogador que está vendo se a skill é de aliado.
                    bool allowed = targetId == session.Id
                        || (spell!.TargetType == SkillUse.TargetAlly && targetId < Mob.FirstId && mine.Contains(targetId));
                    if (allowed && _players.TryGetValue(targetId, out var ally) && !IsDead(ally.Character!)
                        && !buffed.Contains(ally) && Affects.FromSkill(me, spell!, skill, now) is { } affect)
                    {
                        Affects.Apply(ally.Character!, affect);
                        buffed.Add(ally);
                    }
                }
                else if (spell is { InstanceType: SkillUse.Heal })
                {
                    // Cura: em si mesmo ou num jogador que está vendo; aparece como dano negativo.
                    if ((targetId == session.Id || (targetId < Mob.FirstId && mine.Contains(targetId)))
                        && _players.TryGetValue(targetId, out var ally) && !IsDead(ally.Character!))
                    {
                        var a = ally.Character!;
                        int healed = Math.Min(SkillUse.HealAmount(me, spell, skill), a.MaxHp - a.BaseScore.Hp);
                        a.BaseScore.Hp += Math.Max(0, healed);
                        damage = (short)-Math.Max(0, healed);
                        outgoing.Add((ally, ServerPackets.UpdateHpMp(ally.Id, a.BaseScore.Hp, a.BaseScore.Mp)));
                    }
                }
                else if (SkillUse.IsDamage(spell)
                    && targetId >= Mob.FirstId && mine.Contains(targetId) && _mobs.TryGetValue(targetId, out var mob)
                    && mob.Alive && mob.Attackable && !hit.Contains(mob)
                    && Math.Abs(mob.X - me.PosX) <= attackRange && Math.Abs(mob.Y - me.PosY) <= attackRange)
                {
                    damage = (short)(auraDamage is int aura
                        ? Math.Clamp(aura - mob.Template.Score.Ac / 8, 1, short.MaxValue)       // aura (Trovão)
                        : spell is null
                            ? Math.Min(short.MaxValue, Combat.Damage(me.CurrentScore, mob.Template.Score,
                                  perforation: me.ItemBonus.Perforation) * (Combat.IsCritical(me.Critical) ? Combat.CriticalMultiplier : 1))
                            : SkillUse.Damage(me, spell, skill, mob.Template.Score));
                    mob.Hp -= damage;
                    hit.Add(mob);
                    if (mob.Hp <= 0) killed.Add(mob);
                    else if (mob.Target == 0 && !mob.IsSummon)
                    {
                        mob.Target = session.Id;                 // revida: passa a perseguir quem bateu
                        if (mob.NextAttack < now) mob.NextAttack = now + MobAttackInterval / 2;
                    }
                }
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(response.AsSpan(o + 2), damage);
            }

            // Buff sem alvo de jogador no pacote (o client manda o próprio id ou nenhum): vale em quem lançou.
            if (SkillUse.IsBuff(spell) && buffed.Count == 0 && Affects.FromSkill(me, spell!, skill, now) is { } self)
            {
                Affects.Apply(me, self);
                buffed.Add(session);
            }

            foreach (var mob in killed)
            {
                long gained = Math.Max(0, mob.Template.Exp) * (100 + me.ItemBonus.ExpBonus) / 100;   // ancient de experiência
                me.Exp = Math.Min(uint.MaxValue, me.Exp + gained);
                me.Coin = (int)Math.Min(Handlers.ItemHandler.MaxCoin, (long)me.Coin + Math.Max(0, mob.Template.Coin));
            }
            exp = me.Exp;
            levels = killed.Count > 0 ? Leveling.Apply(me, expTable.For(me.Evolution)) : 0;
            noExpTable = killed.Count > 0 && expTable.For(me.Evolution) is null && !session.WarnedNoExpTable;
            if (noExpTable) session.WarnedNoExpTable = true;

            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(ServerPackets.AttackLayout.CurrentMp),
                Score.Unsigned(me.BaseScore.Mp));
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(ServerPackets.AttackLayout.Hold), 0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(ServerPackets.AttackLayout.Exp), ServerPackets.ClientExp(exp));

            // Quem vê o atacante ou algum dos alvos recebe o ataque com os danos.
            foreach (var player in _players.Values)
                if (player == session || InRange(player.Character!, me) || hit.Any(m => InRange(player.Character!, m)))
                    outgoing.Add((player, response));

            // Depois da animação da skill: a ficha com o efeito (o client liga o efeito visual, como a aura do
            // Trovão, a partir dela) para o alvo e quem o vê, e os ícones com o tempo para o alvo.
            foreach (var target in buffed)
            {
                var score = ServerPackets.UpdateScore(target.Id, target.Character!);
                outgoing.Add((target, score));
                outgoing.Add((target, ServerPackets.AffectList(target.Id, target.Character!)));
                foreach (var viewer in _players.Values)
                    if (viewer != target && _visible[viewer.Id].Contains(target.Id))
                        outgoing.Add((viewer, score));
            }

            foreach (var mob in killed)
            {
                mob.Alive = false;
                mob.Hp = 0;
                mob.Target = 0;
                mob.RespawnAt = now + TimeSpan.FromSeconds(Math.Max(1, options.Value.RespawnSeconds));
                GridRemove(mob);
                foreach (var player in _players.Values)
                {
                    if (!_visible[player.Id].Remove(mob.Id)) continue;
                    outgoing.Add((player, ServerPackets.MobKill(mob.Id, session.Id, unchecked((int)ServerPackets.ClientExp(exp)))));
                    outgoing.Add((player, ServerPackets.RemoveMob(mob.Id, ServerPackets.RemoveDead)));
                }
                if (mob.IsSummon)
                {
                    _mobs.Remove(mob.Id);                        // invocação: não renasce nem dropa
                    continue;
                }

                // Drop: os itens da lista do monstro que saírem no sorteio vão direto para o inventário.
                var (into, missed) = StoreDrops(me, RollDrops(mob, me.ItemBonus.DropBonus));
                stored.AddRange(into);
                lost.AddRange(missed);
            }
        }

        await SendAllAsync(outgoing);
        if (noSkillData)
        {
            logger.LogWarning("Sem SkillData.bin: as skills não têm efeito. Copie a SkillData.bin do client para a pasta data.");
            await session.SendAsync(ServerPackets.Whisper(session.Id, "Sistema",
                "Servidor sem data/SkillData.bin: as skills não têm efeito. Copie a SkillData.bin do client."));
        }
        if (castSpell)
            await session.SendAsync(ServerPackets.UpdateHpMp(session.Id, session.Character!.BaseScore.Hp, session.Character.BaseScore.Mp));
        if (killed.Count > 0)
        {
            var me = session.Character!;
            await session.SendAsync(ServerPackets.UpdateEtc(session.Id, me));   // exp, gold e pontos
            if (noExpTable)
            {
                logger.LogWarning("Sem tabela de experiência para {Evolution}: ninguém sobe de nível. Rode extract-exptable <WYD.exe>.",
                    Evolutions.Name(me.Evolution));
                await session.SendAsync(ServerPackets.Whisper(session.Id, "Sistema",
                    "Servidor sem data/ExpTable.txt: ninguém sobe de nível. Rode extract-exptable com o WYD.exe."));
            }
            if (levels > 0)
            {
                var levelUp = ServerPackets.LevelUp(session.Id);
                await session.SendAsync(levelUp);
                foreach (var viewer in Viewers(session))
                    await viewer.SendAsync(levelUp);
                await session.SendAsync(ServerPackets.UpdateScore(session.Id, me));
                await session.SendAsync(ServerPackets.UpdateHpMp(session.Id, me.BaseScore.Hp, me.BaseScore.Mp));
                logger.LogInformation("[{Id}] {Name} subiu para o nível {Level}.", session.Id, me.Name, me.BaseScore.Level + 1);
                await session.SendMessageAsync(Leveling.CanEvolve(me)
                    ? $"Nível máximo de {Evolutions.Name(me.Evolution)}! Fale com o rei do seu reino (/kingdom) para virar {Evolutions.Name(me.Evolution + 1)}."
                    : ServerPackets.LevelUpText);
            }
            logger.LogInformation("[{Id}] {Name} matou {Mobs} (exp {Exp}, gold {Coin}).", session.Id, me.Name,
                string.Join(", ", killed.Select(m => m.Template.Name)), exp, me.Coin);
            foreach (var (slot, item) in stored)
                await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, slot, item));
            if (stored.Count > 0)
                logger.LogInformation("[{Id}] Drop para o inventário: {Items}.", session.Id, string.Join(", ", stored.Select(s => s.Item.Index)));
            if (lost.Count > 0)
            {
                logger.LogInformation("[{Id}] Inventário cheio, drop perdido: {Items}.", session.Id, string.Join(", ", lost.Select(i => i.Index)));
                await session.SendMessageAsync("Inventário cheio: o drop foi perdido.");
            }
            if (stored.Count > 0)
            {
                try { await characters.SaveItemsAsync(me); }
                catch (Exception ex) { logger.LogError(ex, "Não foi possível salvar os itens de {Name}.", me.Name); }
            }
            try { await characters.SaveProgressAsync(me); }
            catch (Exception ex) { logger.LogError(ex, "Não foi possível salvar o progresso de {Name}.", me.Name); }
        }
    }

    /// <summary>
    /// Monstros que estão perseguindo alguém: chegam perto e batem a cada <see cref="MobAttackInterval"/>.
    /// Desistem se o jogador sair, morrer ou se afastar mais que o campo de visão.
    /// Retorna os monstros que estão ocupados (não devem passear).
    /// </summary>
    public async Task<HashSet<Mob>> MobActionsAsync(DateTime now)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        var chases = new List<(Mob Mob, short X, short Y)>();
        var busy = new HashSet<Mob>();
        var deaths = new List<ClientSession>();
        lock (_lock)
        {
            foreach (var mob in _mobs.Values)
            {
                if (!mob.Alive || mob.Target == 0) continue;
                if (!_players.TryGetValue(mob.Target, out var victim) || IsDead(victim.Character!)
                    || !InRange(victim.Character!, mob))
                {
                    mob.Target = 0;
                    continue;
                }
                busy.Add(mob);

                var c = victim.Character!;
                int dx = c.PosX - mob.X, dy = c.PosY - mob.Y;
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > MobAttackRange)
                {
                    if (mob.NextMove > now) continue;
                    // Para uma célula antes do jogador, andando no máximo ChaseStep.
                    int stepX = Math.Clamp(dx - Math.Sign(dx), -ChaseStep, ChaseStep);
                    int stepY = Math.Clamp(dy - Math.Sign(dy), -ChaseStep, ChaseStep);
                    mob.NextMove = now + TimeSpan.FromSeconds(1);
                    chases.Add((mob, (short)(mob.X + stepX), (short)(mob.Y + stepY)));
                    continue;
                }

                if (mob.NextAttack > now) continue;
                mob.NextAttack = now + MobAttackInterval;
                var buff = Affects.Bonus(c);
                int damage = Random.Shared.Next(100) < buff.Dodge
                    ? 0                                                       // esquiva (Dodge)
                    : Combat.Damage(mob.Template.Score, c.CurrentScore, absorption: c.ItemBonus.Absorption);
                c.BaseScore.Hp = Math.Max(0, c.BaseScore.Hp - damage);

                var attack = ServerPackets.MobAttack(mob.Id, mob.X, mob.Y, victim.Id, c.PosX, c.PosY, (short)damage);
                foreach (var player in _players.Values)
                    if (player == victim || InRange(player.Character!, mob) || InRange(player.Character!, c))
                        outgoing.Add((player, attack));
                outgoing.Add((victim, ServerPackets.UpdateHpMp(victim.Id, c.BaseScore.Hp, c.BaseScore.Mp)));

                if (IsDead(c))
                {
                    deaths.Add(victim);
                    // Todos que veem o jogador recebem a morte; os monstros param de persegui-lo.
                    foreach (var player in _players.Values)
                        if (player == victim || _visible[player.Id].Contains(victim.Id))
                            outgoing.Add((player, ServerPackets.MobKill(victim.Id, mob.Id, 0)));
                    foreach (var other in _mobs.Values.Where(m => m.Target == victim.Id))
                        other.Target = 0;
                }
            }
        }

        await SendAllAsync(outgoing);
        foreach (var (mob, x, y) in chases)
            await MoveMobAsync(mob, x, y);
        foreach (var victim in deaths)
            logger.LogInformation("[{Id}] {Name} morreu.", victim.Id, victim.Character!.Name);
        return busy;
    }

    /// <summary>
    /// 0x289: o personagem morto renasce com vida e mana cheias na cidade (StartX/StartY). Quem o via perde o
    /// corpo e quem estiver perto do novo lugar passa a vê-lo.
    /// </summary>
    public async Task ReviveAsync(ClientSession session)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        var me = session.Character!;
        lock (_lock)
        {
            if (!_players.ContainsKey(session.Id) || !IsDead(me)) return;
            me.BaseScore.Hp = me.MaxHp;
            me.BaseScore.Mp = me.MaxMp;
            TeleportLocked(session, (short)options.Value.StartX, (short)options.Value.StartY, outgoing);
            outgoing.Add((session, ServerPackets.UpdateScore(session.Id, me)));
            outgoing.Add((session, ServerPackets.UpdateHpMp(session.Id, me.BaseScore.Hp, me.BaseScore.Mp)));
        }
        await SendAllAsync(outgoing);
        logger.LogInformation("[{Id}] {Name} renasceu em ({X}, {Y}).", session.Id, me.Name, me.PosX, me.PosY);
        await SaveAsync(me);
    }

    /// <summary>Vida (em % da máxima) com que a Ressurreição traz o personagem de volta.</summary>
    public const int ResurrectHpPercent = 30;

    /// <summary>
    /// Ressurreição: morto, o jogador recusa voltar para a cidade, clica com o botão direito e confirma (diálogo 99);
    /// 5 s depois o client faz a animação e manda o 0x39D com a skill 99 nele mesmo (0x472329). Renasce ali com
    /// <see cref="ResurrectHpPercent"/>% da vida.
    /// </summary>
    public async Task ResurrectAsync(ClientSession session)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        var me = session.Character!;
        lock (_lock)
        {
            if (!_players.ContainsKey(session.Id) || !IsDead(me) || !Skills.Knows(me, Skills.Resurrect)) return;
            me.BaseScore.Hp = Math.Max(1, me.MaxHp * ResurrectHpPercent / 100);
            TeleportLocked(session, me.PosX, me.PosY, outgoing);
            outgoing.Add((session, ServerPackets.UpdateScore(session.Id, me)));
            outgoing.Add((session, ServerPackets.UpdateHpMp(session.Id, me.BaseScore.Hp, me.BaseScore.Mp)));
        }
        await SendAllAsync(outgoing);
        logger.LogInformation("[{Id}] {Name} ressuscitou em ({X}, {Y}).", session.Id, me.Name, me.PosX, me.PosY);
        await SaveAsync(me);
    }

    /// <summary>Tamanho do mapa em células (0 a 4095).</summary>
    public const int MapSize = 4096;

    /// <summary>Teleporta o jogador (comandos de GM) e grava a posição.</summary>
    public async Task TeleportAsync(ClientSession session, short x, short y)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            if (!_players.ContainsKey(session.Id)) return;
            TeleportLocked(session, x, y, outgoing);
        }
        await SendAllAsync(outgoing);
        await SaveAsync(session.Character!);
    }

    /// <summary>Leva o jogador direto para (x, y) e refaz quem ele vê e quem o vê (chamar com o lock).</summary>
    private void TeleportLocked(ClientSession session, short x, short y, List<(ClientSession To, byte[] Packet)> outgoing)
    {
        var me = session.Character!;
        session.TeleportedFrom ??= (me.PosX, me.PosY, DateTime.UtcNow);
        session.TeleportedFrom = session.TeleportedFrom.Value with { At = DateTime.UtcNow };
        var mine = _visible[session.Id];
        foreach (var id in mine.Where(id => id < Mob.FirstId).ToList())
        {
            mine.Remove(id);
            if (!_players.TryGetValue(id, out var other)) continue;
            _visible[id].Remove(session.Id);
            outgoing.Add((session, ServerPackets.RemoveMob(id)));
            outgoing.Add((other, ServerPackets.RemoveMob(session.Id)));
        }

        (x, y) = FreeCellLocked(x, y, session);
        me.PosX = x;
        me.PosY = y;
        outgoing.Add((session, ServerPackets.Teleport(session.Id, x, y)));

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

    /// <summary>Recuperação natural: a cada <see cref="RegenInterval"/>, 5% da vida e da mana máximas.</summary>
    public Task RegenerateAsync(DateTime now)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            if (now < _nextRegen) return Task.CompletedTask;
            _nextRegen = now + RegenInterval;
            foreach (var player in _players.Values)
            {
                var s = player.Character!;
                if (IsDead(s) || (s.BaseScore.Hp >= s.MaxHp && s.BaseScore.Mp >= s.MaxMp)) continue;
                // 5% do máximo, a regeneração dos itens (efeitos 47 e 48, como os anéis de vida) e a Aura da Vida
                s.BaseScore.Hp = Math.Min(s.MaxHp,
                    s.BaseScore.Hp + Math.Max(1, s.MaxHp / 20) + s.ItemBonus.RegenHp + Affects.Bonus(s).RegenHp);
                s.BaseScore.Mp = Math.Min(s.MaxMp, s.BaseScore.Mp + Math.Max(1, s.MaxMp / 20) + s.ItemBonus.RegenMp);
                outgoing.Add((player, ServerPackets.UpdateHpMp(player.Id, s.BaseScore.Hp, s.BaseScore.Mp)));
            }
        }
        return SendAllAsync(outgoing);
    }

    /// <summary>Golpe que o client desenha em cada alvo da aura: o raio da Thunderbolt (33) no Trovão.</summary>
    public const short ThunderboltSkill = 33;

    private static short AuraHitSkill(Affect aura) =>
        aura.Type == AffectType.LightningStorm ? ThunderboltSkill : (short)aura.Skill;

    /// <summary>Distância (em células) que as auras de dano (Trovão, Corrente Elemental) alcançam.</summary>
    public const int AuraRange = 3;

    /// <summary>A cada quanto tempo as auras de dano batem.</summary>
    public static readonly TimeSpan AuraInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Auras de dano (Tempestade de Raios, Corrente Elemental): enquanto o efeito dura, batem nos monstros em volta
    /// a cada <see cref="AuraInterval"/>, com o golpe da skill (o client desenha o raio em cada alvo).
    /// </summary>
    public async Task AurasAsync(DateTime now)
    {
        var hits = new List<(ClientSession Session, byte[] Packet, int Damage)>();
        var refresh = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            foreach (var player in _players.Values)
            {
                var c = player.Character!;
                if (IsDead(c) || c.NextAuraTick > now) continue;
                var aura = c.Affects.FirstOrDefault(a => a.Type is AffectType.LightningStorm or AffectType.ElementalStream && a.Expires > now);
                if (aura is null) continue;
                c.NextAuraTick = now + AuraInterval;
                // A ficha com o efeito de novo: se o client apagou a aura visual, ela volta (0x52D286).
                var score = ServerPackets.UpdateScore(player.Id, c);
                refresh.Add((player, score));
                foreach (var viewer in _players.Values)
                    if (viewer != player && _visible[viewer.Id].Contains(player.Id))
                        refresh.Add((viewer, score));
                var targets = MobsNear(c.PosX, c.PosY)
                    .Where(m => m.Alive && m.Attackable && _visible[player.Id].Contains(m.Id)
                                && Math.Abs(m.X - c.PosX) <= AuraRange && Math.Abs(m.Y - c.PosY) <= AuraRange)
                    .Take(ServerPackets.AttackLayout.MaxTargets).ToList();
                if (targets.Count == 0) continue;
                int damage = aura.Amount + c.CurrentScore.Int / 4;
                hits.Add((player, ServerPackets.SkillHit(player.Id, c.PosX, c.PosY, AuraHitSkill(aura),
                    targets.Select(m => (m.Id, m.X, m.Y)).ToList()), damage));
            }
        }
        await SendAllAsync(refresh);
        foreach (var (session, packet, damage) in hits)
            await AttackAsync(session, packet, damage);
    }

    /// <summary>Tira os buffs que acabaram e manda a ficha nova (sem o ícone) a quem perdeu algum.</summary>
    public Task ExpireAffectsAsync(DateTime now)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            foreach (var player in _players.Values)
            {
                var c = player.Character!;
                if (c.Affects.Count == 0 || !Affects.Expire(c, now)) continue;
                c.BaseScore.Hp = Math.Min(c.BaseScore.Hp, c.MaxHp);
                c.BaseScore.Mp = Math.Min(c.BaseScore.Mp, c.MaxMp);
                outgoing.Add((player, ServerPackets.UpdateScore(player.Id, c)));
                outgoing.Add((player, ServerPackets.AffectList(player.Id, c)));
            }
        }
        return SendAllAsync(outgoing);
    }

    /// <summary>Muro de Espinhos: tira o muro anterior do jogador e põe as plantas nas células livres (chamar com o lock).</summary>
    private void SummonThornsLocked(ClientSession session, short targetX, short targetY, DateTime now,
        List<(ClientSession To, byte[] Packet)> outgoing)
    {
        var me = session.Character!;
        foreach (var old in _mobs.Values.Where(m => m.IsSummon && m.Owner == session.Id).ToList())
            RemoveSummonLocked(old, outgoing);

        var template = ThornWall.Template(me);
        var rule = new WYD.Server.Data.SpawnRule { Leader = ThornWall.Name, StartX = me.PosX, StartY = me.PosY };
        foreach (var (x, y) in ThornWall.Cells(me.PosX, me.PosY, targetX, targetY))
        {
            if (x is < 0 or >= MapSize || y is < 0 or >= MapSize || OccupiedLocked(x, y, null)) continue;
            if (FreeSummonIdLocked() is not ushort id) break;
            var mob = new Mob(id, template, rule, x, y) { Expires = now + ThornWall.Duration, Owner = session.Id };
            _mobs[id] = mob;
            GridAdd(mob);
            foreach (var player in _players.Values)
            {
                if (!InRange(player.Character!, mob)) continue;
                _visible[player.Id].Add(mob.Id);
                outgoing.Add((player, mob.CreatePacket()));
            }
        }
    }

    /// <summary>Id livre para uma invocação, de cima para baixo (os monstros do mapa usam os de baixo).</summary>
    private ushort? FreeSummonIdLocked()
    {
        for (int id = Mob.LastId; id >= Mob.FirstId; id--)
            if (!_mobs.ContainsKey((ushort)id)) return (ushort)id;
        return null;
    }

    /// <summary>Tira uma invocação do mapa de vez (chamar com o lock).</summary>
    private void RemoveSummonLocked(Mob mob, List<(ClientSession To, byte[] Packet)> outgoing)
    {
        if (mob.Alive) GridRemove(mob);
        mob.Alive = false;
        _mobs.Remove(mob.Id);
        foreach (var player in _players.Values)
            if (_visible[player.Id].Remove(mob.Id))
                outgoing.Add((player, ServerPackets.RemoveMob(mob.Id)));
    }

    /// <summary>Tira as invocações cujo tempo acabou.</summary>
    public Task ExpireSummonsAsync(DateTime now)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
            foreach (var mob in _mobs.Values.Where(m => m.IsSummon && (m.Expires <= now || !m.Alive)).ToList())
                RemoveSummonLocked(mob, outgoing);
        return SendAllAsync(outgoing);
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
            (mob.X, mob.Y) = FreeCellLocked(x, y, mob);
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

    /// <summary>Quanto tempo depois de um teleporte uma queda do client conta como "o client não aguentou o destino".</summary>
    public static readonly TimeSpan TeleportCrashWindow = TimeSpan.FromSeconds(60);

    private void OnClosed(ClientSession session)
    {
        if (session.TeleportedFrom is { } from && DateTime.UtcNow - from.At < TeleportCrashWindow)
        {
            var c = session.Character!;
            logger.LogWarning("[{Id}] {Name} saiu logo depois de um teleporte para ({X}, {Y}) sem andar: volta para ({FX}, {FY}).",
                session.Id, c.Name, c.PosX, c.PosY, from.X, from.Y);
            c.PosX = from.X;
            c.PosY = from.Y;
        }

        var outgoing = RemovePlayer(session);
        if (outgoing is null) return;
        _ = Task.Run(async () =>
        {
            await SendAllAsync(outgoing);
            await SaveAsync(session.Character!);
            // Os buffs ficam gravados com o tempo que falta: o relógio para até o próximo login.
            try { await characters.SaveProgressAsync(session.Character!); }
            catch (Exception ex) { logger.LogError(ex, "Não foi possível salvar o progresso de {Name}.", session.Character!.Name); }
        });
    }

    /// <summary>
    /// Tira o jogador do mundo sem desconectar (volta para a seleção de personagens): some da tela dos outros e a
    /// posição é gravada.
    /// </summary>
    public async Task LeaveAsync(ClientSession session)
    {
        session.Closed -= OnClosed;
        session.TeleportedFrom = null;
        if (RemovePlayer(session) is not { } outgoing) return;
        await SendAllAsync(outgoing);
        await SaveAsync(session.Character!);
    }

    /// <summary>Tira o jogador das listas e devolve os 0x165 para quem o via (null se ele já não estava no mundo).</summary>
    private List<(ClientSession To, byte[] Packet)>? RemovePlayer(ClientSession session)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            if (!_players.Remove(session.Id)) return null;
            if (_visible.Remove(session.Id, out var mine))
            {
                foreach (var id in mine)
                {
                    if (!_players.TryGetValue(id, out var other)) continue;
                    _visible[id].Remove(session.Id);
                    outgoing.Add((other, ServerPackets.RemoveMob(session.Id)));
                }
            }
            foreach (var mob in _mobs.Values.Where(m => m.Target == session.Id))
                mob.Target = 0;
        }
        return outgoing;
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
