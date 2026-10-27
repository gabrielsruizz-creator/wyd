using WYD.Server.Game;
using WYD.Server.Network;
using static WYD.Server.Protocol.GameConstants;

namespace WYD.Server.Protocol;

// ======================= Client -> Servidor =======================

/// <summary>
/// 0x20D MSG_AccountLogin (116 bytes), conferido com o client 7.57 (montado em 0x48587E):
/// <code>
/// 0x0C char AccountPassword[12]
/// 0x18 char AccountName[16]
/// 0x28 byte ?[52]
/// 0x5C int  ClientVersion     (758, fixo no exe)
/// 0x60 int  DBNeedSave
/// 0x64 int  AdapterName[4]    (identificação da placa de rede)
/// </code>
/// </summary>
public sealed record AccountLoginRequest(string Username, string Password, int ClientVersion)
{
    public const int PacketSize = 116;

    public static AccountLoginRequest Read(PacketReader r)
    {
        string password = r.ReadString(AccountPasswordLength);
        string username = r.ReadString(AccountNameLength);
        r.Skip(52);
        int version = r.ReadInt32();
        return new AccountLoginRequest(username, password, version);
    }

    /// <summary>Monta o pacote como o client faria (usado nos testes e ferramentas).</summary>
    public byte[] Write() =>
        new PacketWriter(Opcodes.AccountLogin, PacketSize)
            .WriteString(Password, AccountPasswordLength)
            .WriteString(Username, AccountNameLength)
            .Skip(52)
            .WriteInt32(ClientVersion)
            .WriteInt32(1)  // DBNeedSave
            .Skip(16)       // AdapterName
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
    /// STRUCT_SELCHAR do client 7.57: os 4 slots da tela de seleção (840 bytes), conferido no WYD.exe (0x4AE1BA).
    /// <code>
    /// 0x000 short SPX[4]          0x008 short SPY[4]         0x010 char  Name[4][16]
    /// 0x050 SCORE Score[4] (48)   0x110 ITEM  Equip[4][16]   0x310 short Guild[4]
    /// 0x318 int   Coin[4]         0x328 long  Exp[4]
    /// </code>
    /// </summary>
    public const int SelCharSize =
        CharactersPerAccount * 2 * 2              // SPX[4], SPY[4]
        + CharactersPerAccount * NameLength       // Name[4][16]
        + CharactersPerAccount * Score.Size       // Score[4]
        + CharactersPerAccount * MaxEquip * Item.Size // Equip[4][16]
        + CharactersPerAccount * 2                // Guild[4]
        + CharactersPerAccount * 4                // Coin[4]
        + CharactersPerAccount * 8;               // Exp[4]

    public const int MessagePanelSize = PacketHeader.Size + MessageLength;

    /// <summary>0x10A: cabeçalho, byte Keys[16], 4 bytes, SELCHAR, Cargo[128], int Coin e 28 bytes (conta).</summary>
    /// <remarks>Os tamanhos seguem a tabela que o WYD.exe usa para conferir cada pacote recebido.</remarks>
    public const int AccountLoginOkSize = 1928;

    /// <summary>0x110 / 0x112: cabeçalho, int (não lido), SELCHAR.</summary>
    public const int SelCharPacketSize = PacketHeader.Size + 4 + SelCharSize;
    public const int SignalSize = PacketHeader.Size + 4;
    public const int HeaderOnlySize = PacketHeader.Size;

    /// <summary>
    /// STRUCT_MOB do client 7.57: a ficha completa do personagem (1024 bytes), conferida no WYD.exe.
    /// <code>
    /// 0x000 char  Name[16]       0x010 byte  Clan          0x011 byte  Merchant     0x012 ushort Guild
    /// 0x014 byte  Class          0x015 byte  Rsv           0x016 ushort Quest       0x018 int   Coin
    /// 0x01C int   ?              0x020 long  Exp           0x028 short SPX          0x02A short SPY
    /// 0x02C SCORE BaseScore      0x05C SCORE CurrentScore  0x08C ITEM  Equip[16]    0x10C ITEM  Carry[64]
    /// 0x30C uint  LearnedSkill[2] 0x314 short ScoreBonus   0x316 short SpecialBonus 0x318 short SkillBonus
    /// 0x31A byte  Critical      0x31B byte  SaveMana      0x31C byte  SkillBar[4]  0x320 byte  GuildLevel
    /// 0x321 byte  Magic         0x322 byte  RegenHP       0x323 byte  RegenMP      0x324 byte  Resist[4]
    /// 0x328 (216 bytes que o servidor manda zerados)
    /// </code>
    /// </summary>
    public const int MobSize = 0x400;

