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

/// <summary>
/// 0xFDE MSG_SecondPassword (32 bytes): senha numérica da tela de personagens.
/// <code>
/// 12  char Password[16]   (o client corta em 14 caracteres)
/// 28  int  Change         (0 = conferir; 1 = gravar como nova senha)
/// </code>
/// </summary>
public sealed record SecondPasswordRequest(string Password, bool Change)
{
    public const int PacketSize = 32;

    public static SecondPasswordRequest Read(PacketReader r) => new(r.ReadString(16), r.ReadInt32() != 0);

    public byte[] Write() =>
        new PacketWriter(Opcodes.SecondPassword, PacketSize).WriteString(Password, 16).WriteInt32(Change ? 1 : 0).ToArray();
}

/// <summary>0x334: o jogador digitou "/comando texto" no chat (128 bytes; também é o sussurro "/nome texto").</summary>
public sealed record CommandRequest(string Command, string Text)
{
    public static CommandRequest Read(PacketReader r)
    {
        string command = r.ReadString(NameLength);
        string text = r.ReadString(Math.Min(r.Remaining, 96));
        return new CommandRequest(command.Trim(), text.Trim());
    }
}

/// <summary>0x213 MSG_CharacterLogin (36 bytes): o client escolheu o personagem do slot.</summary>
public sealed record CharacterLoginRequest(int Slot)
{
    public const int PacketSize = 36;

    public static CharacterLoginRequest Read(PacketReader r) => new(r.ReadInt32());
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

    /// <summary>0x10E: cabeçalho, SELCHAR, Cargo[128], int Coin, char Account[16], byte Keys[16] e 8 bytes livres.</summary>
    /// <remarks>Os tamanhos seguem a tabela que o WYD.exe usa para conferir cada pacote recebido.</remarks>
    public const int AccountLoginOkSize = 1824;
    public const int SelCharPacketSize = PacketHeader.Size + SelCharSize;
    public const int SignalSize = PacketHeader.Size + 4;
    public const int HeaderOnlySize = PacketHeader.Size;

    /// <summary>
    /// STRUCT_MOB do client 7.54: a ficha completa do personagem (756 bytes), conferida no WYD.exe.
    /// <code>
    /// 0x000 char  Name[16]       0x010 byte  Clan          0x011 byte  Merchant     0x012 ushort Guild
    /// 0x014 byte  Class          0x015 byte  Rsv           0x016 ushort Quest       0x018 int   Coin
    /// 0x01C int   Exp            0x020 short SPX           0x022 short SPY
    /// 0x024 SCORE BaseScore      0x040 SCORE CurrentScore  0x05C ITEM  Equip[16]    0x0DC ITEM  Carry[64]
    /// 0x2DC uint  LearnedSkill   0x2E0 short ScoreBonus    0x2E2 short SpecialBonus 0x2E4 short SkillBonus
    /// 0x2E6 byte  Critical      0x2E7 byte  SaveMana      0x2E8 byte  SkillBar[4]  0x2EC byte  GuildLevel
    /// 0x2ED byte  Magic         0x2EE byte  RegenHP       0x2EF byte  RegenMP      0x2F0 byte  Resist[4]
    /// </code>
    /// </summary>
    public const int MobSize = 0x2F4;

    /// <summary>0x114: cabeçalho, short X, short Y, STRUCT_MOB, id do jogador, clima, barra de skills extra (e o resto zerado).</summary>
    public const int CharacterLoginOkSize = 1244;

    public const int CreateMobSize = 176;
    public const int RemoveMobSize = 16;
    public const int MoveSize = 52;
    public const int ChatSize = PacketHeader.Size + MessageLength;

    /// <summary>Skill vazia na barra (o client trata valores negativos como slot livre).</summary>
    public const byte EmptySkill = 0xFF;

    /// <summary>0x101: caixa de mensagem na tela do client.</summary>
    public static byte[] MessagePanel(string text, ushort id = 0) =>
        new PacketWriter(Opcodes.MessagePanel, MessagePanelSize, id)
            .WriteString(text, MessageLength)
            .ToArray();

    /// <summary>0xFDE: segunda senha aceita (o client libera a tela de personagens).</summary>
    public static byte[] SecondPasswordOk(ushort id) =>
        new PacketWriter(Opcodes.SecondPassword, SecondPasswordRequest.PacketSize, id).Skip(20).ToArray();

