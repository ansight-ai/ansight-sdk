# Ansight.Native.Apple.Binding

Internal slim binding package for the Ansight Apple runtime.

The package builds and binds `AnsightDotNetBridge.xcframework`, whose curated
Objective-C surface delegates to the Swift `AnsightCore` implementation.

This framework records normal Pencil input but contains no Pencil simulation
implementation or activation API. Experimental playback is supplied separately by
`Ansight.Pencil` and its own `Ansight.Pencil.Native.Apple.Binding` framework.

The binding project tracks shared Swift/C sources outside its Xcode project directory
and invalidates its native-resource package after a framework rebuild. This prevents
incremental NuGet builds from retaining an older native binary.
