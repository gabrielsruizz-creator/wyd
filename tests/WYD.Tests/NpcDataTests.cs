using System.Buffers.Binary;
using System.Text;
using WYD.Server.Data;

namespace WYD.Tests;

public class NpcDataTests
{
    /// <summary>Monta um arquivo de npc de 816 bytes como os do servidor 7.56 (com os 0xCC de sobra de memória).</summary>
    internal static byte[] NpcFile(string name, short model, int level, int hp, byte merchant = 0, byte speed = 2)
    {
        var d = new byte[NpcTemplate.FileSize];
        Encoding.Latin1.GetBytes(name).CopyTo(d, 0);
        d[0x10] = 1;                      // Clan
        d[0x11] = merchant;
        d[0x14] = 1;                      // Class
        d[0x15] = 0xCC;
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0x1C), 200);   // Coin
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(0x20), 280);   // Exp
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0x2C), level);
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0x30), 80);    // Ac
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0x34), 34);    // Damage
        d[0x39] = speed;                  // AttackRun
        d[0x3A] = 0xCC; d[0x3B] = 0xCC;
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0x3C), hp);    // MaxHp
        BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(0x44), hp);    // Hp
        BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(0x4C), 4);     // Str
        BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(0x8C), model); // Equip[0]
        BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(0x10C), 1818); // Carry[0]
        return d;
    }

    [Fact]
    public void Parses_npc_file()
    {
        var npc = NpcTemplate.Parse("Lobo", NpcFile("Lobo", 299, 18, 200));
        Assert.Equal("Lobo", npc.Name);
        Assert.Equal(299, npc.Model);
        Assert.Equal(18, npc.Score.Level);
        Assert.Equal(80, npc.Score.Ac);
        Assert.Equal(34, npc.Score.Damage);
        Assert.Equal(200, npc.Score.MaxHp);
        Assert.Equal(4, npc.Score.Str);
        Assert.Equal(2, npc.Score.AttackRun);
        Assert.Equal(200, npc.Coin);
        Assert.Equal(280, npc.Exp);
        Assert.Equal(1818, npc.Carry[0].Index);
    }

    [Fact]
    public void Rejects_wrong_size()
    {
        Assert.Throws<InvalidDataException>(() => NpcTemplate.Parse("x", new byte[700]));
    }

    [Fact]
    public void Parses_npcgener()
    {
        const string text = "﻿// ********************************************\r\n" +
            "#\t[   0]\r\n\tMinuteGenerate:\t-1\r\n\tMaxNumMob:\t100\r\n\tMinGroup:\t4\r\n\tMaxGroup:\t7\r\n" +
            "\tLeader:\t\tCiclope_Forte\r\n\tFollower:\tArq_Ciclope\r\n\tRouteType:\t2\r\n\tFormation:\t0\r\n" +
            "\tStartX:\t\t2640\r\n\tStartY:\t\t1726\r\n\tStartRange:\t5\r\n\tStartWait:\t10\r\n" +
            "\tDestX:\t\t2650\r\n\tDestY:\t\t1730\r\n\tDestRange:\t10\r\n\tDestWait:\t10\r\n\r\n\r\n" +
            "// comentário com bytes estranhos ï¿½Ý·\r\n" +
            "#\t[   1]\r\n\tMaxNumMob:\t1\r\n\tLeader:\t\tZakum_Lord\r\n\tStartX:\t\t2235\r\n\tStartY:\t\t1186\r\n";

        var rules = NpcGener.Parse(text);
        Assert.Equal(2, rules.Count);

        var r = rules[0];
        Assert.Equal(("Ciclope_Forte", "Arq_Ciclope"), (r.Leader, r.Follower));
        Assert.Equal((100, 4, 7, -1), (r.MaxNumMob, r.MinGroup, r.MaxGroup, r.MinuteGenerate));
        Assert.Equal((2640, 1726, 5, 10), ((int)r.StartX, (int)r.StartY, r.StartRange, r.StartWait));
        Assert.Equal((2650, 1730, 10), ((int)r.DestX, (int)r.DestY, r.DestRange));

        // Sem Follower nem Dest: usa o líder e a posição inicial.
        Assert.Equal("Zakum_Lord", rules[1].Follower);
        Assert.Equal((2235, 1186), ((int)rules[1].DestX, (int)rules[1].DestY));
    }
}
