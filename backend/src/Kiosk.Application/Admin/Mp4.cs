using System.Buffers.Binary;

namespace Kiosk.Application.Admin;

/// <summary>
/// Just enough of the ISO base media format (MP4) to check an upload: that it is an MP4 at all (an ftyp box first)
/// and how long it plays (moov → mvhd duration / timescale). No decoding, so no native video libraries are needed.
/// </summary>
public static class Mp4
{
    /// <summary>Playing time in seconds, or null when the bytes are not an MP4 with a readable movie header.</summary>
    public static double? ReadDurationSeconds(ReadOnlySpan<byte> file)
    {
        if (!TryReadBox(file, out var type, out _, out _) || type != "ftyp")
            return null;

        var moov = FindBox(file, "moov");
        if (moov is not { } m)
            return null;
        var mvhd = FindBox(file.Slice(m.Start, m.Length), "mvhd");
        if (mvhd is not { } h)
            return null;

        var header = file.Slice(m.Start + h.Start, h.Length);
        if (header.Length < 4)
            return null;
        var version = header[0];
        // version 0: created(4) modified(4) timescale(4) duration(4); version 1: created(8) modified(8) timescale(4) duration(8)
        uint timescale;
        ulong duration;
        if (version == 0 && header.Length >= 20)
        {
            timescale = BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
            duration = BinaryPrimitives.ReadUInt32BigEndian(header[16..]);
        }
        else if (version == 1 && header.Length >= 32)
        {
            timescale = BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
            duration = BinaryPrimitives.ReadUInt64BigEndian(header[24..]);
        }
        else
        {
            return null;
        }

        return timescale == 0 ? null : (double)duration / timescale;
    }

    /// <summary>Finds a direct child box; returns where its payload starts and how long it is.</summary>
    private static (int Start, int Length)? FindBox(ReadOnlySpan<byte> container, string wanted)
    {
        var offset = 0;
        while (offset < container.Length)
        {
            if (!TryReadBox(container[offset..], out var type, out var headerSize, out var boxSize))
                return null;
            if (type == wanted)
                return (offset + headerSize, (int)(boxSize - headerSize));
            offset += (int)boxSize;
        }
        return null;
    }

    private static bool TryReadBox(ReadOnlySpan<byte> data, out string type, out int headerSize, out long boxSize)
    {
        type = "";
        headerSize = 8;
        boxSize = 0;
        if (data.Length < 8)
            return false;

        boxSize = BinaryPrimitives.ReadUInt32BigEndian(data);
        type = System.Text.Encoding.ASCII.GetString(data.Slice(4, 4));
        if (boxSize == 1)
        {
            if (data.Length < 16)
                return false;
            boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian(data[8..]);
            headerSize = 16;
        }
        else if (boxSize == 0)
        {
            boxSize = data.Length; // box runs to the end of the file
        }

        return boxSize >= headerSize && boxSize <= data.Length;
    }
}
