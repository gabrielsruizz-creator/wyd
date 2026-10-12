namespace WYD.Server.Protocol;

/// <summary>
/// Opcodes (campo Type do cabeçalho). Os que estão marcados "a validar" vêm da documentação da
/// comunidade e precisam ser confirmados com o seu client usando Server:LogPackets = true.
/// </summary>
public static class Opcodes
{
    // ---- Client -> Servidor ----
    public const ushort AccountLogin = 0x20D;      // MSG_AccountLogin, 116 bytes
    public const ushort CreateCharacter = 0x20F;   // MSG_NewCharacter, 36 bytes
    public const ushort DeleteCharacter = 0x211;   // MSG_DeleteCharacter, 44 bytes
    public const ushort CharacterLogin = 0x213;    // MSG_CharacterLogin (a validar)
    public const ushort CharacterLogout = 0x215;   // volta para a seleção (a validar)
    public const ushort ClientPing = 0x3A0;        // keep-alive (a validar)

    // ---- Servidor -> Client ----
    public const ushort MessagePanel = 0x101;          // MSG_MessagePanel, 140 bytes
    public const ushort AccountLoginOk = 0x10A;        // MSG_CNFAccountLogin (a validar)
    public const ushort CreateCharacterOk = 0x110;     // MSG_CNFNewCharacter (a validar)
    public const ushort DeleteCharacterOk = 0x112;     // MSG_CNFDeleteCharacter (a validar)
    public const ushort CreateCharacterFail = 0x11A;   // sinal de falha (a validar)
    public const ushort DeleteCharacterFail = 0x11B;   // sinal de falha (a validar)

    public static string Name(ushort type) => type switch
    {
        AccountLogin => "AccountLogin",
        CreateCharacter => "CreateCharacter",
        DeleteCharacter => "DeleteCharacter",
        CharacterLogin => "CharacterLogin",
        CharacterLogout => "CharacterLogout",
        ClientPing => "ClientPing",
        MessagePanel => "MessagePanel",
        AccountLoginOk => "AccountLoginOk",
        CreateCharacterOk => "CreateCharacterOk",
        DeleteCharacterOk => "DeleteCharacterOk",
        CreateCharacterFail => "CreateCharacterFail",
        DeleteCharacterFail => "DeleteCharacterFail",
        _ => $"0x{type:X3}",
    };
}
