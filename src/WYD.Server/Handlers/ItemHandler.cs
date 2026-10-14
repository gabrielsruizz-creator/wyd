using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using WYD.Server.Data;
using WYD.Server.Database;
using WYD.Server.Game;
using WYD.Server.Network;
using WYD.Server.Protocol;

namespace WYD.Server.Handlers;

/// <summary>
/// Itens: mover/equipar (0x376), usar (0x373), comprar (0x379) e vender (0x37A).
/// Formatos lidos no WYD.exe 7.54. Os tipos de lugar são 0 = equipamento, 1 = inventário, 2 = baú.
/// </summary>
public sealed class ItemHandler(
    ItemCatalog catalog,
    ICharacterRepository characters,
    IAccountRepository accounts,
    World world,
    ILogger<ItemHandler> logger)
{
    public const int MaxCoin = 2_000_000_000;

    /// <summary>Efeitos fixos do ItemList.bin usados aqui.</summary>
    public const byte EfHp = 4, EfMp = 5, EfUsable = 38, EfAmount = 61;

    /// <summary>Valores do efeito 38 (EfUsable): 1 = poção, 185 = barra de gold.</summary>
    public const short UsablePotion = 1, UsableGoldBar = 185;

    /// <summary>Gold de cada barra (o nome no ItemList.bin diz: 100 milhões e 1 bilhão).</summary>
    public static readonly IReadOnlyDictionary<int, int> GoldBars = new Dictionary<int, int>
    {
        [4010] = 100_000_000,
        [4011] = 1_000_000_000,
    };

    /// <summary>Venda paga 1/4 do preço de compra.</summary>
    public const int SellDivisor = 4;

    public const int ShopDistance = WorldHandler.ShopDistance;

    private Item[]? Place(ClientSession session, int type) => type switch
    {
        0 => session.Character!.Equip,
        1 => session.Character!.Inventory,
        2 => session.Account!.Cargo,
        _ => null,
    };

    // ---------------------------------------------------------------- mover / equipar

    public async Task HandleMoveAsync(ClientSession session, PacketReader packet)
    {
        var raw = packet.ToArray();
        int destType = raw[0x0C], destPos = raw[0x0D], srcType = raw[0x0E], srcPos = raw[0x0F];
        var src = Place(session, srcType);
        var dest = Place(session, destType);
        var c = session.Character!;

        string? error =
            src is null || dest is null || srcPos >= src.Length || destPos >= dest.Length ? "Posição inválida." :
            src[srcPos].IsEmpty ? null : // nada para mover: ignora em silêncio
            (srcType == 0 && srcPos == 0) || (destType == 0 && destPos == 0) ? "O rosto do personagem não pode ser trocado." :
            destType == 0 && !CanEquip(c, src[srcPos], destPos, out var why) ? why :
            srcType == 0 && !dest[destPos].IsEmpty && !CanEquip(c, dest[destPos], srcPos, out var why2) ? why2 :
            null;

        if (src is null || dest is null || src[srcPos].IsEmpty || error is not null)
        {
            if (error is not null) await session.SendMessageAsync(error);
            return;
        }

        (src[srcPos], dest[destPos]) = (dest[destPos], src[srcPos]);
        await session.SendAsync(ServerPackets.Relay(raw, session.Id)); // o client faz a troca na tela ao receber de volta

        if (srcType == 0 || destType == 0)
        {
            // Botas e montaria mudam a velocidade: a ficha nova vai para o próprio jogador.
            Game.Stats.Recalculate(c, catalog.List);
            c.BaseScore.Hp = Math.Min(c.BaseScore.Hp, c.MaxHp);     // tirou um item de HP: a vida não passa do máximo
            c.BaseScore.Mp = Math.Min(c.BaseScore.Mp, c.MaxMp);
            await session.SendAsync(ServerPackets.UpdateScore(session.Id, c));
            var update = ServerPackets.UpdateEquip(session.Id, c.Equip);
            foreach (var viewer in world.Viewers(session))
                await viewer.SendAsync(update);
        }
        await SaveAsync(session, cargo: srcType == 2 || destType == 2);
    }

    /// <summary>O item pode ir para o slot de equipamento? (máscara Pos do ItemList.bin e nível mínimo)</summary>
    private bool CanEquip(Character c, Item item, int slot, out string? reason)
    {
        reason = null;
        if (catalog.List?[item.Index] is not { } def) return true; // sem ItemList.bin não dá para conferir
        if ((def.Pos & (1 << slot)) == 0)
        {
            reason = $"{Name(def)} não pode ser equipado aí.";
            return false;
        }
        if (def.ReqLevel > c.BaseScore.Level + 1)
        {
            reason = $"{Name(def)} precisa do nível {def.ReqLevel}.";
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- usar

    public async Task HandleUseAsync(ClientSession session, PacketReader packet)
    {
        int srcType = packet.ReadInt32(), srcPos = packet.ReadInt32();
        int destType = packet.ReadInt32(), destPos = packet.ReadInt32();   // item em que a gema foi arrastada
        var place = Place(session, srcType);
        var c = session.Character!;
        if (place is null || srcType == 0 || srcPos < 0 || srcPos >= place.Length || place[srcPos].IsEmpty) return;

        var item = place[srcPos];
        if (catalog.List?[item.Index] is not { } def)
        {
            await session.SendMessageAsync("Para usar itens o servidor precisa do ItemList.bin na pasta data.");
            return;
        }

        short usable = Effect(def, EfUsable);
        if (usable == UsableGoldBar && GoldBars.TryGetValue(item.Index, out int gold))
        {
            if ((long)c.Coin + gold > MaxCoin)
            {
                await session.SendMessageAsync("Você não pode carregar tanto gold.");
                return;
            }
            c.Coin += gold;
            Consume(place, srcPos);
            await session.SendAsync(ServerPackets.UpdateItem(session.Id, (ServerPackets.ItemPlace)srcType, srcPos, place[srcPos]));
            await session.SendAsync(ServerPackets.UpdateCoin(session.Id, c.Coin));
        }
        else if (usable == Game.Ancient.UsableGem && Game.Ancient.GemEffect(item.Index) is { } gemEffect)
        {
            if (!await ApplyGemAsync(session, place, srcPos, srcType, destType, destPos, gemEffect)) return;
        }
        else if (usable == UsablePotion)
        {
            c.BaseScore.Hp = Math.Min(c.MaxHp, c.BaseScore.Hp + Effect(def, EfHp));
            c.BaseScore.Mp = Math.Min(c.MaxMp, c.BaseScore.Mp + Effect(def, EfMp));
            Consume(place, srcPos);
            await session.SendAsync(ServerPackets.UpdateItem(session.Id, (ServerPackets.ItemPlace)srcType, srcPos, place[srcPos]));
            await session.SendAsync(ServerPackets.UpdateHpMp(session.Id, c.BaseScore.Hp, c.BaseScore.Mp));
        }
        else
        {
            await session.SendMessageAsync($"{Name(def)} ainda não tem função neste servidor.");
            return;
        }

        logger.LogInformation("[{Id}] {Name} usou {Item}.", session.Id, c.Name, Name(def));
        await SaveAsync(session, cargo: srcType == 2);
    }

    /// <summary>Gema arrastada num item ancient: troca o efeito dele pelo da gema (o item vira a cópia do grupo).</summary>
    private async Task<bool> ApplyGemAsync(ClientSession session, Item[] gems, int gemPos, int gemType, int destType, int destPos,
        Game.AncientEffect effect)
    {
        var c = session.Character!;
        var dest = Place(session, destType);
        if (dest is null || destPos < 0 || destPos >= dest.Length || dest[destPos].IsEmpty || (destType == gemType && destPos == gemPos))
        {
            await session.SendMessageAsync("Arraste a gema em cima do item ancient.");
            return false;
        }

        var target = dest[destPos];
        var current = Game.Ancient.EffectOf(catalog.List![target.Index]);
        if (current is null)
        {
            await session.SendMessageAsync("A gema só funciona em itens ancient.");
            return false;
        }
        if (current == effect)
        {
            await session.SendMessageAsync($"Este item já tem o efeito de {Game.Ancient.Name(effect)}.");
            return false;
        }
        if (Game.Ancient.Variant(target.Index, catalog.List, effect) is not { } index)
        {
            await session.SendMessageAsync("Este item não tem a versão com esse efeito.");
            return false;
        }

        dest[destPos] = target with { Index = index };                 // refinação e adicionais continuam
        Consume(gems, gemPos);
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, (ServerPackets.ItemPlace)gemType, gemPos, gems[gemPos]));
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, (ServerPackets.ItemPlace)destType, destPos, dest[destPos]));
        if (destType == 0)
        {
            Game.Stats.Recalculate(c, catalog.List);
            await session.SendAsync(ServerPackets.UpdateScore(session.Id, c));
            var update = ServerPackets.UpdateEquip(session.Id, c.Equip);
            foreach (var viewer in world.Viewers(session))
                await viewer.SendAsync(update);
        }
        await session.SendMessageAsync($"Efeito do ancient trocado para {Game.Ancient.Name(effect)}.");
        if (destType == 2 || gemType == 2) await SaveAsync(session, cargo: true);
        return true;
    }

    /// <summary>Gasta uma unidade: itens empilhados (efeito 61 = quantidade) diminuem; os outros somem.</summary>
    private static void Consume(Item[] place, int slot)
    {
        ref var item = ref place[slot];
        if (item.Effect1 == EfAmount && item.Value1 > 1) { item.Value1--; return; }
        if (item.Effect2 == EfAmount && item.Value2 > 1) { item.Value2--; return; }
        if (item.Effect3 == EfAmount && item.Value3 > 1) { item.Value3--; return; }
        item = default;
    }

    // ---------------------------------------------------------------- comprar / vender

    public async Task HandleBuyAsync(ClientSession session, PacketReader packet)
    {
        ushort npcId = packet.ReadUInt16();
        short npcSlot = packet.ReadInt16();
        short mySlot = packet.ReadInt16();
        var c = session.Character!;

        var npc = ShopNear(session, npcId);
        if (npc is null || !IsShopSlot(npcSlot) || npcSlot >= npc.Template.Carry.Length || npc.Template.Carry[npcSlot].IsEmpty) return;

        var item = npc.Template.Carry[npcSlot];
        if (catalog.List?[item.Index] is not { } def)
        {
            await session.SendMessageAsync("Para comprar o servidor precisa do ItemList.bin na pasta data.");
            return;
        }
        if (c.Coin < def.Price)
        {
            await session.SendMessageAsync($"Gold insuficiente: {Name(def)} custa {def.Price}.");
            return;
        }

        if (mySlot < 0 || mySlot >= CommandHandler.BasicInventorySlots || !c.Inventory[mySlot].IsEmpty)
            mySlot = (short)Array.FindIndex(c.Inventory, 0, CommandHandler.BasicInventorySlots, i => i.IsEmpty);
        if (mySlot < 0)
        {
            await session.SendMessageAsync("Inventário cheio.");
            return;
        }

        c.Coin -= def.Price;
        c.Inventory[mySlot] = item;
        await session.SendAsync(ServerPackets.BuyOk(session.Id, npcId, npcSlot, mySlot, c.Coin));
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, ServerPackets.ItemPlace.Inventory, mySlot, item));
        await session.SendAsync(ServerPackets.UpdateCoin(session.Id, c.Coin));
        logger.LogInformation("[{Id}] {Name} comprou {Item} por {Price}.", session.Id, c.Name, Name(def), def.Price);
        await SaveAsync(session);
    }

    public async Task HandleSellAsync(ClientSession session, PacketReader packet)
    {
        ushort npcId = packet.ReadUInt16();
        short myType = packet.ReadInt16();
        short myPos = packet.ReadInt16();
        var c = session.Character!;
        var place = Place(session, myType);

        if (ShopNear(session, npcId) is null || place is null || myType == 0 || myPos < 0 || myPos >= place.Length || place[myPos].IsEmpty)
            return;

        var item = place[myPos];
        int price = (catalog.List?[item.Index]?.Price ?? 0) / SellDivisor;
        if ((long)c.Coin + price > MaxCoin)
        {
            await session.SendMessageAsync("Você não pode carregar tanto gold.");
            return;
        }

        place[myPos] = default;
        c.Coin += price;
        var reply = packet.ToArray();
        await session.SendAsync(ServerPackets.Relay(reply, session.Id));
        await session.SendAsync(ServerPackets.UpdateItem(session.Id, (ServerPackets.ItemPlace)myType, myPos, default));
        await session.SendAsync(ServerPackets.UpdateCoin(session.Id, c.Coin));
        logger.LogInformation("[{Id}] {Name} vendeu o item {Item} por {Price}.", session.Id, c.Name, item.Index, price);
        await SaveAsync(session, cargo: myType == 2);
    }

    private Mob? ShopNear(ClientSession session, ushort npcId)
    {
        var npc = world.VisibleMob(session, npcId);
        var me = session.Character!;
        return npc is not null && npc.IsShop
               && Math.Abs(npc.X - me.PosX) <= ShopDistance && Math.Abs(npc.Y - me.PosY) <= ShopDistance
            ? npc
            : null;
    }

    /// <summary>Slots do inventário do NPC que aparecem na loja: 0–8, 27–35 e 54–62.</summary>
    public static bool IsShopSlot(int slot) => slot >= 0 && slot % 27 < 9 && slot / 27 < 3;

    // ---------------------------------------------------------------- utilitários

    private static short Effect(ItemDefinition def, byte effect) =>
        def.Effects.FirstOrDefault(e => e.Effect == effect).Value;

    private static string Name(ItemDefinition def) => def.Name.Replace('_', ' ');

    private async Task SaveAsync(ClientSession session, bool cargo = false)
    {
        try
        {
            await characters.SaveItemsAsync(session.Character!);
            if (cargo) await accounts.SaveCargoAsync(session.Account!);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Não foi possível salvar os itens de {Name}.", session.Character!.Name);
        }
    }
}
