namespace WYD.Server.Game;

/// <summary>
/// Evolução do personagem. Fica no segundo valor de efeito do rosto (Equip[0]): o client 7.54 trata 0 como
/// Mortal e qualquer outro valor como evoluído (e usa a tabela de Celestial quando a capa é de Celestial).
/// </summary>
public enum Evolution : byte
{
    Mortal = 0,
    Arch = 1,
    Celestial = 2,
    SubCelestial = 3,
}

public static class Evolutions
{
    /// <summary>Maior nível de cada evolução como aparece na tela (o servidor guarda nível - 1).</summary>
    public static int MaxShownLevel(Evolution e) => e >= Evolution.Celestial ? 200 : 400;

    /// <summary>Maior nível interno (0 = nível 1 na tela).</summary>
    public static int MaxLevel(Evolution e) => MaxShownLevel(e) - 1;

    public static string Name(Evolution e) => e switch
    {
        Evolution.Mortal => "Mortal",
        Evolution.Arch => "Arch",
        Evolution.Celestial => "Celestial",
        _ => "Sub Celestial",
    };

    /// <summary>Aceita "mortal", "arch", "celestial", "sub" ou o número (0 a 3).</summary>
    public static Evolution? Parse(string text) => text.Trim().ToLowerInvariant() switch
    {
        "0" or "mortal" => Evolution.Mortal,
        "1" or "arch" => Evolution.Arch,
        "2" or "celestial" or "cele" => Evolution.Celestial,
        "3" or "sub" or "subcelestial" or "sub celestial" or "subcele" => Evolution.SubCelestial,
        _ => null,
    };
}
