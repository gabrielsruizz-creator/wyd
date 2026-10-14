using Microsoft.Extensions.Logging;
using WYD.Server.Database;
using WYD.Server.Game;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>
/// Os reis do reino (Rei Harabard, azul; Rei Glantuar, vermelho): no nível máximo, quem fala com o rei do
/// próprio reino evolui. Neutros (capa branca ou sem capa) podem evoluir com qualquer um.
/// </summary>
public sealed class KingHandler(World world, ICharacterRepository characters, ILogger<KingHandler> logger)
{
    /// <summary>NPCs de reino têm Merchant 15 e o clã do reino (7 ou 8).</summary>
    public static bool IsKing(Mob npc) =>
        (npc.Template.Score.Merchant & 0x0F) == 15 && npc.Template.Clan is (byte)Kingdom.Blue or (byte)Kingdom.Red;

    /// <summary>0x28B: o jogador clicou num NPC que conversa (id do NPC em 0x0C).</summary>
    public async Task HandleNpcClickAsync(ClientSession session, PacketReader packet)
    {
        ushort npcId = (ushort)packet.ReadInt32();
        var npc = world.VisibleMob(session, npcId);
        var c = session.Character!;
        if (npc is null || Math.Abs(npc.X - c.PosX) > WorldHandler.ShopDistance || Math.Abs(npc.Y - c.PosY) > WorldHandler.ShopDistance)
            return;

        if (!IsKing(npc))
        {
            logger.LogInformation("[{Id}] Clique em {Npc}: este NPC ainda não tem função.", session.Id, npc.Template.Name);
            return;
        }

        var kingdom = (Kingdom)npc.Template.Clan;
        var mine = Kingdoms.Of(c);
        if (!Kingdoms.IsNeutral(mine) && mine != kingdom)
        {
            await session.SendMessageAsync($"Você é do reino {Kingdoms.Name(mine)}. Evolua com o {Kingdoms.KingName(mine)}.");
            return;
        }
        if (c.Evolution >= Evolution.SubCelestial)
        {
            await session.SendMessageAsync("Sub Celestial já é a última evolução.");
            return;
        }
        if (!Leveling.CanEvolve(c))
        {
            await session.SendMessageAsync(
                $"Volte quando chegar ao nível {Evolutions.MaxShownLevel(c.Evolution)} de {Evolutions.Name(c.Evolution)} para virar {Evolutions.Name(c.Evolution + 1)}.");
            return;
        }

        await ChangeEvolutionAsync(session, c.Evolution + 1);
    }

    /// <summary>/kingdom ou /king: vai para o castelo do seu reino (neutros vão para o centro do reino).</summary>
    public async Task GoToKingdomAsync(ClientSession session)
    {
        var c = session.Character!;
        if (c.BaseScore.Hp <= 0) return;
        var kingdom = Kingdoms.Of(c);
        var (x, y) = Kingdoms.Destination(kingdom);
        await world.TeleportAsync(session, x, y);
        await session.SendMessageAsync(Kingdoms.IsNeutral(kingdom)
            ? "Reino: neutro. Os reis Harabard (azul) e Glantuar (vermelho) ficam ao norte e ao sul."
            : $"Reino {Kingdoms.Name(kingdom)}: o {Kingdoms.KingName(kingdom)} está à sua frente.");
    }

    /// <summary>Muda a evolução (nível 1), atualiza a tela do jogador e salva.</summary>
    public async Task ChangeEvolutionAsync(ClientSession session, Evolution to)
    {
        var c = session.Character!;
        Leveling.Evolve(c, to);
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Equip, 0, c.Equip[0]));
        await session.SendAsync(ServerPackets.UpdateScore(session.Id, c));
        await session.SendAsync(ServerPackets.UpdateEtc(session.Id, c));
        await session.SendAsync(ServerPackets.UpdateHpMp(session.Id, c.BaseScore.Hp, c.BaseScore.Mp));
        await characters.SaveItemsAsync(c);
        await characters.SaveProgressAsync(c);

        logger.LogInformation("[{Id}] {Name} agora é {Evolution}.", session.Id, c.Name, Evolutions.Name(to));
        await session.SendMessageAsync($"Parabéns! Agora você é {Evolutions.Name(to)} (nível 1).");
    }
}