    /// <summary>Pacote só com o cabeçalho (os sinais de falha 0x119/0x11A/0x11B têm 12 bytes no 7.54).</summary>
    public static byte[] HeaderOnly(ushort type, ushort id = 0) => new PacketWriter(type, HeaderOnlySize, id).ToArray();

    /// <summary>0x3B4: na seleção de personagens, o client (0x4A69DF) solta um efeito em cima do personagem do slot.</summary>
    public static byte[] SelCharEffect(ushort id, int slot) => new PacketWriter(Opcodes.SelCharEffect, 16, id).WriteInt32(slot).ToArray();

    /// <summary>
    /// 0x364: põe um jogador na tela de quem recebe. Layout conferido no WYD.exe 7.54:
    /// <code>
    /// 0x0C short X, Y          0x10 ushort Id          0x12 char Name[12]
    /// 0x1E byte ChaosPoint     0x1F byte CurrentKill   0x20 ushort TotalKill
    /// 0x22 ushort Equip[16]    0x42 byte Affect[32]    0x62 ushort Guild
    /// 0x64 SCORE Score         0x80 ushort CreateType  0x82 byte Refino[16]
    /// 0x92 char Tab[26]        0xAC (4 bytes)
    /// </code>
    /// </summary>
    public static byte[] CreateMob(ushort mobId, Character c) =>
        CreateMob(mobId, c.Name, c.PosX, c.PosY, c.Equip.Select(i => i.Index), c.Guild, c.CurrentScore);

    public static byte[] CreateMob(ushort mobId, string name, short x, short y, IEnumerable<short> equip, ushort guild, Score score)
    {
        var w = new PacketWriter(Opcodes.CreateMob, CreateMobSize, mobId);
        w.WriteInt16(x).WriteInt16(y);
        w.WriteUInt16(mobId);
        w.WriteString(name, 12);
        w.WriteByte(0).WriteByte(0).WriteUInt16(0);   // ChaosPoint, CurrentKill, TotalKill
        var slots = equip.Take(MaxEquip).ToArray();
        for (int i = 0; i < MaxEquip; i++) w.WriteInt16(i < slots.Length ? slots[i] : (short)0);
        w.Skip(32);                                   // Affect
        w.WriteUInt16(guild);
        score.Write(w);
        w.WriteUInt16(0);                             // CreateType: aparece normalmente
        w.Skip(16);                                   // refino de cada equipamento
        w.WriteString("", 26);                        // Tab
        w.Skip(4);
        return w.ToArray();
    }

    /// <summary>
    /// 0x366 gerado pelo servidor (movimento de um mob). A rota é uma sequência de dígitos no estilo do
    /// teclado numérico, um por célula: 8 = +Y, 2 = -Y, 6 = +X, 4 = -X, 9 = +X+Y, 7 = -X+Y, 3 = +X-Y, 1 = -X-Y
    /// (conferido nos pacotes que o client 7.54 manda: "98888" vai de (2096, 2094) a (2097, 2099)).
    /// </summary>
    public static byte[] Move(ushort id, short x, short y, short destX, short destY, int speed)
    {
        return new PacketWriter(Opcodes.Move, MoveSize, id)
            .WriteInt16(x).WriteInt16(y)
            .WriteInt32(speed)
            .WriteInt32(MoveWalk)
            .WriteInt16(destX).WriteInt16(destY)
            .WriteString(Route(x, y, destX, destY), MaxRoute)
            .ToArray();
    }

    /// <summary>Tipo do 0x366 em 0x14: 0 (ou 2) anda pela rota; 1 aparece direto no destino com o efeito de teleporte.</summary>
    public const int MoveWalk = 0, MoveTeleport = 1;

    /// <summary>0x366 de teleporte: o client põe o personagem direto em (x, y).</summary>
    public static byte[] Teleport(ushort id, short x, short y) =>
        new PacketWriter(Opcodes.Move, MoveSize, id)
            .WriteInt16(x).WriteInt16(y)
            .WriteInt32(0)
            .WriteInt32(MoveTeleport)
            .WriteInt16(x).WriteInt16(y)
            .Skip(MaxRoute)
            .ToArray();

