using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace HotRepl.Serialization
{
    /// <summary>
    /// Serializes arbitrary evaluation results to human-readable strings
    /// suitable for sending over the wire.
    /// </summary>
    internal static class ResultSerializer
    {
        /// <summary>
        /// Converts a runtime value to its string representation.
        /// </summary>
        /// <param name="value">The value to serialize (may be null).</param>
        /// <param name="maxElements">
        /// Maximum number of elements to enumerate from <see cref="IEnumerable"/> values.
        /// Prevents unbounded iteration on large or infinite sequences.
        /// </param>
        /// <returns>A string representation, or null when <paramref name="value"/> is null.</returns>
        public static string? Serialize(object? value, int maxElements = 100)
        {
            if (value is null)
                return null;

            if (value is string s)
                return s;

            // Primitives: int, float, double, decimal, bool, byte, etc.
            if (value.GetType().IsPrimitive || value is decimal)
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            // Reflection types — ToString() is more useful than JSON for these.
            if (value is Type || value is MemberInfo)
                return value.ToString();

            // Enumerables (but not string, already handled, and not byte[] which is noisy).
            if (value is IEnumerable enumerable && !(value is byte[]))
                return SerializeEnumerable(enumerable, maxElements);

            // Try structured JSON serialization, fall back to ToString().
            try
            {
                return JsonConvert.SerializeObject(value, Formatting.None);
            }
            catch
            {
                return value.ToString();
            }
        }

        private static string SerializeEnumerable(IEnumerable enumerable, int maxElements)
        {
            var sb = new StringBuilder("[");
            int count = 0;
            foreach (var item in enumerable)
            {
                if (count > 0)
                    sb.Append(", ");

                if (count >= maxElements)
                {
                    sb.Append("...");
                    break;
                }

                sb.Append(Serialize(item, maxElements) ?? "null");
                count++;
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
