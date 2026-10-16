using WYD.Server.Data;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Muro de Espinhos (skill 98 de Sephira): convoca plantas com espinhos em fila no lugar escolhido. Elas ficam paradas,
/// ocupam a célula (os monstros não pisam nelas), podem ser eliminadas com ataques e somem depois de
/// <see cref="Duration"/>. Não dão experiência, gold nem drop e não renascem. Um novo muro do mesmo jogador tira o
/// anterior.
/// </summary>
public static class ThornWall
{
    public const int Plants = 3;
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);

    /// <summary>Modelo "Vine" do ItemList.</summary>
    public const short Model = 358;
    public const string Name = "Espinhos";

    /// <summary>Até onde o muro pode ser posto (alcance 5 da skill, mais a Força Espectral e uma célula de folga).</summary>
    public const int MaxDistance = 7;

    public static int MaxHp(Character caster) => 300 + 20 * caster.BaseScore.Level;

    public static NpcTemplate Template(Character caster)
    {
        var score = new Score
        {
            Level = caster.BaseScore.Level,
            Ac = caster.CurrentScore.Ac,
            MaxHp = MaxHp(caster),
            Hp = MaxHp(caster),
        };
        var equip = new Item[GameConstants.MaxEquip];
        equip[0] = new Item(Model);
        return new NpcTemplate(Name, 0, 0, 0, 0, 0, 0, score, equip, new Item[GameConstants.MaxCarry]);
    }

    /// <summary>
    /// As células do muro: a escolhida e as vizinhas na perpendicular à direção de quem lança (de frente para
    /// ele). Um alvo longe demais vira o lugar de quem lança.
    /// </summary>
    public static (short X, short Y)[] Cells(short casterX, short casterY, short targetX, short targetY)
    {
        if (Math.Max(Math.Abs(targetX - casterX), Math.Abs(targetY - casterY)) > MaxDistance)
            (targetX, targetY) = (casterX, casterY);
        int dx = Math.Sign(targetX - casterX), dy = Math.Sign(targetY - casterY);
        // Perpendicular à direção (dx, dy); sem direção (no próprio lugar), na horizontal.
        (int px, int py) = (dx, dy) == (0, 0) ? (1, 0) : (-dy, dx);
        var cells = new (short X, short Y)[Plants];
        for (int i = 0; i < Plants; i++)
        {
            int k = i - Plants / 2;
            cells[i] = ((short)(targetX + k * px), (short)(targetY + k * py));
        }
        return cells;
    }
}
