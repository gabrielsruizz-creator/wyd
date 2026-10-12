using System.Buffers.Binary;

namespace WYD.Server.Network;

/// <summary>
/// Cabeçalho de 12 bytes presente em todo pacote (MSG_STANDARD):
/// <code>
/// 0  ushort Size
/// 2  byte   KeyWord
/// 3  byte   CheckSum
/// 4  ushort Type      (opcode)
/// 6  ushort ID        (id da conexão / mob)
/// 8  uint   Tick
/// </code>
/// </summary>
public static class PacketHeader
{
    public const int Size = 12;
    public const int KeyWordOffset = 2;
    public const int CheckSumOffset = 3;
    public const int TypeOffset = 4;
    public const int IdOffset = 6;
    public const int TickOffset = 8;

    /// <summary>A cifra começa no campo Type.</summary>
    public const int EncryptedStart = 4;

    /// <summary>Maior pacote aceito vindo do client.</summary>
    public const int MaxPacketSize = 8192;

    /// <summary>Primeiros 4 bytes que o client envia ao conectar (INITCODE).</summary>
    public const uint InitCode = 0x1F11F311;

    public static int ReadSize(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet);
    public static ushort ReadType(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet[TypeOffset..]);
    public static ushort ReadId(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt16LittleEndian(packet[IdOffset..]);
    public static uint ReadTick(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt32LittleEndian(packet[TickOffset..]);
}