    /// <summary>0x114: cabeçalho, short X, short Y, STRUCT_MOB, id do jogador, clima, barra de skills extra (e o resto zerado).</summary>
    public const int CharacterLoginOkSize = 1712;

    public const int CreateMobSize = 232;
    public const int RemoveMobSize = 16;
    public const int MoveSize = 52;
    public const int ChatSize = PacketHeader.Size + MessageLength;

    /// <summary>Skill vazia na barra (o client trata valores negativos como slot livre).</summary>
    public const byte EmptySkill = 0xFF;

    /// <summary>
    /// 0x101: mensagem na faixa do alto da tela e no chat. O client só mostra com o id do cabeçalho 0 (0x499438) e
    /// copia o texto com "%s" (não é formato de printf: "%" aparece como está).
    /// </summary>
    public static byte[] MessagePanel(string text, ushort id = 0) =>
        new PacketWriter(Opcodes.MessagePanel, MessagePanelSize, id)
            .WriteString(text, MessageLength)
            .ToArray();

    /// <summary>0xFDE: segunda senha aceita (o client libera a tela de personagens).</summary>
    public static byte[] SecondPasswordOk(ushort id) =>
        new PacketWriter(Opcodes.SecondPassword, SecondPasswordRequest.PacketSize, id).Skip(20).ToArray();

    /// <summary>Pacote só com o cabeçalho (os sinais de falha 0x119/0x11A/0x11B têm 12 bytes).</summary>
    public static byte[] HeaderOnly(ushort type, ushort id = 0) => new PacketWriter(type, HeaderOnlySize, id).ToArray();

    /// <summary>
    /// 0x36A: o personagem <paramref name="id"/> faz uma animação (o client em 0x532FDF lê o movimento em 0x0C e o
    /// parâmetro em 0x0E; o movimento 100 solta um efeito).
    /// </summary>
    public static byte[] Motion(ushort id, short motion, short parm) =>
        new PacketWriter(Opcodes.Motion, 20, id).WriteInt16(motion).WriteInt16(parm).Skip(4).ToArray();

    /// <summary>Animação de comemorar e de lamentar.</summary>
    public const short MotionCheer = 14, MotionSad = 15;

    /// <summary>Parâmetro 3 do 0x36A: o client cria o efeito de subir de nível em cima do personagem (0x4D3E70).</summary>
    public const short MotionLevelUpEffect = 3;

    /// <summary>Faixa do alto quando o personagem sobe de nível.</summary>
    public const string LevelUpText = "++++ LEVEL UP ++++";

    /// <summary>Subiu de nível: comemora com o efeito de LEVEL UP.</summary>
    public static byte[] LevelUp(ushort id) => Motion(id, MotionCheer, MotionLevelUpEffect);

    /// <summary>0x3B4: na seleção de personagens, o client (0x4A69DF) solta um efeito em cima do personagem do slot.</summary>
    public static byte[] SelCharEffect(ushort id, int slot) => new PacketWriter(Opcodes.SelCharEffect, 16, id).WriteInt32(slot).ToArray();

