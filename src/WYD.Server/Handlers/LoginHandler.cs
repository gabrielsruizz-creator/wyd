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

    [GeneratedRegex("^[A-Za-z0-9]{4,14}$")]
    public static partial Regex SecondPasswordPattern();

    /// <summary>Tentativas erradas de segunda senha antes de desconectar.</summary>
    public const int MaxSecondPasswordFailures = 5;

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
            await FailAsync(session, "Conta ou senha inválida.");
            return;
        }

        var account = await accounts.FindByUsernameAsync(request.Username);
        if (account is null)
        {
            if (!_options.AutoRegister)
            {
                await FailAsync(session, "Esta conta não existe.");
                return;
            }
            account = await accounts.CreateAsync(request.Username, PasswordHasher.Hash(request.Password));
            logger.LogInformation("[{Id}] Conta '{Account}' criada automaticamente.", session.Id, account.Username);
        }
        else if (!PasswordHasher.Verify(request.Password, account.PasswordHash))
        {
            logger.LogInformation("[{Id}] Senha incorreta para '{Account}'.", session.Id, account.Username);
            await FailAsync(session, "Senha incorreta.");
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

    /// <summary>
    /// Login recusado: o 0x11C faz a tela de login do client mostrar o erro e destravar (só com a mensagem ela
    /// ficava presa em "conectando"); a mensagem do servidor vem logo depois, com o motivo.
    /// </summary>
    private static async Task FailAsync(ClientSession session, string message)
    {
        await session.SendAsync(ServerPackets.HeaderOnly(Opcodes.AccountLoginFail, session.Id));
        await session.SendMessageAsync(message);
    }

    /// <summary>
    /// 0xFDE: segunda senha. Ao enviar, o client já desbloqueia a tela por conta própria; responder 0xFDF
    /// faz ele voltar a bloquear e mostrar "senha incorreta". Na primeira vez, a senha digitada vira a da conta.
    /// </summary>
    public async Task HandleSecondPasswordAsync(ClientSession session, PacketReader packet)
    {
        var request = SecondPasswordRequest.Read(packet);
        var account = session.Account!;

        bool ok;
        if (!SecondPasswordPattern().IsMatch(request.Password))
        {
            ok = false;
        }
        else if (request.Change)
        {
            // Troca: o client só manda depois de a senha atual ser aceita nesta conexão.
            ok = session.SecondPasswordVerified;
            if (ok)
            {
                account.SecondPasswordHash = PasswordHasher.Hash(request.Password);
                await accounts.SetSecondPasswordAsync(account.Id, account.SecondPasswordHash);
                logger.LogInformation("[{Id}] '{Account}' trocou a segunda senha.", session.Id, account.Username);
            }
        }
        else if (account.SecondPasswordHash is null)
        {
            account.SecondPasswordHash = PasswordHasher.Hash(request.Password);
            await accounts.SetSecondPasswordAsync(account.Id, account.SecondPasswordHash);
            logger.LogInformation("[{Id}] '{Account}' definiu a segunda senha.", session.Id, account.Username);
            ok = true;
        }
        else
        {
            ok = PasswordHasher.Verify(request.Password, account.SecondPasswordHash);
        }

        if (ok)
        {
            session.SecondPasswordVerified = true;
            session.SecondPasswordFailures = 0;
            await session.SendAsync(ServerPackets.SecondPasswordOk(session.Id));
            return;
        }

        session.SecondPasswordVerified = false;
        session.SecondPasswordFailures++;
        logger.LogInformation("[{Id}] Segunda senha incorreta para '{Account}' ({Count}/{Max}).",
            session.Id, account.Username, session.SecondPasswordFailures, MaxSecondPasswordFailures);

        if (session.SecondPasswordFailures >= MaxSecondPasswordFailures)
        {
            await session.KickAsync("Muitas tentativas de senha incorretas.");
            return;
        }
        await session.SendAsync(ServerPackets.Signal(Opcodes.SecondPasswordFail, id: session.Id));
    }
}
