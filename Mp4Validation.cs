using System.Buffers.Binary;

namespace DontBlink.Services;

internal static class Mp4Validation
{
    // Check the complete top-level ISO BMFF box layout. Manifest size is not a
    // transfer-length guarantee. This catches error pages and truncated boxes;
    // it does not replace decoding the video/audio streams.
    public static void Validate(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) throw new IOException("Blink returned an empty clip. Please retry.");
        bool fileType = false, movie = false, media = false;
        int offset = 0;
        while (offset < data.Length)
        {
            var remaining = data[offset..];
            if (remaining.Length < 8) throw new IOException("Downloaded MP4 is truncated (incomplete box header). Please retry.");
            ulong length = BinaryPrimitives.ReadUInt32BigEndian(remaining);
            int header = 8;
            if (length == 1)
            {
                if (remaining.Length < 16) throw new IOException("Downloaded MP4 is truncated (incomplete large box header). Please retry.");
                length = BinaryPrimitives.ReadUInt64BigEndian(remaining[8..]);
                header = 16;
            }
            else if (length == 0) length = (ulong)remaining.Length;
            if (length < (ulong)header || length > (ulong)remaining.Length)
                throw new IOException("Downloaded MP4 is truncated or has an invalid box length. Please retry.");
            var type = remaining.Slice(4, 4);
            if (type.SequenceEqual("ftyp"u8) && length >= (ulong)(header + 8)) fileType = true;
            if (type.SequenceEqual("moov"u8) && length > (ulong)header) movie = true;
            if (type.SequenceEqual("mdat"u8) && length > (ulong)header) media = true;
            offset += (int)length;
        }
        if (!fileType || !movie || !media)
            throw new IOException("Blink did not return a complete MP4 clip (missing file type, movie metadata, or video data). Please retry.");
    }
}
