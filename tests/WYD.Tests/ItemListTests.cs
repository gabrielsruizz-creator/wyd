using System.Buffers.Binary;
using System.Text;
using WYD.Server.Data;
using WYD.Server.Game;
using WYD.Server.Protocol;

namespace WYD.Tests;

public class ItemListTests
{
    private static byte[] PlainList()
    {
        var plain = new byte[ItemList.DataSize];
        void Put(int index, string name, short mesh, short level, short pos, int price, short effect, short value)
        {
            var d = plain.AsSpan(index * ItemDefinition.Size, ItemDefinition.Size);
            Encoding.Latin1.GetBytes(name).CopyTo(d);
            BinaryPrimitives.WriteInt16LittleEndian(d[64..], mesh);
            BinaryPrimitives.WriteInt16LittleEndian(d[70..], level);
            BinaryPrimitives.WriteInt16LittleEndian(d[80..], effect);
            BinaryPrimitives.WriteInt16LittleEndian(d[82..], value);
            BinaryPrimitives.WriteInt32LittleEndian(d[128..], price);
            BinaryPrimitives.WriteInt16LittleEndian(d[134..], pos);
        }
        Put(1, "Rosto TransKnight", 1, 0, 1, 0, 0, 0);
        Put(905, "Espada Longa", 205, 12, 64, 3500, 2, 25);
        Put(6499, "Último", 9, 0, 0, 1, 0, 0);
        return plain;
    }

    /// <summary>ItemList de exemplo para os testes do servidor (valores parecidos com os do jogo).</summary>
    internal static ItemList TestItems()
    {
        var plain = new byte[ItemList.DataSize];
        void Put(int index, string name, short pos, short level, int price, params (short Effect, short Value)[] effects)
        {
            var d = plain.AsSpan(index * ItemDefinition.Size, ItemDefinition.Size);
            Encoding.Latin1.GetBytes(name).CopyTo(d);
            BinaryPrimitives.WriteInt16LittleEndian(d[70..], level);
            for (int i = 0; i < effects.Length; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(d[(80 + i * 4)..], effects[i].Effect);
                BinaryPrimitives.WriteInt16LittleEndian(d[(82 + i * 4)..], effects[i].Value);
            }
            BinaryPrimitives.WriteInt32LittleEndian(d[128..], price);
            BinaryPrimitives.WriteInt16LittleEndian(d[134..], pos);
        }
        Put(1, "Transknight", 1, 0, 0);
        Put(11, "Foema", 1, 0, 0);
        Put(58, "Blacksmith", 1, 0, 0);                 // modelos de NPC usados nos testes
        Put(202, "Gremlin", 1, 0, 0);
        Put(299, "Wolf", 1, 0, 0);
        Put(304, "Orange_King", 1, 0, 0);
        Put(400, "Healing_Potion(S)", 0, 0, 150, (18, 255), (38, 1), (4, 50));
        Put(905, "Espada_Longa", 64, 0, 3500, (2, 25));
        Put(1208, "Elmo", 2, 0, 1000);
        Put(1300, "Espada_Lendaria", 64, 99, 1_000_000);
        Put(2370, "Parade_Horse", 16384, 0, 0, (18, 255));
        Put(508, "Medal_Of_Guild", 4096, 0, 0);
        Put(4159, "Traje", 8192, 0, 0);
        Put(3197, "Master_Of_Hekalotia", unchecked((short)0x8000), 0, 0);   // capa: slot 15
        Put(5076, "Immunity", 0, 60, 0, (18, 8));
        BinaryPrimitives.WriteInt16LittleEndian(plain.AsSpan(5076 * ItemDefinition.Size + 74), 90);   // "INT" 90 = Especial 2
        Put(2443, "Coral", 0, 0, 600, (38, 9), (33, 0));            // pedra de composição (não é gema)
        Put(3388, "Coral_Gem", 0, 0, 1_000_000, (38, 182), (33, 0));
        for (short g = 5; g <= 8; g++)
        {
            Put(2451 + g - 5, "Club_(Ancient)", 192, 0, 350, (2, 83));
            var d = plain.AsSpan((2451 + g - 5) * ItemDefinition.Size, ItemDefinition.Size);
            BinaryPrimitives.WriteInt16LittleEndian(d[64..], 801);   // Mesh
            BinaryPrimitives.WriteInt16LittleEndian(d[138..], g);    // Grade: o efeito ancient
        }
        for (short g = 5; g <= 8; g++)                               // ancient com outro nome; e um grupo sem a 4ª cópia
        {
            foreach (int first in new[] { 3625, 3700 })
            {
                if (first == 3700 && g == 8) continue;
                Put(first + g - 5, "Arco_Antigo", 192, 0, 350, (2, 90));
                var d = plain.AsSpan((first + g - 5) * ItemDefinition.Size, ItemDefinition.Size);
                BinaryPrimitives.WriteInt16LittleEndian(d[64..], 2892);
                BinaryPrimitives.WriteInt16LittleEndian(d[138..], g);
            }
        }
        Put(4011, "Silver_Wyden(1_billion_gold)", 0, 0, 50000, (38, 185), (33, 0), (43, 4));
        Put(5000, "Pedra_Sem_Uso", 0, 0, 10);
        Put(412, "Oriharukon_Powder", 0, 0, 1_000_000, (38, 4), (33, 1));
        Put(413, "Laktorerium_Powder", 0, 0, 900, (38, 5), (33, 1));
        Put(1210, "Armadura_Grau5", 4, 0, 1000, (3, 50), (87, 5));
        Put(3397, "Blue_Firal", 8192, 0, 15000, (18, 255), (33, 0));          // Tintura azul
        Put(3398, "Red_Firal", 8192, 0, 15000, (18, 255), (33, 0));
        Put(3407, "Blue_Enamel", 0, 0, 300, (38, 186), (33, 0));              // Feijão Mágico azul
        Put(3408, "Red_Enamel", 0, 0, 300, (38, 186), (33, 0));
        Put(3417, "Remover_Enamel", 0, 0, 50, (38, 186), (33, 0));

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, ItemList.Encode(plain));
            return ItemList.Load(path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reads_encoded_list()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, ItemList.Encode(PlainList()));
            var list = ItemList.Load(path);

