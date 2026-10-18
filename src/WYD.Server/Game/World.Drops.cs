using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Drop dos monstros. No 7.54 não há item no chão (só existia no 6.56): o que o monstro derruba vai direto para o
/// inventário de quem matou, junto com o gold, e o pedido de largar item no chão (0x272) é ignorado.
/// </summary>
public sealed partial class World
{
    /// <summary>Efeitos de 100 em diante na lista de drop do NPC marcam adicionais sorteados; saem do item.</summary>
    public const byte DropMarkerEffect = 100;

    /// <summary>Sorteio do drop (troca nos testes).</summary>
    public Random Random { get; set; } = Random.Shared;

    /// <summary>
    /// O sorteio do drop de um monstro morto: cada item da lista dele (os 64 slots do arquivo de NPC) tem
    /// <see cref="Config.ServerOptions.DropChance"/> (vezes o bônus de drop de quem matou) de cair.
    /// </summary>
    public List<Item> RollDrops(Mob mob, int dropBonus)
    {
        var drops = new List<Item>();
        long chance = (long)Math.Max(0, options.Value.DropChance) * (100 + Math.Max(0, dropBonus)) / 100;
        foreach (var carried in mob.Template.Carry)
        {
            if (carried.IsEmpty || Random.Next(10000) >= chance) continue;
            var item = carried;
            if (item.Effect1 >= DropMarkerEffect) (item.Effect1, item.Value1) = (0, 0);
            if (item.Effect2 >= DropMarkerEffect) (item.Effect2, item.Value2) = (0, 0);
            if (item.Effect3 >= DropMarkerEffect) (item.Effect3, item.Value3) = (0, 0);
            drops.Add(item);
        }
        return drops;
    }

    /// <summary>
    /// Põe o drop no inventário (primeiro slot livre do inventário básico). Devolve os slots usados e os itens que
    /// não couberam (perdidos). Chamar com o lock.
    /// </summary>
    private static (List<(int Slot, Item Item)> Stored, List<Item> Lost) StoreDrops(Character c, List<Item> drops)
    {
        var stored = new List<(int, Item)>();
        var lost = new List<Item>();
        foreach (var drop in drops)
        {
            int slot = Bags.FirstFree(c);
            if (slot < 0)
            {
                lost.Add(drop);
                continue;
            }
            c.Inventory[slot] = drop;
            stored.Add((slot, drop));
        }
        return (stored, lost);
    }
}
