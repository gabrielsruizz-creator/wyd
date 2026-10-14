using Microsoft.Extensions.Logging;
using WYD.Server.Data;
using WYD.Server.Database;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>
/// 0x334: "/comando texto" digitado no chat. Os comandos de GM (nível de acesso 1+) começam com /gm
/// ("/gm item 905": o client manda "gm" como comando e "item 905" como texto). /ajuda lista os comandos;
/// qualquer outro "/nome texto" é um sussurro para o jogador com esse nome.
/// </summary>
public sealed class CommandHandler(
    ItemCatalog catalog,
    ExpCatalog expTable,
    ICharacterRepository characters,
    SessionManager sessions,
    Game.World world,
    KingHandler kings,
    ILogger<CommandHandler> logger)
{
    /// <summary>Inventário básico (sem bolsas): os slots que o client mostra.</summary>
    public const int BasicInventorySlots = 30;

    /// <summary>Os comandos de GM com o que fazem, na ordem do /gm ajuda.</summary>
    public static readonly (string Usage, string Description)[] GmCommands =
    [
        ("/gm item <índice> [efeito valor]...", "cria um item no inventário (até 3 efeitos)"),
        ("/gm nivel <número>", "muda o seu nível"),
        ("/gm evoluir <mortal|arch|celestial|sub>", "muda a sua evolução (nível 1)"),
        ("/gm pontos <quantia>", "dá pontos de atributo, especiais e de skill"),
        ("/gm gold <quantia>", "define o seu gold"),
        ("/gm curar", "enche a sua vida e mana"),
        ("/gm ir <x> <y>", "teleporta você para a posição"),
        ("/gm ir <nome>", "teleporta você até o jogador"),
        ("/gm trazer <nome>", "traz o jogador até você"),
        ("/gm online", "lista quem está no jogo"),
        ("/gm aviso <texto>", "mensagem para todos os jogadores"),
        ("/gm kick <nome>", "desconecta o jogador"),
        ("/gm limpar", "esvazia o seu inventário"),
        ("/gm ajuda", "mostra esta lista"),
    ];

    public async Task HandleAsync(ClientSession session, PacketReader packet)
    {
        var request = CommandRequest.Read(packet);
        switch (request.Command.ToLowerInvariant())
        {
            case "gm":
                await GmAsync(session, request.Text.Trim());
                break;
            case "kingdom" or "king" or "reino":
                await kings.GoToKingdomAsync(session);
                break;
            case "ajuda" or "help":
                var lines = new List<string>
                {
                    "/nome texto - sussurra para o jogador 'nome'",
                    "/kingdom ou /king - vai para o castelo do seu reino",
                    "Evolução: no nível máximo, fale com o rei do seu reino (Mortal 400 > Arch 400 > Celestial 200 > Sub Celestial 200)",
                };
                if (session.Account!.AccessLevel > 0)
                    lines.Add("/gm ajuda - lista os comandos de GM");
                await SystemLinesAsync(session, lines);
                break;
            default:
                await WhisperAsync(session, request.Command, request.Text);
                break;
        }
    }

    /// <summary>Cada linha no chat como um sussurro do "Sistema" (a caixa de mensagem some rápido).</summary>
    private static async Task SystemLinesAsync(ClientSession session, IEnumerable<string> lines)
    {
        foreach (var line in lines)
            await session.SendAsync(ServerPackets.Whisper(session.Id, "Sistema", line));
    }

    private async Task GmAsync(ClientSession session, string text)
    {
        if (session.Account!.AccessLevel < 1)
        {
            await session.SendMessageAsync("Comando só para GMs.");
            return;
        }

        int space = text.IndexOf(' ');
        string sub = (space < 0 ? text : text[..space]).ToLowerInvariant();
        string args = space < 0 ? "" : text[(space + 1)..].Trim();

        switch (sub)
        {
            case "item": await GiveItemAsync(session, args); break;
            case "nivel" or "nível" or "level": await SetLevelAsync(session, args); break;
            case "gold": await SetGoldAsync(session, args); break;
            case "evoluir": await SetEvolutionAsync(session, args); break;
            case "pontos": await GivePointsAsync(session, args); break;
            case "curar": await HealAsync(session); break;
            case "ir": await GoAsync(session, args); break;
            case "trazer": await BringAsync(session, args); break;
            case "online": await OnlineAsync(session); break;
            case "aviso": await NoticeAsync(session, args); break;
            case "kick": await KickAsync(session, args); break;
            case "limpar": await ClearInventoryAsync(session); break;
            default:
                await SystemLinesAsync(session, GmCommands.Select(c => $"{c.Usage} - {c.Description}"));
                break;
        }
        if (sub is not ("" or "ajuda" or "help"))
            logger.LogInformation("[{Id}] GM '{Account}': /gm {Text}", session.Id, session.Account.Username, text);
    }

    private ClientSession? FindPlayer(string name) =>
        sessions.All.FirstOrDefault(s =>
            s.State == SessionState.InGame && string.Equals(s.Character?.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>/gm gold 1000000</summary>
    private async Task SetGoldAsync(ClientSession session, string args)
    {
        if (!long.TryParse(args, out long amount) || amount < 0 || amount > ItemHandler.MaxCoin)
        {
            await session.SendMessageAsync($"Uso: /gm gold <0 a {ItemHandler.MaxCoin}>");
            return;
        }
        var c = session.Character!;
        c.Coin = (int)amount;
        await session.SendAsync(ServerPackets.UpdateCoin(session.Id, c.Coin));
        await characters.SaveProgressAsync(c);
        await session.SendMessageAsync($"Gold: {c.Coin:N0}");
    }

    /// <summary>/gm pontos 100: soma pontos de atributo, especiais e de skill.</summary>
    private async Task GivePointsAsync(ClientSession session, string args)
    {
        if (!short.TryParse(args, out short amount) || amount <= 0)
        {
            await session.SendMessageAsync("Uso: /gm pontos <quantia>");
            return;
        }
        var c = session.Character!;
        c.ScoreBonus = (short)Math.Min(short.MaxValue, c.ScoreBonus + amount);
        c.SpecialBonus = (short)Math.Min(short.MaxValue, c.SpecialBonus + amount);
        c.SkillBonus = (short)Math.Min(short.MaxValue, c.SkillBonus + amount);
        await session.SendAsync(ServerPackets.UpdateEtc(session.Id, c));
        await characters.SaveProgressAsync(c);
        await session.SendMessageAsync($"+{amount} pontos de atributo, especiais e de skill.");
    }

    /// <summary>/gm curar</summary>
    private async Task HealAsync(ClientSession session)
    {
        var c = session.Character!;
        c.BaseScore.Hp = c.BaseScore.MaxHp;
        c.BaseScore.Mp = c.BaseScore.MaxMp;
        await session.SendAsync(ServerPackets.UpdateHpMp(session.Id, c.BaseScore.Hp, c.BaseScore.Mp));
        await session.SendMessageAsync("Vida e mana cheias.");
    }

    /// <summary>/gm ir 2100 2100 ou /gm ir Fulano</summary>
    private async Task GoAsync(ClientSession session, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        short x, y;
        if (parts.Length == 2 && short.TryParse(parts[0], out x) && short.TryParse(parts[1], out y))
        {
            if (x is < 0 or > Game.World.MapSize - 1 || y is < 0 or > Game.World.MapSize - 1)
            {
                await session.SendMessageAsync($"Posição fora do mapa (0 a {Game.World.MapSize - 1}).");
                return;
            }
        }
        else if (parts.Length == 1 && FindPlayer(parts[0]) is { } target)
        {
            (x, y) = (target.Character!.PosX, target.Character.PosY);
        }
        else
        {
            await session.SendMessageAsync(parts.Length == 1 ? $"{parts[0]} não está online." : "Uso: /gm ir <x> <y> ou /gm ir <nome>");
            return;
        }
        await world.TeleportAsync(session, x, y);
    }

    /// <summary>/gm trazer Fulano</summary>
    private async Task BringAsync(ClientSession session, string name)
    {
        if (FindPlayer(name) is not { } target)
        {
            await session.SendMessageAsync(name.Length == 0 ? "Uso: /gm trazer <nome>" : $"{name} não está online.");
            return;
        }
        await world.TeleportAsync(target, session.Character!.PosX, session.Character.PosY);
        await target.SendMessageAsync("Você foi chamado por um GM.");
    }

    /// <summary>/gm online</summary>
    private Task OnlineAsync(ClientSession session)
    {
        var online = sessions.All.Where(s => s.State == SessionState.InGame && s.Character is not null)
            .Select(s => $"{s.Character!.Name} (nível {s.Character.BaseScore.Level + 1}) em {s.Character.PosX} {s.Character.PosY}")
            .ToList();
        return SystemLinesAsync(session, online.Prepend($"{online.Count} jogador(es) online:"));
    }

    /// <summary>/gm aviso texto: aparece na caixa de mensagem de todos.</summary>
    private async Task NoticeAsync(ClientSession session, string text)
    {
        if (text.Length == 0)
        {
            await session.SendMessageAsync("Uso: /gm aviso <texto>");
            return;
        }
        foreach (var s in sessions.All.Where(s => s.State == SessionState.InGame).ToList())
            await s.SendMessageAsync($"[GM] {text}");
    }

    /// <summary>/gm kick Fulano</summary>
    private async Task KickAsync(ClientSession session, string name)
    {
        if (FindPlayer(name) is not { } target)
        {
            await session.SendMessageAsync(name.Length == 0 ? "Uso: /gm kick <nome>" : $"{name} não está online.");
            return;
        }
        await target.SendMessageAsync("Você foi desconectado por um GM.");
        target.Close();
        await session.SendMessageAsync($"{target.Character!.Name} foi desconectado.");
    }

    /// <summary>/gm limpar: apaga os itens do inventário básico.</summary>
    private async Task ClearInventoryAsync(ClientSession session)
    {
        var c = session.Character!;
        for (int slot = 0; slot < BasicInventorySlots; slot++)
        {
            if (c.Inventory[slot].IsEmpty) continue;
            c.Inventory[slot] = default;
            await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, slot, default));
        }
        await characters.SaveInventoryAsync(c.Id, c.Inventory);
        await session.SendMessageAsync("Inventário limpo.");
    }

    /// <summary>/gm item 905 ou /gm item 905 43 9 (índice e até 3 pares efeito/valor).</summary>
    private async Task GiveItemAsync(ClientSession session, string args)
    {
        var account = session.Account!;
        var character = session.Character!;

        var numbers = args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(a => int.TryParse(a, out int n) ? n : (int?)null).ToList();
        if (numbers.Count == 0 || numbers.Any(n => n is null) || numbers.Count > 7)
        {
            await session.SendMessageAsync("Uso: /gm item <índice> [efeito valor] [efeito valor] [efeito valor]");
            return;
        }

        int index = numbers[0]!.Value;
        if (index is <= 0 or >= ItemList.MaxItems)
        {
            await session.SendMessageAsync($"Índice inválido: {index} (de 1 a {ItemList.MaxItems - 1}).");
            return;
        }

        var definition = catalog.List?[index];
        if (catalog.List is not null && definition is null)
        {
            await session.SendMessageAsync($"O item {index} não existe no ItemList.bin.");
            return;
        }

        int slot = Array.FindIndex(character.Inventory, 0, BasicInventorySlots, i => i.IsEmpty);
        if (slot < 0)
        {
            await session.SendMessageAsync("Inventário cheio.");
            return;
        }

        byte B(int i) => (byte)Math.Clamp(i < numbers.Count ? numbers[i]!.Value : 0, 0, 255);
        var item = new Item((short)index)
        {
            Effect1 = B(1), Value1 = B(2),
            Effect2 = B(3), Value2 = B(4),
            Effect3 = B(5), Value3 = B(6),
        };

        character.Inventory[slot] = item;
        await characters.SaveInventoryAsync(character.Id, character.Inventory);
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, slot, item));

        string name = definition?.Name.Replace('_', ' ') ?? $"item {index}";
        logger.LogInformation("[{Id}] GM '{Account}' criou {Item} ({Index}) no slot {Slot} de {Name}.",
            session.Id, account.Username, name, index, slot, character.Name);
        await session.SendMessageAsync($"{name} criado no inventário.");
    }

    /// <summary>/gm nivel 100: leva o personagem ao nível 100 (como aparece na tela).</summary>
    private async Task SetLevelAsync(ClientSession session, string args)
    {
        var account = session.Account!;
        var character = session.Character!;

        int max = Game.Evolutions.MaxShownLevel(character.Evolution);
        if (!int.TryParse(args.Trim(), out int shown) || shown < 1 || shown > max)
        {
            await session.SendMessageAsync($"Uso: /gm nivel <1 a {max}> ({Game.Evolutions.Name(character.Evolution)} vai até o {max})");
            return;
        }

        Game.Leveling.SetLevel(character, shown - 1, expTable.For(character.Evolution));
        await SendProgressAsync(session);
        await characters.SaveProgressAsync(character);

        logger.LogInformation("[{Id}] GM '{Account}' colocou {Name} no nível {Level}.", session.Id, account.Username, character.Name, shown);
        await session.SendMessageAsync($"Agora você está no nível {shown}.");
    }

    /// <summary>Ficha, experiência/pontos e vida/mana novas para o próprio jogador.</summary>
    private static async Task SendProgressAsync(ClientSession session)
    {
        var c = session.Character!;
        await session.SendAsync(ServerPackets.UpdateScore(session.Id, c));
        await session.SendAsync(ServerPackets.UpdateEtc(session.Id, c));
        await session.SendAsync(ServerPackets.UpdateHpMp(session.Id, c.BaseScore.Hp, c.BaseScore.Mp));
    }

    /// <summary>/gm evoluir arch: muda a evolução direto (nível 1).</summary>
    private async Task SetEvolutionAsync(ClientSession session, string args)
    {
        if (Game.Evolutions.Parse(args) is not { } to)
        {
            await session.SendMessageAsync("Uso: /gm evoluir <mortal|arch|celestial|sub>");
            return;
        }
        await kings.ChangeEvolutionAsync(session, to);
    }

    private async Task WhisperAsync(ClientSession session, string target, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var to = sessions.All.FirstOrDefault(s =>
            s.State == SessionState.InGame && string.Equals(s.Character?.Name, target, StringComparison.OrdinalIgnoreCase));
        if (to is null)
        {
            await session.SendMessageAsync($"{target} não está online.");
            return;
        }

        await to.SendAsync(ServerPackets.Whisper(to.Id, session.Character!.Name, text));
    }
}
