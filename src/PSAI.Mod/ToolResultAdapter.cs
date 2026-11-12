using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PSAI
{
    internal sealed partial class GameAdapter
    {
        private readonly Dictionary<string, JObject> toolResultViews = new Dictionary<string, JObject>();

        private void CaptureToolResults(JObject snapshot, JArray screens, JArray available)
        {
            toolResultViews.Clear();
            var water = WaterScannerUIManager.Instance;
            if (water != null)
            {
                var view = AddToolResult("water_scanner", water.panel, water.currentWaterContainer,
                    () => water.currentWaterContainer, water.Close, screens, available);
                if (view != null)
                {
                    view["tested"] = PromptText(water.testedTMP);
                    view["label"] = PromptText(water.labelTMP);
                    view["contaminants"] = PromptText(water.contaminantTMP);
                    GuardToolButton(view, water.matchButton, () => water.currentWaterContainer != null &&
                        WaterScannerUIManager.CanMatch(water.currentWaterContainer), available);
                    GuardToolButton(view, water.confrontButton, () => water.currentWaterContainer != null &&
                        WaterScannerUIManager.CanConfront(water.currentWaterContainer), available);
                }
            }
            var strip = WaterStripUIManager.Instance;
            if (strip != null)
            {
                var view = AddToolResult("water_strip", strip.panel, strip.currentWaterContainer,
                    () => strip.currentWaterContainer, strip.Close, screens, available);
                if (view != null)
                {
                    view["tested"] = PromptText(strip.testedTMP);
                    view["label"] = PromptText(strip.labelTMP);
                    view["purity"] = PromptText(strip.purityTMP);
                    GuardToolButton(view, strip.matchButton, () => strip.currentWaterContainer != null &&
                        strip.CanMatch(strip.currentWaterContainer), available);
                    GuardToolButton(view, strip.confrontButton, () => strip.currentWaterContainer != null &&
                        strip.CanConfront(strip.currentWaterContainer), available);
                }
            }
            var chem = ChemScannerUIManager.Instance;
            if (chem != null)
            {
                var view = AddToolResult("chemical_scanner", chem.panel, chem.currentScannedItem,
                    () => chem.currentScannedItem, chem.Close, screens, available);
                if (view != null)
                {
                    view["product"] = PromptText(chem.productNameTMP);
                    view["tested"] = PromptText(chem.testedTMP);
                    view["label"] = PromptText(chem.labelTMP);
                    view["contaminants"] = PromptText(chem.contaminantTMP);
                    view["total_volume"] = PromptText(chem.totalVolumeTMP);
                    // The original handler selects water or chemical mode and applies its native checks.
                    GuardToolButton(view, chem.matchButton, () => chem.currentScannedItem != null, available);
                    GuardToolButton(view, chem.confrontButton, () => chem.currentScannedItem != null, available);
                }
            }
            var plant = PlantAnalyserUIManager.Instance;
            if (plant != null)
            {
                var view = AddToolResult("plant_analyser", plant.UIPanel, plant.currentItem,
                    () => plant.currentItem, plant.HideUI, screens, available);
                if (view != null)
                {
                    view["nutrient_range"] = PromptText(plant.nutrimentRangeTMP);
                    view["nutrient_use"] = PromptText(plant.nutrientUseTMP);
                    view["water_use"] = PromptText(plant.waterUsageTMP);
                    view["contaminant_sensitivity"] = PromptText(plant.contaminantSensitivityTMP);
                    view["growth"] = PromptText(plant.body2TMP);
                }
            }
            CaptureLabelEditors(screens, available);
            snapshot["scanners"] = new JObject {
                ["gene_scanner_active"] = PlayerStore.Instance.isGeneScannerScanning,
                ["instruction"] = "基因扫描器使用 toggle_item 开关；开启后用 inspect tooltip 读取种子等物品的原版扩展说明。"
            };
        }

        private void PublishToolResults(JObject snapshot, JArray available)
        {
            // Copy only after the global busy/transition policy has finalized action availability.
            foreach (var view in toolResultViews.Values)
                view["controls"] = new JArray(available.OfType<JObject>().Where(action =>
                    ((JArray) view["action_ids"]!).Values<string>().Contains(action.Value<string>("action"))).Select(action => action.DeepClone()));
            snapshot["tool_results"] = new JArray(toolResultViews.Values.Select(view => view.DeepClone()));
        }

        private JObject? AddToolResult(string kind, GameObject? root, GameItem? item, Func<GameItem?> current,
            Action close, JArray screens, JArray available)
        {
            if (root == null || !root.activeInHierarchy) return null;
            var id = kind + "-" + root.GetInstanceID() + "-" + (item == null ? "none" : item.uniqueId.ToString());
            var screen = AddPromptScreen(id, root, "tool_result", true, null, screens, available)!;
            screen["kind"] = kind;
            screen["target"] = id;
            // A result may refer to an item inside a now closed container. Do not register it for movement.
            screen["item_id"] = item == null ? null : items.FirstOrDefault(pair => ReferenceEquals(pair.Value, item)).Key;
            screen["item_name"] = item?.GetDisplayName(true);
            screen["source"] = "current_visible_tool_result";
            Action validate = () => {
                if (root == null || !root.activeInHierarchy || !ReferenceEquals(current(), item))
                    throw new InvalidOperationException("tool_result_changed_or_closed");
            };
            foreach (var actionId in ((JArray) screen["action_ids"]!).Values<string>().OfType<string>())
                if (actions.TryGetValue(actionId, out var original))
                    actions[actionId] = () => { validate(); original(); };
            var closeId = "close_" + kind;
            var enabled = !IsBusy(PlayerStore.Instance);
            var control = new JObject { ["action"] = closeId, ["label"] = "关闭当前工具结果窗口",
                ["target"] = id, ["parameters"] = new JArray("target"), ["enabled"] = enabled };
            available.Add(control);
            ((JArray) screen["action_ids"]!).Add(closeId);
            if (enabled) parameterActions[closeId] = args => {
                if (args.Value<string>("target") != id) throw new InvalidOperationException("tool_result_target_changed");
                validate(); close();
            };
            toolResultViews[id] = screen;
            return screen;
        }

        private void GuardToolButton(JObject view, Button? button, Func<bool> allowed, JArray available)
        {
            if (button == null || !button.isActiveAndEnabled) return;
            var actionId = "button-" + button.GetInstanceID();
            if (!actions.TryGetValue(actionId, out var validateAndInvoke)) return;
            var enabled = button.IsInteractable() && allowed() && !IsBusy(PlayerStore.Instance);
            var control = available.OfType<JObject>().First(option => option.Value<string>("action") == actionId);
            control["enabled"] = enabled;
            // Retain the original button callback (including persistent UI listeners) and result identity check.
            if (enabled) actions[actionId] = () => {
                if (button == null || !button.isActiveAndEnabled || !button.IsInteractable() || !allowed())
                    throw new InvalidOperationException("tool_control_not_available");
                validateAndInvoke();
            };
            else actions.Remove(actionId);
        }

        private void CaptureLabelEditors(JArray screens, JArray available)
        {
            var labeler = LabelerUIManager.Instance;
            if (labeler != null)
            {
                var view = AddToolResult("label_editor", labeler.panel, null, () => null, labeler.CloseUI, screens, available);
                if (view != null)
                {
                    var fields = new JArray();
                    foreach (var row in labeler.panel.GetComponentsInChildren<LabelerElement>(false))
                    {
                        var dropdown = row.dropdown;
                        if (dropdown == null || !dropdown.isActiveAndEnabled) continue;
                        var options = dropdown.options.Select(option => option.text).ToArray();
                        var action = "label-option-" + dropdown.GetInstanceID();
                        var target = view.Value<string>("id")!;
                        var enabled = dropdown.IsInteractable() && !IsBusy(PlayerStore.Instance);
                        fields.Add(new JObject { ["name"] = PromptText(row.featureName),
                            ["label"] = PromptText(row.currentFeatureDisplay), ["value"] = dropdown.value,
                            ["options"] = new JArray(options), ["action"] = action, ["target"] = target, ["enabled"] = enabled });
                        available.Add(new JObject { ["action"] = action, ["target"] = target, ["enabled"] = enabled,
                            ["label"] = PromptText(row.featureName), ["parameters"] = new JArray("target", "value") });
                        ((JArray) view["action_ids"]!).Add(action);
                        if (enabled) parameterActions[action] = args => {
                            if (args.Value<string>("target") != target || !labeler.panel.activeInHierarchy ||
                                dropdown == null || !dropdown.isActiveAndEnabled || !dropdown.IsInteractable() ||
                                !dropdown.options.Select(option => option.text).SequenceEqual(options))
                                throw new InvalidOperationException("tool_control_not_available");
                            var token = args["value"];
                            var index = ToolControlInput.OptionIndex(token != null &&
                                (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) ? token.Value<double>() : (double?) null, options.Length);
                            // Invoke the original dropdown event; never assign feature conditions directly.
                            dropdown.value = index;
                        };
                    }
                    view["fields"] = fields;
                    view["editing_note"] = "选择的是展示标签，不代表完成检测；可能与真实品质不符。";
                }
            }
            var blue = LabelerBlueUIManager.Instance;
            if (blue == null) return;
            var textView = AddToolResult("name_editor", blue.panel, null, () => null, blue.CloseUI, screens, available);
            var field = blue.inputField;
            if (textView == null || field == null || !field.isActiveAndEnabled) return;
            var textTarget = textView.Value<string>("id")!;
            var editable = field.IsInteractable() && !field.readOnly && !IsBusy(PlayerStore.Instance);
            textView["input"] = new JObject { ["text"] = field.text, ["character_limit"] = field.characterLimit,
                ["enabled"] = editable, ["action"] = "set_item_label_text", ["target"] = textTarget };
            available.Add(new JObject { ["action"] = "set_item_label_text", ["target"] = textTarget,
                ["label"] = "编辑物品名称标签", ["parameters"] = new JArray("target", "text"), ["enabled"] = editable });
            ((JArray) textView["action_ids"]!).Add("set_item_label_text");
            if (editable) parameterActions["set_item_label_text"] = args => {
                if (args.Value<string>("target") != textTarget || !blue.panel.activeInHierarchy || !field.isActiveAndEnabled ||
                    !field.IsInteractable() || field.readOnly) throw new InvalidOperationException("tool_control_not_available");
                field.text = ToolControlInput.LabelText(args["text"]?.Type == JTokenType.String ? args.Value<string>("text") : null, field.characterLimit);
                // End-edit listeners preserve the native rename callback and typing state.
                field.onEndEdit.Invoke(field.text);
            };
        }

        private JObject InspectToolResult(string target)
        {
            if (!toolResultViews.TryGetValue(target, out var view)) throw new InvalidOperationException("tool_result_not_open");
            return (JObject) view.DeepClone();
        }
    }
}
