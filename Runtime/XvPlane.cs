using System.Collections.Generic;

namespace Xvisio.Unity
{
    public struct XvPlane
    {
        public string Id { get; set; }
        public XvVector3d Normal { get; set; }
        public double D { get; set; }
        public List<XvVector3d> Points { get; set; }
        public List<XvVector3d> Vertices { get; set; }
        public List<(uint A, uint B, uint C)> Triangles { get; set; }
    }
}