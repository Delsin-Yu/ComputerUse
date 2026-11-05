namespace ComputerUse.Geo;

/// <summary>Integer point in a named coordinate space (desktop physical pixels, or window pixels).</summary>
internal readonly record struct PointI(int X, int Y)
{
    public override string ToString() => X + "," + Y;
}

/// <summary>Integer rectangle in a named coordinate space (top-left origin, exclusive right/bottom).</summary>
internal readonly record struct RectI(int X, int Y, int W, int H)
{
    public int Right => X + W;

    public int Bottom => Y + H;

    public int CenterX => X + W / 2;

    public int CenterY => Y + H / 2;

    public PointI Center => new(CenterX, CenterY);

    public bool IsEmpty => W <= 0 || H <= 0;

    public bool Contains(PointI p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;

    public RectI Inflate(int pad) => new(X - pad, Y - pad, W + pad * 2, H + pad * 2);

    public RectI Offset(int dx, int dy) => new(X + dx, Y + dy, W, H);

    public override string ToString() => X + "," + Y + "," + W + "," + H;
}
