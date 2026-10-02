using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Attributes;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class CompositorBindingTests
    {
        [Fact]
        public void WindowControlBinding_ValidatesActionAndDirection()
        {
            var actionProp = typeof(WindowControlBinding).GetProperty(nameof(WindowControlBinding.Action));
            Assert.NotNull(actionProp);

            var actionValidate = actionProp.GetCustomAttributes(typeof(PropertyValidatedAttribute), true)
                .Cast<PropertyValidatedAttribute>()
                .FirstOrDefault();
            Assert.NotNull(actionValidate);

            var validActions = actionValidate.GetValue<IEnumerable<string>>(actionProp);
            Assert.NotNull(validActions);
            Assert.Contains("Focus Window", validActions);
            Assert.Contains("Move Window", validActions);
            Assert.Contains("Toggle Float", validActions);
            Assert.Contains("Toggle Fullscreen", validActions);

            var actionDefault = actionProp.GetCustomAttributes(typeof(DefaultPropertyValueAttribute), true)
                .Cast<DefaultPropertyValueAttribute>()
                .FirstOrDefault();
            Assert.NotNull(actionDefault);
            Assert.Equal("Focus Window", actionDefault.Value);

            var dirProp = typeof(WindowControlBinding).GetProperty(nameof(WindowControlBinding.Direction));
            Assert.NotNull(dirProp);

            var dirValidate = dirProp.GetCustomAttributes(typeof(PropertyValidatedAttribute), true)
                .Cast<PropertyValidatedAttribute>()
                .FirstOrDefault();
            Assert.NotNull(dirValidate);

            var validDirs = dirValidate.GetValue<IEnumerable<string>>(dirProp);
            Assert.NotNull(validDirs);
            Assert.Contains("Next", validDirs);
            Assert.Contains("Previous", validDirs);
            Assert.Contains("Left", validDirs);
            Assert.Contains("Right", validDirs);
            Assert.Contains("Up", validDirs);
            Assert.Contains("Down", validDirs);

            var dirDefault = dirProp.GetCustomAttributes(typeof(DefaultPropertyValueAttribute), true)
                .Cast<DefaultPropertyValueAttribute>()
                .FirstOrDefault();
            Assert.NotNull(dirDefault);
            Assert.Equal("Next", dirDefault.Value);
        }

        [Fact]
        public void WorkspaceControlBinding_ValidatesActionAndTarget()
        {
            var actionProp = typeof(WorkspaceControlBinding).GetProperty(nameof(WorkspaceControlBinding.Action));
            Assert.NotNull(actionProp);

            var actionValidate = actionProp.GetCustomAttributes(typeof(PropertyValidatedAttribute), true)
                .Cast<PropertyValidatedAttribute>()
                .FirstOrDefault();
            Assert.NotNull(actionValidate);

            var validActions = actionValidate.GetValue<IEnumerable<string>>(actionProp);
            Assert.NotNull(validActions);
            Assert.Contains("Focus Workspace", validActions);
            Assert.Contains("Move Window to Workspace", validActions);

            var actionDefault = actionProp.GetCustomAttributes(typeof(DefaultPropertyValueAttribute), true)
                .Cast<DefaultPropertyValueAttribute>()
                .FirstOrDefault();
            Assert.NotNull(actionDefault);
            Assert.Equal("Focus Workspace", actionDefault.Value);

            var targetProp = typeof(WorkspaceControlBinding).GetProperty(nameof(WorkspaceControlBinding.Target));
            Assert.NotNull(targetProp);

            var targetValidate = targetProp.GetCustomAttributes(typeof(PropertyValidatedAttribute), true)
                .Cast<PropertyValidatedAttribute>()
                .FirstOrDefault();
            Assert.NotNull(targetValidate);

            var validTargets = targetValidate.GetValue<IEnumerable<string>>(targetProp);
            Assert.NotNull(validTargets);
            Assert.Contains("1", validTargets);
            Assert.Contains("Next", validTargets);
            Assert.Contains("Previous", validTargets);
            Assert.Contains("Special", validTargets);

            var targetDefault = targetProp.GetCustomAttributes(typeof(DefaultPropertyValueAttribute), true)
                .Cast<DefaultPropertyValueAttribute>()
                .FirstOrDefault();
            Assert.NotNull(targetDefault);
            Assert.Equal("1", targetDefault.Value);
        }

        [Fact]
        public void ZoomBinding_ValidatesDirection()
        {
            var dirProp = typeof(ZoomBinding).GetProperty(nameof(ZoomBinding.Direction));
            Assert.NotNull(dirProp);

            var dirValidate = dirProp.GetCustomAttributes(typeof(PropertyValidatedAttribute), true)
                .Cast<PropertyValidatedAttribute>()
                .FirstOrDefault();
            Assert.NotNull(dirValidate);

            var validDirs = dirValidate.GetValue<IEnumerable<string>>(dirProp);
            Assert.NotNull(validDirs);
            Assert.Contains("In", validDirs);
            Assert.Contains("Out", validDirs);
        }

        [Fact]
        public void PluginSettingStore_HumanReadableString_WindowControl()
        {
            var storeFocus = new PluginSettingStore(new WindowControlBinding { Action = "Focus Window", Direction = "Next" });
            Assert.Equal("Focus Window (Next)", storeFocus.GetHumanReadableString());
            Assert.Equal("Focus Next", PluginSettingStore.FormatCompactAction(storeFocus));

            var storeMove = new PluginSettingStore(new WindowControlBinding { Action = "Move Window", Direction = "Left" });
            Assert.Equal("Move Window (Left)", storeMove.GetHumanReadableString());
            Assert.Equal("Move Left", PluginSettingStore.FormatCompactAction(storeMove));

            var storeFloat = new PluginSettingStore(new WindowControlBinding { Action = "Toggle Float" });
            Assert.Equal("Toggle Float", storeFloat.GetHumanReadableString());
            Assert.Equal("Toggle Float", PluginSettingStore.FormatCompactAction(storeFloat));

            var storeFullscreen = new PluginSettingStore(new WindowControlBinding { Action = "Toggle Fullscreen" });
            Assert.Equal("Toggle Fullscreen", storeFullscreen.GetHumanReadableString());
            Assert.Equal("Fullscreen", PluginSettingStore.FormatCompactAction(storeFullscreen));
        }

        [Fact]
        public void PluginSettingStore_HumanReadableString_WorkspaceControl()
        {
            var storeFocus = new PluginSettingStore(new WorkspaceControlBinding { Action = "Focus Workspace", Target = "1" });
            Assert.Equal("Workspace 1", storeFocus.GetHumanReadableString());
            Assert.Equal("WS 1", PluginSettingStore.FormatCompactAction(storeFocus));

            var storeMove = new PluginSettingStore(new WorkspaceControlBinding { Action = "Move Window to Workspace", Target = "3" });
            Assert.Equal("Move to Workspace 3", storeMove.GetHumanReadableString());
            Assert.Equal("Move -> WS 3", PluginSettingStore.FormatCompactAction(storeMove));
        }
    }
}
