using System.Buffers.Binary;
using System.Text;

namespace WYD.Server.Network;

/// <summary>
/// Monta um pacote campo a campo, na mesma ordem das structs do client.
/// O tamanho total é fixo e conferido em <see cref="ToArray"/>, para que erros de layout apareçam cedo.
/// </summary>
public sealed class PacketWriter
{
    private readonly byte[] _buffer;
    private int _position;

    public PacketWriter(ushort type, int size, ushort id = 0, uint tick = 0)
    {
        if (size < PacketHeader.Size || size > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(size));

        _buffer = new byte[size];
        WriteUInt16((ushort)size);
        WriteByte(0); // KeyWord: preenchido ao cifrar
        WriteByte(0); // CheckSum: preenchido ao cifrar
        WriteUInt16(type);
        WriteUInt16(id);
        WriteUInt32(tick);
    }

    public int Position => _position;

    public PacketWriter WriteByte(byte value)
    {
        _buffer[_position++] = value;
        return this;
    }

    public PacketWriter WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

    public PacketWriter WriteInt16(short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_position), value);
        _position += 2;
        return this;
    }

    public PacketWriter WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position), value);
        _position += 2;
        return this;
    }

    public PacketWriter WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
        return this;
    }

    public PacketWriter WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
        return this;
    }

    public PacketWriter WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position), value);
        _position += 8;
        return this;
    }

    public PacketWriter WriteBytes(ReadOnlySpan<byte> data)
    {
        data.CopyTo(_buffer.AsSpan(_position));
        _position += data.Length;
        return this;
    }

    /// <summary>Escreve uma string de tamanho fixo (char[length] em C), sempre terminada em zero.</summary>
    public PacketWriter WriteString(string? value, int length)
    {
        var field = _buffer.AsSpan(_position, length);
        field.Clear();
        if (!string.IsNullOrEmpty(value))
        {
            int count = Math.Min(value.Length, length - 1);
            Encoding.Latin1.GetBytes(value.AsSpan(0, count), field);
        }
        _position += length;
        return this;
    }

    public PacketWriter Skip(int count)
    {
        _position += count;
        return this;
    }

    public byte[] ToArray()
    {
        if (_position != _buffer.Length)
            throw new InvalidOperationException(
                $"Pacote 0x{PacketHeader.ReadType(_buffer):X3} com layout incorreto: escrito {_position} de {_buffer.Length} bytes.");
        return _buffer;
    }
}
