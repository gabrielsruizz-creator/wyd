using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Grupo, como o client 7.57 conversa (0x37F convite, 0x3AB aceitar, 0x37D membro na janela, 0x37E sair):
/// <list type="bullet">
/// <item>Quem não tem grupo, ou o líder, convida clicando num jogador; o convidado aceita e entra no grupo do líder.</item>
/// <item>Cada um vê na janela os outros membros (o líder marcado) e as criaturas da Evocação de todos.</item>
/// <item>Sair: o membro sai sozinho; se o líder sai, ou sobra só ele, o grupo acaba. O líder também tira membros.</item>
/// <item>Quem sai do grupo (ou do jogo) perde a criatura da Evocação, como no original.</item>
/// <item>A experiência de quem mata é dividida entre os membros vivos perto dele (ver <see cref="PartyShare"/>).</item>
/// </list>
/// </summary>
public sealed partial class World
{
    public const int MaxPartyMembers = 12;

    /// <summary>Quanto a experiência do grupo soma por membro além do primeiro (%).</summary>
    public const int PartyBonusPercent = 10;

    private sealed class Party(ClientSession leader)
    {
        public ClientSession Leader { get; } = leader;
        public List<ClientSession> Members { get; } = [leader];
    }

    /// <summary>Grupo de cada jogador (pelo id da sessão).</summary>
    private readonly Dictionary<ushort, Party> _parties = [];

    /// <summary>Convites pendentes: convidado → líder.</summary>
    private readonly Dictionary<ushort, ushort> _invites = [];

    /// <summary>Experiência de cada um quando <paramref name="members"/> dividem <paramref name="exp"/>.</summary>
    public static long PartyShare(long exp, int members) =>
        members <= 1 ? exp : exp * (100 + PartyBonusPercent * (members - 1)) / 100 / members;

