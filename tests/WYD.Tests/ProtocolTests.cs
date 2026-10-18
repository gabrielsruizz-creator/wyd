using System.Buffers.Binary;
using WYD.Server.Game;
using WYD.Server.Network;
using WYD.Server.Protocol;
using WYD.Server.Security;

namespace WYD.Tests;

public class ProtocolTests
{
    internal static KeyTable RandomKeyTable(int seed = 1234)
    {
        var bytes = new byte[KeyTable.Length];
        new Random(seed).NextBytes(bytes);
        return new KeyTable(bytes);
    }

    [Fact]
    public void Cipher_round_trip_restores_packet_and_checksum_matches()
    {
        var table = RandomKeyTable();
        var original = ServerPackets.MessagePanel("Bem-vindo ao WYD!", id: 7);

        for (int keyWord = 0; keyWord < 256; keyWord++)
        {
            var packet = (byte[])original.Clone();
            PacketCipher.Encrypt(packet, table, (byte)keyWord);

            Assert.Equal(original.AsSpan(0, 2).ToArray(), packet.AsSpan(0, 2).ToArray()); // Size em claro
            Assert.Equal(keyWord, packet[PacketHeader.KeyWordOffset]);
            Assert.NotEqual(original, packet);

            Assert.True(PacketCipher.Decrypt(packet, table));
            Assert.Equal(original.AsSpan(4).ToArray(), packet.AsSpan(4).ToArray());
        }
    }

    [Fact]
    public void Decrypt_detects_wrong_key_table()
    {
        var packet = ServerPackets.MessagePanel("teste");
        PacketCipher.Encrypt(packet, RandomKeyTable(1), 42);
        Assert.False(PacketCipher.Decrypt(packet, RandomKeyTable(2)));
    }

    [Fact]
    public void Framer_requires_init_code_and_splits_packets()
    {
        var a = ServerPackets.MessagePanel("um");
        var b = ServerPackets.Signal(0x123, 99);
        var stream = new byte[4 + a.Length + b.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(stream, PacketHeader.InitCode);
        a.CopyTo(stream, 4);
        b.CopyTo(stream, 4 + a.Length);

        var framer = new PacketFramer();
        // Entrega o fluxo aos pedaços para simular a fragmentação do TCP.
        var received = new List<byte[]>();
        for (int i = 0; i < stream.Length; i += 7)
        {
            framer.Append(stream.AsSpan(i, Math.Min(7, stream.Length - i)));
            while (framer.TryRead() is { } p) received.Add(p);
        }

        Assert.Equal(2, received.Count);
        Assert.Equal(a, received[0]);
        Assert.Equal(b, received[1]);
    }

    [Fact]
    public void Framer_rejects_wrong_init_code()
    {
        var framer = new PacketFramer();
        framer.Append([1, 2, 3, 4, 5, 6]);
        Assert.Throws<ProtocolException>(() => framer.TryRead());
    }

    [Fact]
    public void Framer_rejects_invalid_size()
    {
        var framer = new PacketFramer();
        var data = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(data, PacketHeader.InitCode);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 3);
        framer.Append(data);
        Assert.Throws<ProtocolException>(() => framer.TryRead());
    }

    [Fact]
    public void Packet_layouts_have_expected_sizes()
    {
        // Tamanhos da tabela que o WYD.exe 7.57 usa para conferir cada pacote recebido.
        Assert.Equal(108, ServerPackets.MessagePanelSize);
        Assert.Equal(0x348, ServerPackets.SelCharSize);
        Assert.Equal(856, ServerPackets.SelCharPacketSize);
        Assert.Equal(1928, ServerPackets.AccountLoginOkSize);
        Assert.Equal(48, Score.Size);
        Assert.Equal(72, ServerPackets.AttackOneSize);
        Assert.Equal(80, ServerPackets.AttackLayout.SizeFor(2));
        Assert.Equal(168, ServerPackets.AttackLayout.SizeFor(13));

        var account = new Account { Username = "teste" };
        var character = ClassTemplates.Create(1, 2, "Guerreiro", CharacterClass.TransKnight, 2100, 2100);
        Assert.Equal(1928, ServerPackets.AccountLoginOk(1, account, [character]).Length);
        Assert.Equal(856, ServerPackets.SelCharList(Opcodes.CreateCharacterOk, 1, [character]).Length);
        Assert.Equal(232, ServerPackets.CreateMob(1, character).Length);
        Assert.Equal(144, ServerPackets.UpdateScore(1, character).Length);
        Assert.Equal(48, ServerPackets.UpdateEtc(1, character).Length);
        Assert.Equal(32, ServerPackets.MobKill(1000, 1, 5).Length);
        Assert.Equal(28, ServerPackets.UpdateHpMp(1, 10, 10).Length);
    }

