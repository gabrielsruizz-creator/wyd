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

    /// <summary>Velocidade de corrida com botas e montaria (calculada por <see cref="Stats.Recalculate"/>).</summary>
    public byte RunSpeed { get; set; } = Score.DefaultAttackRun & 0x0F;

    /// <summary>O que o equipamento soma (calculado por <see cref="Stats.Recalculate"/>).</summary>
    public ItemBonus ItemBonus { get; set; }

    /// <summary>Os atributos que valem no jogo: os base mais o equipamento, com a velocidade das botas/montaria.</summary>
    public Score CurrentScore
    {
        get
        {
            var s = BaseScore;
            var b = ItemBonus;
            s.AttackRun = (byte)((s.AttackRun & 0xF0) | RunSpeed);
            s.Damage += b.Damage;
            s.Ac += b.Ac;
            s.MaxHp += b.MaxHp;
            s.MaxMp += b.MaxMp;
            s.Str = (short)Math.Clamp(s.Str + b.Str, 0, short.MaxValue);
            s.Int = (short)Math.Clamp(s.Int + b.Int, 0, short.MaxValue);
            s.Dex = (short)Math.Clamp(s.Dex + b.Dex, 0, short.MaxValue);
            s.Con = (short)Math.Clamp(s.Con + b.Con, 0, short.MaxValue);
            s.Special1 = (byte)Math.Clamp(s.Special1 + b.Special1, 0, 255);
            s.Special2 = (byte)Math.Clamp(s.Special2 + b.Special2, 0, 255);
            s.Special3 = (byte)Math.Clamp(s.Special3 + b.Special3, 0, 255);
            s.Special4 = (byte)Math.Clamp(s.Special4 + b.Special4, 0, 255);
            s.Hp = Math.Min(s.Hp, s.MaxHp);
            s.Mp = Math.Min(s.Mp, s.MaxMp);
            return s;
        }
    }

    /// <summary>Vida e mana máximas com o equipamento.</summary>
    public int MaxHp => Math.Max(1, BaseScore.MaxHp + ItemBonus.MaxHp);
    public int MaxMp => Math.Max(0, BaseScore.MaxMp + ItemBonus.MaxMp);

    /// <summary>Mortal, Arch, Celestial ou Sub Celestial: guardado no rosto, junto com o equipamento.</summary>
    public Evolution Evolution
    {
        get => (Evolution)Math.Min(Equip[0].Value2, (byte)Evolution.SubCelestial);
        set => Equip[0].Value2 = (byte)value;
    }
}
