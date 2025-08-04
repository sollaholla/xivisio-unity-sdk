using UnityEngine;

namespace Xvisio.Unity
{
    public readonly struct XvVector3d
    {
        public readonly double X, Y, Z;

        public XvVector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static implicit operator Vector3(XvVector3d rhs)
        {
            return new Vector3(
                (float)rhs.X, -(float)rhs.Y, (float)rhs.Z);
        }
    }
}