    [Fact]
    public void SelChar_places_character_in_its_slot()
    {
        var character = ClassTemplates.Create(1, 2, "Guerreiro", CharacterClass.Foema, 2100, 2101);
        var packet = ServerPackets.SelCharList(Opcodes.CreateCharacterOk, 1, [character]);
        var r = new PacketReader(packet);
        r.Skip(4);                                   // int em 0x0C

        var spx = Enumerable.Range(0, 4).Select(_ => r.ReadInt16()).ToArray();
        var spy = Enumerable.Range(0, 4).Select(_ => r.ReadInt16()).ToArray();
        var names = Enumerable.Range(0, 4).Select(_ => r.ReadString(16)).ToArray();

        Assert.Equal(new short[] { 0, 0, 2100, 0 }, spx);
        Assert.Equal(new short[] { 0, 0, 2101, 0 }, spy);
        Assert.Equal(new[] { "", "", "Guerreiro", "" }, names);

        r.Skip(4 * Score.Size + 2 * 16 * Item.Size); // Score[4] e Equip dos slots 0 e 1
        Assert.Equal(11, r.ReadInt16()); // Equip[0] do slot 2 = rosto da Foema
    }

    /// <summary>Offsets que o WYD.exe 7.57 lê da lista de personagens e do pacote 0x10A.</summary>
    [Fact]
    public void Login_reply_uses_offsets_read_by_the_client()
    {
        var account = new Account { Username = "jogador", CargoCoin = 777 };
        account.Cargo[0] = new Item(4011);
        var c = ClassTemplates.Create(1, 2, "Arqueira", CharacterClass.Huntress, 2100, 2101);
        c.Guild = 55;
        c.Coin = 1234;
        c.Exp = 5_000_000_000;
        c.BaseScore.Level = 9;
        c.BaseScore.Hp = 75;

        var p = ServerPackets.AccountLoginOk(1, account, [c]);
        Assert.Equal(Opcodes.AccountLoginOk, PacketHeader.ReadType(p));
        const int sel = 0x20, slot = 2;
        short I16(int o) => BitConverter.ToInt16(p, o);
        int I32(int o) => BitConverter.ToInt32(p, o);

        Assert.Equal(2100, I16(sel + 0x000 + slot * 2));                 // SPX
        Assert.Equal(2101, I16(sel + 0x008 + slot * 2));                 // SPY
        Assert.Equal((byte)'A', p[sel + 0x010 + slot * 16]);             // Name
        Assert.Equal(9, I32(sel + 0x050 + slot * 48));                   // Score.Level
        Assert.Equal(75, I32(sel + 0x050 + slot * 48 + 0x18));           // Score.Hp
        Assert.Equal(31, I16(sel + 0x110 + slot * 0x80));                // Equip[0] (rosto da HT)
        Assert.Equal(55, I16(sel + 0x310 + slot * 2));                   // Guild
        Assert.Equal(1234, I32(sel + 0x318 + slot * 4));                 // Coin
        Assert.Equal(5_000_000_000, BitConverter.ToInt64(p, sel + 0x328 + slot * 8)); // Exp
        Assert.Equal(4011, I16(0x368));                                  // Cargo[0]
        Assert.Equal(777, I32(0x768));                                   // gold do baú
    }

