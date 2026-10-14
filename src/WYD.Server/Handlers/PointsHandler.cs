using Microsoft.Extensions.Logging;
using WYD.Server.Database;
using WYD.Server.Game;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>0x277: o jogador clicou no "+" de um atributo ou especial na ficha.</summary>
public sealed class PointsHandler(ICharacterRepository characters, ILogger<PointsHandler> logger)
{
    public async Task HandleAsync(ClientSession session, PacketReader packet)
    {
        short mode = packet.ReadInt16();
        short which = packet.ReadInt16();
        var c = session.Character!;
        if (c.BaseScore.Hp <= 0) return;

        if (Points.Apply(c, mode, which) is { } refused)
        {
            await session.SendMessageAsync(refused);
            // A ficha volta ao que o servidor tem, caso o client tenha mudado algo por conta própria.
            await session.SendAsync(ServerPackets.UpdateEtc(session.Id, c));
            return;
        }

        await session.SendAsync(ServerPackets.UpdateScore(session.Id, c));
        await session.SendAsync(ServerPackets.UpdateEtc(session.Id, c));
        try { await characters.SaveProgressAsync(c); }
        catch (Exception ex) { logger.LogError(ex, "Não foi possível salvar os pontos de {Name}.", c.Name); }
    }
}
