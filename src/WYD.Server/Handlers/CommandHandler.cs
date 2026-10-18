using WYD.Server.Game;
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
    Game.KingdomPoints kingdomPoints,
    Game.TeleportTable teleports,
    ILogger<CommandHandler> logger)
{
    /// <summary>
    /// Slots do inventário onde podem estar itens comuns no client 7.57: as 4 páginas de 15 (ver <see cref="Bags"/>;
    /// as duas últimas só com a Bolsa do Andarilho). Para pôr um item, use <see cref="Bags.FirstFree"/>.
    /// </summary>
    public const int BasicInventorySlots = Bags.Usable;

    /// <summary>Os comandos de GM com o que fazem, na ordem do /gm ajuda.</summary>
    public static readonly (string Usage, string Description)[] GmCommands =
    [
        ("/gm item <índice> [efeito valor]...", "cria um item no inventário (até 3 efeitos)"),
        ("/gm capa <índice>", "troca a capa equipada (0 tira a capa)"),
        ("/gm face <índice>", "troca o rosto (o modelo do personagem): TK, FM, BM, HT ou o de um monstro"),
        ("/gm nivel <número>", "muda o seu nível"),
        ("/gm evoluir <mortal|arch|celestial|sub>", "muda a sua evolução (nível 1)"),
        ("/gm pontos <quantia>", "dá pontos de atributo, especiais e de skill"),
        ("/gm gold <quantia>", "define o seu gold"),
        ("/gm curar", "enche a sua vida e mana"),
        ("/gm ir <x> <y>", "teleporta você para a posição"),
        ("/gm ir <nome>", "teleporta você até o jogador"),
        ("/gm trazer <nome>", "traz o jogador até você"),
        ("/gm pos", "mostra a sua posição"),
        ("/gm reino <azul|vermelho|neutro>", "o /kingdom desse reino passa a levar para onde você está"),
        ("/gm tp add <x> <y> [preço] [nome]", "o teleporte onde você está passa a levar para x y"),
        ("/gm tp remover", "apaga o teleporte onde você está"),
        ("/gm tp lista", "mostra os teleportes perto de você"),
        ("/gm online", "lista quem está no jogo"),
        ("/gm aviso <texto>", "mensagem para todos os jogadores"),
        ("/gm kick <nome>", "desconecta o jogador"),
        ("/gm limpar", "esvazia o seu inventário (os 64 slots) e redesenha todos os slots no client"),
        ("/gm soul", "dá a Soul (Limite da Alma) ao seu personagem"),
        ("/gm soul <1-10>", "escolhe a combinação da Soul (1 FOR/CON, 2 INT/CON, 3 DES/CON, 4 FOR/DES, 5 INT/DES, 6 CON/DES, 7 FOR, 8 INT, 9 CON, 10 DES)"),
        ("/gm inventario", "lista os itens que o servidor tem no seu inventário, com o slot (linha e coluna)"),
        ("/gm npc", "lista os NPCs e monstros perto de você (id, nome, posição)"),
        ("/gm npc remover <id|nome>", "tira o NPC do mapa até reiniciar o servidor (para achar NPC que derruba o client)"),
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
            case "add":
                await AddPointsAsync(session, request.Text);
                break;
            case "kingdom" or "king" or "reino":
                await kings.GoToKingdomAsync(session);
                break;
            case "ajuda" or "help":
                var lines = new List<string>
                {
                    "/nome texto - sussurra para o jogador 'nome'",
                    "/add for 100 - distribui pontos (for, int, des, con, esp1 a esp4); vários: /add for 100 con 50",
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
            case "capa": await SetCapeAsync(session, args); break;
            case "face" or "rosto": await SetFaceAsync(session, args); break;
            case "nivel" or "nível" or "level": await SetLevelAsync(session, args); break;
            case "gold": await SetGoldAsync(session, args); break;
            case "evoluir": await SetEvolutionAsync(session, args); break;
            case "pontos": await GivePointsAsync(session, args); break;
            case "curar": await HealAsync(session); break;
            case "ir": await GoAsync(session, args); break;
            case "trazer": await BringAsync(session, args); break;
            case "online": await OnlineAsync(session); break;
            case "pos": await session.SendMessageAsync($"Posição: {session.Character!.PosX} {session.Character.PosY}"); break;
            case "reino": await SetKingdomPointAsync(session, args); break;
            case "tp" or "teleporte": await TeleportCommandAsync(session, args); break;
            case "aviso": await NoticeAsync(session, args); break;
            case "kick": await KickAsync(session, args); break;
            case "limpar": await ClearInventoryAsync(session); break;
            case "inventario" or "inv": await ListInventoryAsync(session); break;
            case "soul": await SoulAsync(session, args); break;
            case "npc": await NpcCommandAsync(session, args); break;
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

    /// <summary>/gm reino azul: marca a posição atual como destino do /kingdom do reino azul.</summary>
    private async Task SetKingdomPointAsync(ClientSession session, string args)
    {
        if (Game.KingdomPoints.Parse(args) is not { } kingdom)
        {
            await session.SendMessageAsync("Uso: /gm reino <azul|vermelho|neutro>");
            return;
        }
        var c = session.Character!;
        kingdomPoints.Set(kingdom, c.PosX, c.PosY);
        await session.SendMessageAsync($"/kingdom do reino {Game.Kingdoms.Name(kingdom)} agora leva para {c.PosX} {c.PosY}.");
    }

    /// <summary>/gm tp add 2480 1700 500 Azran | /gm tp remover | /gm tp lista</summary>
    private async Task TeleportCommandAsync(ClientSession session, string args)
    {
        var c = session.Character!;
        var parts = args.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
        string sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        switch (sub)
        {
            case "add" or "criar"
                when parts.Length >= 3 && short.TryParse(parts[1], out var x) && short.TryParse(parts[2], out var y):
            {
                int price = parts.Length > 3 && int.TryParse(parts[3], out var p) ? Math.Max(0, p) : 0;
                string name = parts.Length > 4 ? parts[4] : "";
                teleports.Add(new Game.TeleportPoint(c.PosX, c.PosY, x, y, price, name));
                await session.SendMessageAsync($"Teleporte em {c.PosX} {c.PosY} -> {x} {y}" + (price > 0 ? $" por {price:N0} de gold." : "."));
                break;
            }
            case "remover" or "apagar":
                var removed = teleports.Remove(c.PosX, c.PosY);
                await session.SendMessageAsync(removed is null
                    ? "Nenhum teleporte aqui."
                    : $"Teleporte em {removed.X} {removed.Y} -> {removed.DestX} {removed.DestY} apagado.");
                break;
            case "lista":
                var near = teleports.Near(c.PosX, c.PosY, Game.World.ViewRange * 4);
                await SystemLinesAsync(session, near
                    .Select(t => $"{t.X} {t.Y} -> {t.DestX} {t.DestY} ({t.Price:N0} gold) {t.Name}")
                    .Prepend($"{teleports.Count} teleporte(s) no total, {near.Count} perto de você:"));
                break;
            default:
                await session.SendMessageAsync("Uso: /gm tp add <x> <y> [preço] [nome] | /gm tp remover | /gm tp lista");
                break;
        }
    }

    /// <summary>/gm npc | /gm npc remover 1035 | /gm npc remover Rei_Harabard</summary>
    private async Task NpcCommandAsync(ClientSession session, string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            var near = world.NearbyMobs(session, Game.World.ViewRange).OrderBy(m => m.Id).ToList();
            await SystemLinesAsync(session, near
                .Select(m => $"{m.Id} {m.Template.Name} {m.X} {m.Y}")
                .Prepend($"{near.Count} NPC(s)/monstro(s) perto de você:"));
            return;
        }
        if (parts[0].ToLowerInvariant() is not ("remover" or "apagar") || parts.Length < 2)
        {
            await session.SendMessageAsync("Uso: /gm npc | /gm npc remover <id|nome>");
            return;
        }

        string target = parts[1].Trim();
        var removed = ushort.TryParse(target, out ushort id)
            ? await world.RemoveMobsAsync(m => m.Id == id)
            : await world.RemoveMobsAsync(m => SameNpcName(m.Template.Name, target));
        await session.SendMessageAsync(removed.Count == 0
            ? $"Nenhum NPC '{target}' no mapa."
            : $"{removed.Count} removido(s) até reiniciar o servidor: " +
              string.Join(", ", removed.Take(8).Select(m => $"{m.Id} {m.Template.Name} ({m.X} {m.Y})")));
    }

    /// <summary>O nome do NPC no client tem só 12 letras ("Rei_Harabar"): aceita o nome inteiro do arquivo também.</summary>
    private static bool SameNpcName(string npc, string typed) =>
        npc.Equals(typed, StringComparison.OrdinalIgnoreCase)
        || (npc.Length >= 8 && typed.StartsWith(npc, StringComparison.OrdinalIgnoreCase));

    /// <summary>/gm curar</summary>
    private async Task HealAsync(ClientSession session)
    {
        var c = session.Character!;
        c.BaseScore.Hp = c.MaxHp;
        c.BaseScore.Mp = c.MaxMp;
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

    /// <summary>
    /// /gm limpar: apaga todos os itens do inventário (os 64 do servidor) e manda todos os slots vazios ao client,
    /// também os que o servidor já achava vazios, para sumir qualquer diferença entre o servidor e a tela.
    /// </summary>
    private async Task ClearInventoryAsync(ClientSession session)
    {
        var c = session.Character!;
        int removed = c.Inventory.Count(i => !i.IsEmpty);
        for (int slot = 0; slot < c.Inventory.Length; slot++)
        {
            c.Inventory[slot] = default;
            await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, slot, default));
        }
        await characters.SaveInventoryAsync(c.Id, c.Inventory);
        await session.SendMessageAsync($"Inventário limpo ({removed} itens).");
    }

    /// <summary>/gm soul: dá a Soul (bit 30 das skills); /gm soul n: escolhe a combinação.</summary>
    private async Task SoulAsync(ClientSession session, string args)
    {
        var c = session.Character!;
        bool celestial = c.Evolution >= Game.Evolution.Celestial;
        if (args.Trim().Length == 0)
            Game.Skills.Teach(c, Game.Skills.SoulOfLimits);
        else if (int.TryParse(args.Trim(), out int combo) && combo is >= 1 and <= Game.Soul.Combinations)
            c.SoulCombo = (byte)combo;
        else
        {
            await session.SendMessageAsync("Uso: /gm soul  ou  /gm soul <1-10>");
            return;
        }
        await session.SendAsync(ServerPackets.UpdateEtc(session.Id, c));
        await session.SendAsync(ServerPackets.UpdateScore(session.Id, c));
        await characters.SaveProgressAsync(c);
        await session.SendMessageAsync($"Soul: {Game.Soul.Describe(c.SoulCombo, celestial)}");
    }

    /// <summary>/gm inventario: o que o servidor tem em cada slot do inventário (para achar item que não aparece).</summary>
    private Task ListInventoryAsync(ClientSession session)
    {
        var c = session.Character!;
        var lines = new List<string>();
        for (int slot = 0; slot < c.Inventory.Length; slot++)
        {
            var item = c.Inventory[slot];
            if (item.IsEmpty) continue;
            string name = catalog.List?[item.Index]?.Name ?? "?";
            lines.Add($"slot {slot} (linha {slot / 9 + 1}, coluna {slot % 9 + 1}): {item.Index} {name}");
        }
        if (lines.Count == 0) lines.Add("Inventário vazio no servidor.");
        return SystemLinesAsync(session, lines);
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

        int slot = Bags.FirstFree(character);
        if (slot < 0)
        {
            await session.SendMessageAsync("Inventário cheio.");
            return;
        }

        byte B(int i) => (byte)Math.Clamp(i < numbers.Count ? numbers[i]!.Value : 0, 0, 255);
        var item = numbers.Count == 1 && Game.Stats.IsMount((short)index)
            ? Game.Stats.NewMount((short)index)               // montaria sem vida não anda nem dá velocidade
            : new Item((short)index)
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

    /// <summary>/gm face 11: troca o rosto (Equip[0], o modelo do personagem), de classe ou de monstro.</summary>
    private async Task SetFaceAsync(ClientSession session, string args)
    {
        var c = session.Character!;
        if (!int.TryParse(args.Trim(), out int index) || index is <= 0 or >= ItemList.MaxItems)
        {
            await session.SendMessageAsync("Uso: /gm face <índice> (1 TK, 11 FM, 21 BM, 31 HT, ou o modelo de um monstro)");
            return;
        }
        var definition = catalog.List?[index];
        if (catalog.List is not null && (definition is null || (definition.Pos & 1) == 0))
        {
            await session.SendMessageAsync(definition is null ? $"O item {index} não existe no ItemList.bin." : $"{definition.Name.Replace('_', ' ')} ({index}) não é um rosto.");
            return;
        }

        c.Equip[0] = new Item((short)index) { Effect1 = c.Equip[0].Effect1, Value1 = c.Equip[0].Value1 };
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Equip, 0, c.Equip[0]));
        var look = ServerPackets.UpdateEquip(session.Id, c.Equip);
        foreach (var viewer in world.Viewers(session))
            await viewer.SendAsync(look);
        await characters.SaveItemsAsync(c);
        string name = definition?.Name.Replace('_', ' ') ?? $"rosto {index}";
        logger.LogInformation("[{Id}] GM '{Account}' trocou o rosto de {Name}: {Face} ({Index}).", session.Id, session.Account!.Username, c.Name, name, index);
        await session.SendMessageAsync($"Rosto: {name}. Se não aparecer, troque de canal ou relogue.");
    }

    /// <summary>
    /// /gm capa 3197: põe a capa direto no slot dela (as capas não se equipam arrastando). A capa diz o reino do
    /// personagem; 0 tira a capa.
    /// </summary>
    private async Task SetCapeAsync(ClientSession session, string args)
    {
        var c = session.Character!;
        if (!int.TryParse(args.Trim(), out int index) || index is < 0 or >= ItemList.MaxItems)
        {
            await session.SendMessageAsync("Uso: /gm capa <índice da capa> (0 tira a capa)");
            return;
        }
        var definition = index == 0 ? null : catalog.List?[index];
        if (index > 0 && catalog.List is not null)
        {
            if (definition is null)
            {
                await session.SendMessageAsync($"O item {index} não existe no ItemList.bin.");
                return;
            }
            if ((definition.Pos & (1 << Game.Kingdoms.CapeSlot)) == 0)
            {
                await session.SendMessageAsync($"{definition.Name.Replace('_', ' ')} ({index}) não é capa.");
                return;
            }
        }

        c.Equip[Game.Kingdoms.CapeSlot] = index == 0 ? default : new Item((short)index);
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Equip, Game.Kingdoms.CapeSlot, c.Equip[Game.Kingdoms.CapeSlot]));
        await session.SendAsync(ServerPackets.UpdateScore(session.Id, c));
        var look = ServerPackets.UpdateEquip(session.Id, c.Equip);
        foreach (var viewer in world.Viewers(session))
            await viewer.SendAsync(look);
        await characters.SaveItemsAsync(c);

        string name = index == 0 ? "sem capa" : definition?.Name.Replace('_', ' ') ?? $"capa {index}";
        logger.LogInformation("[{Id}] GM '{Account}' trocou a capa de {Name}: {Cape} ({Index}).",
            session.Id, session.Account!.Username, c.Name, name, index);
        await session.SendMessageAsync(index == 0 ? "Capa tirada." : $"Capa: {name}.");
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

        bool up = shown - 1 > character.BaseScore.Level;
        Game.Leveling.SetLevel(character, shown - 1, expTable.For(character.Evolution));
        if (up)
        {
            var levelUp = ServerPackets.LevelUp(session.Id);
            await session.SendAsync(levelUp);
            foreach (var viewer in world.Viewers(session))
                await viewer.SendAsync(levelUp);
        }
        await SendProgressAsync(session);
        await characters.SaveProgressAsync(character);

        logger.LogInformation("[{Id}] GM '{Account}' colocou {Name} no nível {Level}.", session.Id, account.Username, character.Name, shown);
        await session.SendMessageAsync(up ? $"{ServerPackets.LevelUpText} (nível {shown})" : $"Agora você está no nível {shown}.");
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

    /// <summary>Nome digitado no /add → (tipo, qual) do 0x277.</summary>
    private static (short Mode, short Which, string Label)? PointTarget(string name) => name.ToLowerInvariant() switch
    {
        "for" or "str" or "forca" or "força" => (Game.Points.ModeScore, 0, "FOR"),
        "int" => (Game.Points.ModeScore, 1, "INT"),
        "des" or "dex" or "destreza" => (Game.Points.ModeScore, 2, "DES"),
        "con" or "constituicao" or "constituição" => (Game.Points.ModeScore, 3, "CON"),
        "esp1" or "especial1" => (Game.Points.ModeSpecial, 0, "Especial 1"),
        "esp2" or "especial2" => (Game.Points.ModeSpecial, 1, "Especial 2"),
        "esp3" or "especial3" => (Game.Points.ModeSpecial, 2, "Especial 3"),
        "esp4" or "especial4" => (Game.Points.ModeSpecial, 3, "Especial 4"),
        _ => null,
    };

    /// <summary>/add for 100 [con 50 ...]: o mesmo que clicar 100 vezes no "+" da ficha.</summary>
    private async Task AddPointsAsync(ClientSession session, string text)
    {
        var c = session.Character!;
        var parts = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length % 2 != 0)
        {
            await session.SendMessageAsync("Uso: /add for 100 (for, int, des, con, esp1 a esp4). Vários: /add for 100 con 50");
            return;
        }
        if (c.BaseScore.Hp <= 0) return;

        var done = new List<string>();
        string? stop = null;
        for (int i = 0; i < parts.Length && stop is null; i += 2)
        {
            if (PointTarget(parts[i]) is not { } target || !int.TryParse(parts[i + 1], out int amount) || amount <= 0)
            {
                stop = $"Não entendi \"{parts[i]} {parts[i + 1]}\". Use: for, int, des, con, esp1 a esp4 e a quantidade.";
                break;
            }
            int applied = 0;
            while (applied < amount && Game.Points.Apply(c, target.Mode, target.Which) is not { } refused) applied++;
            if (applied < amount)
                stop = Game.Points.Apply(c, target.Mode, target.Which);   // o motivo (sem pontos, no máximo...)
            if (applied > 0) done.Add($"+{applied} {target.Label}");
        }

        if (done.Count > 0)
        {
            await session.SendAsync(ServerPackets.UpdateScore(session.Id, c));
            await session.SendAsync(ServerPackets.UpdateEtc(session.Id, c));
            await characters.SaveProgressAsync(c);
        }
        await session.SendMessageAsync(string.Join(", ", done)
            + (stop is null ? "" : (done.Count > 0 ? ". " : "") + stop)
            + $" Restam {c.ScoreBonus} de atributo e {c.SpecialBonus} especiais.");
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
