using WYD.Server.Data;
using WYD.Server.Protocol;

namespace WYD.Server.Game;

/// <summary>Um monstro ou NPC no mapa. Os ids começam em <see cref="FirstId"/>, depois dos jogadores.</summary>
public sealed class Mob
{
    public const ushort FirstId = 1000;

    /// <summary>O client trata o id como short com sinal: ids a partir daqui ficariam negativos.</summary>
    public const ushort LastId = 32767;

    public Mob(ushort id, NpcTemplate template, SpawnRule spawn, short x, short y)
    {
        Id = id;
        Template = template;
        Spawn = spawn;
        X = x;
        Y = y;
        Hp = template.Score.MaxHp;
    }

    public ushort Id { get; }
    public NpcTemplate Template { get; }
    public SpawnRule Spawn { get; }
    public short X { get; set; }
    public short Y { get; set; }
    public int Hp { get; set; }

    public bool Alive { get; set; } = true;

    /// <summary>Quando um monstro morto volta ao mapa.</summary>
    public DateTime RespawnAt { get; set; }

    /// <summary>NPCs (lojas, guardas, mestres...) não podem ser atacados.</summary>
    public bool Attackable => Template.Merchant == 0 && !SoulQuest.IsEhre(Template.Name);

    /// <summary>NPCs (lojas etc.) e spawns com raio zero ficam parados.</summary>
    public bool Wanders => Attackable && Math.Max(Spawn.StartRange, Spawn.DestRange) > 0;

    /// <summary>O client só pede a lista da loja quando os 4 bits de baixo de Score.Merchant valem 1.</summary>
    public bool IsShop => (Template.Score.Merchant & 0x0F) == 1;

    /// <summary>Mestre de skill: o client pede a lista dele com o 0x27B, como numa loja (Score.Merchant &amp; 0xF == 3).</summary>
    public bool IsSkillMaster => Template.Merchant == Skills.MasterMerchant && (Template.Score.Merchant & 0x0F) == 3;

    /// <summary>Jogador que o monstro está perseguindo (0 = nenhum): quem bateu nele por último.</summary>
    public ushort Target { get; set; }

    /// <summary>Quando o monstro pode bater de novo.</summary>
    public DateTime NextAttack { get; set; }

    /// <summary>Quando o monstro pode andar de novo.</summary>
    public DateTime NextMove { get; set; }

    public int Speed => Math.Max(1, Template.Score.AttackRun & 0x0F);

    public Score Score
    {
        get
        {
            var score = Template.Score;
            score.Hp = Hp;
            return score;
        }
    }

    /// <summary>Trocas de aparência (ver <see cref="NpcVisuals"/>); definido pelo MobService.</summary>
    public NpcVisuals? Visuals { get; set; }

    /// <summary>Rei do reino (Rei Harabard, azul; Rei Glantuar, vermelho): Merchant 15 e o clã do reino (7 ou 8).</summary>
    public bool IsKing => (Template.Score.Merchant & 0x0F) == 15 && Template.Clan is (byte)Kingdom.Blue or (byte)Kingdom.Red;

    /// <summary>Oracle do reino que faz a Eternal Stone (ver <see cref="Oracle"/>), ou null.</summary>
    public Kingdom? OracleKingdom => Oracle.KingdomOf(Template.Name);

    /// <summary>
    /// O 0x364 do mob, sem itens que o client não tem (ver <see cref="NpcVisuals"/>); reis vão com o
    /// <see cref="KingLook"/> e os Oracles com o tipo que sempre abre a pergunta.
    /// </summary>
    public byte[] CreatePacket()
    {
        if (IsKing)
            return ServerPackets.CreateMob(Id, Template.Name, X, Y, KingLook.Equip((Kingdom)Template.Clan), 0,
                KingLook.Score(Template.Score.Merchant));

        var score = Score;
        if (OracleKingdom is not null) score.Merchant = (byte)((score.Merchant & 0xF0) | Oracle.ClientMerchant);
        else if (SoulQuest.IsEhre(Template.Name)) score.Merchant = (byte)((score.Merchant & 0xF0) | SoulQuest.EhreClientMerchant);
        return ServerPackets.CreateMob(Id, Template.Name, X, Y,
            Template.Equip.Select((item, slot) => Visuals?.Visible(Template.Name, slot, item.Index) ?? item.Index),
            Template.Guild, score);
    }
}
