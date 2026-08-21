using System.Numerics;
using System.Runtime.InteropServices;

namespace AssettoServer.Server.Ai.Splines;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SplinePoint
{
    public int Id;
    public Vector3 Position;
    public float Speed;
    public float Radius;
    public float Camber;
    public float Length;
    public float SideLeft;
    public float SideRight;

    public int JunctionStartId;
    public int JunctionEndId;
    public int PreviousId;
    public int NextId;
    public int LeftId;
    public int RightId;
    public int LanesId;
}