    /// <summary>
    /// 0x364: põe um jogador na tela de quem recebe. Layout do 7.57 (0x482E0E):
    /// <code>
    /// 0x0C short X, Y          0x10 ushort Id          0x12 char Name[12]
    /// 0x1E byte ChaosPoint     0x1F byte CurrentKill   0x20 ushort TotalKill
    /// 0x22 ushort Equip[16]    0x42 short Affect[32]   0x82 ushort Guild
    /// 0x84 (4 bytes)           0x88 SCORE Score        0xB8 ushort CreateType
    /// 0xBA byte Refino[16]     0xCA char Tab[26]       0xE4 (4 bytes)
    /// </code>
    /// Com a SCORE em 0x84 o client lia a vida errada (0) e mostrava os NPCs mortos.
    /// </summary>
    public static byte[] CreateMob(ushort mobId, Character c)
    {
        var (equip, anct) = VisualEquip(c.VisibleEquip);
        return CreateMob(mobId, c.Name, c.PosX, c.PosY, equip, c.Guild, c.CurrentScore, Affects.ToClient(c, DateTime.UtcNow), anct);
    }

    /// <summary>
    /// Slots cujo refino vai nos 4 bits de cima do índice (0x50BB00: do rosto, que brilha na transformação, até as duas
    /// armas).
    /// </summary>
    public const int FirstRefinedSlot = 0, LastRefinedSlot = 7;

    /// <summary>
    /// Equipamento como os outros jogadores o veem (0x364 em 0x22 e 0xBA, 0x36B em 0x0C e 0x2C; lidos em 0x50B6C4 e
    /// 0x50C7BB):
    /// <list type="bullet">
    /// <item>Equip[i] = índice | refino &lt;&lt; 12, nos slots 1 a 7 (o client tira o refino com sar 12).</item>
    /// <item>Anct[i], até o +9: o tipo do efeito do refino (43, ou 116 a 125 da tintura; 0 sem refino).</item>
    /// <item>Anct[i], do +10 em diante: cor nos 4 bits de baixo (tipo − 115; 0 sem cor) e gema + 1 nos de cima (o
    /// client soma 8 e usa como brilho, como faz no próprio personagem com 0x415535).</item>
    /// <item>Montaria (slot 14): índice | (nível ÷ 10) &lt;&lt; 12 (0x50BE8E), o mesmo que o client usa no próprio
    /// personagem (efeito 81, o nível, ÷ 10) para o tamanho e a armadura da montaria.</item>
    /// </list>
    /// </summary>
    public static (short[] Equip, byte[] Anct) VisualEquip(IReadOnlyList<Item> items)
    {
        var equip = new short[MaxEquip];
        var anct = new byte[MaxEquip];
        for (int i = 0; i < MaxEquip && i < items.Count; i++)
        {
            var item = items[i];
            equip[i] = item.Index;
            if (i == Stats.MountSlot && Stats.IsMount(item.Index) && item.Index < 0x1000)
            {
                equip[i] = (short)(item.Index | Math.Min(15, Stats.MountLevel(item) / 10) << 12);
                continue;
            }
            if (item.IsEmpty || i < FirstRefinedSlot || i > LastRefinedSlot || item.Index >= 0x1000) continue;
            int refine = Math.Clamp(Stats.Refine(item), 0, 15);
            equip[i] = (short)(item.Index | refine << 12);
            int slot = Stats.SancSlot(item);
            byte type = slot switch { 1 => item.Effect1, 2 => item.Effect2, 3 => item.Effect3, _ => 0 };
            if (refine < Ancient.MinRefine) anct[i] = type;
            else
            {
                int color = type is >= 116 and <= 125 ? type - 115 : 0;
                int gem = (Stats.RawSanc(item) - Ancient.RefinedBase) % 4 + 1;
                anct[i] = (byte)(gem << 4 | color);
            }
        }
        return (equip, anct);
    }

    /// <summary>
    /// CreateType 3: o client toca o efeito de invocação (luz, som e a animação de surgir, 0x484B97) em quem aparece.
    /// </summary>
    public const ushort CreateSummoned = 3;

