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
    public const ushort Command = 0x334;           // 128 bytes: "/xxx texto" -> char Cmd[16] em 0x0C, char Texto[...] em 0x1C; também é o sussurro (conferido no WYD.exe 7.54)
    public const ushort Chat = 0x333;              // MSG_MessageChat, 108 bytes, nos dois sentidos (conferido no WYD.exe 7.54)
    public const ushort Move = 0x366;              // MSG_Action, 52 bytes, nos dois sentidos (conferido no WYD.exe 7.54)
    public const ushort MoveStop = 0x367;          // MSG_Action, 52 bytes (conferido no WYD.exe 7.54)
    public const ushort MoveItem = 0x376;          // 20 bytes: byte TipoDest, SlotDest, TipoOrig, SlotOrig (0 equip, 1 inventário, 2 baú); o servidor devolve para confirmar
    public const ushort UseItem = 0x373;           // 36 bytes: int TipoOrig, SlotOrig, TipoDest, SlotDest
    public const ushort Buy = 0x379;               // 24 bytes: short Npc, SlotDoNpc, SlotMeu; resposta com int GoldTotal em 0x14
    public const ushort Sell = 0x37A;              // 20 bytes: short Npc, TipoOrig, SlotOrig
    public const ushort RequestShop = 0x27B;       // 16 bytes: id do NPC em 0x0C; o client só manda se Score.Merchant & 0xF == 1 (conferido no WYD.exe 7.54)
    public const ushort Attack = 0x36C;            // até 13 alvos (96 bytes), nos dois sentidos (conferido no WYD.exe 7.54)
    public const ushort AttackOne = 0x39D;         // 1 alvo (48 bytes)
    public const ushort AttackTwo = 0x39E;         // 2 alvos (52 bytes)
    public const ushort Revive = 0x289;            // só o cabeçalho: o personagem morto pede para renascer (botão ou 3 minutos depois; conferido no WYD.exe 7.54)
    public const ushort RequestMob = 0x369;        // client pede o 0x364 de um id que ele não conhece (conferido no WYD.exe 7.54)
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
    public const ushort CreateMob = 0x364;             // MSG_CreateMob, 176 bytes: põe um jogador/mob na tela (conferido no WYD.exe 7.54)
    public const ushort RemoveMob = 0x165;
    public const ushort MobKill = 0x338;               // 24 bytes: int Hold, ushort Morto, ushort Matador, int ExpDoMatador (conferido no WYD.exe 7.54)
    public const ushort UpdateEtc = 0x337;             // 36 bytes: int Hold, int Exp, uint Skills, short Pontos[3], int Gold (conferido no WYD.exe 7.54)
    public const ushort UpdateScore = 0x336;           // 92 bytes: SCORE em 0x0C, ... (atributos depois de subir de nível)
    public const ushort UpdateCoin = 0x3AF;            // 16 bytes: int Gold (conferido no WYD.exe 7.54)
    public const ushort UpdateHpMp = 0x181;            // 20 bytes: int Hp, int Mp
    public const ushort UpdateEquip = 0x36B;           // 60 bytes: ushort Equip[16], byte Refino[16] (para quem está vendo)
    public const ushort UpdateItem = 0x182;            // 24 bytes: short Tipo (0 equip, 1 inventário, 2 baú), short Slot, ITEM (conferido no WYD.exe 7.54)
    public const ushort ShopList = 0x17C;              // 236 bytes: int tipo, ITEM Lista[27], int imposto (conferido no WYD.exe 7.54)             // 16 bytes: tira da tela o id do cabeçalho (conferido no WYD.exe 7.54)
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
        Chat => "Chat",
        Command => "Command",
        UpdateItem => "UpdateItem",
        MoveItem => "MoveItem",
        UseItem => "UseItem",
        Buy => "Buy",
        Sell => "Sell",
        UpdateCoin => "UpdateCoin",
        UpdateEtc => "UpdateEtc",
        UpdateScore => "UpdateScore",
        UpdateHpMp => "UpdateHpMp",
        UpdateEquip => "UpdateEquip",
        Move => "Move",
        MoveStop => "MoveStop",
        RequestMob => "RequestMob",
        Attack => "Attack",
        AttackOne => "AttackOne",
        AttackTwo => "AttackTwo",
        MobKill => "MobKill",
        Revive => "Revive",
        RequestShop => "RequestShop",
        ShopList => "ShopList",
        CreateMob => "CreateMob",
        RemoveMob => "RemoveMob",
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