    public const int MaxRoute = 24;

    /// <summary>Rota em linha reta (diagonal primeiro), com no máximo 23 passos.</summary>
    public static string Route(int x, int y, int destX, int destY)
    {
        var route = new System.Text.StringBuilder();
        while ((x != destX || y != destY) && route.Length < MaxRoute - 1)
        {
            int dx = Math.Sign(destX - x), dy = Math.Sign(destY - y);
            route.Append((dx, dy) switch
            {
                (0, 1) => '8', (0, -1) => '2', (1, 0) => '6', (-1, 0) => '4',
                (1, 1) => '9', (-1, 1) => '7', (1, -1) => '3', _ => '1',
            });
            x += dx;
            y += dy;
        }
        return route.ToString();
    }

    /// <summary>
    /// Ataque (0x39D / 0x39E / 0x36C), lido no WYD.exe 7.54. O client manda o dano como -1 e o servidor
    /// devolve o mesmo pacote com o dano de cada alvo para todos que estão vendo (inclusive quem atacou).
    /// <code>
    /// 0x0C ushort Atacante        0x10 short X, Y do atacante     0x14 short X, Y do alvo
    /// 0x18 short Skill            0x1C byte Movimento             0x20 int Hold
    /// 0x24 int ExpDoAtacante      0x2C {ushort Alvo, short Dano} × 1, 2 ou 13
    /// </code>
    /// </summary>
    public static class AttackLayout
    {
        public const int Attacker = 0x0C;
        public const int Skill = 0x18;
        public const int CurrentMp = 0x1A;
        public const int Hold = 0x20;
        public const int Exp = 0x24;
        public const int Targets = 0x2C;
        public const int TargetSize = 4;
        public const int MaxTargets = 13;
    }

    /// <summary>
    /// 0x39D de um monstro batendo num jogador: atacante e posições em 0x0C..0x16, skill -1 (golpe normal)
    /// em 0x18 e o alvo com o dano em 0x2C, como no ataque que o client manda.
    /// </summary>
    public static byte[] MobAttack(ushort attacker, short x, short y, ushort target, short targetX, short targetY, short damage) =>
        new PacketWriter(Opcodes.AttackOne, AttackOneSize, attacker)
            .WriteUInt16(attacker).WriteInt16(0)
            .WriteInt16(x).WriteInt16(y).WriteInt16(targetX).WriteInt16(targetY)
            .WriteInt16(-1)                              // skill: nenhuma
            .WriteInt16(-1)                              // mana atual (não muda)
            .WriteByte(0xFF).Skip(3)                     // movimento: o client escolhe
            .WriteInt32(0).WriteInt32(0)                 // Hold, Exp: só valem para o atacante jogador
            .Skip(4)
            .WriteUInt16(target).WriteInt16(damage)
            .ToArray();

    public const int AttackOneSize = 48;

    /// <summary>O client guarda a experiência como número sem sinal de 32 bits (até 4.294.967.295).</summary>
    public static uint ClientExp(long exp) => (uint)Math.Clamp(exp, 0, uint.MaxValue);

    public const int MobKillSize = 24;

    /// <summary>0x338: <paramref name="killed"/> morreu; se quem recebe é o matador, o client mostra a nova experiência.</summary>
    public static byte[] MobKill(ushort killed, ushort killer, int killerExp, int hold = 0) =>
        new PacketWriter(Opcodes.MobKill, MobKillSize, killed)
            .WriteInt32(hold).WriteUInt16(killed).WriteUInt16(killer).WriteInt32(killerExp)
            .ToArray();

    public const int CommandSize = 128;
    public const int UpdateItemSize = 24;

    public enum ItemPlace : short { Equip = 0, Inventory = 1, Cargo = 2 }

    /// <summary>0x182: troca o item de um slot (equipamento, inventário ou baú) na tela do jogador.</summary>
    public static byte[] UpdateItem(ushort id, ItemPlace place, int slot, Item item)
    {
        var w = new PacketWriter(Opcodes.UpdateItem, UpdateItemSize, id);
        w.WriteInt16((short)place).WriteInt16((short)slot);
        item.Write(w);
        return w.ToArray();
    }

