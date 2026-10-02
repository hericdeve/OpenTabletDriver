using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eto.Forms;
using OpenTabletDriver.Desktop;

namespace OpenTabletDriver.UX.Controls.Generic.Reflection
{
    public class TypeDropDown<T> : DropDown<TypeInfo> where T : class
    {
        public TypeDropDown(Func<TypeInfo, bool>? filter = null)
        {
            _filter = filter;
            this.ItemTextBinding = Binding.Property<TypeInfo, string>(t => t.GetFriendlyName());
            this.ItemKeyBinding = Binding.Property<TypeInfo, string>(t => t.FullName!);

            AppInfo.PluginManager.AssembliesChanged += HandleAssembliesChanged;
        }

        private readonly Func<TypeInfo, bool>? _filter;

        public T? ConstructSelectedType()
        {
            return SelectedItem != null ? AppInfo.PluginManager.ConstructObject<T>(SelectedItem.FullName!) : null;
        }

        public void Select(Func<T?, bool> predicate)
        {
            foreach (TypeInfo type in DataStore)
            {
                var obj = AppInfo.PluginManager.ConstructObject<T>(type.FullName!);
                if (predicate(obj))
                {
                    this.SelectedValue = type;
                    break;
                }
            }
        }

        protected override IEnumerable<object> CreateDefaultDataStore()
        {
            var query = from type in AppInfo.PluginManager.GetChildTypes<T>()
                        where type.FullName != "OpenTabletDriver.Desktop.Binding.MultiActionBinding"
                        where _filter == null || _filter(type)
                        orderby type.GetFriendlyName()
                        select type;
            return query.ToList();
        }

        private void HandleAssembliesChanged(object? sender, EventArgs e) =>
            Application.Instance.AsyncInvoke(() => this.DataStore = CreateDefaultDataStore());
    }
}
