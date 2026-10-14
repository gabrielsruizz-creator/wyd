using Microsoft.Extensions.Logging;
using WYD.Server.Data;
using WYD.Server.Database;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>
/// 0x334: "/comando texto" digitado no chat. Comandos de GM (nível de acesso 1+): /item. Qualquer outro
/// "/nome texto" é um sussurro para o jogador com esse nome.
/// </summary>
public sealed class CommandHandler(
    ItemCatalog catalog,
    ICharacterRepository characters,
    SessionManager sessions,
    ILogger<CommandHandler> logger)
{
    /// <summary>Inventário básico (sem bolsas): os slots que o client mostra.</summary>
    public const int BasicInventorySlots = 30;

    public async Task HandleAsync(ClientSession session, PacketReader packet)
    {
        var request = CommandRequest.Read(packet);
        switch (request.Command.ToLowerInvariant())
        {
            case "item":
                await GiveItemAsync(session, request.Text);
                break;
            case "ajuda" or "help":
                // Cada comando numa linha do chat (como um sussurro do "Sistema"): a caixa de mensagem some rápido.
                var lines = new List<string> { "/nome texto - sussurra para o jogador 'nome'" };
                if (session.Account!.AccessLevel > 0)
                {
                    lines.Add("/item <índice> - cria um item no inventário (GM)");
                    lines.Add("/item <índice> <efeito> <valor> ... - com até 3 efeitos (GM)");
                }
                foreach (var line in lines)
                    await session.SendAsync(ServerPackets.Whisper(session.Id, "Sistema", line));
                break;
            default:
                await WhisperAsync(session, request.Command, request.Text);
                break;
        }
    }

    /// <summary>/item 905 ou /item 905 43 9 (índice e até 3 pares efeito/valor).</summary>
    private async Task GiveItemAsync(ClientSession session, string args)
    {
        var account = session.Account!;
        var character = session.Character!;
        if (account.AccessLevel < 1)
        {
            await session.SendMessageAsync("Comando só para GMs.");
            return;
        }

        var numbers = args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(a => int.TryParse(a, out int n) ? n : (int?)null).ToList();
        if (numbers.Count == 0 || numbers.Any(n => n is null) || numbers.Count > 7)
        {
            await session.SendMessageAsync("Uso: /item <índice> [efeito valor] [efeito valor] [efeito valor]");
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
