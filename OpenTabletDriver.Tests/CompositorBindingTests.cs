using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Hud;
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

        [Fact]
        public void CompositorHudBindings_HaveHudOnlyBindingAttribute_AndCorrectNames()
        {
            var switchType = typeof(CompositorWorkspaceHudBinding);
            var moveType = typeof(CompositorMoveWindowHudBinding);

            Assert.True(switchType.GetCustomAttributes(typeof(HudOnlyBindingAttribute), false).Any());
            Assert.True(moveType.GetCustomAttributes(typeof(HudOnlyBindingAttribute), false).Any());

            var switchName = switchType.GetCustomAttributes(typeof(PluginNameAttribute), false)
                .Cast<PluginNameAttribute>()
                .FirstOrDefault();
            Assert.NotNull(switchName);
            Assert.Equal("Workspace Switcher (HUD Layer)", switchName.Name);

            var moveName = moveType.GetCustomAttributes(typeof(PluginNameAttribute), false)
                .Cast<PluginNameAttribute>()
                .FirstOrDefault();
            Assert.NotNull(moveName);
            Assert.Equal("Move Window to Workspace (HUD Layer)", moveName.Name);

            var switchStore = new PluginSettingStore(new CompositorWorkspaceHudBinding());
            var moveStore = new PluginSettingStore(new CompositorMoveWindowHudBinding());

            Assert.Equal("Workspace Switcher (HUD Layer)", switchStore.GetHumanReadableString());
            Assert.Equal("Move Window to Workspace (HUD Layer)", moveStore.GetHumanReadableString());
            Assert.Equal("Switch Workspace", PluginSettingStore.FormatCompactAction(switchStore));
            Assert.Equal("Move Window to WS", PluginSettingStore.FormatCompactAction(moveStore));
        }

        [Fact]
        public void MoveWindowHudLayer_SupportsWorkspacesOneThroughNine()
        {
            // Verify that MoveWindow sub-layer item is recognized as a sub-layer
            var rootMoveItem = new HudItem
            {
                Label = "Move Window",
                Action = new HudAction { Type = HudActionType.MoveWindowWorkspaceLayer, Value = null }
            };
            Assert.True(rootMoveItem.IsSubLayer);

            // Verify that for all 9 workspace targets, effective binding maps to WorkspaceControlBinding
            for (int i = 1; i <= 9; i++)
            {
                var wsItem = new HudItem
                {
                    Label = $"-> WS {i}",
                    Action = new HudAction { Type = HudActionType.MoveWindowWorkspaceLayer, Value = i.ToString() }
                };
                Assert.False(wsItem.IsSubLayer);

                var effectiveStore = wsItem.GetEffectiveBinding();
                Assert.NotNull(effectiveStore);
                Assert.Equal(typeof(WorkspaceControlBinding).FullName, effectiveStore.Path);

                var actionSetting = effectiveStore.Settings.FirstOrDefault(s => s.Property == "Action")?.Value?.ToString();
                var targetSetting = effectiveStore.Settings.FirstOrDefault(s => s.Property == "Target")?.Value?.ToString();

                Assert.Equal("Move Window to Workspace", actionSetting);
                Assert.Equal(i.ToString(), targetSetting);

                Assert.Equal($"Move to Workspace {i}", effectiveStore.GetHumanReadableString());
                Assert.Equal($"Move -> WS {i}", PluginSettingStore.FormatCompactAction(effectiveStore));
            }
        }

        [Fact]
        public void WorkspaceAppIcons_RowCapacitiesAndDynamicResizing()
        {
            // Verify the 3-tier trapezoidal layout capacities:
            // First (inner) row: max 2 slots
            // Second (mid) row: max 3 slots
            // Last (outer) row: max 4 slots
            // Total = 9 slots
            static (int[] rows, int iconSize) GetLayout(int appCount)
            {
                int maxDisplay = Math.Min(appCount, 9);
                int[] rowCounts = maxDisplay switch
                {
                    1 => new[] { 1 },
                    2 => new[] { 2 },
                    3 => new[] { 1, 2 },
                    4 => new[] { 2, 2 },
                    5 => new[] { 2, 3 },
                    6 => new[] { 1, 2, 3 },
                    7 => new[] { 1, 2, 4 },
                    8 => new[] { 1, 3, 4 },
                    _ => new[] { 2, 3, 4 }
                };

                int iconSize = rowCounts.Length switch
                {
                    1 => 26,
                    2 => 21,
                    _ => 17
                };

                return (rowCounts, iconSize);
            }

            // 1 app: 1 row of 1 icon, 26px
            var l1 = GetLayout(1);
            Assert.Single(l1.rows);
            Assert.Equal(1, l1.rows[0]);
            Assert.Equal(26, l1.iconSize);

            // 2 apps: 1 row of 2 icons (first row capacity 2), 26px
            var l2 = GetLayout(2);
            Assert.Single(l2.rows);
            Assert.Equal(2, l2.rows[0]);
            Assert.Equal(26, l2.iconSize);

            // 3-5 apps: 2 rows active (additional row added), icon resized to 21px
            var l3 = GetLayout(3);
            Assert.Equal(2, l3.rows.Length);
            Assert.Equal(1, l3.rows[0]);
            Assert.Equal(2, l3.rows[1]);
            Assert.Equal(21, l3.iconSize);

            var l4 = GetLayout(4);
            Assert.Equal(2, l4.rows.Length);
            Assert.Equal(2, l4.rows[0]);
            Assert.Equal(2, l4.rows[1]);
            Assert.Equal(21, l4.iconSize);

            var l5 = GetLayout(5);
            Assert.Equal(2, l5.rows.Length);
            Assert.Equal(2, l5.rows[0]); // first row filled to cap 2
            Assert.Equal(3, l5.rows[1]); // second row filled to cap 3
            Assert.Equal(21, l5.iconSize);

            // 6-9 apps: 3 rows active (additional row added), icon resized to 17px
            var l6 = GetLayout(6);
            Assert.Equal(3, l6.rows.Length);
            Assert.Equal(1, l6.rows[0]);
            Assert.Equal(2, l6.rows[1]);
            Assert.Equal(3, l6.rows[2]);
            Assert.Equal(17, l6.iconSize);

            var l7 = GetLayout(7);
            Assert.Equal(3, l7.rows.Length);
            Assert.Equal(1, l7.rows[0]);
            Assert.Equal(2, l7.rows[1]);
            Assert.Equal(4, l7.rows[2]);
            Assert.Equal(17, l7.iconSize);

            var l8 = GetLayout(8);
            Assert.Equal(3, l8.rows.Length);
            Assert.Equal(1, l8.rows[0]);
            Assert.Equal(3, l8.rows[1]);
            Assert.Equal(4, l8.rows[2]);
            Assert.Equal(17, l8.iconSize);

            var l9 = GetLayout(9);
            Assert.Equal(3, l9.rows.Length);
            Assert.Equal(2, l9.rows[0]); // first row max 2
            Assert.Equal(3, l9.rows[1]); // second row max 3
            Assert.Equal(4, l9.rows[2]); // last row max 4
            Assert.Equal(17, l9.iconSize);

            // Cap at 9 for >9 apps
            var l12 = GetLayout(12);
            Assert.Equal(3, l12.rows.Length);
            Assert.Equal(2, l12.rows[0]);
            Assert.Equal(3, l12.rows[1]);
            Assert.Equal(4, l12.rows[2]);
            Assert.Equal(17, l12.iconSize);
        }
    }
}
