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

Configure additional tagged roots:

```csharp
using Ansight;
using Ansight.Tools.FileSystem;

var options = Options.CreateBuilder()
    .WithFileSystemTools(fileSystem =>
    {
        fileSystem.AddRoot("logs", "/absolute/path/to/logs");
    })
    .WithReadOnlyToolAccess()
    .Build();
```
