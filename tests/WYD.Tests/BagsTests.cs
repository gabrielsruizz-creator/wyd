using WYD.Server.Game;
using WYD.Server.Protocol;

namespace WYD.Tests;

public class BagsTests
{
    private static Character New() => new() { Name = "Teste" };

    [Fact]
    public void Only_the_first_two_pages_are_free_without_bags()
    {
        var c = New();
        Assert.True(Bags.CanHold(c, 29));
        Assert.False(Bags.CanHold(c, 30));
        Assert.False(Bags.CanHold(c, 60));                        // slot da bolsa: nunca item comum
        for (int i = 0; i < 30; i++) c.Inventory[i] = new Item(400);
        Assert.Equal(-1, Bags.FirstFree(c));                      // não põe item onde o client não mostra
    }

    [Fact]
    public void Bag_unlocks_a_page_for_thirty_days_and_a_second_one_the_last_page()
    {
        var c = New();
        var now = new DateTime(2026, 10, 18, 15, 0, 0, DateTimeKind.Utc);
        Assert.Equal(60, Bags.Use(c, now));
        Assert.True(Bags.CanHold(c, 30));
        Assert.False(Bags.CanHold(c, 45));
        var bag = c.Inventory[60];
        Assert.Equal((Bags.EfDay, (byte)16), (bag.Effect1, bag.Value1));             // o client mostra 16/11
        Assert.Equal((Bags.EfMonth, (byte)11), (bag.Effect2, bag.Value2));
        Assert.Equal(new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc), Bags.Expires(bag));

        Assert.Equal(61, Bags.Use(c, now));
        Assert.True(Bags.CanHold(c, 59));
        Assert.Equal(60, Bags.Use(c, now));                                          // as duas valendo: estende a que vence antes
        Assert.Equal(new DateTime(2026, 12, 17, 0, 0, 0, DateTimeKind.Utc), Bags.Expires(c.Inventory[60]));
    }

    [Fact]
    public void Repair_removes_expired_bags_and_moves_hidden_items()
    {
        var c = New();
        var cargo = new Item[GameConstants.MaxCargo];
        c.Inventory[60] = Bags.Make(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));   // vencida
        c.Inventory[40] = new Item(905);                                                     // página trancada
        c.Inventory[62] = new Item(1208);                                                    // slot que o client não mostra
        Assert.True(Bags.Repair(c, cargo, new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)));
        Assert.True(c.Inventory[60].IsEmpty);
        Assert.Equal(new short[] { 905, 1208 }, new[] { c.Inventory[0].Index, c.Inventory[1].Index });
        Assert.True(c.Inventory[40].IsEmpty && c.Inventory[62].IsEmpty);
    }
}
