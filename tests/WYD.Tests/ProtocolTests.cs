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
        Assert.Equal(140, ServerPackets.MessagePanelSize);
        Assert.Equal(840, ServerPackets.SelCharSize);
        Assert.Equal(852, ServerPackets.SelCharPacketSize);
        Assert.Equal(1928, ServerPackets.AccountLoginOkSize);

        var account = new Account { Username = "teste" };
        var character = ClassTemplates.Create(1, 2, "Guerreiro", CharacterClass.TransKnight, 2100, 2100);
        Assert.Equal(1928, ServerPackets.AccountLoginOk(1, account, [character]).Length);
        Assert.Equal(852, ServerPackets.SelCharList(Opcodes.CreateCharacterOk, 1, [character]).Length);
    }

    [Fact]
    public void SelChar_places_character_in_its_slot()
    {
        var character = ClassTemplates.Create(1, 2, "Guerreiro", CharacterClass.Foema, 2100, 2101);
        var packet = ServerPackets.SelCharList(Opcodes.CreateCharacterOk, 1, [character]);
        var r = new PacketReader(packet);

        var spx = Enumerable.Range(0, 4).Select(_ => r.ReadInt16()).ToArray();
        var spy = Enumerable.Range(0, 4).Select(_ => r.ReadInt16()).ToArray();
        var names = Enumerable.Range(0, 4).Select(_ => r.ReadString(16)).ToArray();

        Assert.Equal(new short[] { 0, 0, 2100, 0 }, spx);
        Assert.Equal(new short[] { 0, 0, 2101, 0 }, spy);
        Assert.Equal(new[] { "", "", "Guerreiro", "" }, names);

        r.Skip(4 * Score.Size + 2 * 16 * Item.Size); // Score[4] e Equip dos slots 0 e 1
        Assert.Equal(11, r.ReadInt16()); // Equip[0] do slot 2 = rosto da Foema
    }

    [Fact]
    public void Account_login_request_is_parsed()
    {
        var w = new PacketWriter(Opcodes.AccountLogin, AccountLoginRequest.PacketSize);
        w.WriteString("senha123", 12).WriteString("jogador", 16).Skip(52).WriteInt32(756).Skip(4 + 16);
        var request = AccountLoginRequest.Read(new PacketReader(w.ToArray()));

        Assert.Equal("jogador", request.Username);
        Assert.Equal("senha123", request.Password);
        Assert.Equal(756, request.ClientVersion);
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
}
