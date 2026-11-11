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
        private JObject? inspectionView;
        private GameItem? inspectionItem;
        private object? inspectionImageContext;

        private void CaptureInspection(JObject snapshot, JArray screens, JArray available)
        {
            var ui = InspectionUIManager.Instance;
            if (ui == null || ui.panel == null || !ui.panel.activeInHierarchy)
            {
                inspectionItem = null;
                inspectionImageContext = null;
                return;
            }
            var item = ui.currentInspectingItem;
            if (item == null) return;
            if (!ReferenceEquals(inspectionItem, item) || inspectionImageContext == null)
            {
                inspectionItem = item;
                inspectionImageContext = new object();
            }
            var itemId = RegisterItem(item);
            var target = "inspection-" + itemId;
            AddScreen("inspection", ui.panel, screens, available);
            var screen = screens.OfType<JObject>().Last(view => view.Value<string>("id") == "inspection");
            screen["target"] = target;
            var controls = new JArray();
            inspectionView = new JObject {
                ["id"] = target, ["item_id"] = itemId, ["name"] = item.GetDisplayName(true),
                ["item_tooltip"] = item is GameItemElement element ? element.GetTooltip().nakedText : null,
                ["text"] = screen["text"]!.DeepClone(), ["controls"] = controls,
                ["action_ids"] = screen["action_ids"]!.DeepClone(),
                ["source"] = "visible_inspection_ui",
                ["features"] = CaptureInspectionFeatures(ui),
                ["image_target"] = target,
                ["image_note"] = "图形变体编号仅表示当前展示部件，不代表真假或价值；文字不足时按需查询 image。"
            };
            snapshot["inspection"] = inspectionView;
            Action validate = () => {
                if (ui == null || ui.panel == null || !ui.panel.activeInHierarchy || !ReferenceEquals(ui.currentInspectingItem, item))
                    throw new InvalidOperationException("inspection_not_available");
            };
            imageContexts[target] = inspectionImageContext;
            imageTargets[target] = () => {
                validate();
                return GameImages.VisibleRect(ui.panel.GetComponent<RectTransform>());
            };
            RegisterInspectionAction("close_inspection", "关闭鉴定界面", target, true, controls, available, args => {
                validate();
                ui.CloseUI();
                inspectionItem = null;
                inspectionImageContext = null;
            });

            foreach (var toggle in ui.panel.GetComponentsInChildren<Toggle>(false))
            {
                if (!toggle.isActiveAndEnabled) continue;
                var action = "inspection-toggle-" + toggle.GetInstanceID();
                var label = string.Join(" ", VisibleInspectionText(toggle.gameObject));
                if (label.Length == 0) label = toggle.gameObject.name;
                RegisterInspectionAction(action, label, target, toggle.IsInteractable(), controls, available, args => {
                    validate();
                    if (toggle == null || !toggle.isActiveAndEnabled || !toggle.IsInteractable())
                        throw new InvalidOperationException("control_not_available");
                    var token = args["value"];
                    if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                        throw new InvalidOperationException("missing_control_value");
                    var value = token.Value<double>();
                    if (value != 0 && value != 1) throw new InvalidOperationException("invalid_toggle_value");
                    // The property invokes the original onValueChanged event and ToggleGroup rules.
                    toggle.isOn = value == 1;
                }, "value");
                ((JObject) controls.Last!)["selected"] = toggle.isOn;
                ((JObject) controls.Last!)["values"] = new JArray(0, 1);
            }
            var field = ui.apprisalServiceInputField;
            if (ui.appraisalPanel != null && ui.appraisalPanel.activeInHierarchy && field != null && field.isActiveAndEnabled)
            {
                inspectionView["appraisal"] = new JObject {
                    ["value_text"] = field.text, ["editable"] = field.IsInteractable() && !field.readOnly && !IsBusy(PlayerStore.Instance),
                    ["set_action"] = "set_appraisal_value"
                };
                RegisterInspectionAction("set_appraisal_value", "输入鉴定估值", target,
                    field.IsInteractable() && !field.readOnly, controls, available, args => {
                        validate();
                        if (!ui.appraisalPanel.activeInHierarchy || !field.isActiveAndEnabled || !field.IsInteractable() || field.readOnly)
                            throw new InvalidOperationException("control_not_available");
                        var token = args["value"];
                        if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                            throw new InvalidOperationException("missing_control_value");
                        var text = AppraisalInput.Format(token.Value<double>(), field.characterLimit);
                        ui.StartEditingAppraisalField();
                        try { field.text = text; }
                        finally { ui.FinishEditingAppraisalField(); }
                    }, "value");
            }
        }

        private void RegisterInspectionAction(string action, string label, string target, bool enabled,
            JArray controls, JArray available, Action<JObject> callback, params string[] parameters)
        {
            enabled = enabled && !IsBusy(PlayerStore.Instance);
            var view = new JObject { ["action"] = action, ["label"] = label, ["target"] = target,
                ["enabled"] = enabled, ["parameters"] = new JArray(new[] { "target" }.Concat(parameters)) };
            controls.Add(view);
            available.Add(view.DeepClone());
            ((JArray) inspectionView!["action_ids"]!).Add(action);
            if (enabled) parameterActions[action] = args => {
                if (args.Value<string>("target") != target) throw new InvalidOperationException("inspection_target_changed");
                callback(args);
            };
        }

        private JObject InspectAppraisal(string target)
        {
            if (inspectionView == null) throw new InvalidOperationException("inspection_not_open");
            if (target != inspectionView.Value<string>("id") && target != inspectionView.Value<string>("item_id"))
                throw new InvalidOperationException("inspection_target_changed");
            return (JObject) inspectionView.DeepClone();
        }

        private static string[] VisibleInspectionText(GameObject root) => root.GetComponentsInChildren<TextMeshProUGUI>(false)
            .Where(field => field.isActiveAndEnabled).Select(FullText)
            .Concat(root.GetComponentsInChildren<UnityEngine.UI.Text>(false).Where(field => field.isActiveAndEnabled).Select(field => field.text))
            .Where(text => !string.IsNullOrWhiteSpace(text)).Distinct().ToArray();

        private static JArray InspectionVariants(IEnumerable<GameObject> variants, string[]? labels = null)
        {
            var result = new JArray();
            var index = 0;
            foreach (var node in variants)
            {
                if (node != null && node.activeInHierarchy)
                    result.Add(new JObject { ["variant_index"] = index,
                        ["label"] = labels != null && index < labels.Length ? labels[index] : null,
                        ["text"] = new JArray(VisibleInspectionText(node)), ["graphics"] = VisibleInspectionGraphics(node) });
                index++;
            }
            return result;
        }

        private static JObject CaptureInspectionFeatures(InspectionUIManager ui)
        {
            // Read only active display components, never appraisal answer/value tags.
            var cigarette = ui.cigaretteUISmall;
            if (cigarette != null && cigarette.gameObject.activeInHierarchy)
                return new JObject { ["kind"] = "cigarette", ["seals"] = InspectionVariants(cigarette.seals),
                    ["letterings"] = InspectionVariants(cigarette.letterings), ["logos"] = InspectionVariants(cigarette.logos),
                    ["boxes"] = InspectionVariants(cigarette.boxes) };
            var hand = ui.appHandUISmall;
            if (hand != null && hand.gameObject.activeInHierarchy)
            {
                var body = new[] { "普通外观", "磨损外观", "损坏外观" };
                var addons = new[] { "装甲", "刀刃" };
                var interfaces = new[] { "EMG", "EMG+", "NEURAL" };
                return new JObject { ["kind"] = "mechanical_hand",
                    ["steel"] = HandFeatures(hand.steelHands, hand.steelAddons, hand.steelFullInterfaces, hand.steelSymbolInterfaces, body, addons, interfaces),
                    ["carbon"] = HandFeatures(hand.carbonHands, hand.carbonAddons, hand.carbonFullInterfaces, hand.carbonSymbolInterfaces, body, addons, interfaces),
                    ["gold"] = HandFeatures(hand.goldHands, hand.goldAddons, hand.goldFullInterfaces, hand.goldSymbolInterfaces, body, addons, interfaces) };
            }
            var ribwich = ui.ribwichUISmall;
            if (ribwich != null && ribwich.gameObject.activeInHierarchy)
                return new JObject { ["kind"] = "ribwich",
                    ["meat"] = InspectionVariants(new[] { ribwich.meatNormal, ribwich.meatDouble }, new[] { "普通肉饼", "双层肉饼" }),
                    ["bun"] = InspectionVariants(ribwich.topBun), ["wrap"] = InspectionVariants(ribwich.wrap),
                    ["pickles"] = InspectionVariants(ribwich.pickles), ["davre_logo"] = InspectionVariants(ribwich.davreLogo),
                    ["ribwich_logo"] = InspectionVariants(ribwich.ribwichLogo),
                    ["production_mark_visible"] = ribwich.barcode != null && ribwich.barcode.activeInHierarchy,
                    ["production_text"] = FullText(ribwich.barcodeTMP),
                    ["facility_mark_visible"] = ribwich.facility != null && ribwich.facility.activeInHierarchy,
                    ["facility_text"] = FullText(ribwich.facilityTMP) };
            var fuel = ui.fuelUISmall;
            if (fuel != null && fuel.gameObject.activeInHierarchy)
                return new JObject { ["kind"] = "fuel_cell", ["serial"] = FullText(fuel.bottomSerial),
                    ["graphics"] = VisibleInspectionGraphics(fuel.gameObject) };
            return new JObject { ["kind"] = "other", ["text"] = new JArray(VisibleInspectionText(ui.panel)) };
        }

        private static JObject HandFeatures(IEnumerable<GameObject> body, IEnumerable<GameObject> addons,
            IEnumerable<GameObject> full, IEnumerable<GameObject> symbol, string[] bodyLabels, string[] addonLabels, string[] interfaceLabels) =>
            new JObject { ["body"] = InspectionVariants(body, bodyLabels), ["addons"] = InspectionVariants(addons, addonLabels),
                ["full_interfaces"] = InspectionVariants(full, interfaceLabels), ["symbol_interfaces"] = InspectionVariants(symbol, interfaceLabels) };

        private static JArray VisibleInspectionGraphics(GameObject root) => new JArray(root.GetComponentsInChildren<Image>(false)
            .Where(image => image.isActiveAndEnabled && image.color.a > 0 && image.sprite != null)
            .Select(image => new JObject { ["id"] = "graphic-" + image.GetInstanceID(),
                ["sprite"] = image.sprite.name, ["tint_rgba"] = ColorUtility.ToHtmlStringRGBA(image.color),
                ["source"] = "active_ui_graphic",
                ["text"] = new JArray(VisibleInspectionText(image.gameObject)), ["requires_image_for_pattern"] = true }));
    }
}
