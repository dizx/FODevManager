using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Utils
{
    public static class Helpers
    {
        public static bool IsNullOrEmpty(this string? value)
        {
            return string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(value);
        }
        public static bool SameAs(this string? left, string? right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    }
}
