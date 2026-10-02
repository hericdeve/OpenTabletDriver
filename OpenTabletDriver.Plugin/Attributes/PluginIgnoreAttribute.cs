using System;

namespace OpenTabletDriver.Plugin.Attributes
{
    /// <summary>
    /// Marks a plugin class or property to be ignored in reflection calls and settings editors.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Property)]
    public class PluginIgnoreAttribute : Attribute
    {
    }
}
