using Microsoft.Extensions.Logging;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>Encaminha cada pacote recebido para o handler do seu opcode, conferindo tamanho e estado da sessão.</summary>
public sealed class PacketDispatcher
{
    private sealed record Route(int MinSize, SessionState State, Func<ClientSession, PacketReader, Task> Handle);

    private readonly Dictionary<ushort, Route> _routes = new();
    private readonly ILogger<PacketDispatcher> _logger;

    public PacketDispatcher(LoginHandler login, CharacterHandler characters, WorldHandler world, CommandHandler commands, ILogger<PacketDispatcher> logger)
    {
        _logger = logger;

        Register(Opcodes.AccountLogin, AccountLoginRequest.PacketSize, SessionState.Connected, login.HandleAccountLoginAsync);
        Register(Opcodes.SecondPassword, SecondPasswordRequest.PacketSize, SessionState.CharacterSelect, login.HandleSecondPasswordAsync);
        Register(Opcodes.CreateCharacter, CreateCharacterRequest.PacketSize, SessionState.CharacterSelect, characters.HandleCreateAsync);
        Register(Opcodes.DeleteCharacter, DeleteCharacterRequest.PacketSize, SessionState.CharacterSelect, characters.HandleDeleteAsync);
        Register(Opcodes.CharacterLogin, PacketHeader.Size + 4, SessionState.CharacterSelect, characters.HandleEnterWorldAsync);

        Register(Opcodes.Move, ServerPackets.MoveSize, SessionState.InGame, world.HandleMoveAsync);
        Register(Opcodes.MoveStop, ServerPackets.MoveSize, SessionState.InGame, world.HandleMoveAsync);
        Register(Opcodes.RequestMob, PacketHeader.Size + 2, SessionState.InGame, world.HandleRequestMobAsync);
        Register(Opcodes.Chat, ServerPackets.ChatSize, SessionState.InGame, world.HandleChatAsync);
        Register(Opcodes.RequestShop, PacketHeader.Size + 2, SessionState.InGame, world.HandleRequestShopAsync);
        Register(Opcodes.Command, PacketHeader.Size + GameConstants.NameLength, SessionState.InGame, commands.HandleAsync);
    }

    private void Register(ushort type, int minSize, SessionState state, Func<ClientSession, PacketReader, Task> handle) =>
        _routes[type] = new Route(minSize, state, handle);

    public async Task DispatchAsync(ClientSession session, PacketReader packet)
    {
        if (packet.Type == Opcodes.ClientPing) return;

        if (!_routes.TryGetValue(packet.Type, out var route))
        {
            _logger.LogDebug("[{Id}] Pacote não tratado {Type} ({Size} bytes).", session.Id, Opcodes.Name(packet.Type), packet.Length);
            return;
        }

        if (packet.Length < route.MinSize)
        {
            _logger.LogWarning("[{Id}] Pacote {Type} curto demais: {Size} bytes (esperado {Expected}).",
                session.Id, Opcodes.Name(packet.Type), packet.Length, route.MinSize);
            return;
        }

        if (session.State != route.State)
        {
            _logger.LogDebug("[{Id}] Pacote {Type} ignorado no estado {State}.", session.Id, Opcodes.Name(packet.Type), session.State);
            return;
        }

        await route.Handle(session, packet);
    }
}