            Assert.Equal(3, list.All.Count());
            var sword = list[905]!;
            Assert.Equal("Espada Longa", sword.Name);
            Assert.Equal(205, sword.Mesh);
            Assert.Equal(12, sword.ReqLevel);
            Assert.Equal(64, sword.Pos);
            Assert.Equal(3500, sword.Price);
            Assert.Equal(new ItemEffect(2, 25), sword.Effects[0]);
            Assert.Null(list[0]);
            Assert.Null(list[2]);
            Assert.Equal("Último", list[6499]!.Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Extra_items_replace_entries()
    {
        var path = Path.GetTempFileName();
        var extraPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, ItemList.Encode(PlainList()));
            var extra = new byte[2 + ItemDefinition.Size];
            BinaryPrimitives.WriteInt16LittleEndian(extra, 905);
            Encoding.Latin1.GetBytes("Espada Nova").CopyTo(extra, 2);
            File.WriteAllBytes(extraPath, extra);

            Assert.Equal("Espada Nova", ItemList.Load(path, extraPath)[905]!.Name);
        }
        finally { File.Delete(path); File.Delete(extraPath); }
    }

    [Fact]
    public void Checksum_follows_client_algorithm()
    {
        // Valores calculados à mão seguindo o código do WYD.exe (posição módulo 9).
        // i=0: b=10 -> +20 +2 | i=1: b=-1 -> +(-1^0xFF)= -256, +0 | i=2: b=9 -> +3 +1 | i=5: b=7 -> -7
        var data = new byte[] { 10, 0xFF, 9, 0, 0, 7 };
        Assert.Equal(20 + 2 + (-1 ^ 0xFF) + 0 + 3 + 1 + 0 + 0 + (0 - (0 ^ 0x5A)) + 0 - 7, ItemList.ComputeChecksum(data));
    }

    [Fact]
    public void Fix_checksum_reaches_the_client_value_using_only_empty_items()
    {
        var file = ItemList.Encode(PlainList());
        Assert.True(ItemList.FixChecksum(file) > 0);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, file);
            var list = ItemList.Load(path);
            Assert.True(list.MatchesClient);
            Assert.Equal(3, list.All.Count());                 // os itens mexidos continuam vazios
            Assert.Equal("Espada Longa", list[905]!.Name);
        }
        finally { File.Delete(path); }
        Assert.Equal(ItemList.ClientChecksum, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(ItemList.DataSize)));
    }

    [Fact]
    public void Skill_data_checksum_is_fixed_in_empty_skills()
    {
        var file = SkillData.Encode([new SkillDefinition(0, 1, 2, 30, 4, 5), new SkillDefinition(SkillData.ClientCount - 1, 9, 1, 8, 2, 3)]);
        SkillData.FixChecksum(file);
        Assert.Equal(SkillData.ClientChecksum, SkillData.ComputeChecksum(file));
        var data = SkillData.Parse(file);
        Assert.Equal(30, data[0]!.ManaSpent);
        Assert.Equal(8, data[SkillData.ClientCount - 1]!.ManaSpent);
    }

    [Fact]
    public void Rejects_short_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[100]);
            Assert.Throws<InvalidDataException>(() => ItemList.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Paint_keeps_the_refine_in_the_color_type()
    {
        var armor = new Item(1210) { Effect1 = 43, Value1 = 7 };
        Assert.Equal((short)3407, Dyes.BeanOf(3397));
        Assert.Null(Dyes.BeanOf(3407));
        Assert.Equal((byte)116, Dyes.ColorOf(3407));
        Assert.Equal((byte)125, Dyes.ColorOf(3416));
        Assert.Equal((byte)43, Dyes.ColorOf(Dyes.Remover));

        var blue = Dyes.Paint(armor, 116)!.Value;
        Assert.Equal((116, 7), (blue.Effect1, blue.Value1));
        Assert.Equal(7, Stats.Refine(blue));
        Assert.Null(Dyes.Paint(blue, 116));                                  // já está azul
        Assert.Null(Dyes.Paint(new Item(1210), 43));                         // nada para tirar

        // Sem refinação: a cor vai para o primeiro efeito livre, com +0.
        var plain = Dyes.Paint(new Item(1210) { Effect1 = 3, Value1 = 10 }, 118)!.Value;
        Assert.Equal((118, 0), (plain.Effect2, plain.Value2));
        var refined = Refining.WithRefine(plain, 1, 0);
        Assert.Equal((118, 1), (refined.Effect2, refined.Value2));

        // +10 em diante: a gema e a refinação alta continuam no mesmo efeito colorido.
        var high = Refining.WithHighRefine(blue with { Value1 = 230 }, 12);
        Assert.Equal((116, 238), (high.Effect1, high.Value1));
        Assert.Equal(12, Stats.Refine(high));
    }
}
