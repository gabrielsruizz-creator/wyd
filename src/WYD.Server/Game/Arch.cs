using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Criação do Arch no rei do reino: o Mortal no nível 400 leva o Sefirot (1760 TK, 1761 FM, 1762 BM, 1763 HT) e
/// nasce um personagem novo, com o mesmo nome, num slot livre da conta. O corpo é o da classe do Mortal e as skills
/// são as da classe do Sefirot (dá para ter corpo de HT com skills de Foema). O Arch começa no nível 1, sem itens,
/// com 528 pontos de atributo; o Mortal continua na conta, só sem o Sefirot.
/// </summary>
public static class Arch
{
    /// <summary>Pontos de atributo com que o Arch nasce.</summary>
    public const short ScorePoints = 528;

    /// <summary>
    /// Rostos de Arch no ItemList: 6-9 Transknight, 16-19 Foema, 26-29 Beastmaster, 36-39 Hunter (malha 40). As
    /// quatro variações de cada corpo ficam para as quatro classes de skill.
    /// </summary>
    public static short Face(CharacterClass body, CharacterClass skills) => (short)(10 * (int)body + 6 + (int)skills);

    /// <summary>Classe do corpo pelo rosto (1-39), ou a classe do personagem.</summary>
    public static CharacterClass BodyOf(Character c) =>
        c.Equip[0].Index is >= 1 and < 40 ? (CharacterClass)((c.Equip[0].Index - 1) / 10) : c.Class;

    /// <summary>Slot do primeiro Sefirot no inventário, ou -1.</summary>
    public static int SefirotSlot(Character c, int basicSlots) =>
        Array.FindIndex(c.Inventory, 0, basicSlots, i => i.Index is >= Sefirot.FirstSefirot and < Sefirot.FirstSefirot + 4);

    public static CharacterClass SefirotClass(Item sefirot) => (CharacterClass)(sefirot.Index - Sefirot.FirstSefirot);

    /// <summary>O Arch novo do <paramref name="mortal"/>, com as skills de <paramref name="skills"/>.</summary>
    public static Character Create(Character mortal, CharacterClass skills, int slot, short x, short y)
    {
        var arch = ClassTemplates.Create(mortal.AccountId, slot, mortal.Name, skills, x, y);
        arch.Equip[0] = new Item(Face(BodyOf(mortal), skills));
        arch.Evolution = Evolution.Arch;
        arch.ScoreBonus = ScorePoints;
        return arch;
    }
}
