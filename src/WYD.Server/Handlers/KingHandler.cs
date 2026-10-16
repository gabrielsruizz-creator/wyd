using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;
using WYD.Server.Database;
using WYD.Server.Game;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>
/// Os reis do reino (Rei Harabard, azul; Rei Glantuar, vermelho): no nível máximo, quem fala com o rei do
/// próprio reino evolui. Neutros (capa branca ou sem capa) podem evoluir com qualquer um.
/// </summary>
public sealed class KingHandler(
    World world, KingdomPoints points, ICharacterRepository characters, CharacterHandler characterHandler,
    IOptions<ServerOptions> options, ILogger<KingHandler> logger)
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

        if (npc.OracleKingdom is { } oracleKingdom)
        {
            await EternalStoneAsync(session, npc, oracleKingdom);
            return;
        }

        if (SoulQuest.IsKibita(npc))
        {
            await KibitaAsync(session);
            return;
        }
        if (SoulQuest.IsEhre(npc.Template.Name))
        {
            await EhreAsync(session);
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

        if (c.Evolution == Evolution.Mortal)
            await CreateArchAsync(session);
        else
            await ChangeEvolutionAsync(session, c.Evolution + 1);
    }

    /// <summary>0x28E: clique em NPC de tipo 6 a 8. O Odin faz a Pedra Secreta.</summary>
    public async Task HandleNpcTalkAsync(ClientSession session, PacketReader packet)
    {
        ushort npcId = (ushort)packet.ReadInt32();
        var npc = world.VisibleMob(session, npcId);
        var c = session.Character!;
        if (npc is null || Math.Abs(npc.X - c.PosX) > WorldHandler.ShopDistance || Math.Abs(npc.Y - c.PosY) > WorldHandler.ShopDistance)
            return;
        if (!SoulQuest.IsOdin(npc))
        {
            logger.LogInformation("[{Id}] Conversa com {Npc}: este NPC ainda não tem função.", session.Id, npc.Template.Name);
            return;
        }

        var (refusal, changed) = SoulQuest.MakeStone(c, CommandHandler.BasicInventorySlots);
        if (refusal is not null)
        {
            await session.SendMessageAsync(refusal);
            return;
        }
        foreach (int slot in changed)
            await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, slot, c.Inventory[slot]));
        await session.SendAsync(ServerPackets.UpdateCoin(session.Id, c.Coin));
        await characters.SaveItemsAsync(c);
        await characters.SaveProgressAsync(c);
        short stone = SoulQuest.SecretStone(c.Class);
        logger.LogInformation("[{Id}] {Name} fez a {Stone} com o Odin.", session.Id, c.Name, SoulQuest.StoneName(stone));
        await session.SendMessageAsync($"{SoulQuest.StoneName(stone)} criada.");
    }

    /// <summary>Kibita: o Mortal 371+ ou o Arch com a Pedra Secreta ganha a Soul e o Manto do Herói e volta para a seleção.</summary>
    private async Task KibitaAsync(ClientSession session)
    {
        var c = session.Character!;
        var (refusal, changed) = SoulQuest.GiveSoul(c, CommandHandler.BasicInventorySlots);
        if (refusal is not null)
        {
            await session.SendMessageAsync(refusal);
            return;
        }
        foreach (int slot in changed)
            await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, slot, c.Inventory[slot]));
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Equip, Kingdoms.CapeSlot, c.Equip[Kingdoms.CapeSlot]));
        await characters.SaveItemsAsync(c);
        await characters.SaveProgressAsync(c);
        logger.LogInformation("[{Id}] {Name} recebeu a Soul da Kibita.", session.Id, c.Name);
        await characterHandler.ReturnToSelectionAsync(session, listChanged: true);
    }

    /// <summary>Ehre: as 3 joias do inventário escolhem a combinação da Soul.</summary>
    private async Task EhreAsync(ClientSession session)
    {
        var c = session.Character!;
        var (refusal, combo, changed) = SoulQuest.Configure(c, CommandHandler.BasicInventorySlots);
        if (refusal is not null)
        {
            await session.SendMessageAsync(refusal);
            return;
        }
        foreach (int slot in changed)
            await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, slot, c.Inventory[slot]));
        await session.SendAsync(ServerPackets.UpdateScore(session.Id, c));
        await characters.SaveItemsAsync(c);
        await characters.SaveProgressAsync(c);
        bool celestial = c.Evolution >= Evolution.Celestial;
        logger.LogInformation("[{Id}] {Name} configurou a Soul na Ehre: {Combo}.", session.Id, c.Name, combo);
        await session.SendMessageAsync($"Soul: {Soul.Describe(combo, celestial)}");
    }

    /// <summary>Mortal 400 com o Sefirot: nasce o Arch num slot livre e o client volta para a seleção de personagens.</summary>
    private async Task CreateArchAsync(ClientSession session)
    {
        var c = session.Character!;
        if (Arch.FindSefirot(c, CommandHandler.BasicInventorySlots) is not { } sefirot)
        {
            await session.SendMessageAsync("Equipe a Eternal Stone e o Sefirot da classe que o Arch vai ter (as skills): TK, FM, BM ou HT.");
            return;
        }
        var slots = (await characters.ListByAccountAsync(c.AccountId)).Select(x => x.Slot).ToHashSet();
        int free = Enumerable.Range(0, GameConstants.CharactersPerAccount).FirstOrDefault(s => !slots.Contains(s), -1);
        if (free < 0)
        {
            await session.SendMessageAsync("Para criar o Arch é preciso um slot livre na conta.");
            return;
        }

        var place = sefirot.Equipped ? c.Equip : c.Inventory;
        var skills = Arch.SefirotClass(place[sefirot.Slot]);
        var arch = Arch.Create(c, skills, free, (short)options.Value.StartX, (short)options.Value.StartY);
        if (!await characters.CreateAsync(arch))
        {
            await session.SendMessageAsync("Não foi possível criar o Arch agora. Tente de novo.");
            return;
        }

        place[sefirot.Slot] = default;
        await session.SendAsync(ServerPackets.UpdateItem(session.Id,
            sefirot.Equipped ? ServerPackets.ItemPlace.Equip : ServerPackets.ItemPlace.Inventory, sefirot.Slot, default));
        await characters.SaveItemsAsync(c);
        logger.LogInformation("[{Id}] {Name} criou o Arch (corpo {Body}, skills {Skills}) no slot {Slot}.",
            session.Id, c.Name, Arch.BodyOf(c), skills, free);
        await session.SendMessageAsync($"Arch criado com as skills de {Sefirot.ClassName(skills)}. Escolha-o na seleção de personagens.");
        await characterHandler.ReturnToSelectionAsync(session, listChanged: true, effectSlot: free);
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

    /// <summary>Oracle do reino: Unicorn's Spirit + Phoenix's Spirit + 10 Sapphires viram a Eternal Stone.</summary>
    private async Task EternalStoneAsync(ClientSession session, Mob oracle, Kingdom kingdom)
    {
        var c = session.Character!;
        var mine = Kingdoms.Of(c);
        if (!Kingdoms.IsNeutral(mine) && mine != kingdom)
        {
            await session.SendMessageAsync($"Você é do reino {Kingdoms.Name(mine)}. Fale com o Oracle do seu reino.");
            return;
        }
        if (Oracle.Missing(c, CommandHandler.BasicInventorySlots) is { } missing)
        {
            await session.SendMessageAsync($"Para a Eternal Stone falta: {missing}.");
            return;
        }

        var changed = Oracle.Create(c, CommandHandler.BasicInventorySlots)!;
        foreach (int slot in changed)
            await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, slot, c.Inventory[slot]));
        await characters.SaveItemsAsync(c);

        logger.LogInformation("[{Id}] {Name} criou a Eternal Stone com o {Npc}.", session.Id, c.Name, oracle.Template.Name);
        await session.SendMessageAsync("Você recebeu a Eternal Stone.");
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
