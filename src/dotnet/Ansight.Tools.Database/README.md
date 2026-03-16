# Ansight.Tools.Database

Grouped database inspection tool registrations for the Ansight .NET SDK.

## Usage

```csharp
using Ansight;
using Ansight.Tools.Database;

var options = Options.CreateBuilder()
    .WithDatabaseTools()
    .WithReadOnlyToolAccess()
    .Build();
```
