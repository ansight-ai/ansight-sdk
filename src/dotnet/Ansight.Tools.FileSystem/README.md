# Ansight.Tools.FileSystem

Grouped sandboxed file access tool registrations for the Ansight .NET SDK.

## Usage

```csharp
using Ansight;
using Ansight.Tools.FileSystem;

var options = Options.CreateBuilder()
    .WithFileSystemTools()
    .WithReadOnlyToolAccess()
    .Build();
```
