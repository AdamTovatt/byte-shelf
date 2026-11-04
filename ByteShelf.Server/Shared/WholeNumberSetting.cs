using System.Globalization;
using EasyReasy.EnvironmentVariables;

namespace ByteShelf.Server.Shared
{
    /// <summary>
    /// An optional environment variable holding a whole number of some unit, within bounds. The upper
    /// bound keeps a length of time made from it small enough to subtract from now.
    /// </summary>
    public static class WholeNumberSetting
    {
        /// <summary>The number <paramref name="value"/>, the value of <paramref name="variable"/>, holds; null when it is unset or blank.</summary>
        /// <param name="unit">What the number counts, in the plural, for the message.</param>
        /// <exception cref="InvalidOperationException">It is not a whole number from <paramref name="min"/> to <paramref name="max"/>.</exception>
        public static int? Parse(VariableName variable, string? value, string unit, int min, int max)
        {
            if (value == null)
            {
                return null;
            }

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < min || parsed > max)
            {
                throw new InvalidOperationException($"{variable.Name} must be a whole number of {unit} from {min} to {max}; it is '{value}'.");
            }

            return parsed;
        }
    }
}