    /// <summary>0x334 do servidor para o client: sussurro de <paramref name="from"/> (aparece no chat de quem recebe).</summary>
    public static byte[] Whisper(ushort id, string from, string text) =>
        new PacketWriter(Opcodes.Command, CommandSize, id)
            .WriteString(from, NameLength)
            .WriteString(text, CommandSize - PacketHeader.Size - NameLength)
            .ToArray();

    /// <summary>
    /// 0x337: experiência, skills aprendidas, pontos e gold (o client copia cada campo para a ficha).
    /// <code>0x0C int Hold, 0x10 int Exp, 0x14 uint Skills, 0x18 short Pontos, 0x1A Especiais, 0x1C Skill, 0x20 int Gold</code>
    /// </summary>
    public static byte[] UpdateEtc(ushort id, Character c) =>
        new PacketWriter(Opcodes.UpdateEtc, 36, id)
            .WriteInt32(0)
            .WriteUInt32(ClientExp(c.Exp))
            .WriteUInt32(c.LearnedSkill)
            .WriteInt16(c.ScoreBonus).WriteInt16(c.SpecialBonus).WriteInt16(c.SkillBonus).Skip(2)
            .WriteInt32(c.Coin)
            .ToArray();

    /// <summary>0x336: atributos do jogador (nível, vida, mana...), por exemplo depois de subir de nível.</summary>
    public static byte[] UpdateScore(ushort id, Character c)
    {
        var w = new PacketWriter(Opcodes.UpdateScore, 92, id);
        c.CurrentScore.Write(w);                      // 0x0C
        w.WriteByte(0).WriteByte(0);                  // 0x28 Critical, SaveMana
        w.Skip(32);                                   // 0x2A Affect
        w.WriteUInt16(c.Guild).WriteUInt16(0);        // 0x4A Guild, GuildLevel
        w.Skip(92 - w.Position);                      // 0x4E Resist e o resto
        return w.ToArray();
    }

    /// <summary>0x3AF: novo total de gold do jogador.</summary>
    public static byte[] UpdateCoin(ushort id, int coin) =>
        new PacketWriter(Opcodes.UpdateCoin, 16, id).WriteInt32(coin).ToArray();

    /// <summary>0x181: HP e MP atuais do jogador.</summary>
    public static byte[] UpdateHpMp(ushort id, int hp, int mp) =>
        new PacketWriter(Opcodes.UpdateHpMp, 20, id).WriteInt32(hp).WriteInt32(mp).ToArray();

    /// <summary>0x36B: equipamentos visíveis de um jogador, para quem está vendo ele.</summary>
    public static byte[] UpdateEquip(ushort id, IReadOnlyList<Item> equip)
    {
        var w = new PacketWriter(Opcodes.UpdateEquip, 60, id);
        for (int i = 0; i < MaxEquip; i++) w.WriteInt16(i < equip.Count ? equip[i].Index : (short)0);
        w.Skip(16);
        return w.ToArray();
    }

    /// <summary>Resposta da compra (0x379): o mesmo pedido com o slot onde o item foi parar e o gold total.</summary>
    public static byte[] BuyOk(ushort id, ushort npcId, short npcSlot, short mySlot, int coin) =>
        new PacketWriter(Opcodes.Buy, 24, id)
            .WriteUInt16(npcId).WriteInt16(npcSlot).WriteInt16(mySlot).Skip(2).WriteInt32(coin)
            .ToArray();

    public const int ShopListSize = 236;
    public const int ShopSlots = 27;

    /// <summary>
    /// 0x17C: abre a janela da loja (tipo 1 = itens). As 3 abas de 9 itens vêm dos slots 0–8, 27–35 e
    /// 54–62 do inventário do NPC, como no servidor original.
    /// </summary>
    /// <summary>Tipo da janela no 0x17C (0x0C): 1 = loja; 3 = mestre de skill (o client monta as abas pela classe do 1º item).</summary>
    public const int ShopTypeStore = 1, ShopTypeSkillMaster = 3;

