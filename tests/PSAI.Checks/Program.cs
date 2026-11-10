using System;
using PSAI;

static void Reject(Action operation, string expected)
{
    try { operation(); }
    catch (InvalidOperationException error) when (error.Message == expected) { return; }
    throw new Exception("Expected rejection: " + expected);
}

UpgradePayment.Validate(10, 10, 0, false);
UpgradePayment.Validate(0, 0, 0, false);
UpgradePayment.Validate(10, 3, 7, true);
UpgradePayment.Validate(int.MaxValue, int.MaxValue, int.MaxValue, true);
Reject(() => UpgradePayment.Validate(10, 9, 1000, false), "upgrade_requires_more_favor");
Reject(() => UpgradePayment.Validate(10, 3, 6, true), "upgrade_insufficient_funds");
Reject(() => UpgradePayment.Validate(-1, 10, 10, true), "invalid_upgrade_cost");
if (UpgradePayment.ReadPrice("<color=red>1,250</color>\ue000") != 1250) throw new Exception("Formatted price failed");
if (UpgradePayment.ReadPrice("0\ue000") != 0) throw new Exception("Free upgrade failed");
foreach (var value in new[] { "", "5 / 10", "-5", "1,2", "999999999999" })
    Reject(() => UpgradePayment.ReadPrice(value), "upgrade_cost_unreadable");
Console.WriteLine("Upgrade payment checks passed (14 cases).");
