using WYD.Server.Game;
using WYD.Server.Network;
using static WYD.Server.Protocol.GameConstants;

namespace WYD.Server.Protocol;

// ======================= Client -> Servidor =======================

/// <summary>
/// 0x20D MSG_AccountLogin (116 bytes), conferido com o client 7.54:
/// <code>
/// 12  char AccountName[16]
/// 28  char AccountPassword[12]
/// 40  int  ClientVersion     (754 / 756)
/// 44  int  DBNeedSave
/// 48  int  AdapterName[4]    (identificação da placa de rede)
/// 64  char Zero[52]
/// </code>
/// </summary>
public sealed record AccountLoginRequest(string Username, string Password, int ClientVersion)
{
    public const int PacketSize = 116;

    public static AccountLoginRequest Read(PacketReader r)
    {
        string username = r.ReadString(AccountNameLength);
        string password = r.ReadString(AccountPasswordLength);
        int version = r.ReadInt32();
        return new AccountLoginRequest(username, password, version);
    }

    /// <summary>Monta o pacote como o client faria (usado nos testes e ferramentas).</summary>
    public byte[] Write() =>
        new PacketWriter(Opcodes.AccountLogin, PacketSize)
            .WriteString(Username, AccountNameLength)
            .WriteString(Password, AccountPasswordLength)
            .WriteInt32(ClientVersion)
            .WriteInt32(1)  // DBNeedSave
            .Skip(16 + 52)  // AdapterName, Zero
            .ToArray();
}

/// <summary>0x20F MSG_NewCharacter (36 bytes).</summary>
public sealed record CreateCharacterRequest(int Slot, string Name, int ClassId)
{
    public const int PacketSize = 36;

    public static CreateCharacterRequest Read(PacketReader r) =>
        new(r.ReadInt32(), r.ReadString(NameLength), r.ReadInt32());
}

/// <summary>0x211 MSG_DeleteCharacter (44 bytes).</summary>
public sealed record DeleteCharacterRequest(int Slot, string Name, string Password)
{
    public const int PacketSize = 44;

    public static DeleteCharacterRequest Read(PacketReader r) =>
        new(r.ReadInt32(), r.ReadString(NameLength), r.ReadString(AccountPasswordLength));
}

// ======================= Servidor -> Client =======================

public static class ServerPackets
{
    /// <summary>
    /// STRUCT_SELCHAR do client 7.54: os 4 slots da tela de seleção (744 bytes), conferido no WYD.exe.
    /// <code>
    /// 0x000 short SPX[4]          0x008 short SPY[4]         0x010 char  Name[4][16]
    /// 0x050 SCORE Score[4] (28)   0x0C0 ITEM  Equip[4][16]   0x2C0 short Guild[4]
    /// 0x2C8 int   Coin[4]         0x2D8 int   Exp[4]
    /// </code>
    /// </summary>
    public const int SelCharSize =
        CharactersPerAccount * 2 * 2              // SPX[4], SPY[4]
        + CharactersPerAccount * NameLength       // Name[4][16]
        + CharactersPerAccount * Score.Size       // Score[4]
        + CharactersPerAccount * MaxEquip * Item.Size // Equip[4][16]
        + CharactersPerAccount * 2                // Guild[4]
        + CharactersPerAccount * 4                // Coin[4]
        + CharactersPerAccount * 4;               // Exp[4]

    public const int MessagePanelSize = PacketHeader.Size + MessageLength;

    /// <summary>0x10E: cabeçalho, SELCHAR, Cargo[128], int Coin, char Account[16], byte Keys[16].</summary>
    public const int AccountLoginOkSize = PacketHeader.Size + SelCharSize + MaxCargo * Item.Size + 4 + AccountNameLength + 16;
    public const int SelCharPacketSize = PacketHeader.Size + SelCharSize;
    public const int SignalSize = PacketHeader.Size + 4;

    /// <summary>0x101: caixa de mensagem na tela do client.</summary>
    public static byte[] MessagePanel(string text, ushort id = 0) =>
        new PacketWriter(Opcodes.MessagePanel, MessagePanelSize, id)
            .WriteString(text, MessageLength)
            .ToArray();

    /// <summary>Pacote só com um parâmetro inteiro (MSG_STANDARDPARM).</summary>
    public static byte[] Signal(ushort type, int parm = 0, ushort id = 0) =>
        new PacketWriter(type, SignalSize, id).WriteInt32(parm).ToArray();

    /// <summary>0x10E: login aceito, com a lista de personagens e o baú da conta. O client vai para a tela de seleção.</summary>
    public static byte[] AccountLoginOk(ushort id, Account account, IReadOnlyList<Character> characters)
    {
        var w = new PacketWriter(Opcodes.AccountLoginOk, AccountLoginOkSize, id);
        WriteSelChar(w, characters);                  // 0x00C
        foreach (var item in account.Cargo) item.Write(w); // 0x2F4
        w.WriteInt32(account.CargoCoin);              // 0x6F4
        w.WriteString(account.Username, AccountNameLength); // 0x6F8
        w.Skip(16);                                   // 0x708: chaves que o client guarda no módulo de rede
        return w.ToArray();
    }

    /// <summary>0x110 / 0x112: lista de personagens atualizada após criar/apagar.</summary>
    public static byte[] SelCharList(ushort type, ushort id, IReadOnlyList<Character> characters)
    {
        var w = new PacketWriter(type, SelCharPacketSize, id);
        WriteSelChar(w, characters);
        return w.ToArray();
    }

    internal static void WriteSelChar(PacketWriter w, IReadOnlyList<Character> characters)
    {
        var slots = new Character?[CharactersPerAccount];
        foreach (var c in characters)
            if (c.Slot is >= 0 and < CharactersPerAccount)
                slots[c.Slot] = c;

        foreach (var c in slots) w.WriteInt16(c?.PosX ?? 0);
        foreach (var c in slots) w.WriteInt16(c?.PosY ?? 0);
        foreach (var c in slots) w.WriteString(c?.Name, NameLength);
        foreach (var c in slots) (c?.BaseScore ?? default).Write(w);
        foreach (var c in slots)
            for (int i = 0; i < MaxEquip; i++)
                (c?.Equip[i] ?? default).Write(w);
        foreach (var c in slots) w.WriteUInt16(c?.Guild ?? 0);
        foreach (var c in slots) w.WriteInt32(c?.Coin ?? 0);
        foreach (var c in slots) w.WriteInt32((int)Math.Clamp(c?.Exp ?? 0, 0, int.MaxValue));
    }
}
