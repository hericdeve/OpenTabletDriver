using System;

namespace OpenTabletDriver.Plugin.Attributes
{
    /// <summary>
    /// Marks a binding plugin to be available only for HUD menu configuration,
    /// excluding it from standard hardware button and pen tip configuration menus.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class HudOnlyBindingAttribute : Attribute
    {
    }
}