    public static byte[] CreateMob(ushort mobId, string name, short x, short y, IEnumerable<short> equip, ushort guild, Score score,
        short[]? affects = null, byte[]? anct = null, ushort createType = 0)
    {
        var w = new PacketWriter(Opcodes.CreateMob, CreateMobSize, mobId);
        w.WriteInt16(x).WriteInt16(y);
        w.WriteUInt16(mobId);
        w.WriteString(name, 12);
        w.WriteByte(0).WriteByte(0).WriteUInt16(0);   // ChaosPoint, CurrentKill, TotalKill
        var slots = equip.Take(MaxEquip).ToArray();
        for (int i = 0; i < MaxEquip; i++) w.WriteInt16(i < slots.Length ? slots[i] : (short)0);
        WriteAffects(w, affects);                     // 0x42 Affect
        w.WriteUInt16(guild);
        w.Skip(4);                                    // 0x84
        score.Write(w);                               // 0x88 (vida lida em 0xA0 e vida máxima em 0x98, 0x483A18)
        w.WriteUInt16(createType);                    // CreateType: 0 aparece normalmente
        w.WriteBytes(anct ?? new byte[MaxEquip]);     // 0xBA tipo/cor/gema do refino de cada equipamento
        w.WriteString("", 26);                        // Tab
        w.Skip(4);
        return w.ToArray();
    }

    /// <summary>
    /// Movimento (0x366 andando, 0x36C parando), 52 bytes. No 7.57 (montado em 0x505B27, lido em 0x515A0A) o tipo
    /// e a velocidade trocaram de lugar e o destino foi para depois da rota:
    /// <code>
    /// 0x0C short X, Y       0x10 int Tipo       0x14 int Velocidade
    /// 0x18 char Rota[24]    0x30 short DestX, DestY
    /// </code>
    /// </summary>
    public static class MoveLayout
    {
        public const int Type = 0x10, Speed = 0x14, Route = 0x18, DestX = 0x30, DestY = 0x32;
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
            .WriteInt32(MoveWalk)
            .WriteInt32(speed)
            .WriteString(Route(x, y, destX, destY), MaxRoute)
            .WriteInt16(destX).WriteInt16(destY)
            .ToArray();
    }

    /// <summary>Tipo do 0x366: 0 (ou 2) anda pela rota; 1 aparece direto no destino com o efeito de teleporte.</summary>
    public const int MoveWalk = 0, MoveTeleport = 1;

    /// <summary>0x366 de teleporte: o client põe o personagem direto em (x, y).</summary>
    public static byte[] Teleport(ushort id, short x, short y) =>
        new PacketWriter(Opcodes.Move, MoveSize, id)
            .WriteInt16(x).WriteInt16(y)
            .WriteInt32(MoveTeleport)
            .WriteInt32(0)
            .Skip(MaxRoute)
            .WriteInt16(x).WriteInt16(y)
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
    /// Ataque (0x39D 1 alvo, 0x39E 2 alvos, 0x367 até 13), lido no WYD.exe 7.57 (montado em 0x41DCFA, lido em
    /// 0x48A5DD). O client manda o dano como -1 e o servidor devolve o mesmo pacote com o dano de cada alvo para todos
    /// que estão vendo (inclusive quem atacou).
    /// <code>
    /// 0x0C int Hold               0x10 int ManaGasta              0x18 long ExpDoAtacante
    /// 0x22 short X, Y do atacante 0x26 short X, Y do alvo         0x2A ushort Atacante
    /// 0x2E byte Movimento         0x34 int ManaAtual              0x38 short Skill
    /// 0x3C {int Alvo, int Dano} × 1, 2 ou 13, e um int no fim
    /// </code>
    /// </summary>
    public static class AttackLayout
    {
        public const int Hold = 0x0C;
        public const int Exp = 0x18;
        public const int PosX = 0x22, PosY = 0x24;
        public const int TargetX = 0x26, TargetY = 0x28;
        public const int Attacker = 0x2A;
        public const int Motion = 0x2E;
        public const int CurrentMp = 0x34;
        public const int Skill = 0x38;
        public const int Targets = 0x3C;
        public const int TargetSize = 8;
        public const int MaxTargets = 13;

        /// <summary>Tamanho do pacote com <paramref name="targets"/> alvos (72, 80 e 168 bytes).</summary>
        public static int SizeFor(int targets) => Targets + targets * TargetSize + 4;
    }

