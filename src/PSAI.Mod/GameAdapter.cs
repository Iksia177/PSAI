using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PSAI
{
    internal sealed class GameAdapter
    {
        private readonly Dictionary<GameItem, string> itemIds = new Dictionary<GameItem, string>();
        private readonly Dictionary<string, GameItem> items = new Dictionary<string, GameItem>();
        private readonly Dictionary<string, GameInventory> inventories = new Dictionary<string, GameInventory>();
        private readonly Dictionary<string, Action> actions = new Dictionary<string, Action>();
        private long nextItem;
        private EmporiumOption? controlledOptions;
        private bool previousAutoDialogue;
        private bool previousAutoCall;
        private bool previousTypewriter;

        internal object? World => Ready ? (object) EmporiumEntry.Instance : null;
        internal bool Ready => EmporiumEntry.Instance != null && PlayerStore.HasInstance &&
            DialogUIManager.Instance != null && StoreUIManager.Instance != null;
        internal bool Compatible => PlayerStore.VERSION == "VERSION 100-Rev13";

        internal void Clear()
        {
            itemIds.Clear(); items.Clear(); inventories.Clear(); actions.Clear(); nextItem = 0;
        }

        internal void RestoreOptions()
        {
            if (controlledOptions != null)
            {
                controlledOptions.autoNextDialog = previousAutoDialogue;
                controlledOptions.autoCallNext = previousAutoCall;
                controlledOptions.enableTypewriterEffect = previousTypewriter;
            }
            controlledOptions = null;
        }

        internal JObject Capture()
        {
            items.Clear(); inventories.Clear(); actions.Clear();
            var snapshot = new JObject { ["ready"] = Ready, ["controlled"] = controlledOptions != null };
            if (!Ready) return snapshot;
            var store = PlayerStore.Instance;
            var entry = EmporiumEntry.Instance;
            var dialogue = DialogUIManager.Instance;
            var options = EmporiumOption.Instance;
            if (options != null) snapshot["control_options"] = new JObject {
                ["auto_next_dialogue"] = options.autoNextDialog,
                ["auto_call_next"] = options.autoCallNext,
                ["typewriter"] = options.enableTypewriterEffect
            };
            snapshot["phase"] = store.storeState.ToString();
            snapshot["day"] = StoreStation.Instance.dayCounter;
            snapshot["cash"] = store.playerCash;
            snapshot["customer"] = Text(dialogue.clientNameTMP);
            snapshot["dialogue"] = new JObject {
                ["customer"] = Text(dialogue.clientBubble?.textfield),
                ["player"] = Text(dialogue.playerBubble?.textfield),
                ["security"] = Text(dialogue.secBubble?.textfield)
            };
            var available = new JArray();
            var stock = new JArray();
            AddInventory("inventory", entry.invElement, entry.invWindow, stock);
            AddInventory("counter", entry.frontInvinvElement, entry.frontInvWindow, stock);
            AddInventory("back_counter", entry.backInvinvElement, entry.backInvWindow, stock);
            AddInventory("display_case", entry.showcaseElement, entry.showcaseWindow, stock);
            AddInventory("documents", entry.docInvElement, entry.docInvWindow, stock);
            snapshot["inventories"] = stock;
            var screens = new JArray();
            AddScreen("trade", NegociationUIManager.Instance?.uiObject, screens, available);
            AddScreen("bargain", BargainUIManager.Instance?.uiObject, screens, available);
            AddScreen("nightly_report", EndOfDayUIManager.Instance?.UI, screens, available);
            AddScreen("morning_report", StartOfDayUIManager.Instance?.UIPanel, screens, available);
            AddScreen("close_shop_confirmation", StoreUIManager.Instance.closeShutterConfirmationPanel, screens, available);
            snapshot["screens"] = screens;

            AddAction(available, "begin_control", "Use manual dialogue and customer progression", () => {
                if (controlledOptions == null)
                {
                    controlledOptions = EmporiumOption.Instance;
                    if (controlledOptions == null) throw new InvalidOperationException("options_not_ready");
                    previousAutoDialogue = controlledOptions.autoNextDialog;
                    previousAutoCall = controlledOptions.autoCallNext;
                    previousTypewriter = controlledOptions.enableTypewriterEffect;
                }
                controlledOptions.autoNextDialog = false;
                controlledOptions.autoCallNext = false;
                controlledOptions.enableTypewriterEffect = false;
            });
            if (controlledOptions != null) AddAction(available, "end_control", "Restore previous dialogue options", RestoreOptions);
            var busy = IsBusy(store);
            snapshot["busy"] = busy;
            if (!busy)
            {
                if (dialogue.currentDialog != null)
                    AddAction(available, "dialogue_advance", "Advance the displayed dialogue", dialogue.OnAdvancePressed);
                foreach (var bubble in dialogue.dialogChoiceBubbles)
                {
                    if (bubble == null || !bubble.gameObject.activeInHierarchy || dialogue.currentDialog == null) continue;
                    var index = bubble.index;
                    if (index < 0 || index >= dialogue.currentDialog.choices.Count) continue;
                    var enabled = dialogue.currentDialog.IsChoiceEnabledAt(index);
                    var id = "choice-" + index;
                    available.Add(new JObject { ["action"] = id, ["label"] = Text(bubble.TMP), ["enabled"] = enabled });
                    if (enabled) actions[id] = bubble.OnDialogChoiceClick;
                }
                if ((int) store.storeState == 1 && store.CanNextClient())
                    AddAction(available, "call_next", "Call the next customer", store.TryCallNextClient);
                if (store.currentClientInstance != null && store.CanDismissCurrentClient())
                    AddAction(available, "dismiss_customer", "Dismiss the current customer", () => store.TryPlayerDismissCurrentClient(false));
                if (store.currentClientInstance != null && NegociationUIManager.Instance != null)
                    AddAction(available, "open_trade", "Open the trade tablet", NegociationUIManager.Instance.OpenUI);
                if (store.lockShutterButtonFlag == 0 && StoreShutterButton.Instance != null &&
                    ((int) store.storeState == 2 || ((int) store.storeState == 1 && store.CanEndDay())))
                    AddAction(available, "shutter", "Use the shop shutter control", StoreShutterButton.Instance.OnClick);
                var bed = UnityEngine.Object.FindObjectOfType<StoreBedroll>();
                if (bed != null && (int) store.storeState == 0)
                    AddAction(available, "open_nightly_report", "Use the bedroll to open the nightly report", bed.OnClick);
            }
            if (!busy && items.Count > 0 && store.lockAllInventoryFlag == 0)
            {
                available.Add(new JObject { ["action"] = "move_item", ["label"] = "Move a visible item into a visible inventory",
                    ["parameters"] = new JArray("item", "destination", "count (optional)"), ["enabled"] = true });
                available.Add(new JObject { ["action"] = "activate_item", ["label"] = "Perform the item's normal activate operation",
                    ["parameters"] = new JArray("item"), ["enabled"] = true });
            }
            snapshot["actions"] = available;
            foreach (var stale in itemIds.Keys.Where(item => !items.ContainsValue(item)).ToArray()) itemIds.Remove(stale);
            return snapshot;
        }

        private void AddInventory(string id, GameInventory inventory, PixelWindow window, JArray result)
        {
            if (inventory == null || window == null || !window.IsVisible() || inventories.ContainsKey(id)) return;
            inventories[id] = inventory;
            var contents = new JArray();
            foreach (var item in inventory.childItems.ToArray())
            {
                if (!itemIds.TryGetValue(item, out var itemId)) { itemId = "item-" + (++nextItem); itemIds[item] = itemId; }
                items[itemId] = item;
                contents.Add(new JObject { ["id"] = itemId, ["name"] = item.GetDisplayName(true), ["count"] = item.unitCount });
                if (item.contentWindow != null && item.contentWindow.IsVisible() && item.contentWindow.child is GameInventory child)
                    AddInventory("container-" + itemId, child, item.contentWindow, result);
            }
            result.Add(new JObject { ["id"] = id, ["items"] = contents });
        }

        private void AddScreen(string id, GameObject? root, JArray screens, JArray available)
        {
            if (root == null || !root.activeInHierarchy) return;
            screens.Add(new JObject {
                ["id"] = id,
                ["text"] = new JArray(root.GetComponentsInChildren<TextMeshProUGUI>(false)
                    .Select(Text).Where(text => text.Length > 0).Distinct())
            });
            foreach (var button in root.GetComponentsInChildren<Button>(false))
            {
                if (!button.isActiveAndEnabled || !button.IsInteractable()) continue;
                var target = "button-" + button.GetInstanceID();
                if (actions.ContainsKey(target)) continue;
                var label = string.Join(" ", button.GetComponentsInChildren<TextMeshProUGUI>(false).Select(Text).Where(text => text.Length > 0));
                if (label.Length == 0) label = button.gameObject.name;
                AddAction(available, target, label, () => {
                    if (button == null || !button.isActiveAndEnabled || !button.IsInteractable())
                        throw new InvalidOperationException("button_not_available");
                    button.onClick.Invoke();
                });
            }
        }

        private void AddAction(JArray result, string id, string label, Action handler)
        {
            actions[id] = handler;
            result.Add(new JObject { ["action"] = id, ["label"] = label, ["enabled"] = true });
        }

        private static string Text(TextMeshProUGUI? field)
        {
            if (field == null || !field.isActiveAndEnabled) return "";
            var text = field.GetParsedText();
            return text.Substring(0, Math.Min(text.Length, Math.Max(0, field.maxVisibleCharacters)));
        }

        private static bool IsBusy(PlayerStore store)
        {
            if (store.isHandlingShootout || store.isClientBeingArrested ||
                (store.currentClientInstance != null && !store.isClientArrived)) return true;
            var blackscreen = StoreUIManager.Instance.blackscreen;
            if (blackscreen != null && blackscreen.isActiveAndEnabled && blackscreen.color.a > 0.001f) return true;
            var shutter = StoreShutterButton.Instance;
            if (shutter == null || shutter.shutterGameObject == null || shutter.blockerGameObject == null) return false;
            // The shutter-button lock intentionally stays set throughout the night.
            // Movement, rather than that button's availability, describes a transition.
            var open = (int) store.storeState == 1;
            var doorTarget = open ? shutter.endPosition : shutter.startPosition;
            var blockerTarget = open ? shutter.blockerEndPosition : shutter.blockerStartPosition;
            return shutter.shutterGameObject.transform.position != doorTarget ||
                shutter.blockerGameObject.transform.localPosition != blockerTarget;
        }

        internal JObject Inspect(JObject args)
        {
            var target = args.Value<string>("target") ?? "";
            if (!items.TryGetValue(target, out var item)) throw new InvalidOperationException("item_not_visible");
            var detail = args.Value<string>("detail") ?? "description";
            if (detail != "description") throw new InvalidOperationException("detail_not_supported");
            return new JObject { ["id"] = target, ["name"] = item.GetDisplayName(true), ["description"] = item.shortDescription };
        }

        internal JObject Execute(JObject args)
        {
            var action = args.Value<string>("action") ?? "";
            if (action == "end_control")
            {
                RestoreOptions();
                return new JObject { ["invoked"] = true };
            }
            if (IsBusy(PlayerStore.Instance))
                throw new InvalidOperationException("game_busy");
            if (action == "move_item")
            {
                if (PlayerStore.Instance.lockAllInventoryFlag > 0) throw new InvalidOperationException("inventory_locked");
                if (!items.TryGetValue(args.Value<string>("item") ?? "", out var item)) throw new InvalidOperationException("item_not_visible");
                if (!inventories.TryGetValue(args.Value<string>("destination") ?? "", out var destination)) throw new InvalidOperationException("inventory_not_visible");
                if (!item.MayRemove() || item.parentInventory == destination) throw new InvalidOperationException("move_not_allowed");
                var count = args.Value<int?>("count") ?? item.unitCount;
                if (count <= 0 || count > item.unitCount) throw new InvalidOperationException("invalid_count");
                var remaining = GraphUtils.TryAcceptAll(destination, item, count);
                return new JObject { ["requested"] = count, ["moved"] = count - remaining, ["remaining"] = remaining };
            }
            if (action == "activate_item")
            {
                if (!items.TryGetValue(args.Value<string>("item") ?? "", out var item)) throw new InvalidOperationException("item_not_visible");
                if (!item.MayActivateSlotItem() || !item.CanActivateSlotItem()) throw new InvalidOperationException("activation_not_allowed");
                item.ActivateSlotItem();
                return new JObject { ["invoked"] = true };
            }
            if (!actions.TryGetValue(action, out var handler)) throw new InvalidOperationException("action_not_available");
            handler();
            return new JObject { ["invoked"] = true };
        }
    }
}
