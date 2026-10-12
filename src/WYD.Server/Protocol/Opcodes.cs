namespace WYD.Server.Protocol;

/// <summary>
/// Opcodes (campo Type do cabeçalho). Os que estão marcados "a validar" vêm da documentação da
/// comunidade e precisam ser confirmados com o seu client usando Server:LogPackets = true.
/// </summary>
public static class Opcodes
{
    // ---- Client -> Servidor ----
    public const ushort AccountLogin = 0x20D;      // MSG_AccountLogin, 116 bytes (conferido no WYD.exe 7.54)
    public const ushort CreateCharacter = 0x20F;   // MSG_NewCharacter, 36 bytes (conferido no WYD.exe 7.54)
    public const ushort DeleteCharacter = 0x211;   // MSG_DeleteCharacter, 44 bytes (conferido no WYD.exe 7.54)
    public const ushort CharacterLogin = 0x213;    // MSG_CharacterLogin, 36 bytes (conferido no WYD.exe 7.54)
    public const ushort CharacterLogout = 0x215;   // volta para a seleção (a validar)
    public const ushort ClientPing = 0x3A0;        // keep-alive (a validar)
    public const ushort SecondPassword = 0xFDE;    // MSG_SecondPassword, 32 bytes, nos dois sentidos (conferido no WYD.exe 7.54)

    // ---- Servidor -> Client ----
    public const ushort MessagePanel = 0x101;          // MSG_MessagePanel, 140 bytes
    public const ushort AccountLoginOk = 0x10E;        // MSG_CNFAccountLogin, 1816 bytes (conferido no WYD.exe 7.54)
    public const ushort CreateCharacterOk = 0x110;     // MSG_CNFNewCharacter, 756 bytes (conferido no WYD.exe 7.54)
    public const ushort DeleteCharacterOk = 0x112;     // MSG_CNFDeleteCharacter, 756 bytes (conferido no WYD.exe 7.54)
    public const ushort CharacterLoginOk = 0x114;      // MSG_CNFCharacterLogin, com STRUCT_MOB de 756 bytes (próxima etapa)
    public const ushort CharacterLoginFail = 0x119;    // client mostra mensagem própria (conferido no WYD.exe 7.54)
    public const ushort CreateCharacterFail = 0x11A;   // client mostra mensagem própria (conferido no WYD.exe 7.54)
    public const ushort DeleteCharacterFail = 0x11B;   // client mostra mensagem própria (conferido no WYD.exe 7.54)
    public const ushort SecondPasswordFail = 0xFDF;    // segunda senha errada: o client volta a bloquear (conferido no WYD.exe 7.54)

    public static string Name(ushort type) => type switch
    {
        AccountLogin => "AccountLogin",
        CreateCharacter => "CreateCharacter",
        DeleteCharacter => "DeleteCharacter",
        CharacterLogin => "CharacterLogin",
        CharacterLogout => "CharacterLogout",
        ClientPing => "ClientPing",
        SecondPassword => "SecondPassword",
        SecondPasswordFail => "SecondPasswordFail",
        MessagePanel => "MessagePanel",
        AccountLoginOk => "AccountLoginOk",
        CreateCharacterOk => "CreateCharacterOk",
        DeleteCharacterOk => "DeleteCharacterOk",
        CharacterLoginOk => "CharacterLoginOk",
        CharacterLoginFail => "CharacterLoginFail",
        CreateCharacterFail => "CreateCharacterFail",
        DeleteCharacterFail => "DeleteCharacterFail",
        _ => $"0x{type:X3}",
    };
}
