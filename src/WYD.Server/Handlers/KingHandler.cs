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
public sealed class KingHandler(World world, KingdomPoints points, ICharacterRepository characters, ILogger<KingHandler> logger)
{
    /// <summary>
    /// 0x28B: o jogador falou com um NPC que conversa (id do NPC em 0x0C). O client manda com Confirma (0x10) = 0
    /// ao clicar e abrir a pergunta, e com 1 quando o jogador aceita.
    /// </summary>
    public async Task HandleNpcClickAsync(ClientSession session, PacketReader packet)
    {
        ushort npcId = (ushort)packet.ReadInt32();
        bool confirmed = packet.Remaining >= 4 && packet.ReadInt32() != 0;
        var npc = world.VisibleMob(session, npcId);
        var c = session.Character!;
        if (npc is null || Math.Abs(npc.X - c.PosX) > WorldHandler.ShopDistance || Math.Abs(npc.Y - c.PosY) > WorldHandler.ShopDistance)
            return;

        if (npc.IsSkillMaster)
        {
            // Com a Eternal Stone (1742) no slot 10 o client pergunta "deseja continuar?" e manda o 0x28B em vez
            // de pedir o menu: é a criação do Sefirot da classe do mestre.
            await SefirotAsync(session, npc, confirmed);
            return;
        }

        if (!npc.IsKing)
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

    private async Task SefirotAsync(ClientSession session, Mob master, bool confirmed)
    {
        var c = session.Character!;
        if (!Sefirot.HasEternalStone(c) || Sefirot.MasterClass(master.Template.Carry) is not { } cls)
        {
            await session.SendAsync(ServerPackets.ShopList(master.Id, master.Template.Carry, type: ServerPackets.ShopTypeSkillMaster));
            return;
        }

        string name = $"Sefirot({Sefirot.ClassName(cls)})";
        if (Sefirot.Missing(c, CommandHandler.BasicInventorySlots) is { } missing)
        {
            await session.SendMessageAsync($"Para o {name} falta: {missing}.");
            return;
        }
        if (!confirmed)
        {
            await session.SendMessageAsync($"Confirme para trocar a Eternal Stone, as 8 pedras e {Sefirot.Price:N0} de gold pelo {name}.");
            return;
        }

        var changed = Sefirot.Create(c, cls, CommandHandler.BasicInventorySlots)!;
        foreach (int slot in changed)
            await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, slot, c.Inventory[slot]));
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Equip, Sefirot.EternalStoneSlot, default));
        await session.SendAsync(ServerPackets.UpdateCoin(session.Id, c.Coin));
        foreach (var viewer in world.Viewers(session))
            await viewer.SendAsync(ServerPackets.UpdateEquip(session.Id, c.Equip));
        await characters.SaveItemsAsync(c);
        await characters.SaveProgressAsync(c);

        logger.LogInformation("[{Id}] {Name} criou o {Item} com {Npc}.", session.Id, c.Name, name, master.Template.Name);
        await session.SendMessageAsync($"Você recebeu o {name}.");
    }

    /// <summary>/kingdom ou /king: vai para o castelo do seu reino (neutros vão para o centro do reino).</summary>
    public async Task GoToKingdomAsync(ClientSession session)
    {
        var c = session.Character!;
        if (c.BaseScore.Hp <= 0) return;
        var kingdom = Kingdoms.Of(c);
        var (x, y) = points.Of(kingdom);
        await world.TeleportAsync(session, x, y);
        await session.SendMessageAsync(Kingdoms.IsNeutral(kingdom)
            ? "Reino: neutro. Você pode evoluir com o Rei Harabard (azul) ou com o Rei Glantuar (vermelho)."
            : $"Reino {Kingdoms.Name(kingdom)}: evolua com o {Kingdoms.KingName(kingdom)}.");
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