    /// <summary>0x37F do client: convida o jogador do campo Alvo.</summary>
    public Task InviteAsync(ClientSession session, byte[] packet)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        string? refusal = null;
        lock (_lock)
        {
            ushort targetId = (ushort)System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(0x28));
            if (!_players.ContainsKey(session.Id)) return Task.CompletedTask;
            _parties.TryGetValue(session.Id, out var party);
            if (!_players.TryGetValue(targetId, out var target) || target == session || !_visible[session.Id].Contains(targetId))
                refusal = "Jogador não encontrado.";
            else if (party is not null && party.Leader != session)
                refusal = "Só o líder do grupo pode convidar.";
            else if (party is not null && party.Members.Count >= MaxPartyMembers)
                refusal = "O grupo está cheio.";
            else if (_parties.ContainsKey(targetId))
                refusal = $"{target.Character!.Name} já está num grupo.";
            else
            {
                _invites[targetId] = session.Id;
                outgoing.Add((target, ServerPackets.PartyInvite(session.Id, session.Character!, targetId)));
            }
        }
        return refusal is null ? SendAllAsync(outgoing) : session.SendMessageAsync(refusal);
    }

    /// <summary>0x3AB do client: aceita o convite do líder.</summary>
    public Task AcceptPartyAsync(ClientSession session, byte[] packet)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        string? refusal = null;
        lock (_lock)
        {
            ushort leaderId = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0x0C));
            if (!_players.ContainsKey(session.Id)) return Task.CompletedTask;
            if (!_invites.Remove(session.Id, out var invitedBy) || invitedBy != leaderId
                || !_players.TryGetValue(leaderId, out var leader))
                refusal = "O convite não vale mais.";
            else if (_parties.ContainsKey(session.Id))
                refusal = "Você já está num grupo.";
            else if (_parties.TryGetValue(leaderId, out var other) && other.Leader != leader)
                refusal = "Esse jogador entrou em outro grupo.";
            else
            {
                if (!_parties.TryGetValue(leaderId, out var party))
                    _parties[leaderId] = party = new Party(leader);
                if (party.Members.Count >= MaxPartyMembers)
                    refusal = "O grupo está cheio.";
                else
                {
                    // O novo vê os outros (e as criaturas deles); os outros o veem (e a criatura dele).
                    foreach (var member in party.Members)
                    {
                        outgoing.Add((session, ServerPackets.PartyAdd(member.Id, member.Character!, member == party.Leader)));
                        foreach (var pet in PetsOfLocked(member)) outgoing.Add((session, PetEntry(pet)));
                        outgoing.Add((member, ServerPackets.PartyAdd(session.Id, session.Character!, false)));
                        foreach (var pet in PetsOfLocked(session)) outgoing.Add((member, PetEntry(pet)));
                    }
                    party.Members.Add(session);
                    _parties[session.Id] = party;
                }
            }
        }
        return refusal is null ? SendAllAsync(outgoing) : session.SendMessageAsync(refusal);
    }

    /// <summary>0x37E do client: 0 (ou o próprio id) é sair; o id de outro membro é o líder tirando-o; o de uma criatura dele é dispensá-la.</summary>
    public Task RemovePartyAsync(ClientSession session, byte[] packet)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            if (!_players.ContainsKey(session.Id)) return Task.CompletedTask;
            ushort id = (ushort)System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(0x0C));
            if (id != 0 && _mobs.TryGetValue(id, out var mob) && mob.IsPet && mob.Owner == session.Id)
                RemoveSummonLocked(mob, outgoing);
            else if (id == 0 || id == session.Id)
                LeavePartyLocked(session, outgoing);
            else if (_parties.TryGetValue(session.Id, out var party) && party.Leader == session
                     && party.Members.FirstOrDefault(m => m.Id == id) is { } kicked)
                LeavePartyLocked(kicked, outgoing);
        }
        return SendAllAsync(outgoing);
    }

    /// <summary>
    /// Tira o jogador do grupo (e as criaturas da Evocação dele); o grupo acaba se for o líder ou se sobrar um só.
    /// Com <paramref name="notify"/> falso (saindo do jogo) ele mesmo não recebe nada.
    /// </summary>
    private void LeavePartyLocked(ClientSession session, List<(ClientSession To, byte[] Packet)> outgoing, bool notify = true)
    {
        _invites.Remove(session.Id);
        var pets = PetsOfLocked(session).ToList();
        foreach (var pet in pets)
            RemoveSummonLocked(pet, outgoing);
        bool inParty = _parties.ContainsKey(session.Id);
        if (notify && (inParty || pets.Count > 0)) outgoing.Add((session, ServerPackets.PartyRemove(0)));
        if (!_parties.Remove(session.Id, out var party)) return;

        party.Members.Remove(session);
        if (party.Leader == session || party.Members.Count <= 1)
        {
            foreach (var member in party.Members)
            {
                _parties.Remove(member.Id);
                outgoing.Add((member, ServerPackets.PartyRemove(0)));
            }
            return;
        }
        foreach (var member in party.Members)
            outgoing.Add((member, ServerPackets.PartyRemove(session.Id)));
    }

    /// <summary>De quanto em quanto tempo as barras de vida da janela do grupo são atualizadas.</summary>
    public static readonly TimeSpan PartyBarInterval = TimeSpan.FromSeconds(1);

    private DateTime _nextPartyBars;

    /// <summary>
    /// Barras da janela do grupo: quem mudou de vida, vida máxima ou nível desde a última vez manda o 0x336 para os
    /// outros membros (o client atualiza a barra do id dele na janela, 0x51158F).
    /// </summary>
    public Task PartyBarsAsync(DateTime now)
    {
        var outgoing = new List<(ClientSession To, byte[] Packet)>();
        lock (_lock)
        {
            if (now < _nextPartyBars) return Task.CompletedTask;
            _nextPartyBars = now + PartyBarInterval;
            foreach (var party in _parties.Values.Distinct())
                foreach (var member in party.Members)
                {
                    var c = member.Character!;
                    var bar = (c.BaseScore.Hp, c.MaxHp, c.BaseScore.Level);
                    if (c.PartyBar == bar) continue;
                    c.PartyBar = bar;
                    var score = ServerPackets.UpdateScore(member.Id, c);
                    foreach (var other in party.Members)
                        if (other != member) outgoing.Add((other, score));
                }
        }
        return SendAllAsync(outgoing);
    }

    /// <summary>Quem está no grupo do jogador (ele incluído); só ele se não tem grupo.</summary>
    private IReadOnlyList<ClientSession> PartyOfLocked(ClientSession session) =>
        _parties.TryGetValue(session.Id, out var party) ? party.Members : [session];

    private IEnumerable<Mob> PetsOfLocked(ClientSession owner) =>
        _mobs.Values.Where(m => m.IsPet && m.Alive && m.Owner == owner.Id);

    private static byte[] PetEntry(Mob pet) =>
        ServerPackets.PartyAdd(pet.Id, pet.Template.Name, 0, false, pet.Template.Score.Level, pet.Template.Score.MaxHp, pet.Hp);
}
