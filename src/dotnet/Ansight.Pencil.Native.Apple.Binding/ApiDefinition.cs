using Foundation;
using ObjCRuntime;

namespace Ansight.Pencil.Native.Apple;

[BaseType(typeof(NSObject))]
interface ANSPencilRuntime
{
    [Static]
    [Export("isSupported")]
    bool IsSupported { get; }

    [Static]
    [Export("executeWithArgumentsJson:")]
    string Execute(string argumentsJson);
}