    public static byte[] ShopList(ushort npcId, IReadOnlyList<Item> carry, int tax = 0, int type = ShopTypeStore)
    {
        var w = new PacketWriter(Opcodes.ShopList, ShopListSize, npcId);
        w.WriteInt32(type);
        for (int i = 0; i < ShopSlots; i++)
        {
            int slot = ShopSlotToCarry(i);
            (slot < carry.Count ? carry[slot] : default).Write(w);
        }
        w.WriteInt32(tax);
        return w.ToArray();
    }

    public static int ShopSlotToCarry(int shopSlot) => shopSlot / 9 * 27 + shopSlot % 9;

    /// <summary>Tipo do 0x165 para um monstro que morreu (0 = só some da tela).</summary>
    public const int RemoveDead = 1;

    /// <summary>0x165: tira o id da tela de quem recebe (tipo 0 = simplesmente some, 1 = morreu).</summary>
    public static byte[] RemoveMob(ushort mobId, int type = 0) =>
        new PacketWriter(Opcodes.RemoveMob, RemoveMobSize, mobId).WriteInt32(type).ToArray();

    /// <summary>Copia um pacote recebido trocando o id do cabeçalho (repasse de movimento e chat).</summary>
    public static byte[] Relay(byte[] packet, ushort id)
    {
        var copy = (byte[])packet.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(PacketHeader.IdOffset), id);
        return copy;
    }

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
        w.Skip(AccountLoginOkSize - w.Position);      // 0x718
        return w.ToArray();
    }

    /// <summary>
    /// 0x114: entra no mundo com o personagem escolhido. O client copia a ficha (STRUCT_MOB), usa X/Y como
    /// posição, guarda o id de 0x306 como o id do próprio jogador e troca para a cena do mapa.
    /// </summary>
    public static byte[] CharacterLoginOk(ushort id, Character character)
    {
        var w = new PacketWriter(Opcodes.CharacterLoginOk, CharacterLoginOkSize, id);
        w.WriteInt16(character.PosX);                 // 0x00C
        w.WriteInt16(character.PosY);                 // 0x00E
        WriteMob(w, character);                       // 0x010
        w.Skip(2);                                    // 0x304
        w.WriteUInt16(id);                            // 0x306: id do jogador no servidor
        w.WriteUInt16(0);                             // 0x308: clima
        for (int i = 0; i < 16; i++)                  // 0x30A: segunda barra de skills
            w.WriteByte(EmptySkill);
        w.Skip(2);                                    // 0x31A
        w.WriteInt32(0);                              // 0x31C: (desconhecido, o client só guarda)
        w.Skip(CharacterLoginOkSize - w.Position);    // 0x320: não lido na entrada no mundo
        return w.ToArray();
    }

    internal static void WriteMob(PacketWriter w, Character c)
    {
        int start = w.Position;
        w.WriteString(c.Name, NameLength);
        w.WriteByte(0);                               // Clan
        w.WriteByte(0);                               // Merchant
        w.WriteUInt16(c.Guild);
        w.WriteByte((byte)c.Class);
        w.WriteByte(0);                               // Rsv
        w.WriteUInt16(0);                             // Quest
        w.WriteInt32(c.Coin);
        w.WriteUInt32(ClientExp(c.Exp));
        w.WriteInt16(c.PosX);
        w.WriteInt16(c.PosY);
        c.BaseScore.Write(w);
        c.CurrentScore.Write(w);                      // CurrentScore: com a velocidade das botas e da montaria
        foreach (var item in c.Equip) item.Write(w);
        foreach (var item in c.Inventory) item.Write(w);
        w.WriteUInt32(c.LearnedSkill);
        w.WriteInt16(c.ScoreBonus).WriteInt16(c.SpecialBonus).WriteInt16(c.SkillBonus);
        w.WriteByte(0).WriteByte(0);                  // Critical, SaveMana
        for (int i = 0; i < 4; i++) w.WriteByte(EmptySkill); // SkillBar[4]
        w.WriteByte(0).WriteByte(0);                  // GuildLevel, Magic
        w.WriteByte(0).WriteByte(0);                  // RegenHP, RegenMP
        w.Skip(4);                                    // Resist[4]

        if (w.Position - start != MobSize)
            throw new InvalidOperationException($"STRUCT_MOB com {w.Position - start} bytes (esperado {MobSize}).");
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
        foreach (var c in slots) w.WriteUInt32(ClientExp(c?.Exp ?? 0));
    }
}
