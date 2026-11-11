using System.Text.Json.Nodes;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Ansight.Tools.Avalonia;

internal static class AvaloniaAccessibility
{
    internal static void Populate(JsonObject node, AutomationPeer peer)
    {
        var control = (peer as ControlAutomationPeer)?.Owner;
        var isPassword = control is TextBox { PasswordChar: not '\0' };
        var actions = control is null ? new JsonArray() : AvaloniaElementActions.GetSupportedActions(control, peer);
        var visible = !peer.IsOffscreen();
        var enabled = peer.IsEnabled();
        var interactable = visible
            && enabled
            && actions.Any(action => action?.GetValue<string>() != "bring_into_view");

        node["automationId"] = peer.GetAutomationId();
        node["label"] = isPassword ? "[redacted]" : AvaloniaValueFormatter.Label(peer.GetName());
        node["role"] = peer.GetAutomationControlType().ToString().ToLowerInvariant();
        node["visible"] = visible;
        node["enabled"] = enabled;
        node["focused"] = peer.HasKeyboardFocus();
        node["focusable"] = peer.IsKeyboardFocusable();
        node["interactable"] = interactable;
        node["supportedActions"] = actions;

        var accessibility = new JsonObject
        {
            ["peerType"] = peer.GetType().FullName,
            ["isControlElement"] = peer.IsControlElement(),
            ["isContentElement"] = peer.IsContentElement(),
            ["isOffscreen"] = !visible,
            ["helpText"] = isPassword ? null : AvaloniaValueFormatter.Label(peer.GetHelpText()),
            ["localizedControlType"] = AvaloniaValueFormatter.Label(peer.GetLocalizedControlType())
        };

        if (peer.GetProvider<IToggleProvider>() is { } toggle)
        {
            accessibility["toggleState"] = toggle.ToggleState.ToString();
            node["checked"] = toggle.ToggleState == ToggleState.Indeterminate ? null : toggle.ToggleState == ToggleState.On;
        }

        if (peer.GetProvider<ISelectionItemProvider>() is { } selection)
        {
            node["selected"] = selection.IsSelected;
        }

        if (peer.GetProvider<IExpandCollapseProvider>() is { } expansion)
        {
            accessibility["expandCollapseState"] = expansion.ExpandCollapseState.ToString();
        }

        if (peer.GetProvider<IValueProvider>() is { } text)
        {
            accessibility["isReadOnly"] = text.IsReadOnly;
            // Keep the existing input-text redaction contract, including revealed passwords.
            accessibility["value"] = control is TextBox ? "[redacted]" : AvaloniaValueFormatter.Label(text.Value);
        }

        if (peer.GetProvider<IRangeValueProvider>() is { } range)
        {
            accessibility["isReadOnly"] = range.IsReadOnly;
            accessibility["value"] = AvaloniaValueFormatter.Value(range.Value);
            accessibility["minimum"] = AvaloniaValueFormatter.Value(range.Minimum);
            accessibility["maximum"] = AvaloniaValueFormatter.Value(range.Maximum);
            accessibility["smallChange"] = AvaloniaValueFormatter.Value(range.SmallChange);
            accessibility["largeChange"] = AvaloniaValueFormatter.Value(range.LargeChange);
        }

        node["accessibility"] = accessibility;
    }
}
