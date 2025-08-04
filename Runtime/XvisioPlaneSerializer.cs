using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Xvisio.Unity
{
    public static class XvisioPlaneSerializer
    {
        public static List<XvPlane> Deserialize(ReadOnlySpan<byte> data)
        {
            var r = new XvPlaneBufferReader(data);
            var planeCount = r.ReadInt32();
            var planes = new List<XvPlane>(Math.Max(planeCount, 0));

            for (var i = 0; i < planeCount; i++)
            {
                var pointsCount = r.ReadInt32();
                var points = new List<XvVector3d>(Math.Max(pointsCount, 0));
                for (var p = 0; p < pointsCount; p++)
                {
                    var x = r.ReadDouble();
                    var y = r.ReadDouble();
                    var z = r.ReadDouble();
                    points.Add(new XvVector3d(x, y, z));
                }

                var normal = new XvVector3d(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
                var d = r.ReadDouble();
                var idLen = r.ReadInt32();
                var id = r.ReadStringUtf8(idLen);

                planes.Add(new XvPlane { Points = points, Normal = normal, D = d, Id = id });
            }

            return planes;
        }
    }
    
    public readonly struct XvVector3d
    {
        public readonly double X, Y, Z;
        public XvVector3d(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public sealed class XvPlane
    {
        public List<XvVector3d> Points { get; set; } = new();
        public XvVector3d Normal { get; set; }
        public double D { get; set; }
        public string Id { get; set; } = "";
    }


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
            if (Remaining < 4) throw new IndexOutOfRangeException("Buffer too small (int32).");
            var v = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return v;
        }

        public double ReadDouble()
        {
            if (Remaining < 8) throw new IndexOutOfRangeException("Buffer too small (double).");
            var bits = BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(_offset, 8));
            _offset += 8;
            return BitConverter.Int64BitsToDouble(bits);
        }

        public string ReadStringUtf8(int byteLen)
        {
            if (byteLen < 0 || Remaining < byteLen) throw new IndexOutOfRangeException("Buffer too small (string).");

            // If you're on older .NET without the span overload, use: Encoding.UTF8.GetString(_data.Slice(_offset, byteLen).ToArray())
            var s = Encoding.UTF8.GetString(_data.Slice(_offset, byteLen));
            _offset += byteLen;
            return s;
        }
    }

}