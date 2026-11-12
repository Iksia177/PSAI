using System;

namespace PSAI
{
    internal static class ToolControlInput
    {
        internal static int OptionIndex(double? value, int count)
        {
            if (value == null)
                throw new InvalidOperationException("missing_control_value");
            var number = value.Value;
            if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Truncate(number) || number < 0 || number >= count)
                throw new InvalidOperationException("invalid_control_value");
            return (int) number;
        }

        internal static string LabelText(string? text, int limit)
        {
            if (text == null) throw new InvalidOperationException("missing_label_text");
            if ((limit > 0 && text.Length > limit) || text.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new InvalidOperationException("invalid_label_text");
            return text;
        }
    }
}
