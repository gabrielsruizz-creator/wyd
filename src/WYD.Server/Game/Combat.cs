using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>Fórmulas de combate. São simples por enquanto; ainda não contam os bônus dos itens equipados.</summary>
public static class Combat
{
    /// <summary>
    /// Dano de um ataque: o dano do atacante (para jogadores, a ficha atual já soma FOR, DES, Especial 1 e nível),
    /// variando de 80% a 120%, menos um quarto da defesa do alvo. Sempre pelo menos 1.
    /// </summary>
    public static int Damage(Score attacker, Score target, Random? random = null, int perforation = 0, int absorption = 0)
    {
        random ??= Random.Shared;
        int baseDamage = Math.Max(1, attacker.Damage);
        int rolled = baseDamage * random.Next(80, 121) / 100;
        int ac = target.Ac * (100 - Math.Clamp(perforation, 0, 100)) / 100;   // perfuração: ignora parte da defesa
        int damage = Math.Clamp(rolled - ac / 4, 1, short.MaxValue);
        return Math.Max(1, damage * (100 - Math.Clamp(absorption, 0, 90)) / 100); // absorção: o alvo recebe menos
    }

    /// <summary>Dano do golpe crítico: o dobro.</summary>
    public const int CriticalMultiplier = 2;

    /// <summary>O golpe sai crítico? Chance de crítico × 4 em 1000, como o client mostra (crítico 25 = 10%).</summary>
    public static bool IsCritical(int critical, Random? random = null) =>
        critical > 0 && (random ?? Random.Shared).Next(1000) < critical * 4;
}
