using System;
using System.Globalization;

namespace PSAI
{
    internal static class AppraisalInput
    {
        internal static string Format(double value, int characterLimit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < int.MinValue || value > int.MaxValue)
                throw new InvalidOperationException("invalid_appraisal_value");
            if (value != Math.Truncate(value)) throw new InvalidOperationException("appraisal_value_must_be_integer");
            var text = ((int) value).ToString(CultureInfo.InvariantCulture);
            if (characterLimit > 0 && text.Length > characterLimit)
                throw new InvalidOperationException("appraisal_value_too_long");
            return text;
        }
    }
}
