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
    public const ushort CharacterLogout = 0x215;   // só o cabeçalho: "trocar personagem" (5 s depois do clique; conferido no WYD.exe 7.54)
    public const ushort Command = 0x334;           // 128 bytes: "/xxx texto" -> char Cmd[16] em 0x0C, char Texto[...] em 0x1C; também é o sussurro (conferido no WYD.exe 7.54)
    public const ushort Chat = 0x333;              // MSG_MessageChat, 108 bytes, nos dois sentidos (conferido no WYD.exe 7.54)
    public const ushort Move = 0x366;              // MSG_Action, 52 bytes, nos dois sentidos (conferido no WYD.exe 7.54 e 7.57)
    public const ushort MoveStop = 0x36C;          // MSG_Action, 52 bytes (no 7.54 era 0x367; conferido no WYD.exe 7.57, 0x505B58)
    public const ushort MoveItem = 0x376;          // 20 bytes: byte TipoDest, SlotDest, TipoOrig, SlotOrig (0 equip, 1 inventário, 2 baú); o servidor devolve para confirmar
    public const ushort UseItem = 0x373;           // 36 bytes: int TipoOrig, SlotOrig, TipoDest, SlotDest
    public const ushort Buy = 0x379;               // 24 bytes: short Npc, SlotDoNpc, SlotMeu; resposta com int GoldTotal em 0x14
    public const ushort Sell = 0x37A;              // 20 bytes: short Npc, TipoOrig, SlotOrig
    public const ushort RequestShop = 0x27B;       // 16 bytes: id do NPC em 0x0C; o client só manda se Score.Merchant & 0xF == 1 (conferido no WYD.exe 7.54)
    public const ushort Attack = 0x367;            // até 13 alvos (168 bytes), nos dois sentidos (no 7.54 era 0x36C; conferido no WYD.exe 7.57)
    public const ushort AttackOne = 0x39D;         // 1 alvo (72 bytes no 7.57)
    public const ushort AttackTwo = 0x39E;         // 2 alvos (80 bytes no 7.57)
    public const ushort ApplyBonus = 0x277;        // 20 bytes: short Tipo (0 atributo, 1 especial, 2 skill), short Qual, short Extra (conferido no WYD.exe 7.54)
    public const ushort NpcTalk = 0x28E;           // 16 bytes: int IdDoNpc; clique em NPC de tipo 6 a 8 (Odin) (conferido no WYD.exe 7.54)
    public const ushort EhreCombine = 0x2D3;       // 84 bytes: ITEM Item[8], sbyte SlotNoInventário[8] (-1 vazio); OK na janela de combinação da Ehre (conferido no WYD.exe 7.54)
    public const ushort NpcClick = 0x28B;          // 20 bytes: int IdDoNpc, int Confirma; NPCs que conversam (reis, quests) (conferido no WYD.exe 7.54)
    public const ushort Teleport = 0x290;          // 16 bytes: o client confirmou o teleporte num círculo (célula com a flag 0x10 no mapa) (conferido no WYD.exe 7.54)
    public const ushort Revive = 0x289;            // só o cabeçalho: o personagem morto pede para renascer (botão ou 3 minutos depois; conferido no WYD.exe 7.54)
    public const ushort DropItem = 0x272;          // 32 bytes: int TipoOrig, int SlotOrig, ...; largar no chão: ignorado (no 7.54 não há item no chão)
    public const ushort RequestMob = 0x369;        // client pede o 0x364 de um id que ele não conhece (conferido no WYD.exe 7.54)
    public const ushort ClientPing = 0x3A0;        // keep-alive (a validar)
    public const ushort SecondPassword = 0xFDE;    // MSG_SecondPassword, 32 bytes, nos dois sentidos (conferido no WYD.exe 7.54)

    // ---- Servidor -> Client ----
    public const ushort MessagePanel = 0x101;          // MSG_MessagePanel, 140 bytes
    public const ushort AccountLoginOk = 0x10A;        // MSG_CNFAccountLogin, 1928 bytes (no 7.54 era 0x10E; conferido no WYD.exe 7.57)
    public const ushort CreateCharacterOk = 0x110;     // MSG_CNFNewCharacter, 856 bytes (conferido no WYD.exe 7.57)
    public const ushort DeleteCharacterOk = 0x112;     // MSG_CNFDeleteCharacter, 856 bytes (conferido no WYD.exe 7.57)
    public const ushort CharacterLoginOk = 0x114;      // MSG_CNFCharacterLogin, 1712 bytes com STRUCT_MOB de 1024 (conferido no WYD.exe 7.57)
    public const ushort SkillBar = 0x378;             // 32 bytes: byte Barra[20] (skill dentro da classe, 0xFF vazio); o client manda ao mudar a barra (conferido no WYD.exe 7.54)
    public const ushort AffectList = 0x3B9;            // 268 bytes: AFFECT[32] {byte Tipo, byte Valor, short Nível, int Tempo em 8 s}; ícones dos buffs no canto (conferido no WYD.exe 7.54)
    public const ushort Motion = 0x36A;                // 20 bytes: short Movimento, short Parâmetro; animação do personagem do cabeçalho (conferido no WYD.exe 7.54)
    public const ushort SelCharEffect = 0x3B4;         // 16 bytes: int Slot (0-3); na seleção de personagens, efeito em cima do personagem do slot (conferido no WYD.exe 7.54)
    public const ushort CharacterLogoutOk = 0x116;     // só o cabeçalho: o client volta para a seleção de personagens com a lista que já tem (conferido no WYD.exe 7.54)
    public const ushort AccountLoginFail = 0x11C;      // só o cabeçalho: a tela de login mostra o erro e destrava (0x11D faz o mesmo; conferido no WYD.exe 7.54)
    public const ushort CharacterLoginFail = 0x119;    // client mostra mensagem própria (conferido no WYD.exe 7.54)
    public const ushort CreateCharacterFail = 0x11A;   // client mostra mensagem própria (conferido no WYD.exe 7.54)
    public const ushort DeleteCharacterFail = 0x11B;   // client mostra mensagem própria (conferido no WYD.exe 7.54)
    public const ushort CreateMob = 0x364;             // MSG_CreateMob, 232 bytes: põe um jogador/mob na tela (tamanho conferido no WYD.exe 7.57)
    public const ushort RemoveMob = 0x165;
    public const ushort MobKill = 0x338;               // 32 bytes: int Hold, ushort Morto, ushort Matador, long ExpDoMatador em 0x18 (conferido no WYD.exe 7.57)
    public const ushort UpdateEtc = 0x337;             // 48 bytes: int Hold, long Exp, uint Skills[2], short Pontos[3], int Gold (conferido no WYD.exe 7.57)
    public const ushort UpdateScore = 0x336;           // 144 bytes: SCORE em 0x0C, ... (atributos depois de subir de nível; conferido no WYD.exe 7.57)
    public const ushort CombineDone = 0x3A7;           // só o cabeçalho: fecha as janelas de combinação (Ehre e outras) (conferido no WYD.exe 7.54)
    public const ushort UpdateCoin = 0x3AF;            // 16 bytes: int Gold (conferido no WYD.exe 7.54)
    public const ushort UpdateHpMp = 0x181;            // 28 bytes: int Hp, int Mp, int Hp, int Mp (conferido no WYD.exe 7.57)
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
        DropItem => "DropItem",
        Attack => "Attack",
        AttackOne => "AttackOne",
        AttackTwo => "AttackTwo",
        MobKill => "MobKill",
        Revive => "Revive",
        Teleport => "Teleport",
        NpcClick => "NpcClick",
        EhreCombine => "EhreCombine",
        CombineDone => "CombineDone",
        NpcTalk => "NpcTalk",
        ApplyBonus => "ApplyBonus",
        RequestShop => "RequestShop",
        ShopList => "ShopList",
        CreateMob => "CreateMob",
        RemoveMob => "RemoveMob",
        SecondPasswordFail => "SecondPasswordFail",
        MessagePanel => "MessagePanel",
        AccountLoginOk => "AccountLoginOk",
        AccountLoginFail => "AccountLoginFail",
        CharacterLogoutOk => "CharacterLogoutOk",
        SelCharEffect => "SelCharEffect",
        Motion => "Motion",
        AffectList => "AffectList",
        SkillBar => "SkillBar",
        CreateCharacterOk => "CreateCharacterOk",
        DeleteCharacterOk => "DeleteCharacterOk",
        CharacterLoginOk => "CharacterLoginOk",
        CharacterLoginFail => "CharacterLoginFail",
        CreateCharacterFail => "CreateCharacterFail",
        DeleteCharacterFail => "DeleteCharacterFail",
        _ => $"0x{type:X3}",
    };
}
