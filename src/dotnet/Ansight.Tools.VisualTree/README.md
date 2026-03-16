# Ansight.Tools.VisualTree

Grouped visual tree and screenshot tool registrations for the Ansight .NET SDK.

## Usage

```csharp
using Ansight;
using Ansight.Tools.VisualTree;

var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithReadOnlyToolAccess()
    .Build();
```
