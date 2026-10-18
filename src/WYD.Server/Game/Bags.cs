using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Inventário do client 7.57: 4 páginas de 15 slots (Carry[0..59]). As duas primeiras estão sempre liberadas; a 3ª
/// (Carry[30..44]) só com a Bolsa do Andarilho (<see cref="TravelingBag"/>) em Carry[60] e a 4ª (Carry[45..59])
/// com outra em Carry[61] (o client confere em 0x53DD04 e desenha os cadeados por isso). Carry[60..63] não aparecem
/// na grade: item comum ali fica invisível.
/// <para>
/// Usar a bolsa (0x373) a põe no primeiro slot de bolsa livre (ou vencido) por <see cref="Duration"/>. A data de
/// vencimento fica nos efeitos da bolsa: 106 = dia e 110 = mês (o client mostra "Mês x / y Dia(s)" no botão,
/// 0x497000) e 108 = ano − 2000 (só o servidor usa).
/// </para>
/// </summary>
public static class Bags
{
    public const short TravelingBag = 3467;
    public const int PageSize = 15, FreeSlots = 2 * PageSize, Usable = 4 * PageSize;
    public const int FirstBagSlot = 60;
    public const byte EfDay = 106, EfYear = 108, EfMonth = 110;
    public static readonly TimeSpan Duration = TimeSpan.FromDays(30);

    /// <summary>O slot de bolsa que libera a página do slot (null para as páginas sempre livres).</summary>
    private static int? BagFor(int slot) => slot < FreeSlots ? null : FirstBagSlot + (slot - FreeSlots) / PageSize;

    /// <summary>O slot pode guardar um item comum? (página liberada; nunca os slots de bolsa)</summary>
    public static bool CanHold(Character c, int slot) =>
        slot >= 0 && slot < Usable && (BagFor(slot) is not { } bag || c.Inventory[bag].Index == TravelingBag);

    /// <summary>Primeiro slot livre que o client mostra; -1 se não houver.</summary>
    public static int FirstFree(Character c)
    {
        for (int i = 0; i < Usable; i++)
            if (c.Inventory[i].IsEmpty && CanHold(c, i)) return i;
        return -1;
    }

    public static DateTime? Expires(Item bag) =>
        bag.Index != TravelingBag ? null : Read(bag, EfDay) is int d && Read(bag, EfMonth) is int m && Read(bag, EfYear) is int y
            && m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(2000 + y, m)
            ? new DateTime(2000 + y, m, d, 0, 0, 0, DateTimeKind.Utc).AddDays(1)   // vale até o fim do dia mostrado
            : null;

    private static int? Read(Item i, byte effect) =>
        i.Effect1 == effect ? i.Value1 : i.Effect2 == effect ? i.Value2 : i.Effect3 == effect ? i.Value3 : null;

    /// <summary>Bolsa que vale até <paramref name="expires"/> (meia-noite; o client mostra o dia anterior, o último).</summary>
    public static Item Make(DateTime expires)
    {
        var day = expires.AddDays(-1).Date;      // o último dia em que ainda vale
        return new Item(TravelingBag)
        {
            Effect1 = EfDay, Value1 = (byte)day.Day,
            Effect2 = EfMonth, Value2 = (byte)day.Month,
            Effect3 = EfYear, Value3 = (byte)(day.Year - 2000),
        };
    }

    /// <summary>
    /// Usa a bolsa: vai para o slot de bolsa vazio ou vencido; com as duas valendo, estende a que vence primeiro.
    /// Retorna o slot de bolsa que mudou.
    /// </summary>
    public static int Use(Character c, DateTime now)
    {
        int target = -1;
        for (int s = FirstBagSlot; s < FirstBagSlot + 2; s++)
            if (Expires(c.Inventory[s]) is not { } e || e <= now) { target = s; break; }
        DateTime start = now.Date;               // o dia de hoje conta como o primeiro
        if (target < 0)
        {
            target = Expires(c.Inventory[FirstBagSlot])!.Value <= Expires(c.Inventory[FirstBagSlot + 1])!.Value
                ? FirstBagSlot : FirstBagSlot + 1;
            start = Expires(c.Inventory[target])!.Value;
        }
        c.Inventory[target] = Make(start + Duration);
        return target;
    }

    /// <summary>
    /// Ao entrar no jogo: tira as bolsas vencidas e leva para slots livres os itens que o client não mostra (nos
    /// slots de bolsa sem ser bolsa, em Carry[62..63] ou em página trancada; por exemplo de personagens do 7.54, cujo
    /// inventário era de 63 slots). O que não couber vai para o baú; o que nem lá couber fica onde está.
    /// Retorna se mudou algo.
    /// </summary>
    public static bool Repair(Character c, Item[] cargo, DateTime now)
    {
        bool changed = false;
        for (int s = FirstBagSlot; s < FirstBagSlot + 2; s++)
            if (c.Inventory[s].Index == TravelingBag && !(Expires(c.Inventory[s]) > now))
            {
                c.Inventory[s] = default;
                changed = true;
            }
        for (int i = 0; i < c.Inventory.Length; i++)
        {
            var item = c.Inventory[i];
            bool bag = i is FirstBagSlot or FirstBagSlot + 1 && item.Index == TravelingBag;
            if (item.IsEmpty || bag || CanHold(c, i)) continue;
            int free = FirstFree(c);
            if (free >= 0) c.Inventory[free] = item;
            else if (Array.FindIndex(cargo, x => x.IsEmpty) is int k and >= 0) cargo[k] = item;
            else continue;
            c.Inventory[i] = default;
            changed = true;
        }
        return changed;
    }
}
