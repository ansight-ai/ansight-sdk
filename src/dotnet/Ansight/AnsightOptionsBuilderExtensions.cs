namespace Ansight;

using Ansight.Tools.Database;
using Ansight.Tools.FileSystem;
using Ansight.Tools.Preferences;
using Ansight.Tools.Reflection;
using Ansight.Tools.SecureStorage;
using Ansight.Tools.VisualTree;
using System.Reflection;

/// <summary>
/// Provides all-in-one Ansight extensions for <see cref="Options.OptionsBuilder" />.
/// </summary>
public static class AnsightOptionsBuilderExtensions
{
    private const ushort DefaultSampleFrequencyMilliseconds = 400;
    private const ushort DefaultRetentionPeriodSeconds = 120;
    private const ushort DefaultSessionJpegCaptureIntervalMilliseconds = 2000;
    private const int DefaultSessionJpegCaptureQuality = 60;
    private const int DefaultSessionJpegCaptureMaxWidth = 480;

    /// <summary>
    /// Applies the default Ansight runtime configuration, registers the non-MAUI remote tools, and enables all tool scopes.
    /// </summary>
    /// <param name="builder">The Ansight options builder.</param>
    /// <returns>The current options builder.</returns>
    public static Options.OptionsBuilder WithAnsightSdk(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithAnsightSdk(static _ => { });
    }

    /// <summary>
    /// Applies the default Ansight runtime configuration, enables all tool scopes, runs a customization callback,
    /// and registers the non-MAUI remote tools.
    /// </summary>
    /// <param name="builder">The Ansight options builder.</param>
    /// <param name="configure">
    /// A callback that customizes the existing Ansight options builder after runtime defaults and default tool access,
    /// but before default remote tool registration.
    /// Registering a tool suite inside this callback replaces the default all-in-one registration for that suite.
    /// </param>
    /// <returns>The current options builder.</returns>
    public static Options.OptionsBuilder WithAnsightSdk(
        this Options.OptionsBuilder builder,
        Action<Options.OptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder = builder
            .WithAnsightDefaults()
            .WithAllToolAccess();
        configure(builder);
        return builder.WithAnsightRemoteTools();
    }

    /// <summary>
    /// Applies the default runtime configuration without registering remote tools or enabling tool access.
    /// </summary>
    /// <param name="builder">The Ansight options builder.</param>
    /// <returns>The current options builder.</returns>
    public static Options.OptionsBuilder WithAnsightDefaults(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder = builder
            .WithFramesPerSecond()
            .WithSampleFrequencyMilliseconds(DefaultSampleFrequencyMilliseconds)
            .WithRetentionPeriodSeconds(DefaultRetentionPeriodSeconds)
            .WithSessionJpegCapture(
                DefaultSessionJpegCaptureIntervalMilliseconds,
                DefaultSessionJpegCaptureQuality,
                DefaultSessionJpegCaptureMaxWidth)
            .WithHostAutoProbe();

        var bundledHostConnectionAssembly = ResolveDefaultBundledHostConnectionAssembly();
        if (bundledHostConnectionAssembly is not null)
        {
            builder = builder.WithBundledHostConnection(bundledHostConnectionAssembly);
        }

#if IOS || MACCATALYST
        builder = builder.WithPlatformPairing();
#endif

        return builder;
    }

    /// <summary>
    /// Registers all non-MAUI remote tool suites, skipping any suite that already has a tool registered on the builder.
    /// </summary>
    /// <param name="builder">The Ansight options builder.</param>
    /// <returns>The current options builder.</returns>
    public static Options.OptionsBuilder WithAnsightRemoteTools(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!ContainsAnyTool(builder, visualTreeSuiteToolIds))
        {
            builder = builder.WithVisualTreeTools();
        }

        if (!ContainsAnyTool(builder, databaseSuiteToolIds))
        {
            builder = builder.WithDatabaseTools();
        }

        if (!ContainsAnyTool(builder, fileSystemSuiteToolIds))
        {
            builder = builder.WithFileSystemTools();
        }

        if (!ContainsAnyTool(builder, preferencesSuiteToolIds))
        {
            builder = builder.WithPreferencesTools();
        }

        if (!ContainsAnyTool(builder, reflectionSuiteToolIds))
        {
            builder = builder.WithReflectionTools();
        }

        if (!ContainsAnyTool(builder, secureStorageSuiteToolIds))
        {
            builder = builder.WithSecureStorageTools();
        }

        return builder;
    }

    private static bool ContainsAnyTool(Options.OptionsBuilder builder, IEnumerable<string> toolIds)
    {
        foreach (var toolId in toolIds)
        {
            if (builder.ContainsTool(toolId))
            {
                return true;
            }
        }

        return false;
    }

    private static Assembly? ResolveDefaultBundledHostConnectionAssembly()
        => Assembly.GetEntryAssembly();

    private static readonly string[] visualTreeSuiteToolIds =
    [
        VisualTreeToolIds.GetVisualTree,
        VisualTreeToolIds.GetScreenshot,
        VisualTreeToolIds.InspectNode,
        VisualTreeToolIds.ShowOverlay,
        VisualTreeToolIds.GetOverlay,
        VisualTreeToolIds.QueryOverlays,
        VisualTreeToolIds.UpdateOverlay,
        VisualTreeToolIds.RemoveOverlay,
        VisualTreeToolIds.ClearOverlays
    ];

    private static readonly string[] databaseSuiteToolIds =
    [
        DatabaseToolIds.ListDatabases,
        DatabaseToolIds.DescribeSchema,
        DatabaseToolIds.Query
    ];

    private static readonly string[] fileSystemSuiteToolIds =
    [
        FileSystemToolIds.ListDirectory,
        FileSystemToolIds.ReadFile,
        FileSystemToolIds.GetFileChecksum,
        FileSystemToolIds.DownloadFile,
        FileSystemToolIds.BeginBinaryDownload,
        FileSystemToolIds.PushFile,
        FileSystemToolIds.CopyFile,
        FileSystemToolIds.MoveFile,
        FileSystemToolIds.DeleteFile
    ];

    private static readonly string[] preferencesSuiteToolIds =
    [
        PreferencesToolIds.ListKeys,
        PreferencesToolIds.GetValue,
        PreferencesToolIds.SetValue,
        PreferencesToolIds.RemoveKey
    ];

    private static readonly string[] reflectionSuiteToolIds =
    [
        ReflectionToolIds.ListRoots,
        ReflectionToolIds.InspectObject,
        ReflectionToolIds.DescribeType,
        ReflectionToolIds.SetMemberValue,
        ReflectionToolIds.InvokeMethod
    ];

    private static readonly string[] secureStorageSuiteToolIds =
    [
        SecureStorageToolIds.GetValue,
        SecureStorageToolIds.SetValue,
        SecureStorageToolIds.RemoveKey
    ];
}
