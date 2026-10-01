using System;
using Eto.Forms;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX.Controls.Generic;

namespace OpenTabletDriver.UX.Controls.Bindings
{
    public class BindingDisplayList : GeneratedItemList<PluginSettingStore?>
    {
        public required string Prefix { set; get; }

        public Func<int, string>? GetTitleFunc { get; set; }
        public int TitleWidth { get; set; } = 140;

        protected virtual string GetTextForIndex(int index)
        {
            if (GetTitleFunc != null)
                return GetTitleFunc(index);

            return $"{Prefix} {index + 1}";
        }

        protected override Control CreateControl(int index, DirectBinding<PluginSettingStore?> itemBinding)
        {
            BindingDisplay display = new BindingDisplay();
            display.StoreBinding.Bind(itemBinding);

            return new Group
            {
                Text = GetTextForIndex(index),
                TitleWidth = this.TitleWidth,
                TitleVerticalAlignment = VerticalAlignment.Top,
                Orientation = Orientation.Horizontal,
                ExpandContent = true,
                Content = display
            };
        }
    }
}
