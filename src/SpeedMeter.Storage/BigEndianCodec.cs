using System;
using System.Buffers.Binary;

namespace SpeedMeter.Storage;

/// <summary>
/// Lossless codec for 64-bit unsigned source counter checkpoints in SQLite BLOBs.
/// Fixed 8-byte big-endian representation avoids SQLite signed INTEGER overflow.
/// </summary>
public static class BigEndianCodec
{
    public const int BlobLength = 8;

    public static byte[] EncodeUInt64(ulong value)
    {
        var buffer = new byte[BlobLength];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        return buffer;
    }

    public static ulong DecodeUInt64(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != BlobLength)
        {
            throw new ArgumentException($"Big-endian counter BLOB must be exactly {BlobLength} bytes; received {bytes.Length}.", nameof(bytes));
        }
        return BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }
}
