using System.Numerics;

namespace ActivationMedium;

sealed class Motion
{
    const float Moved = 1;

    Vector2? _anchor;
    long _since;

    internal long StillMs(Vector3? position, long nowMs)
    {
        if (position is not { } at)
        {
            _anchor = null;
            return 0;
        }
        var flat = new Vector2(at.X, at.Y);
        if (_anchor is not { } anchor || Vector2.DistanceSquared(anchor, flat) > Moved * Moved)
        {
            _anchor = flat;
            _since = nowMs;
        }
        return nowMs - _since;
    }
}
