#if MACCATALYST
using System;

namespace Ansight;

/// <summary>
/// macCatalyst does not support shake gestures; this implementation is a no-op.
/// </summary>
internal partial class ShakeGestureListener
{
    partial void OnEnable() { }
    partial void OnDisable() { }
}
#endif
