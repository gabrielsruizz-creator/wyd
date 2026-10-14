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

    /// <summary>Distância máxima para abrir a loja de um NPC.</summary>
    public const int ShopDistance = 8;

    /// <summary>0x27B: o jogador clicou num NPC de loja. Responde com a lista de itens (0x17C).</summary>
    public Task HandleRequestShopAsync(ClientSession session, PacketReader packet)
    {
        ushort npcId = packet.ReadUInt16();
        var npc = world.VisibleMob(session, npcId);
        var me = session.Character!;
        if (npc is null || !(npc.IsShop || npc.IsSkillMaster)
            || Math.Abs(npc.X - me.PosX) > ShopDistance || Math.Abs(npc.Y - me.PosY) > ShopDistance)
            return Task.CompletedTask;

        return session.SendAsync(ServerPackets.ShopList(npc.Id, npc.Template.Carry));
    }

    /// <summary>0x39D / 0x39E / 0x36C: ataque com 1, 2 ou até 13 alvos.</summary>
    public Task HandleAttackAsync(ClientSession session, PacketReader packet) =>
        world.AttackAsync(session, packet.ToArray());

    /// <summary>0x289: o personagem morto pede para renascer.</summary>
    public Task HandleReviveAsync(ClientSession session, PacketReader packet) => world.ReviveAsync(session);
}
