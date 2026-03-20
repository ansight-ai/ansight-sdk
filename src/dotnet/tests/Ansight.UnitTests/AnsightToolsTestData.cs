using Microsoft.Data.Sqlite;

namespace Ansight.UnitTests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "Ansight.UnitTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateDirectory(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var directoryPath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    public string WriteTextFile(string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        var filePath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    public string WriteBinaryFile(string relativePath, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        var filePath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    public string CreateSqliteDatabase(string relativePath, params string[] statements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(statements);

        var databasePath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        foreach (var statement in statements)
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.CommandText = statement;
            _ = command.ExecuteNonQuery();
        }

        return databasePath;
    }

    public void Dispose()
    {
        if (!Directory.Exists(RootPath))
        {
            return;
        }

        Directory.Delete(RootPath, recursive: true);
    }
}
