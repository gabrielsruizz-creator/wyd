using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>
/// Como os reis aparecem para o client. Os dados originais do Rei Harabard e do Rei Glantuar derrubam o client
/// 7.54 (tirando o rei do mapa o crash some), então o client recebe um rei "novo": a aparência e a ficha do
/// Guarda_Real do mesmo castelo, que fica ao lado dele sem problema. Só o tipo de NPC (Merchant) e o nome são
/// do rei, para o clique abrir a evolução. No servidor o rei continua com os dados originais.
/// </summary>
public static class KingLook
{
    public const int EquipSlots = 16;

    /// <summary>Visual do Guarda_Real azul: rosto 60, armadura 165-168, Soul Blade, Tower Shield, capa 734.</summary>
    private static readonly short[] Blue = [60, 0, 165, 166, 167, 168, 868, 1708, 0, 0, 0, 0, 0, 0, 0, 734];

    /// <summary>Visual do Guarda_Real vermelho: rosto 305, armadura 170-173, Soul Blade, Tower Shield, capa 735.</summary>
    private static readonly short[] Red = [305, 0, 170, 171, 172, 173, 868, 1708, 0, 0, 0, 0, 0, 0, 0, 735];

    public static short[] Equip(Kingdom kingdom) => kingdom == Kingdom.Red ? Red : Blue;

    /// <summary>Ficha do Guarda_Real, com o tipo de NPC do rei.</summary>
    public static Score Score(byte merchant) => new()
    {
        Level = 13, Ac = 3130, Damage = 1447, Merchant = merchant, AttackRun = 4,
        MaxHp = 24000, Hp = 24000, Str = 14, Int = 100, Dex = 80, Con = 300,
    };
}
