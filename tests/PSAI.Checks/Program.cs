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
if (InventoryCapacity.CountUsableCells(3, 2, (_, _) => 0) != 6) throw new Exception("Rectangular inventory capacity failed");
if (InventoryCapacity.CountUsableCells(3, 2, (x, y) => (byte)(x == 1 && y == 1 ? 1 : 0)) != 5) throw new Exception("Inventory wall counted as capacity");
if (InventoryCapacity.CountUsableCells(3, 2, (_, _) => 1) != 0) throw new Exception("Blocked inventory capacity failed");
Console.WriteLine("Inventory capacity checks passed (3 cases).");
if (AppraisalInput.Format(16777217, 0) != "16777217") throw new Exception("Appraisal lost integer precision");
if (AppraisalInput.Format(int.MaxValue, 0) != "2147483647") throw new Exception("Appraisal upper bound failed");
if (AppraisalInput.Format(int.MinValue, 0) != "-2147483648") throw new Exception("Appraisal lower bound failed");
if (AppraisalInput.Format(0, 1) != "0") throw new Exception("Zero appraisal failed");
Reject(() => AppraisalInput.Format(2147483648d, 0), "invalid_appraisal_value");
Reject(() => AppraisalInput.Format(-2147483649d, 0), "invalid_appraisal_value");
Reject(() => AppraisalInput.Format(double.NaN, 0), "invalid_appraisal_value");
Reject(() => AppraisalInput.Format(double.PositiveInfinity, 0), "invalid_appraisal_value");
Reject(() => AppraisalInput.Format(12.5, 0), "appraisal_value_must_be_integer");
Reject(() => AppraisalInput.Format(123, 2), "appraisal_value_too_long");
Console.WriteLine("Appraisal input checks passed (10 cases).");
if (!StorageCatalogPolicy.CanReadContents(true, true, false, false)) throw new Exception("Closed ordinary storage must be readable");
if (!StorageCatalogPolicy.CanReadContents(true, true, true, true)) throw new Exception("Previously opened storage must be readable");
if (StorageCatalogPolicy.CanReadContents(true, true, true, false)) throw new Exception("Unopened package leaked");
if (StorageCatalogPolicy.CanReadContents(false, true, false, true)) throw new Exception("Unowned storage leaked");
if (StorageCatalogPolicy.CanReadContents(true, false, false, true)) throw new Exception("Closed machinery leaked");
if (!StorageCatalogPolicy.IsCandidate(true, true, false, false)) throw new Exception("Closed stored goods disappeared from candidates");
if (StorageCatalogPolicy.IsCandidate(false, true, true, false)) throw new Exception("Wrong demand matched");
if (StorageCatalogPolicy.IsCandidate(true, false, false, false)) throw new Exception("Unavailable visible goods offered");
if (!StorageCatalogPolicy.IsCandidate(true, false, false, true)) throw new Exception("Existing quote disappeared");
Console.WriteLine("Storage catalog access and candidate checks passed (9 cases).");
if (ToolControlInput.OptionIndex(0, 2) != 0 || ToolControlInput.OptionIndex(1, 2) != 1) throw new Exception("Label selection failed");
Reject(() => ToolControlInput.OptionIndex(null, 2), "missing_control_value");
foreach (var value in new[] { -1d, 2d, 0.5, double.NaN, double.PositiveInfinity })
    Reject(() => ToolControlInput.OptionIndex(value, 2), "invalid_control_value");
Reject(() => ToolControlInput.OptionIndex(0, 0), "invalid_control_value");
if (ToolControlInput.LabelText("水瓶", 2) != "水瓶" || ToolControlInput.LabelText("", 2) != "") throw new Exception("Label text failed");
Reject(() => ToolControlInput.LabelText(null, 2), "missing_label_text");
Reject(() => ToolControlInput.LabelText("水瓶子", 2), "invalid_label_text");
foreach (var value in new[] { "a\nb", "a\rb", "a\0b" })
    Reject(() => ToolControlInput.LabelText(value, 0), "invalid_label_text");
Console.WriteLine("Tool control input checks passed (16 cases).");
