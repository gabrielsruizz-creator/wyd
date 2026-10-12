using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;
using WYD.Server.Database;
using WYD.Server.Game;
using WYD.Server.Network;
using WYD.Server.Protocol;
using WYD.Server.Security;

namespace WYD.Server.Handlers;

public sealed partial class CharacterHandler(
    IOptions<ServerOptions> options,
    ICharacterRepository characters,
    World world,
    ILogger<CharacterHandler> logger)
{
    private readonly ServerOptions _options = options.Value;

    [GeneratedRegex("^[A-Za-z0-9]{4,12}$")]
    public static partial Regex NamePattern();

    /// <summary>0x20F: criar personagem.</summary>
    public async Task HandleCreateAsync(ClientSession session, PacketReader packet)
    {
        var request = CreateCharacterRequest.Read(packet);
        var account = session.Account!;
        if (!session.SecondPasswordVerified)
        {
            await session.SendAsync(ServerPackets.HeaderOnly(Opcodes.CreateCharacterFail, session.Id));
            return;
        }

        string? error =
            request.Slot is < 0 or >= GameConstants.CharactersPerAccount ? "Slot inválido." :
            session.Characters.Any(c => c.Slot == request.Slot) ? "Já existe um personagem neste slot." :
            !NamePattern().IsMatch(request.Name) ? "O nome deve ter de 4 a 12 letras ou números." :
            !ClassTemplates.IsValid(request.ClassId) ? "Classe inválida." :
            null;

        if (error is null && await characters.NameExistsAsync(request.Name))
            error = "Este nome já está em uso.";

        Character? character = null;
        if (error is null)
        {
            character = ClassTemplates.Create(account.Id, request.Slot, request.Name, (CharacterClass)request.ClassId,
                (short)_options.StartX, (short)_options.StartY);
            if (!await characters.CreateAsync(character))
                error = "Este nome já está em uso.";
        }

        if (error is not null)
        {
            await session.SendMessageAsync(error);
            await session.SendAsync(ServerPackets.HeaderOnly(Opcodes.CreateCharacterFail, session.Id));
            return;
        }

        session.Characters = await characters.ListByAccountAsync(account.Id);
        logger.LogInformation("[{Id}] '{Account}' criou {Name} ({Class}) no slot {Slot}.",
            session.Id, account.Username, character!.Name, character.Class, character.Slot);
        await session.SendAsync(ServerPackets.SelCharList(Opcodes.CreateCharacterOk, session.Id, session.Characters));
    }

    /// <summary>0x211: apagar personagem (o client pede a senha da conta para confirmar).</summary>
    public async Task HandleDeleteAsync(ClientSession session, PacketReader packet)
    {
        var request = DeleteCharacterRequest.Read(packet);
        var account = session.Account!;
        if (!session.SecondPasswordVerified)
        {
            await session.SendAsync(ServerPackets.HeaderOnly(Opcodes.DeleteCharacterFail, session.Id));
            return;
        }
        var target = session.Characters.FirstOrDefault(c => c.Slot == request.Slot);

        string? error =
            target is null || !string.Equals(target.Name, request.Name, StringComparison.OrdinalIgnoreCase) ? "Personagem não encontrado." :
            !PasswordHasher.Verify(request.Password, account.PasswordHash) ? "Senha incorreta." :
            null;

        if (error is null && !await characters.DeleteAsync(account.Id, request.Slot))
            error = "Personagem não encontrado.";

        if (error is not null)
        {
            await session.SendMessageAsync(error);
            await session.SendAsync(ServerPackets.HeaderOnly(Opcodes.DeleteCharacterFail, session.Id));
            return;
        }

        session.Characters = await characters.ListByAccountAsync(account.Id);
        logger.LogInformation("[{Id}] '{Account}' apagou {Name} do slot {Slot}.", session.Id, account.Username, request.Name, request.Slot);
        await session.SendAsync(ServerPackets.SelCharList(Opcodes.DeleteCharacterOk, session.Id, session.Characters));
    }

    /// <summary>0x213: entrar no mundo com o personagem do slot escolhido.</summary>
    public async Task HandleEnterWorldAsync(ClientSession session, PacketReader packet)
    {
        var request = CharacterLoginRequest.Read(packet);
        var character = session.Characters.FirstOrDefault(c => c.Slot == request.Slot);
        if (character is null || !session.SecondPasswordVerified)
        {
            await session.SendAsync(ServerPackets.HeaderOnly(Opcodes.CharacterLoginFail, session.Id));
            return;
        }

        session.Character = character;
        session.State = SessionState.InGame;
        logger.LogInformation("[{Id}] '{Account}' entrou no mundo com {Name} em ({X}, {Y}).",
            session.Id, session.Account!.Username, character.Name, character.PosX, character.PosY);
        await session.SendAsync(ServerPackets.CharacterLoginOk(session.Id, character));
        await world.EnterAsync(session);
    }
}
