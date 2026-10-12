using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;
using WYD.Server.Database;
using WYD.Server.Network;
using WYD.Server.Protocol;
using WYD.Server.Security;

namespace WYD.Server.Handlers;

public sealed partial class LoginHandler(
    IOptions<ServerOptions> options,
    IAccountRepository accounts,
    ICharacterRepository characters,
    SessionManager sessions,
    ILogger<LoginHandler> logger)
{
    private readonly ServerOptions _options = options.Value;

    [GeneratedRegex("^[A-Za-z0-9]{4,15}$")]
    public static partial Regex UsernamePattern();

    /// <summary>0x20D: login da conta. Responde com a lista de personagens (0x10A).</summary>
    public async Task HandleAccountLoginAsync(ClientSession session, PacketReader packet)
    {
        var request = AccountLoginRequest.Read(packet);

        if (!_options.ClientVersions.Contains(request.ClientVersion))
        {
            logger.LogInformation("[{Id}] Versão de client recusada: {Version}.", session.Id, request.ClientVersion);
            await session.KickAsync($"Versão do client inválida ({request.ClientVersion}). Atualize o seu jogo.");
            return;
        }

        if (!UsernamePattern().IsMatch(request.Username) || request.Password.Length < 4)
        {
            await session.SendMessageAsync("Conta ou senha inválida.");
            return;
        }

        var account = await accounts.FindByUsernameAsync(request.Username);
        if (account is null)
        {
            if (!_options.AutoRegister)
            {
                await session.SendMessageAsync("Esta conta não existe.");
                return;
            }
            account = await accounts.CreateAsync(request.Username, PasswordHasher.Hash(request.Password));
            logger.LogInformation("[{Id}] Conta '{Account}' criada automaticamente.", session.Id, account.Username);
        }
        else if (!PasswordHasher.Verify(request.Password, account.PasswordHash))
        {
            logger.LogInformation("[{Id}] Senha incorreta para '{Account}'.", session.Id, account.Username);
            await session.SendMessageAsync("Senha incorreta.");
            return;
        }

        if (account.Banned)
        {
            await session.KickAsync("Esta conta está bloqueada.");
            return;
        }

        session.Account = account;
        if (sessions.BindAccount(account.Username, session) is { } previous)
        {
            logger.LogInformation("[{Id}] Conta '{Account}' já estava conectada na sessão {Previous}; desconectando a anterior.",
                session.Id, account.Username, previous.Id);
            await previous.KickAsync("Sua conta foi conectada em outro lugar.");
        }

        session.Characters = await characters.ListByAccountAsync(account.Id);
        session.State = SessionState.CharacterSelect;
        await accounts.UpdateLastLoginAsync(account.Id, session.RemoteEndPoint?.Address.ToString());

        logger.LogInformation("[{Id}] Login de '{Account}' ({Count} personagens).", session.Id, account.Username, session.Characters.Count);
        await session.SendAsync(ServerPackets.AccountLoginOk(session.Id, account, session.Characters));
    }
}
