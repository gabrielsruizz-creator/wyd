using Microsoft.Extensions.Logging;
using WYD.Server.Database;
using WYD.Server.Game;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>0x277: o jogador clicou no "+" de um atributo ou especial na ficha.</summary>
public sealed class PointsHandler(
    ICharacterRepository characters,
    World world,
    WYD.Server.Data.ItemCatalog items,
    WYD.Server.Data.SkillCatalog skills,
    ILogger<PointsHandler> logger)
{
    public async Task HandleAsync(ClientSession session, PacketReader packet)
    {
        short mode = packet.ReadInt16();
        short which = packet.ReadInt16();
        var c = session.Character!;
        if (c.BaseScore.Hp <= 0) return;

        if (mode == Points.ModeSkill)
        {
            await LearnSkillAsync(session, which);
            return;
        }

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

    /// <summary>
    /// Tipo 2: o jogador escolheu uma skill na lista do mestre e confirmou. "Qual" é o item da skill (5000 + n).
    /// Precisa estar perto de um mestre de skill.
    /// </summary>
    private async Task LearnSkillAsync(ClientSession session, short item)
    {
        var c = session.Character!;
        bool nearMaster = world.NearbyMobs(session, WorldHandler.ShopDistance).Any(m => m.IsSkillMaster);
        if (!nearMaster)
        {
            await session.SendMessageAsync("Fale com o mestre de skill da sua classe.");
            return;
        }

        if (Skills.Learn(c, item, items.List, skills.Data) is { } refused)
        {
            await session.SendMessageAsync(refused);
            await session.SendAsync(ServerPackets.UpdateEtc(session.Id, c));
            return;
        }

        await session.SendAsync(ServerPackets.UpdateEtc(session.Id, c));     // LearnedSkill e pontos de skill novos
        string name = items.List?[item]?.Name.Replace('_', ' ') ?? $"skill {item}";
        await session.SendMessageAsync($"Você aprendeu {name}.");
        logger.LogInformation("[{Id}] {Name} aprendeu {Skill}.", session.Id, c.Name, name);
        try { await characters.SaveProgressAsync(c); }
        catch (Exception ex) { logger.LogError(ex, "Não foi possível salvar as skills de {Name}.", c.Name); }
    }
}