    /// <summary>
    /// 0x39D de um monstro batendo num jogador, como o ataque que o client manda: skill -1 (golpe normal) e o alvo
    /// com o dano. O movimento vai direto para a animação do atacante: os golpes normais são 4, 5 e 6 (no 7.54 o
    /// client usa 4 + contador de 0 a 2, 0x523A19); 0xFF não existe e o monstro bate parado.
    /// </summary>
    public static byte[] MobAttack(ushort attacker, short x, short y, ushort target, short targetX, short targetY, int damage,
        byte motion = FirstAttackMotion)
    {
        var w = new PacketWriter(Opcodes.AttackOne, AttackOneSize, attacker);
        w.Skip(AttackLayout.PosX - PacketHeader.Size);   // Hold, mana gasta, Exp: só valem para o atacante jogador
        w.WriteInt16(x).WriteInt16(y).WriteInt16(targetX).WriteInt16(targetY);
        w.WriteUInt16(attacker).Skip(2);
        w.WriteByte(motion).Skip(AttackLayout.CurrentMp - AttackLayout.Motion - 1);
        w.WriteInt32(-1);                                // mana atual (não muda)
        w.WriteInt16(-1).Skip(2);                        // skill: nenhuma
        w.WriteInt32(target).WriteInt32(damage);
        w.Skip(4);
        return w.ToArray();
    }

    public static readonly int AttackOneSize = AttackLayout.SizeFor(1);

    /// <summary>Movimentos de golpe normal do client: 4, 5 e 6.</summary>
    public const byte FirstAttackMotion = 4, AttackMotions = 3;

    /// <summary>
    /// 0x367 de uma skill do jogador em vários alvos (até 13), como o client manda; o dano é preenchido pelo
    /// servidor ao processar. Usado pelas auras (Trovão), que batem sozinhas.
    /// </summary>
    public static byte[] SkillHit(ushort attacker, short x, short y, short skill, IReadOnlyList<(ushort Id, short X, short Y)> targets)
    {
        int size = AttackLayout.SizeFor(AttackLayout.MaxTargets);
        var w = new PacketWriter(Opcodes.Attack, size, attacker);
        (ushort Id, short X, short Y) first = targets.Count > 0 ? targets[0] : (attacker, x, y);
        w.Skip(AttackLayout.PosX - PacketHeader.Size);
        w.WriteInt16(x).WriteInt16(y).WriteInt16(first.X).WriteInt16(first.Y);
        w.WriteUInt16(attacker).Skip(2);
        w.WriteByte(0xFF).Skip(AttackLayout.CurrentMp - AttackLayout.Motion - 1);
        w.WriteInt32(-1);
        w.WriteInt16(skill).Skip(2);
        foreach (var t in targets.Take(AttackLayout.MaxTargets)) w.WriteInt32(t.Id).WriteInt32(0);
        w.Skip(size - w.Position);
        return w.ToArray();
    }

    /// <summary>Experiência como o client 7.57 guarda: 64 bits, sem negativo.</summary>
    public static long ClientExp(long exp) => Math.Max(0, exp);

    public const int MobKillSize = 32;

    /// <summary>0x338: <paramref name="killed"/> morreu; se quem recebe é o matador, o client mostra a nova experiência.</summary>
    public static byte[] MobKill(ushort killed, ushort killer, long killerExp, int hold = 0) =>
        new PacketWriter(Opcodes.MobKill, MobKillSize, killed)
            .WriteInt32(hold).WriteUInt16(killed).WriteUInt16(killer).Skip(4).WriteInt64(ClientExp(killerExp))
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
    /// <code>
    /// 0x0C int Hold      0x10 long Exp     0x18 uint Skills[2]
    /// 0x20 short Pontos  0x22 Especiais    0x24 Skill            0x28 int Gold
    /// </code>
    /// </summary>
    public static byte[] UpdateEtc(ushort id, Character c) =>
        new PacketWriter(Opcodes.UpdateEtc, 48, id)
            .WriteInt32(0)
            .WriteInt64(ClientExp(c.Exp))
            .WriteUInt32(c.LearnedSkill).WriteUInt32(0)
            .WriteInt16(c.ScoreBonus).WriteInt16(c.SpecialBonus).WriteInt16(c.SkillBonus).Skip(2)
            .WriteInt32(c.Coin)
            .Skip(4)
            .ToArray();

