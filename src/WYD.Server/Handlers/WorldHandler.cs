using WYD.Server.Game;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>Pacotes de quem já está no mapa: movimento, pedido de dados de um mob e chat.</summary>
public sealed class WorldHandler(World world)
{
    /// <summary>0x366 / 0x367: X, Y em 0x0C; destino em 0x18; rota em 0x1C.</summary>
    public async Task HandleMoveAsync(ClientSession session, PacketReader packet)
    {
        var raw = packet.ToArray();
        short destX = BitConverter.ToInt16(raw, 0x18);
        short destY = BitConverter.ToInt16(raw, 0x1A);
        await world.MoveAsync(session, raw, destX, destY);
    }

    /// <summary>0x369: id pedido em 0x0C.</summary>
    public Task HandleRequestMobAsync(ClientSession session, PacketReader packet) =>
        world.RequestMobAsync(session, packet.ReadUInt16());

    /// <summary>0x333: texto de 96 caracteres em 0x0C.</summary>
    public Task HandleChatAsync(ClientSession session, PacketReader packet)
    {
        var raw = packet.ToArray();
        raw[^1] = 0; // garante o fim da string
        return world.ChatAsync(session, raw);
    }
}
