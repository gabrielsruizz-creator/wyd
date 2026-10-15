using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WYD.Server.Config;

namespace WYD.Server.Data;

/// <summary>Uma skill da SkillData.bin: o que interessa ao servidor por enquanto.</summary>
public sealed record SkillDefinition(int Index, int SkillPoint, int TargetType, int ManaSpent, int Delay, int Range,
    int InstanceType = 0, int InstanceValue = 0, int AffectType = 0, int AffectValue = 0, int AffectTime = 0,
    int TickType = 0, int TickValue = 0);

/// <summary>
/// SkillData.bin do client 7.54: registros de 96 bytes em XOR 0x5A (104 skills) e um int de checksum no fim.
/// Cada registro começa com int SkillPoint (pontos de skill para aprender), TargetType, ManaSpent, Delay, Range,
/// InstanceType (1 dano físico, 2-5 dano mágico, 6 cura...), InstanceValue, TickType, TickValue, AffectType,
/// AffectValue, AffectTime.
/// A skill n é o item 5000 + n do ItemList (24 por classe: TK 0-23, FM 24-47, BM 48-71, HT 72-95).
/// </summary>
public sealed class SkillData
{
    public const int RecordSize = 96;
    private const byte XorKey = 0x5A;

    private readonly SkillDefinition[] _skills;

    public SkillData(IReadOnlyList<SkillDefinition> skills) => _skills = skills.ToArray();

    public SkillDefinition? this[int index] => index >= 0 && index < _skills.Length ? _skills[index] : null;

    public int Count => _skills.Length;

    public static SkillData Parse(byte[] file)
    {
        if (file.Length < RecordSize || (file.Length - 4) % RecordSize != 0 && file.Length % RecordSize != 0)
            throw new InvalidDataException($"tamanho {file.Length} não é múltiplo de {RecordSize} (+4 do checksum).");

        int count = file.Length / RecordSize;
        var plain = new byte[count * RecordSize];
        for (int i = 0; i < plain.Length; i++) plain[i] = (byte)(file[i] ^ XorKey);

        var skills = new List<SkillDefinition>(count);
        for (int k = 0; k < count; k++)
        {
            int I(int field) => BinaryPrimitives.ReadInt32LittleEndian(plain.AsSpan(k * RecordSize + field * 4));
            skills.Add(new SkillDefinition(k, I(0), I(1), I(2), I(3), I(4), I(5), I(6), I(9), I(10), I(11), I(7), I(8)));
        }
        return new SkillData(skills);
    }

    /// <summary>Codifica de volta (para os testes).</summary>
    public static byte[] Encode(IEnumerable<SkillDefinition> skills)
    {
        var list = skills.ToList();
        int count = list.Count == 0 ? 0 : list.Max(s => s.Index) + 1;
        var data = new byte[count * RecordSize + 4];
        foreach (var s in list)
        {
            var span = data.AsSpan(s.Index * RecordSize);
            BinaryPrimitives.WriteInt32LittleEndian(span, s.SkillPoint);
            BinaryPrimitives.WriteInt32LittleEndian(span[4..], s.TargetType);
            BinaryPrimitives.WriteInt32LittleEndian(span[8..], s.ManaSpent);
            BinaryPrimitives.WriteInt32LittleEndian(span[12..], s.Delay);
            BinaryPrimitives.WriteInt32LittleEndian(span[16..], s.Range);
            BinaryPrimitives.WriteInt32LittleEndian(span[20..], s.InstanceType);
            BinaryPrimitives.WriteInt32LittleEndian(span[24..], s.InstanceValue);
            BinaryPrimitives.WriteInt32LittleEndian(span[28..], s.TickType);
            BinaryPrimitives.WriteInt32LittleEndian(span[32..], s.TickValue);
            BinaryPrimitives.WriteInt32LittleEndian(span[36..], s.AffectType);
            BinaryPrimitives.WriteInt32LittleEndian(span[40..], s.AffectValue);
            BinaryPrimitives.WriteInt32LittleEndian(span[44..], s.AffectTime);
        }
        for (int i = 0; i < count * RecordSize; i++) data[i] ^= XorKey;
        return data;
    }
}

/// <summary>A SkillData.bin carregada, se existir em data/.</summary>
public sealed class SkillCatalog
{
    public SkillCatalog(IOptions<ServerOptions> options, ILogger<SkillCatalog> logger)
    {
        string path = PathResolver.Resolve(options.Value.SkillDataPath);
        if (!File.Exists(path))
        {
            logger.LogWarning("Sem SkillData.bin em {Path}: as skills não têm efeito (buffs, magias, auras) e aprender não gasta pontos. Copie a SkillData.bin do client para lá.", path);
            return;
        }
        try
        {
            Data = SkillData.Parse(File.ReadAllBytes(path));
            logger.LogInformation("SkillData.bin carregada: {Count} skills.", Data.Count);
        }
        catch (InvalidDataException ex)
        {
            logger.LogWarning("SkillData.bin inválida: {Message}", ex.Message);
        }
    }

    public SkillCatalog(SkillData? data) => Data = data;

    public SkillData? Data { get; }
}
