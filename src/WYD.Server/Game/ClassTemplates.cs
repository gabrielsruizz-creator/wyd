using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Valores iniciais de cada classe. São aproximados: ajuste com base nos arquivos
/// de personagem base (BaseSIDCHM) da versão que você usa.
/// </summary>
public static class ClassTemplates
{
    private sealed record Template(short Face, short Str, short Int, short Dex, short Con, int Hp, int Mp);

    private static readonly Dictionary<CharacterClass, Template> Templates = new()
    {
        [CharacterClass.TransKnight] = new(Face: 1, Str: 8, Int: 4, Dex: 7, Con: 6, Hp: 80, Mp: 45),
        [CharacterClass.Foema] = new(Face: 11, Str: 5, Int: 8, Dex: 5, Con: 5, Hp: 60, Mp: 65),
        [CharacterClass.BeastMaster] = new(Face: 21, Str: 6, Int: 6, Dex: 9, Con: 5, Hp: 70, Mp: 55),
        [CharacterClass.Huntress] = new(Face: 31, Str: 8, Int: 9, Dex: 13, Con: 6, Hp: 75, Mp: 50),
    };

    public static bool IsValid(int classId) => classId is >= 0 and <= 3;

    public static Character Create(int accountId, int slot, string name, CharacterClass cls, short x, short y)
    {
        var t = Templates[cls];
        var character = new Character
        {
            AccountId = accountId,
            Slot = slot,
            Name = name,
            Class = cls,
            PosX = x,
            PosY = y,
            BaseScore = new Score
            {
                Level = 0, // o client mostra Level + 1
                Ac = 4,
                Damage = 5,
                AttackRun = Score.DefaultAttackRun,
                MaxHp = t.Hp,
                Hp = t.Hp,
                MaxMp = t.Mp,
                Mp = t.Mp,
                Str = t.Str,
                Int = t.Int,
                Dex = t.Dex,
                Con = t.Con,
            },
        };
        character.Equip[0] = new Item(t.Face);
        return character;
    }
}
