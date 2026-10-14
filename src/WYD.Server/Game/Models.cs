using WYD.Server.Protocol;

namespace WYD.Server.Game;

public sealed class Account
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    /// <summary>Segunda senha (tela de personagens). Null até o jogador definir a primeira.</summary>
    public string? SecondPasswordHash { get; set; }
    public int AccessLevel { get; set; }
    public bool Banned { get; set; }

    /// <summary>Gold guardado no baú (cargo).</summary>
    public int CargoCoin { get; set; }
    public Item[] Cargo { get; set; } = new Item[GameConstants.MaxCargo];
}

public enum CharacterClass : byte
{
    TransKnight = 0,
    Foema = 1,
    BeastMaster = 2,
    Huntress = 3,
}

public sealed class Character
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public CharacterClass Class { get; set; }
    public long Exp { get; set; }
    public int Coin { get; set; }

    /// <summary>Pontos para distribuir em atributos, especiais e skills (ganhos ao subir de nível).</summary>
    public short ScoreBonus { get; set; }
    public short SpecialBonus { get; set; }
    public short SkillBonus { get; set; }
    public uint LearnedSkill { get; set; }
    public ushort Guild { get; set; }
    public short PosX { get; set; }
    public short PosY { get; set; }

    /// <summary>Atributos base (sem bônus de itens).</summary>
    public Score BaseScore;

    /// <summary>Equip[0] é o "rosto" (modelo do personagem); os demais são os equipamentos.</summary>
    public Item[] Equip { get; set; } = new Item[GameConstants.MaxEquip];
    public Item[] Inventory { get; set; } = new Item[GameConstants.MaxCarry];
}
