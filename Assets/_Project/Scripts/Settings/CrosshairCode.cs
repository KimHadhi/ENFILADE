using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// A drawn crosshair as text to share with friends, like CS2's share codes: "ENF-" and 21 base32 digits in groups of
/// seven. The fields are bit-packed into 12 bytes (opacities and saturation in 32 or 64 steps, colour channels in 256);
/// a later version appends whole bytes after them, so an older code still reads with the missing fields at their
/// defaults. The last byte is a checksum, so a mistyped code is refused instead of turning into a strange crosshair.
/// </summary>
public static class CrosshairCode
{
    public const string Prefix = "ENF-";
    private const string Digits = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford's base32: no I, L, O or U to misread
    private const int Version = 1;
    private const int DataBytes = 12; // version 1's fields
    private const int Group = 7;

    public static string Encode(CrosshairSettings s)
    {
        var bits = new BitWriter();
        bits.Write(Version, 3);
        bits.Write((s.roundDot ? 1 : 0) | (s.spriteLines ? 2 : 0) | (s.spinning ? 4 : 0) | (s.rainbow ? 8 : 0), 4);
        bits.Write(s.dotSize, 4);
        bits.Write(s.lineCount, 4);
        bits.Write(Round(s.lineLength), 6);
        bits.Write(Round(s.lineThickness), 5);
        bits.Write(Round(s.gap), 6);
        bits.Write(Round(s.angle / 5f), 6);
        bits.Write(Round(s.spin / 5f), 7);
        bits.Write(Level(s.color.r, 8), 8);
        bits.Write(Level(s.color.g, 8), 8);
        bits.Write(Level(s.color.b, 8), 8);
        bits.Write(Level(s.color.a, 6), 6);
        bits.Write(Round(10f / s.rainbowSpeed), 7); // seconds per cycle, in tenths
        bits.Write(Level(s.rainbowSaturation, 6), 6);
        bits.Write(Round(s.outline), 3);
        bits.Write(Level(s.outlineOpacity, 5), 5);
        var bytes = bits.Bytes;
        bytes.Add(Checksum(bytes, bytes.Count));
        string digits = ToBase32(bytes);
        var code = new StringBuilder(Prefix);
        for (int i = 0; i < digits.Length; i += Group)
        {
            if (i > 0) code.Append('-');
            code.Append(digits, i, Mathf.Min(Group, digits.Length - i));
        }
        return code.ToString();
    }

    /// <summary>Reads the first code in the text, so a whole pasted chat line works too. Case does not matter.</summary>
    public static bool TryDecode(string text, out CrosshairSettings settings)
    {
        settings = null;
        if (string.IsNullOrEmpty(text)) return false;
        text = text.ToUpperInvariant();
        int start = text.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0) return false;
        var digits = new StringBuilder();
        for (int i = start + Prefix.Length; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '-') continue;
            c = c == 'O' ? '0' : c == 'I' || c == 'L' ? '1' : c;
            if (Digits.IndexOf(c) < 0) break;
            digits.Append(c);
        }
        var bytes = FromBase32(digits.ToString());
        int count = bytes.Count - 1; // the checksum is last
        if (count < DataBytes || bytes[count] != Checksum(bytes, count)) return false;

        var bits = new BitReader(bytes);
        if (bits.Read(3) < 1) return false;
        int flags = bits.Read(4);
        settings = new CrosshairSettings
        {
            custom = true,
            roundDot = (flags & 1) != 0,
            spriteLines = (flags & 2) != 0,
            spinning = (flags & 4) != 0,
            rainbow = (flags & 8) != 0,
        };
        settings.dotSize = bits.Read(4);
        settings.lineCount = bits.Read(4);
        settings.lineLength = bits.Read(6);
        settings.lineThickness = bits.Read(5);
        settings.gap = bits.Read(6);
        settings.angle = bits.Read(6) * 5f;
        settings.spin = bits.Read(7) * 5f;
        settings.color = new Color(Fraction(bits.Read(8), 8), Fraction(bits.Read(8), 8), Fraction(bits.Read(8), 8),
            Fraction(bits.Read(6), 6));
        settings.rainbowSpeed = 10f / Mathf.Max(1, bits.Read(7));
        settings.rainbowSaturation = Fraction(bits.Read(6), 6);
        settings.outline = bits.Read(3);
        settings.outlineOpacity = Fraction(bits.Read(5), 5);
        settings.Clamp();
        return true;
    }

    private static int Round(float value) => Mathf.RoundToInt(value);
    // A 0..1 value as one of 2^bits even steps, and back.
    private static int Level(float value, int bits) => Mathf.RoundToInt(Mathf.Clamp01(value) * ((1 << bits) - 1));
    private static float Fraction(int level, int bits) => level / (float)((1 << bits) - 1);

    private static byte Checksum(List<byte> bytes, int count)
    {
        int sum = 0x5A;
        for (int i = 0; i < count; i++) sum = (sum * 31 + bytes[i]) & 0xFF;
        return (byte)sum;
    }

    private static string ToBase32(List<byte> bytes)
    {
        var text = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                text.Append(Digits[(buffer >> bits) & 31]);
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) text.Append(Digits[(buffer << (5 - bits)) & 31]);
        return text.ToString();
    }

    private static List<byte> FromBase32(string digits)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (char c in digits)
        {
            buffer = (buffer << 5) | Digits.IndexOf(c);
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }
        return bytes;
    }

    /// <summary>Most significant bit first. A value that does not fit its width is clamped, so it cannot spill into the next field.</summary>
    private sealed class BitWriter
    {
        public readonly List<byte> Bytes = new List<byte>();
        private int length;

        public void Write(int value, int count)
        {
            value = Mathf.Clamp(value, 0, (1 << count) - 1);
            for (int i = count - 1; i >= 0; i--, length++)
            {
                if ((length & 7) == 0) Bytes.Add(0);
                if (((value >> i) & 1) != 0) Bytes[Bytes.Count - 1] |= (byte)(0x80 >> (length & 7));
            }
        }
    }

    private sealed class BitReader
    {
        private readonly List<byte> bytes;
        private int position;

        public BitReader(List<byte> bytes) { this.bytes = bytes; }

        public int Read(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++, position++)
                value = (value << 1) | ((bytes[position >> 3] >> (7 - (position & 7))) & 1);
            return value;
        }
    }
}
