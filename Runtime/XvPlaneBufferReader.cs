using System;
using System.Buffers.Binary;
using System.Text;

namespace Xvisio.Unity
{
    internal ref struct XvPlaneBufferReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public XvPlaneBufferReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        private int Remaining => _data.Length - _offset;

        public int ReadInt32()
        {
            if (Remaining < 4) throw new IndexOutOfRangeException();
            var v = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return v;
        }

        public uint ReadUInt32()
        {
            if (Remaining < 4) throw new IndexOutOfRangeException();
            var v = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return v;
        }

        public double ReadDouble()
        {
            if (Remaining < 8) throw new IndexOutOfRangeException();
            var bits = BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(_offset, 8));
            _offset += 8;
            return BitConverter.Int64BitsToDouble(bits);
        }

        public string ReadStringUtf8(int byteLen)
        {
            if (byteLen < 0 || Remaining < byteLen) throw new IndexOutOfRangeException();
            var s = Encoding.UTF8.GetString(_data.Slice(_offset, byteLen));
            _offset += byteLen;
            return s;
        }
    }
}