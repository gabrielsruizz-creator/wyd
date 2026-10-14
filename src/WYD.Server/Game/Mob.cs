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

    /// <summary>NPCs (lojas etc.) e spawns com raio zero ficam parados.</summary>
    public bool Wanders => Template.Merchant == 0 && Math.Max(Spawn.StartRange, Spawn.DestRange) > 0;

    /// <summary>O client só pede a lista da loja quando os 4 bits de baixo de Score.Merchant valem 1.</summary>
    public bool IsShop => (Template.Score.Merchant & 0x0F) == 1;

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

    public byte[] CreatePacket() =>
        ServerPackets.CreateMob(Id, Template.Name, X, Y, Template.Equip.Select(i => i.Index), Template.Guild, Score);
}