    /// <summary>Offsets que o WYD.exe 7.57 lê do pacote 0x114 e da STRUCT_MOB.</summary>
    [Fact]
    public void Enter_world_uses_offsets_read_by_the_client()
    {
        var c = ClassTemplates.Create(1, 0, "Guerreiro", CharacterClass.BeastMaster, 2100, 2101);
        c.Coin = 500;
        c.Exp = 1000;
        c.Inventory[0] = new Item(4010);

        var p = ServerPackets.CharacterLoginOk(7, c);
        Assert.Equal(1712, p.Length);
        Assert.Equal(Opcodes.CharacterLoginOk, PacketHeader.ReadType(p));
        short I16(int o) => BitConverter.ToInt16(p, o);
        int I32(int o) => BitConverter.ToInt32(p, o);
        const int mob = 0x10;

        Assert.Equal(2100, I16(0x0C));                       // X
        Assert.Equal(2101, I16(0x0E));                       // Y
        Assert.Equal((byte)'G', p[mob + 0x000]);             // Name
        Assert.Equal((byte)CharacterClass.BeastMaster, p[mob + 0x014]); // Class
        Assert.Equal(500, I32(mob + 0x018));                 // Coin
        Assert.Equal(1000, BitConverter.ToInt64(p, mob + 0x020)); // Exp
        Assert.Equal(2100, I16(mob + 0x028));                // SPX
        Assert.Equal(c.BaseScore.Hp, I32(mob + 0x02C + 0x18)); // BaseScore.Hp
        Assert.Equal(c.BaseScore.Hp, I32(mob + 0x05C + 0x18)); // CurrentScore.Hp
        Assert.Equal(21, I16(mob + 0x08C));                  // Equip[0]: rosto da BM
        Assert.Equal(4010, I16(mob + 0x10C));                // Carry[0]
        Assert.Equal(0xFF, p[mob + 0x31C]);                  // SkillBar vazia
        Assert.Equal(7, BitConverter.ToUInt16(p, 0x412));    // id do jogador
        Assert.Equal(0xFF, p[0x416]);                        // segunda barra de skills vazia
    }

    [Theory]
    [InlineData(2096, 2094, 2097, 2099, "98888")]   // capturado do client 7.54
    [InlineData(2096, 2096, 2096, 2093, "222")]     // capturado do client 7.54
    [InlineData(10, 10, 7, 12, "774")]
    [InlineData(5, 5, 5, 5, "")]
    public void Route_uses_numpad_directions(int x, int y, int dx, int dy, string expected)
    {
        Assert.Equal(expected, ServerPackets.Route(x, y, dx, dy));
    }

    [Fact]
    public void Route_is_limited_to_23_steps()
    {
        Assert.Equal(23, ServerPackets.Route(0, 0, 100, 0).Length);
    }

    [Fact]
    public void Damage_is_at_least_one_and_varies_around_base()
    {
        var attacker = new Score { Damage = 25 };             // a ficha atual já traz FOR, DES e nível no dano
        var weakTarget = new Score { Ac = 0 };
        var rng = new Random(1);
        var values = Enumerable.Range(0, 500).Select(_ => WYD.Server.Game.Combat.Damage(attacker, weakTarget, rng)).ToList();
        Assert.InRange(values.Min(), 20, 25);
        Assert.InRange(values.Max(), 25, 30);

        var tank = new Score { Ac = 1000 };
        Assert.Equal(1, WYD.Server.Game.Combat.Damage(attacker, tank, rng));
    }

    [Fact]
    public void Account_login_request_uses_the_757_layout()
    {
        // Como o WYD.exe 7.57 monta o 0x20D (0x48587E): senha, conta, 52 bytes, versão 758 em 0x5C.
        var packet = Convert.FromHexString(
            "74000D420D0200008B140000" +                     // cabeçalho: 116 bytes, opcode 0x20D
            "73656E6861000000" + "00000000" +                // senha[12] = "senha"
            "6A6F6761646F720000000000" + "00000000" +        // conta[16] = "jogador"
            new string('0', 52 * 2) +                        // 52 bytes
            "F6020000" +                                     // versão 758
            "01000000" +                                     // DBNeedSave
            "0102030405060708090A0B0C0D0E0F10");             // AdapterName
        Assert.Equal(AccountLoginRequest.PacketSize, packet.Length);

        var request = AccountLoginRequest.Read(new PacketReader(packet));
        Assert.Equal("jogador", request.Username);
        Assert.Equal("senha", request.Password);
        Assert.Equal(758, request.ClientVersion);

        var rebuilt = new AccountLoginRequest("jogador", "senha", 758).Write();
        Assert.Equal(packet.AsSpan(12, 0x64 - 12).ToArray(), rebuilt.AsSpan(12, 0x64 - 12).ToArray()); // até o AdapterName
    }

