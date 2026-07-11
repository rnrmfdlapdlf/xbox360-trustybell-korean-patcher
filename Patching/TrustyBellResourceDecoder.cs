using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TrustyBellKoreanPatcher.Patching
{
    internal sealed class VmtocEntry
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public int DecodedSize { get; set; }
        public byte Flags { get; set; }
    }

    internal static class TrustyBellResourceDecoder
    {
        private const int EntrySize = 0x30;

        public static Dictionary<string, VmtocEntry> ReadVmtoc(string root)
        {
            byte[] data = File.ReadAllBytes(Path.Combine(root, "index.vmtoc"));
            if (data.Length % EntrySize != 0)
            {
                throw new InvalidDataException("index.vmtoc 크기가 올바르지 않습니다.");
            }

            Dictionary<string, VmtocEntry> entries = new Dictionary<string, VmtocEntry>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < data.Length / EntrySize; index++)
            {
                int offset = index * EntrySize;
                int nameLength = 0;
                while (nameLength < 0x20 && data[offset + nameLength] != 0)
                {
                    nameLength++;
                }

                string name = Encoding.ASCII.GetString(data, offset, nameLength).Replace('\\', '/');
                VmtocEntry entry = new VmtocEntry();
                entry.Index = index;
                entry.Name = name;
                entry.DecodedSize = ReadInt32BigEndian(data, offset + 0x20);
                entry.Flags = data[offset + 0x24];
                entries[name] = entry;
            }

            return entries;
        }

        public static byte[] DecodeFile(string root, IDictionary<string, VmtocEntry> entries, string relativePath)
        {
            VmtocEntry entry;
            string normalized = relativePath.Replace('\\', '/');
            if (!entries.TryGetValue(normalized, out entry))
            {
                throw new KeyNotFoundException("index.vmtoc에 없는 리소스입니다: " + relativePath);
            }

            string filePath = Path.Combine(root, entry.Name.Replace('/', Path.DirectorySeparatorChar));
            return DecodePayload(File.ReadAllBytes(filePath), entry.DecodedSize, entry.Flags);
        }

        public static byte[] DecodePayload(byte[] data, int decodedSize, byte flags)
        {
            if (decodedSize < 0)
            {
                throw new InvalidDataException("음수 decoded size입니다.");
            }

            if ((flags & 0x03) == 0)
            {
                if (data.Length < decodedSize)
                {
                    throw new EndOfStreamException("raw 리소스가 decoded size보다 짧습니다.");
                }

                if (data.Length == decodedSize)
                {
                    return data;
                }

                byte[] raw = new byte[decodedSize];
                Buffer.BlockCopy(data, 0, raw, 0, decodedSize);
                return raw;
            }

            RangeReader reader = new RangeReader(data, flags);
            byte[] output = new byte[decodedSize];
            if ((flags & 0x01) == 0)
            {
                for (int index = 0; index < output.Length; index++)
                {
                    output[index] = reader.ReadByte();
                }

                return output;
            }

            byte[] window = new byte[0x1000];
            int windowPosition = 0x0FEE;
            int control = 0;
            int mask = 0;
            int mode = 0;
            int offsetLow = 0;
            int outputPosition = 0;

            while (outputPosition < output.Length)
            {
                if (mode == 0)
                {
                    control = reader.ReadByte();
                    mask = 1;
                    mode = (control & mask) != 0 ? 1 : 2;
                    continue;
                }

                if (mode == 1)
                {
                    byte value = reader.ReadByte();
                    output[outputPosition++] = value;
                    window[windowPosition] = value;
                    windowPosition = (windowPosition + 1) & 0x0FFF;
                }
                else if (mode == 2)
                {
                    offsetLow = reader.ReadByte();
                    mode = 3;
                    continue;
                }
                else
                {
                    int value = reader.ReadByte();
                    int copyOffset = offsetLow + ((value & 0xF0) << 4);
                    int length = (value & 0x0F) + 3;
                    for (int index = 0; index < length && outputPosition < output.Length; index++)
                    {
                        byte copied = window[copyOffset];
                        copyOffset = (copyOffset + 1) & 0x0FFF;
                        output[outputPosition++] = copied;
                        window[windowPosition] = copied;
                        windowPosition = (windowPosition + 1) & 0x0FFF;
                    }
                }

                mask = (mask << 1) & 0xFF;
                if (mask != 0)
                {
                    mode = (control & mask) != 0 ? 1 : 2;
                }
                else
                {
                    mode = 0;
                }
            }

            return output;
        }

        private static int ReadInt32BigEndian(byte[] data, int offset)
        {
            return (data[offset] << 24)
                | (data[offset + 1] << 16)
                | (data[offset + 2] << 8)
                | data[offset + 3];
        }

        private sealed class RangeReader
        {
            private readonly byte[] data;
            private readonly byte flags;
            private readonly int[] frequencies;
            private readonly int[] cumulative;
            private readonly int[] lookup;
            private readonly int total;
            private int position;
            private uint low;
            private uint range;
            private uint code;

            public RangeReader(byte[] data, byte flags)
            {
                this.data = data;
                this.flags = flags;
                frequencies = new int[0x100];
                cumulative = new int[0x100];
                range = UInt32.MaxValue;

                if ((flags & 0x02) != 0)
                {
                    int frequencyTotal = 0;
                    for (int index = 0; index < 0x100; index++)
                    {
                        int value = ReadRawByte();
                        frequencies[index] = value;
                        frequencyTotal += value;
                        cumulative[index] = frequencyTotal;
                    }

                    if (frequencyTotal <= 0)
                    {
                        throw new InvalidDataException("range coder frequency table이 비어 있습니다.");
                    }

                    total = frequencyTotal;
                    lookup = new int[frequencyTotal];
                    int previous = 0;
                    for (int symbol = 0; symbol < cumulative.Length; symbol++)
                    {
                        int high = cumulative[symbol];
                        for (int slot = previous; slot < high; slot++)
                        {
                            lookup[slot] = symbol;
                        }
                        previous = high;
                    }

                    for (int index = 0; index < 4; index++)
                    {
                        code = unchecked((code << 8) | ReadRawByte());
                    }
                }
                else
                {
                    lookup = new int[0];
                    total = 0;
                }
            }

            public byte ReadByte()
            {
                return (flags & 0x02) == 0 ? ReadRawByte() : ReadRangeByte();
            }

            private byte ReadRawByte()
            {
                if (position >= data.Length)
                {
                    throw new EndOfStreamException("압축 리소스가 예기치 않게 끝났습니다.");
                }

                return data[position++];
            }

            private byte ReadRangeByte()
            {
                unchecked
                {
                    while ((((low + range) ^ low) < 0x01000000u))
                    {
                        range <<= 8;
                        low <<= 8;
                        code = (code << 8) | ReadRawByte();
                    }

                    while (range < 0x2000u)
                    {
                        uint oldLow = low;
                        code = (code << 8) | ReadRawByte();
                        low = oldLow << 8;
                        range = ((0u - oldLow) << 8) & 0x001FFF00u;
                    }

                    uint step = range / (uint)total;
                    if (step == 0)
                    {
                        throw new InvalidDataException("range coder step이 0입니다.");
                    }

                    uint slot = (code - low) / step;
                    if (slot >= lookup.Length)
                    {
                        throw new InvalidDataException("range coder lookup 범위를 벗어났습니다.");
                    }

                    int symbol = lookup[slot];
                    int previous = symbol == 0 ? 0 : cumulative[symbol - 1];
                    low += (uint)previous * step;
                    range = (uint)frequencies[symbol] * step;
                    return (byte)symbol;
                }
            }
        }
    }
}
