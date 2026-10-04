using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace robeem.DataModel
{
    /// <summary>
    /// Reflective property access backing <see cref="Entity"/>'s <c>this[string key]</c> indexer.
    /// Method names are dictated by the shared <c>Entity.cs</c> source.
    /// </summary>
    public static class EntityPropertyExtensions
    {
        public static object? getPropertyValue(this Entity entity, string key) => FindProperty(entity, key).GetValue(entity);

        public static void setPropertyValue(this Entity entity, string key, object? value)
        {
            var property = FindProperty(entity, key);
            if (property.SetMethod is null || !property.SetMethod.IsPublic)
            {
                throw new InvalidOperationException($"Property '{key}' on '{entity.EntityType.Name}' is read-only.");
            }

            property.SetValue(entity, ConvertValue(value, property.PropertyType));
        }

        private static PropertyInfo FindProperty(Entity entity, string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            return entity.EntityType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.Name == key && p.GetIndexParameters().Length == 0)
                ?? throw new ArgumentException($"'{entity.EntityType.Name}' has no property '{key}'.", nameof(key));
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (value is null || underlyingType.IsInstanceOfType(value))
            {
                return value;
            }

            if (underlyingType.IsEnum)
            {
                return value is string name ? Enum.Parse(underlyingType, name, ignoreCase: true) : Enum.ToObject(underlyingType, value);
            }

            return Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
        }
    }
}