    public const int UpdateScoreSize = 144;

    /// <summary>
    /// 0x336: atributos do jogador (nível, vida, mana...), por exemplo depois de subir de nível. No 7.57
    /// (0x5113CC): SCORE em 0x0C, Critical e SaveMana em 0x3C, short Affect[32] em 0x3E, Guild em 0x7E, GuildLevel em
    /// 0x80.
    /// </summary>
    public static byte[] UpdateScore(ushort id, Character c) =>
        UpdateScore(id, c.CurrentScore, Affects.ToClient(c, DateTime.UtcNow), c.Critical, c.SaveMana, c.Guild);

    /// <summary>0x336 de um monstro: quem o vê passa a desenhar os efeitos de skill nele.</summary>
    public static byte[] UpdateScore(Mob mob) =>
        UpdateScore(mob.Id, mob.Score, Affects.ToClient(mob.Affects, DateTime.UtcNow), 0, 0, mob.Template.Guild);

    private static byte[] UpdateScore(ushort id, Score score, short[] affects, byte critical, byte saveMana, ushort guild)
    {
        var w = new PacketWriter(Opcodes.UpdateScore, UpdateScoreSize, id);
        score.Write(w);                               // 0x0C
        w.WriteByte(critical).WriteByte(saveMana);    // 0x3C Critical, SaveMana
        WriteAffects(w, affects);                     // 0x3E Affect: ícones dos buffs
        w.WriteUInt16(guild).WriteUInt16(0);          // 0x7E Guild, GuildLevel
        w.Skip(UpdateScoreSize - w.Position);         // 0x82 Resist e o resto
        return w.ToArray();
    }

    /// <summary>
    /// 0x3B9: os buffs do próprio jogador, que o client 7.57 (0x5110E5) copia para a lista que desenha no canto da
    /// tela com o tempo. 32 × {byte Tipo, byte Valor, short Nível, int Tempo} em 0x0C, tempo em unidades de 8 s; Tipo 0
    /// = sem ícone. O client lê sempre os 32 (o 7.54 lia 16): com o pacote curto ele desenhava lixo do buffer como
    /// ícones.
    /// </summary>
    public static byte[] AffectList(ushort id, Character c)
    {
        var w = new PacketWriter(Opcodes.AffectList, AffectListSize, id);
        var now = DateTime.UtcNow;
        var active = c.Affects.Where(a => a.Expires > now).Take(ClientAffects).ToList();
        for (int i = 0; i < ClientAffects; i++)
        {
            if (i >= active.Count) { w.Skip(8); continue; }
            var a = active[i];
            int units = Math.Clamp((int)Math.Ceiling((a.Expires - now).TotalSeconds / Affects.SecondsPerUnit), 1, int.MaxValue);
            w.WriteByte((byte)a.Type).WriteByte((byte)Math.Clamp(a.Amount, 0, 255)).WriteInt16((short)a.Skill).WriteInt32(units);
        }
        return w.ToArray();
    }

    /// <summary>Efeitos que cabem no 0x364, no 0x336 e no 0x3B9 do 7.57 (eram 16 no 7.54).</summary>
    public const int ClientAffects = 32;

    public const int AffectListSize = PacketHeader.Size + ClientAffects * 8;

    /// <summary>32 shorts de efeito: (tipo &lt;&lt; 8) | unidades de 8 s que faltam (ver <see cref="Affects"/>).</summary>
    private static void WriteAffects(PacketWriter w, short[]? affects)
    {
        for (int i = 0; i < ClientAffects; i++)
            w.WriteInt16(affects is not null && i < affects.Length ? affects[i] : (short)0);
    }

