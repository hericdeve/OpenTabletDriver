using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenTabletDriver.Plugin.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public partial class PropertyValidatedAttribute : Attribute
    {
        public PropertyValidatedAttribute(string memberName)
        {
            MemberName = memberName;
        }

        /// <summary>
        /// The name of the member in which the property this is assigned to is allowed to have.
        /// </summary>
        /// <remarks>
        /// This member must return <see cref="System.Collections.Generic.IEnumerable{T}"/> statically.
        /// </remarks>
        public string MemberName { get; }

        public T? GetValue<T>(PropertyInfo property)
        {
            var targetType = property.ReflectedType ?? property.DeclaringType;
            MemberInfo? member = null;

            for (var currentType = targetType; currentType != null; currentType = currentType.BaseType)
            {
                var members = currentType.GetMember(MemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (members.Length > 0)
                {
                    member = members[0];
                    break;
                }
            }

            if (member == null && property.DeclaringType != null && property.DeclaringType != targetType)
            {
                for (var currentType = property.DeclaringType; currentType != null; currentType = currentType.BaseType)
                {
                    var members = currentType.GetMember(MemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    if (members.Length > 0)
                    {
                        member = members[0];
                        break;
                    }
                }
            }

            if (member == null)
            {
                Log.Write("Plugin", $"Failed to find member '{MemberName}' on '{targetType?.FullName}' for validation", LogLevel.Error);
                return default;
            }

            try
            {
                return member.MemberType switch
                {
                    MemberTypes.Property => (T?)((PropertyInfo)member).GetValue(null),
                    MemberTypes.Field => (T?)((FieldInfo)member).GetValue(null),
                    MemberTypes.Method => (T?)((MethodInfo)member).Invoke(null, null),
                    _ => default
                };
            }
            catch (Exception e)
            {
                Log.Write("Plugin", $"Failed to get valid binding values for '{MemberName}'", LogLevel.Error);

                var match = NonStaticTargetRegex().Match(e.Message);

                if (e is TargetException && match.Success)
                {
                    Log.Debug("Plugin", $"Validation {match.Groups[1].Value} must be static");
                }
                else
                {
                    Log.Exception(e);
                }
            }

            return default;
        }

        [GeneratedRegex("Non-static (.*) requires a target\\.")]
        private static partial Regex NonStaticTargetRegex();
    }
}
