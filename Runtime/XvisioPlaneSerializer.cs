using System;
using System.Collections.Generic;
using System.Linq;

namespace Xvisio.Unity
{
    public static class XvisioPlaneSerializer
    {
        public static void Deserialize(ReadOnlySpan<byte> data, ref List<XvPlane> planes, out int planeCount)
        {
            var reader = new XvPlaneBufferReader(data);
            planeCount = reader.ReadInt32();
            planes ??= new List<XvPlane>(Math.Max(planeCount, 0));
            
            while (planes.Count < planeCount)
                planes.Add(default);

            for (var i = 0; i < planeCount; i++)
            {
                // border points
                var ptsN = reader.ReadInt32();
                var pts = new List<XvVector3d>(ptsN);
                for (var p = 0; p < ptsN; p++)
                    pts.Add(new XvVector3d(reader.ReadDouble(),
                        reader.ReadDouble(),
                        reader.ReadDouble()));

                // normal + d
                var normal = new XvVector3d(
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble());
                var dVal = reader.ReadDouble();

                // id
                var idLen = reader.ReadInt32();
                var id = reader.ReadStringUtf8(idLen);

                // detailed vertices - if supported
                var vertsN = reader.ReadInt32();
                var verts = new List<XvVector3d>(vertsN);
                for (var v = 0; v < vertsN; v++)
                    verts.Add(new XvVector3d(
                        reader.ReadDouble(),
                        reader.ReadDouble(),
                        reader.ReadDouble()));

                // triangles - if supported
                var trisN = reader.ReadInt32();
                var tris = new List<(uint, uint, uint)>(trisN);
                for (var t = 0; t < trisN; t++)
                {
                    var a = reader.ReadUInt32();
                    var b = reader.ReadUInt32();
                    var c = reader.ReadUInt32();
                    tris.Add((a, b, c));
                }

                planes[i] = new XvPlane
                {
                    Id = id,
                    Normal = normal,
                    D = dVal,
                    Points = pts,
                    Vertices = verts,
                    Triangles = tris
                };
            }
        }
    }
}