using Microsoft.Extensions.Logging;
using WYD.Server.Database;
using WYD.Server.Game;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>0x290: o jogador confirmou o teleporte em cima de um círculo da cidade.</summary>
public sealed class TeleportHandler(World world, TeleportTable teleports, ICharacterRepository characters, ILogger<TeleportHandler> logger)
{
    public async Task HandleAsync(ClientSession session, PacketReader packet)
    {
        var c = session.Character!;
        if (c.BaseScore.Hp <= 0) return;

        if (teleports.Find(c.PosX, c.PosY) is not { } point)
        {
            logger.LogInformation("[{Id}] {Name} pediu teleporte em ({X}, {Y}), mas não há destino cadastrado aí.", session.Id, c.Name, c.PosX, c.PosY);
            await session.SendMessageAsync(session.Account!.AccessLevel > 0
                ? $"Teleporte sem destino em {c.PosX} {c.PosY}. Use /gm tp add <x> <y> [preço] [nome]."
                : "Este teleporte ainda não está funcionando.");
            return;
        }

        if (c.Coin < point.Price)
        {
            await session.SendMessageAsync($"O teleporte custa {point.Price:N0} de gold.");
            return;
        }

        if (point.Price > 0)
        {
            c.Coin -= point.Price;
            await session.SendAsync(ServerPackets.UpdateCoin(session.Id, c.Coin));
            await characters.SaveProgressAsync(c);
        }
        logger.LogInformation("[{Id}] {Name} usou o teleporte {Tp} ({X}, {Y}) -> ({DX}, {DY}).",
            session.Id, c.Name, point.Name, point.X, point.Y, point.DestX, point.DestY);
        await world.TeleportAsync(session, point.DestX, point.DestY);
    }
}