    /// <summary>0x3AF: novo total de gold do jogador.</summary>
    /// <summary>0x3A7: fecha as janelas de combinação do client.</summary>
    public static byte[] CombineDone(ushort id) => new PacketWriter(Opcodes.CombineDone, PacketHeader.Size, id).ToArray();

    public static byte[] UpdateCoin(ushort id, int coin) =>
        new PacketWriter(Opcodes.UpdateCoin, 16, id).WriteInt32(coin).ToArray();

    /// <summary>
    /// 0x181: HP e MP atuais do jogador (28 bytes no 7.57): int Hp em 0x0C, int Mp em 0x10 e mais dois ints (HP e MP
    /// pedidos, que o servidor manda iguais aos atuais).
    /// </summary>
    public static byte[] UpdateHpMp(ushort id, int hp, int mp) =>
        new PacketWriter(Opcodes.UpdateHpMp, 28, id).WriteInt32(hp).WriteInt32(mp).WriteInt32(hp).WriteInt32(mp).ToArray();

    // ------------------------------------------------------------------ grupo

    public const int PartyInviteSize = 44, PartyAddSize = 40, PartyRemoveSize = 16;

    /// <summary>Vida para a janela do grupo, que lê shorts: acima de 32767 as duas são reduzidas na mesma proporção.</summary>
    public static (short Max, short Hp) PartyHp(int max, int hp)
    {
        max = Math.Max(1, max);
        hp = Math.Clamp(hp, 0, max);
        if (max <= short.MaxValue) return ((short)max, (short)hp);
        return (short.MaxValue, (short)((long)hp * short.MaxValue / max));
    }

    /// <summary>0x37F para o convidado: quem chama (o líder do grupo) e o id dele, que volta no 0x3AB.</summary>
    public static byte[] PartyInvite(ushort leaderId, Character leader, ushort target)
    {
        var (max, hp) = PartyHp(leader.MaxHp, leader.BaseScore.Hp);
        return new PacketWriter(Opcodes.PartyInvite, PartyInviteSize, leaderId)
            .WriteByte((byte)leader.Class).WriteByte(0)
            .WriteInt16((short)Math.Min(short.MaxValue, leader.BaseScore.Level)).WriteInt16(max).WriteInt16(hp)
            .WriteUInt16(leaderId).WriteString(leader.Name, NameLength).Skip(2)
            .WriteInt32(target)
            .ToArray();
    }

    /// <summary>0x37D: um membro na janela do grupo (também a criatura da Evocação, com o id dela).</summary>
    public static byte[] PartyAdd(ushort id, string name, byte cls, bool leader, int level, int maxHp, int hp)
    {
        var (max, cur) = PartyHp(maxHp, hp);
        return new PacketWriter(Opcodes.PartyAdd, PartyAddSize, id)
            .WriteByte(cls).WriteByte(leader ? (byte)0 : (byte)1)
            .WriteInt16((short)Math.Clamp(level, 0, short.MaxValue)).WriteInt16(max).WriteInt16(cur)
            .WriteUInt16(id).WriteString(name, NameLength).Skip(2)
            .ToArray();
    }

    public static byte[] PartyAdd(ushort id, Character c, bool leader) =>
        PartyAdd(id, c.Name, (byte)c.Class, leader, c.BaseScore.Level, c.MaxHp, c.BaseScore.Hp);

    /// <summary>0x37E: tira o id da janela do grupo; 0 apaga o grupo inteiro (saiu ou o grupo acabou).</summary>
    public static byte[] PartyRemove(ushort id) =>
        new PacketWriter(Opcodes.PartyRemove, PartyRemoveSize, id).WriteInt32(id).ToArray();

