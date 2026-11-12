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
        private static string PromptText(TMP_Text? field) => field == null || !field.isActiveAndEnabled ? "" : field.text;

        private void CapturePromptWindows(JArray screens, JArray available)
        {
            var confirmation = GenericConfirmationUIManager.Instance;
            if (confirmation != null && confirmation.panel != null && confirmation.panel.activeInHierarchy)
            {
                var screen = AddPromptScreen("generic_confirmation", confirmation.panel, "confirmation", true,
                    confirmation.confirmationDarkness != null && confirmation.confirmationDarkness.activeInHierarchy, screens, available);
                // TMP's parsed mesh can still contain the preceding frame's text after an action.
                // Read the same source string assigned by the original prompt instead.
                var body = PromptText(confirmation.bodyTMP);
                screen!["body"] = body;
                screen["text"] = new JArray(new[] { body, PromptText(confirmation.confirmButtonTMP), PromptText(confirmation.cancelButtonTMP) }
                    .Where(text => text.Length > 0).Distinct());
                if (body == LocHelper.GetLocalizedMechanic("mech_display_case_timing_tip", Array.Empty<object>()))
                    screen["topic"] = "display_case_timing";
                AddPromptAction(screen, confirmation.confirmButton, "confirm_prompt", PromptText(confirmation.confirmButtonTMP),
                    confirmation.ConfirmButtonPressed, available);
                AddPromptAction(screen, confirmation.cancelButton, "cancel_prompt", PromptText(confirmation.cancelButtonTMP),
                    confirmation.CancelButtonPressed, available);
            }
            var dialog = DialogSettingUIManager.Instance;
            if (dialog != null)
            {
                var screen = AddPromptScreen("dialogue_settings", dialog.optionPanel, "settings", true, null, screens, available);
                if (screen != null)
                {
                    AddPromptClose(screen, dialog.optionPanel, dialog, "close_dialogue_settings", dialog.CloseUI, available);
                    screen["settings"] = ReadPromptSettings(dialog.optionPanel);
                }
            }
            var options = GameOptionUIManager.Instance;
            if (options != null && options.IsOpen)
            {
                var window = CustomUIManager.Instance?.GetWindow(GameOptionUIManager.WINDOW_ID);
                var root = window != null && window.IsVisible ? window.root : options.optionPanel;
                var screen = AddPromptScreen("game_settings", root, "settings", true, null, screens, available);
                if (screen != null)
                {
                    AddPromptClose(screen, root, options, "close_game_settings", options.CloseUI, available);
                    screen["settings"] = ReadPromptSettings(root);
                }
            }
            var tutorial = TutorialUIManager.Instance;
            if (tutorial == null) return;
            // These are concrete UI roots from the original tutorial, not game-resource IDs.
            var roots = new Dictionary<string, GameObject> {
                ["open_shutter"] = tutorial.tutorialOpenShutterDoor, ["step_2"] = tutorial.tutorial2,
                ["step_21"] = tutorial.tutorial21, ["step_3"] = tutorial.tutorial3,
                ["close_shutter"] = tutorial.tutorialCloseShutterDoor, ["double_click"] = tutorial.tutorialDoubleClick,
                ["drag"] = tutorial.tutorialDrag, ["quick_transfer"] = tutorial.tutorialQuickTransfer,
                ["recorder"] = tutorial.tutorialRecorder, ["dossier"] = tutorial.tutorialDossier,
                ["buy_drag"] = tutorial.tutorialBuyDrag, ["buy_tablet"] = tutorial.tutorialBuyTablet,
                ["sell_drag"] = tutorial.tutorialSellDrag, ["sleep"] = tutorial.tutorialSleep,
                ["transfer_button"] = tutorial.tutorialTransferButton, ["barter"] = tutorial.tutorialBarter,
                ["nightly_service"] = tutorial.tutorialNightlyService, ["afterhours"] = tutorial.tutorialAfterhoursActivity,
                ["display_case"] = tutorial.tutorialDisplayCase, ["clientele"] = tutorial.tutorialClienteleUI,
                ["bargain"] = tutorial.tutorialBargain, ["bargain_2"] = tutorial.tutorialBargain2,
                ["reputation"] = tutorial.tutorialReputation, ["advance_dialogue"] = tutorial.tutorialAdvanceDialog,
                ["multi_select"] = tutorial.tutorialMultiSelect, ["sort_menu"] = tutorial.tutorialSortMenu
            };
            var seen = new HashSet<GameObject>();
            foreach (var pair in roots)
                if (pair.Value != null && seen.Add(pair.Value))
                    AddPromptScreen("tutorial_" + pair.Key, pair.Value, "tutorial_hint", false, null, screens, available);
        }

        private JObject? AddPromptScreen(string id, GameObject? root, string kind, bool response, bool? blocking,
            JArray screens, JArray available)
        {
            if (root == null || !root.activeInHierarchy) return null;
            AddScreen(id, root, screens, available);
            var screen = screens.OfType<JObject>().Last(value => value.Value<string>("id") == id);
            screen["text"] = new JArray(root.GetComponentsInChildren<TMP_Text>(false).Select(PromptText)
                .Concat(root.GetComponentsInChildren<UnityEngine.UI.Text>(false).Where(text => text.isActiveAndEnabled).Select(text => text.text))
                .Where(text => text.Length > 0).Distinct());
            screen["attention_kind"] = kind;
            screen["requires_response"] = response;
            screen["blocks_gameplay"] = blocking == null ? JValue.CreateNull() : new JValue(blocking.Value);
            return screen;
        }

        private void AddPromptAction(JObject screen, GameObject root, string id, string label, Action callback, JArray available)
        {
            if (root == null || !root.activeInHierarchy) return;
            var button = root.GetComponent<Button>();
            var enabled = button == null || (button.isActiveAndEnabled && button.IsInteractable());
            var view = new JObject { ["action"] = id, ["label"] = label, ["enabled"] = enabled };
            available.Add(view);
            ((JArray) screen["action_ids"]!).Add(id);
            if (enabled) actions[id] = () => {
                if (root == null || !root.activeInHierarchy || (button != null && (!button.isActiveAndEnabled || !button.IsInteractable())))
                    throw new InvalidOperationException("prompt_not_available");
                callback();
            };
        }

        private void AddPromptClose(JObject screen, GameObject root, UnityEngine.Object owner, string id, Action callback, JArray available)
        {
            AddAction(available, id, "关闭当前设置窗口", () => {
                if (root == null || !root.activeInHierarchy) throw new InvalidOperationException("prompt_not_available");
                // Preserve any additional serialized tutorial listeners on the original close button.
                foreach (var button in root.GetComponentsInChildren<Button>(false))
                    if (button.isActiveAndEnabled && button.IsInteractable())
                        for (var index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                            if (button.onClick.GetPersistentTarget(index) == owner && button.onClick.GetPersistentMethodName(index) == "CloseUI")
                            {
                                button.onClick.Invoke();
                                return;
                            }
                callback();
            });
            ((JArray) screen["action_ids"]!).Add(id);
        }

        private static JObject ReadPromptSettings(GameObject root) => new JObject {
            ["toggles"] = new JArray(root.GetComponentsInChildren<Toggle>(false).Where(toggle => toggle.isActiveAndEnabled)
                .Select(toggle => new JObject { ["name"] = toggle.gameObject.name, ["value"] = toggle.isOn,
                    ["text"] = new JArray(toggle.GetComponentsInChildren<TMP_Text>(false).Select(PromptText)) })),
            ["sliders"] = new JArray(root.GetComponentsInChildren<Slider>(false).Where(slider => slider.isActiveAndEnabled)
                .Select(slider => new JObject { ["name"] = slider.gameObject.name, ["value"] = slider.value,
                    ["min"] = slider.minValue, ["max"] = slider.maxValue }))
        };

        private static JObject CaptureAttention(JArray screens, JArray available)
        {
            var entries = new JArray(screens.OfType<JObject>().Where(screen => screen["attention_kind"] != null)
                .OrderByDescending(screen => screen.Value<bool>("requires_response")).Select(screen => new JObject {
                    ["screen_id"] = screen["id"]!.DeepClone(), ["kind"] = screen["attention_kind"]!.DeepClone(),
                    ["topic"] = screen["topic"]?.DeepClone(), ["text"] = screen["text"]!.DeepClone(),
                    ["requires_response"] = screen["requires_response"]!.DeepClone(),
                    ["blocks_gameplay"] = screen["blocks_gameplay"]!.DeepClone(),
                    ["controls"] = new JArray(available.OfType<JObject>().Where(action =>
                        ((JArray) screen["action_ids"]!).Values<string>().Contains(action.Value<string>("action"))).Select(action => action.DeepClone()))
                }));
            return new JObject { ["has_pending_response"] = entries.OfType<JObject>().Any(entry => entry.Value<bool>("requires_response")),
                ["items"] = entries, ["instruction"] = "先读取待回应窗口再决定下一步；确认可能产生消费、提交或剧情后果，不自动接受。教程提示按内容继续原版步骤，不能用统一关闭跳过。blocks_gameplay=null表示尚未确认是否阻塞。" };
        }
    }
}
