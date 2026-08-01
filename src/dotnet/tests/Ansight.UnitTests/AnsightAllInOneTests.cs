using Ansight.Maui;
using Ansight.Tools;
using Ansight.Tools.Database;
using Ansight.Tools.FileSystem;
using Ansight.Tools.Maui;
using Ansight.Tools.Preferences;
using Ansight.Tools.Reflection;
using Ansight.Tools.SecureStorage;
using Ansight.Tools.VisualTree;
using System.Reflection;

namespace Ansight.UnitTests;

public sealed class AnsightAllInOneTests
{
    [Fact]
    public void WithAnsightSdk_ConfiguresDefaultsAndRemoteTools()
    {
        var bundledConfigAssembly = typeof(AnsightAllInOneTests).Assembly;

        var options = Options.CreateBuilder()
            .WithAnsightSdk(ansight => ansight.WithBundledHostConnection(bundledConfigAssembly))
            .Build();

        Assert.True(options.EnableFramesPerSecond);
        Assert.Equal(400, options.SampleFrequencyMilliseconds);
        Assert.Equal(120, options.RetentionPeriodSeconds);
        Assert.NotNull(options.SessionJpegCapture);
        Assert.Equal(2000, options.SessionJpegCapture.IntervalMilliseconds);
        Assert.Equal(60, options.SessionJpegCapture.Quality);
        Assert.Equal(480, options.SessionJpegCapture.MaxWidth);
        Assert.True(options.SessionJpegCapture.CaptureGpuBackedSurfaces);
        Assert.NotNull(options.TouchCapture);
        Assert.True(options.HostAutoProbe.Enabled);
        Assert.Equal(bundledConfigAssembly, options.HostConnection.BundledConfigAssembly);
        Assert.Equal(
            [ToolScope.Read, ToolScope.Write, ToolScope.Delete],
            options.ToolGuard.AllowedScopes.OrderBy(scope => scope));

        Assert.Equal(expectedRemoteToolIds, options.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public void WithAnsightSdk_AllowsSecureStorageConfigurationBeforeDefaultToolRegistration()
    {
        var options = Options.CreateBuilder()
            .WithAnsightSdk(ansight => ansight.WithSecureStorageTools(secure =>
            {
                secure.WithStorageIdentifier("AnsightHarness");
                secure.AllowKeyPrefix("ansight.secure.");
            }))
            .Build();

        Assert.Equal(
            expectedRemoteToolIds.OrderBy(toolId => toolId),
            options.Tools.Select(tool => tool.Id).OrderBy(toolId => toolId));

        AssertConfiguredSecureStorage(options);
    }

    [Fact]
    public void WithAnsightSdk_AllowsToolGuardOverrideBeforeDefaultToolRegistration()
    {
        var options = Options.CreateBuilder()
            .WithAnsightSdk(ansight => ansight.WithReadOnlyToolAccess())
            .Build();

        Assert.Equal([ToolScope.Read], options.ToolGuard.AllowedScopes.OrderBy(scope => scope));
        Assert.Equal(expectedRemoteToolIds, options.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public void WithAnsightDefaults_ConfiguresRuntimeWithoutTools()
    {
        var options = Options.CreateBuilder()
            .WithAnsightDefaults()
            .Build();

        Assert.True(options.EnableFramesPerSecond);
        Assert.Equal(400, options.SampleFrequencyMilliseconds);
        Assert.Equal(120, options.RetentionPeriodSeconds);
        Assert.NotNull(options.SessionJpegCapture);
        Assert.NotNull(options.TouchCapture);
        Assert.Empty(options.Tools);
        Assert.False(options.ToolGuard.ExecutionEnabled);
    }

    [Fact]
    public void WithAnsightMaui_ConfiguresDefaultsAndAllMauiTools()
    {
        var bundledConfigAssembly = typeof(AnsightAllInOneTests).Assembly;

        var options = Options.CreateBuilder()
            .WithAnsightMaui(ansight => ansight.WithBundledHostConnection(bundledConfigAssembly))
            .Build();

        Assert.True(options.EnableFramesPerSecond);
        Assert.Equal(400, options.SampleFrequencyMilliseconds);
        Assert.Equal(120, options.RetentionPeriodSeconds);
        Assert.NotNull(options.SessionJpegCapture);
        Assert.NotNull(options.TouchCapture);
        Assert.Equal(bundledConfigAssembly, options.HostConnection.BundledConfigAssembly);
        Assert.Equal(
            expectedRemoteToolIds.Concat(expectedMauiToolIds),
            options.Tools.Select(tool => tool.Id));
        Assert.Equal(
            [ToolScope.Read, ToolScope.Write, ToolScope.Delete],
            options.ToolGuard.AllowedScopes.OrderBy(scope => scope));
    }

    [Fact]
    public void WithAnsightMaui_AllowsSecureStorageConfigurationBeforeDefaultToolRegistration()
    {
        var options = Options.CreateBuilder()
            .WithAnsightMaui(ansight => ansight.WithSecureStorageTools(secure =>
            {
                secure.WithStorageIdentifier("AnsightHarness");
                secure.AllowKeyPrefix("ansight.secure.");
            }))
            .Build();

        Assert.Equal(
            expectedRemoteToolIds.Concat(expectedMauiToolIds).OrderBy(toolId => toolId),
            options.Tools.Select(tool => tool.Id).OrderBy(toolId => toolId));

        AssertConfiguredSecureStorage(options);
    }

    private static void AssertConfiguredSecureStorage(Options options)
    {
        Assert.Equal(1, options.Tools.Count(tool => string.Equals(tool.Id, SecureStorageToolIds.SetValue, StringComparison.OrdinalIgnoreCase)));

        var setTool = Assert.IsType<SetSecureStorageValueTool>(
            options.Tools.Single(tool => string.Equals(tool.Id, SecureStorageToolIds.SetValue, StringComparison.OrdinalIgnoreCase)));
        var secureStorageOptions = GetSecureStorageOptions(setTool);

        Assert.Equal("AnsightHarness", secureStorageOptions.AndroidStore);
        Assert.Equal("AnsightHarness", secureStorageOptions.AppleService);
        Assert.Contains("ansight.secure.", secureStorageOptions.AllowedKeyPrefixes);
    }

    private static readonly string[] expectedRemoteToolIds =
    [
        VisualTreeToolIds.GetVisualTree,
        VisualTreeToolIds.GetScreenshot,
        VisualTreeToolIds.InspectNode,
        VisualTreeToolIds.ShowOverlay,
        VisualTreeToolIds.GetOverlay,
        VisualTreeToolIds.QueryOverlays,
        VisualTreeToolIds.UpdateOverlay,
        VisualTreeToolIds.RemoveOverlay,
        VisualTreeToolIds.ClearOverlays,
        DatabaseToolIds.ListDatabases,
        DatabaseToolIds.DescribeSchema,
        DatabaseToolIds.Query,
        FileSystemToolIds.ListDirectory,
        FileSystemToolIds.ReadFile,
        FileSystemToolIds.GetFileChecksum,
        FileSystemToolIds.DownloadFile,
        FileSystemToolIds.BeginBinaryDownload,
        FileSystemToolIds.PushFile,
        FileSystemToolIds.CopyFile,
        FileSystemToolIds.MoveFile,
        FileSystemToolIds.DeleteFile,
        PreferencesToolIds.ListKeys,
        PreferencesToolIds.GetValue,
        PreferencesToolIds.SetValue,
        PreferencesToolIds.RemoveKey,
        ReflectionToolIds.ListRoots,
        ReflectionToolIds.InspectObject,
        ReflectionToolIds.DescribeType,
        ReflectionToolIds.SetMemberValue,
        ReflectionToolIds.InvokeMethod,
        SecureStorageToolIds.GetValue,
        SecureStorageToolIds.SetValue,
        SecureStorageToolIds.RemoveKey
    ];

    private static readonly string[] expectedMauiToolIds =
    [
        MauiToolIds.GetCurrentPage,
        MauiToolIds.GetVisualTree,
        MauiToolIds.FindElements,
        MauiToolIds.GetElement,
        MauiToolIds.GetBindableProperty,
        MauiToolIds.SetBindableProperty,
        MauiToolIds.ClearBindableProperty,
        MauiToolIds.InflateXaml,
        MauiToolIds.AddElement,
        MauiToolIds.RemoveElement,
        MauiToolIds.SetAppTheme,
        MauiToolIds.GetBindingContext,
        MauiToolIds.GetBindings,
        MauiToolIds.GetResourceState,
        MauiToolIds.GetNavigationState,
        MauiToolIds.InvokeElementAction,
        MauiToolIds.WaitForUi,
        MauiToolIds.GetLayoutDiagnostics,
        MauiToolIds.GetHandlerDiagnostics,
        MauiToolIds.InvokeBindingContextCommand,
        MauiToolIds.SetBindingContextProperty
    ];

    private static SecureStorageToolsOptions GetSecureStorageOptions(SetSecureStorageValueTool tool)
    {
        var optionsField = typeof(SetSecureStorageValueTool).GetField("options", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(optionsField);
        return Assert.IsType<SecureStorageToolsOptions>(optionsField.GetValue(tool));
    }
}