    /// <summary>0x36B: equipamentos visíveis de um jogador, para quem está vendo ele.</summary>
    public static byte[] UpdateEquip(ushort id, IReadOnlyList<Item> items)
    {
        var (equip, anct) = VisualEquip(items);
        var w = new PacketWriter(Opcodes.UpdateEquip, 60, id);
        foreach (var index in equip) w.WriteInt16(index);
        w.WriteBytes(anct);                           // 0x2C: como o 0xBA do 0x364 (ver VisualEquip)
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

    /// <summary>
    /// 0x10A: login aceito, com a lista de personagens e o baú da conta. O client (0x485B3F) vai para a tela de
    /// seleção. As chaves de 0x0C (o client guarda no módulo de rede) vão zeradas, como no 7.54.
    /// </summary>
    public static byte[] AccountLoginOk(ushort id, Account account, IReadOnlyList<Character> characters)
    {
        var w = new PacketWriter(Opcodes.AccountLoginOk, AccountLoginOkSize, id);
        w.Skip(16 + 4);                               // 0x00C Keys[16], 0x01C
        WriteSelChar(w, characters);                  // 0x020
        foreach (var item in account.Cargo) item.Write(w); // 0x368
        w.WriteInt32(account.CargoCoin);              // 0x768
        w.WriteString(account.Username, AccountNameLength); // 0x76C
        w.Skip(AccountLoginOkSize - w.Position);      // 0x77C
        return w.ToArray();
    }

    /// <summary>
    /// 0x114: entra no mundo com o personagem escolhido. O client (0x485C1B) copia a ficha (STRUCT_MOB), usa X/Y
    /// como posição, guarda o id de 0x412 como o id do próprio jogador e troca para a cena do mapa.
    /// </summary>
    public static byte[] CharacterLoginOk(ushort id, Character character)
    {
        var w = new PacketWriter(Opcodes.CharacterLoginOk, CharacterLoginOkSize, id);
        w.WriteInt16(character.PosX);                 // 0x00C
        w.WriteInt16(character.PosY);                 // 0x00E
        WriteMob(w, character);                       // 0x010
        w.Skip(2);                                    // 0x410
        w.WriteUInt16(id);                            // 0x412: id do jogador no servidor
        w.WriteUInt16(0);                             // 0x414: clima
        for (int i = 0; i < 16; i++)                  // 0x416: resto da barra de skills (o client junta com o SkillBar[4])
            w.WriteByte(character.SkillBar[4 + i]);
        w.Skip(2);                                    // 0x426
        w.WriteInt32(0);                              // 0x428: Hold
        w.Skip(CharacterLoginOkSize - w.Position);    // 0x42C: não lido na entrada no mundo
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
        w.WriteInt32(0);                              // 0x01C
        w.WriteInt64(ClientExp(c.Exp));
        w.WriteInt16(c.PosX);
        w.WriteInt16(c.PosY);
        c.BaseScore.Write(w);
        c.CurrentScore.Write(w);                      // CurrentScore: com a velocidade das botas e da montaria
        foreach (var item in c.VisibleEquip) item.Write(w);
        foreach (var item in c.Inventory) item.Write(w);
        w.WriteUInt32(c.LearnedSkill).WriteUInt32(0);  // LearnedSkill[2]
        w.WriteInt16(c.ScoreBonus).WriteInt16(c.SpecialBonus).WriteInt16(c.SkillBonus);
        w.WriteByte(c.Critical).WriteByte(c.SaveMana); // Critical, SaveMana
        for (int i = 0; i < 4; i++) w.WriteByte(c.SkillBar[i]);   // SkillBar[4]: começo da barra de skills
        w.WriteByte(0).WriteByte(0);                  // GuildLevel, Magic
        w.WriteByte(0).WriteByte(0);                  // RegenHP, RegenMP
        w.Skip(4);                                    // Resist[4]
        w.Skip(MobSize - (w.Position - start));       // 0x328: campos novos do 7.57

        if (w.Position - start != MobSize)
            throw new InvalidOperationException($"STRUCT_MOB com {w.Position - start} bytes (esperado {MobSize}).");
    }

    /// <summary>0x110 / 0x112: lista de personagens atualizada após criar/apagar.</summary>
    public static byte[] SelCharList(ushort type, ushort id, IReadOnlyList<Character> characters)
    {
        var w = new PacketWriter(type, SelCharPacketSize, id);
        w.WriteInt32(0);                              // 0x0C: não lido
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
        foreach (var c in slots) w.WriteInt64(ClientExp(c?.Exp ?? 0));
    }
}
