using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>Fórmulas de combate. São simples por enquanto; ainda não contam os bônus dos itens equipados.</summary>
public static class Combat
{
    /// <summary>
    /// Dano de um ataque: o dano do atacante mais metade da força, variando de 80% a 120%,
    /// menos um quarto da defesa do alvo. Sempre pelo menos 1.
    /// </summary>
    public static int Damage(Score attacker, Score target, Random? random = null)
    {
        random ??= Random.Shared;
        int baseDamage = Math.Max(1, attacker.Damage + attacker.Str / 2);
        int rolled = baseDamage * random.Next(80, 121) / 100;
        return Math.Clamp(rolled - target.Ac / 4, 1, short.MaxValue);
    }
}