    [Fact]
    public void Writer_rejects_incomplete_layout()
    {
        var w = new PacketWriter(0x999, 20);
        w.WriteInt32(1);
        Assert.Throws<InvalidOperationException>(() => w.ToArray());
    }

    [Fact]
    public void Item_blob_round_trip()
    {
        var items = new Item[4];
        items[1] = new Item(1101) { Effect1 = 43, Value1 = 9 };
        var restored = Item.FromBlob(Item.ToBlob(items), 4);
        Assert.Equal(items, restored);
        Assert.Equal(new Item[4], Item.FromBlob(null, 4));
    }

    [Fact]
    public void Password_hash_verifies()
    {
        var hash = PasswordHasher.Hash("minhasenha");
        Assert.True(PasswordHasher.Verify("minhasenha", hash));
        Assert.False(PasswordHasher.Verify("outrasenha", hash));
        Assert.False(PasswordHasher.Verify("minhasenha", "lixo"));
    }

    [Fact]
    public void Score_fields_are_ints_and_life_goes_up_to_the_client_maximum()
    {
        var score = new Score { Level = 399, Ac = 40000, Damage = 50000, Merchant = 1, AttackRun = 2,
            MaxHp = 100000, MaxMp = 50000, Hp = 90000, Mp = 70000, Str = 1000, Con = 900, Special1 = 7 };
        var w = new PacketWriter(Opcodes.UpdateScore, PacketHeader.Size + Score.Size, 1);
        score.Write(w);
        var p = w.ToArray().AsSpan(PacketHeader.Size);
        Assert.Equal(399, BinaryPrimitives.ReadInt32LittleEndian(p));
        Assert.Equal(40000, BinaryPrimitives.ReadInt32LittleEndian(p[4..]));
        Assert.Equal(50000, BinaryPrimitives.ReadInt32LittleEndian(p[8..]));
        Assert.Equal(1, p[12]);
        Assert.Equal(2, p[13]);
        Assert.Equal(100000, BinaryPrimitives.ReadInt32LittleEndian(p[16..]));
        Assert.Equal(90000, BinaryPrimitives.ReadInt32LittleEndian(p[24..]));
        Assert.Equal(70000, BinaryPrimitives.ReadInt32LittleEndian(p[28..]));
        Assert.Equal(1000, BinaryPrimitives.ReadInt16LittleEndian(p[32..]));
        Assert.Equal(900, BinaryPrimitives.ReadInt16LittleEndian(p[38..]));
        Assert.Equal(7, BinaryPrimitives.ReadInt16LittleEndian(p[40..]));

        var hpMp = ServerPackets.UpdateHpMp(1, 95000, 40000).AsSpan(PacketHeader.Size);
        Assert.Equal(95000, BinaryPrimitives.ReadInt32LittleEndian(hpMp));
        Assert.Equal(40000, BinaryPrimitives.ReadInt32LittleEndian(hpMp[4..]));
    }

    [Fact]
    public void Monster_attack_uses_the_757_layout()
    {
        var p = ServerPackets.MobAttack(1500, 2100, 2101, 7, 2102, 2103, 40000, 5);
        Assert.Equal(Opcodes.AttackOne, PacketHeader.ReadType(p));
        Assert.Equal(2100, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(0x22)));
        Assert.Equal(2103, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(0x28)));
        Assert.Equal(1500, BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(0x2A)));   // atacante
        Assert.Equal(5, p[0x2E]);                                                        // movimento
        Assert.Equal(-1, BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(0x38)));        // skill
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0x3C)));         // alvo
        Assert.Equal(40000, BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0x40)));     // dano
    }
}
