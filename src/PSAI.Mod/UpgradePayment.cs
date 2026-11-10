using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PSAI
{
    internal static class UpgradePayment
    {
        internal static int ReadPrice(string text)
        {
            var plain = Regex.Replace(text, "<[^>]+>", "");
            var matches = Regex.Matches(plain, @"\d{1,3}(?:,\d{3})+|\d+");
            if (Regex.IsMatch(plain, @"-\s*\d") || matches.Count != 1 ||
                !int.TryParse(matches[0].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var cost))
                throw new InvalidOperationException("upgrade_cost_unreadable");
            return cost;
        }

        internal static void Validate(int cost, int favor, int cash, bool allowCash)
        {
            if (cost < 0) throw new InvalidOperationException("invalid_upgrade_cost");
            if (!allowCash && favor < cost) throw new InvalidOperationException("upgrade_requires_more_favor");
            if ((long) favor + (allowCash ? cash : 0) < cost) throw new InvalidOperationException("upgrade_insufficient_funds");
        }
    }
}
