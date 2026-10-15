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

    /// <summary>Tamanho da barra de skills: 4 na STRUCT_MOB e 16 na segunda barra do 0x114.</summary>
    public const int SkillBarSize = 20;

    /// <summary>
    /// Barra de skills como o client manda no 0x378: número da skill dentro da classe (0-23), 0xFF vazio.
    /// </summary>
    public byte[] SkillBar { get; set; } = EmptySkillBar();

    public static byte[] EmptySkillBar() => Enumerable.Repeat((byte)0xFF, SkillBarSize).ToArray();

    public static byte[] ReadSkillBar(byte[]? stored)
    {
        var bar = EmptySkillBar();
        stored?.AsSpan(0, Math.Min(stored.Length, SkillBarSize)).CopyTo(bar);
        return bar;
    }
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

    /// <summary>Efeitos de skill ativos (buffs), até 16 (ver <see cref="Game.Affects"/>).</summary>
    public List<Affect> Affects { get; } = [];

    /// <summary>Quando a aura de dano (Trovão) bate de novo.</summary>
    public DateTime NextAuraTick { get; set; }

    /// <summary>
    /// Os atributos que valem no jogo, calculados como o client 7.54 (0x55A392): base mais equipamento; o dano
    /// soma FOR/3 + DES/8 + Especial 1 + nível (o dobro do nível para rostos evoluídos); CON e INT dão vida e mana;
    /// os efeitos 45 e 46 dos itens aumentam vida e mana em %.
    /// </summary>
    public Score CurrentScore
    {
        get
        {
            var s = BaseScore;
            var b = ItemBonus;
            var buff = Game.Affects.Bonus(this);
            int speed = Math.Min(Stats.MaxRunSpeed, RunSpeed + buff.RunSpeed);
            s.AttackRun = (byte)((s.AttackRun & 0xF0) | speed);
            s.Ac += b.Ac + buff.Ac;
            s.Str = (short)Math.Clamp(s.Str + b.Str, 0, short.MaxValue);
            s.Int = (short)Math.Clamp(s.Int + b.Int, 0, short.MaxValue);
            s.Dex = (short)Math.Clamp(s.Dex + b.Dex, 0, short.MaxValue);
            s.Con = (short)Math.Clamp(s.Con + b.Con, 0, short.MaxValue);
            s.Special1 = (byte)Math.Clamp(s.Special1 + b.Special1, 0, 255);
            s.Special2 = (byte)Math.Clamp(s.Special2 + b.Special2, 0, 255);
            s.Special3 = (byte)Math.Clamp(s.Special3 + b.Special3, 0, 255);
            s.Special4 = (byte)Math.Clamp(s.Special4 + b.Special4, 0, 255);

            int levelBonus = Equip[0].Index % 10 > 5 ? s.Level * 2 : s.Level;
            s.Damage += b.Damage + buff.Damage + s.Str / 3 + s.Dex / 8 + s.Special1 + levelBonus;
            s.MaxHp = Math.Clamp((s.MaxHp + b.MaxHp + s.Con * Points.HpPerCon) * (100 + b.HpPercent) / 100, 1, 64000);
            s.MaxMp = Math.Clamp((s.MaxMp + b.MaxMp + s.Int * Points.MpPerInt) * (100 + b.MpPercent) / 100, 0, 64000);
            if (buff.ManaToHpPercent > 0)
            {
                // Controle de Mana: parte da mana máxima vira vida máxima enquanto dura.
                int moved = s.MaxMp * buff.ManaToHpPercent / 100;
                s.MaxMp -= moved;
                s.MaxHp = Math.Min(64000, s.MaxHp + moved);
            }
            s.Hp = Math.Min(s.Hp, s.MaxHp);
            s.Mp = Math.Min(s.Mp, s.MaxMp);
            return s;
        }
    }

    /// <summary>Vida e mana máximas com o equipamento.</summary>
    public int MaxHp => CurrentScore.MaxHp;
    public int MaxMp => CurrentScore.MaxMp;

    /// <summary>Mortal, Arch, Celestial ou Sub Celestial: guardado no rosto, junto com o equipamento.</summary>
    public Evolution Evolution
    {
        get => (Evolution)Math.Min(Equip[0].Value2, (byte)Evolution.SubCelestial);
        set => Equip[0].Value2 = (byte)value;
    }
}
