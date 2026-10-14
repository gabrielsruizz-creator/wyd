namespace WYD.Server.Game;

/// <summary>
/// Distribuição de pontos (0x277): cada clique no "+" da ficha gasta 1 ponto. Tipo 0 = atributo
/// (0 FOR, 1 INT, 2 DES, 3 CON), tipo 1 = especial (1 a 4), tipo 2 = aprender skill (no mestre de skills).
/// </summary>
public static class Points
{
    public const short ModeScore = 0, ModeSpecial = 1, ModeSkill = 2;

    /// <summary>Maior valor de um atributo e de um especial (o especial é um byte na ficha).</summary>
    public const short MaxAttribute = 32000;
    public const byte MaxSpecial = 255;

    /// <summary>Vida a mais por ponto de CON e mana a mais por ponto de INT (somadas na ficha atual, ver Character.CurrentScore).</summary>
    public const int HpPerCon = 2, MpPerInt = 2;

    /// <summary>Aplica um ponto. Retorna o motivo da recusa, ou null se deu certo.</summary>
    public static string? Apply(Character c, short mode, short which)
    {
        ref var s = ref c.BaseScore;
        switch (mode)
        {
            case ModeScore:
                if (c.ScoreBonus <= 0) return "Sem pontos de atributo.";
                switch (which)
                {
                    case 0: if (s.Str >= MaxAttribute) return Full; s.Str++; break;
                    case 1: if (s.Int >= MaxAttribute) return Full; s.Int++; break;
                    case 2: if (s.Dex >= MaxAttribute) return Full; s.Dex++; break;
                    case 3: if (s.Con >= MaxAttribute) return Full; s.Con++; break;
                    default: return "Atributo inválido.";
                }
                c.ScoreBonus--;
                return null;

            case ModeSpecial:
                if (c.SpecialBonus <= 0) return "Sem pontos especiais.";
                switch (which)
                {
                    case 0: if (s.Special1 >= MaxSpecial) return Full; s.Special1++; break;
                    case 1: if (s.Special2 >= MaxSpecial) return Full; s.Special2++; break;
                    case 2: if (s.Special3 >= MaxSpecial) return Full; s.Special3++; break;
                    case 3: if (s.Special4 >= MaxSpecial) return Full; s.Special4++; break;
                    default: return "Especial inválido.";
                }
                c.SpecialBonus--;
                return null;

            case ModeSkill:
                return "Para aprender skills, fale com o mestre de skill da sua classe.";

            default:
                return "Tipo de ponto inválido.";
        }
    }

    private const string Full = "Este atributo já está no máximo.";
}
