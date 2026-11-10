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
        private readonly Dictionary<string, PanelTarget> panels = new Dictionary<string, PanelTarget>();
        private readonly Dictionary<string, JObject> inventoryViews = new Dictionary<string, JObject>();
        private readonly List<PanelTarget> panelList = new List<PanelTarget>();
        private JArray panelActions = new JArray();
        private long nextItem;
        private EmporiumOption? controlledOptions;
        private bool previousAutoDialogue;
        private bool previousAutoCall;
        private bool previousTypewriter;

        private sealed class PanelTarget
        {
            internal string Id = "";
            internal PixelWindow Window = null!;
            internal Action? Open;
            internal Action? Close;
            internal JObject View = null!;
        }

        internal object? World => Ready ? (object) EmporiumEntry.Instance : null;
        internal bool Ready => EmporiumEntry.Instance != null && PlayerStore.HasInstance &&
            DialogUIManager.Instance != null && StoreUIManager.Instance != null;
        internal bool Compatible => PlayerStore.VERSION == "VERSION 100-Rev13";

        internal void Clear()
        {
            itemIds.Clear(); items.Clear(); inventories.Clear(); actions.Clear(); panels.Clear();
            inventoryViews.Clear(); panelList.Clear(); nextItem = 0;
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
            items.Clear(); inventories.Clear(); actions.Clear(); panels.Clear(); inventoryViews.Clear(); panelList.Clear();
            panelActions = new JArray();
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
            AddInventory("trash_fixture", entry.trashInvElement, entry.trashInvWindow, stock);
            AddPanel("display_case", "展示柜", entry.showcaseWindow, null, null, null, stock);
            if (entry.dossier != null && PanelVisible(entry.docInvWindow))
                AddItemPanel(entry.dossier, stock);
            if (entry.trashcan != null && PanelVisible(entry.trashInvWindow))
                AddItemPanel(entry.trashcan, stock);
            var poster = StorePoster.Instance;
            if (poster != null && poster.gameObject.activeInHierarchy)
            {
                AddPanel("poster_stash", "海报藏物处", entry.hiddenWindow, null,
                    () => { if (!poster.isPulled) poster.OnClick(); },
                    () => { if (poster.isPulled) poster.OnClick(); }, stock);
            }
            snapshot["inventories"] = stock;
            snapshot["panels"] = new JArray(panelList.Select(panel => panel.View));
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
            if (!busy && store.lockAllInventoryFlag == 0 && panelList.Count > 0)
            {
                foreach (var control in panelActions) available.Add(control);
                available.Add(new JObject { ["action"] = "open_panel", ["label"] = "Open an available container or machine panel",
                    ["parameters"] = new JArray("target"), ["enabled"] = true });
                available.Add(new JObject { ["action"] = "close_panel", ["label"] = "Close an available container or machine panel",
                    ["parameters"] = new JArray("target"), ["enabled"] = true });
            }
            foreach (var panel in panelList)
            {
                panel.View["can_open"] = !busy && store.lockAllInventoryFlag == 0 && panel.Open != null;
                panel.View["can_close"] = !busy && store.lockAllInventoryFlag == 0 && panel.Close != null;
            }
            snapshot["actions"] = available;
            foreach (var stale in itemIds.Keys.Where(item => !items.ContainsValue(item)).ToArray()) itemIds.Remove(stale);
            return snapshot;
        }

        private void AddInventory(string id, GameInventory inventory, PixelWindow window, JArray result,
            string? panelId = null, string? slotName = null)
        {
            if (inventory == null || window == null || !PanelVisible(window) || inventories.ContainsKey(id) ||
                !((PixelElement) inventory).rectTransform.gameObject.activeInHierarchy) return;
            inventories[id] = inventory;
            var contents = new JArray();
            var view = new JObject { ["id"] = id, ["items"] = contents,
                ["panel"] = panelId, ["slot_name"] = slotName ?? window.titleString };
            inventoryViews[id] = view;
            result.Add(view);
            foreach (var item in inventory.childItems.ToArray())
            {
                var itemId = RegisterItem(item);
                contents.Add(new JObject { ["id"] = itemId, ["name"] = item.GetDisplayName(true), ["count"] = item.unitCount });
                AddItemPanel(item, result);
            }
        }

        private string RegisterItem(GameItem item)
        {
            if (!itemIds.TryGetValue(item, out var id)) { id = "item-" + (++nextItem); itemIds[item] = id; }
            items[id] = item;
            return id;
        }

        private void AddItemPanel(GameItem item, JArray stock)
        {
            if (item.contentWindow == null) return;
            var id = RegisterItem(item);
            AddPanel(id, item.GetDisplayName(true), item.contentWindow, item,
                () => {
                    if (ItemMouseDoubleClickHandler.current == null) throw new InvalidOperationException("interaction_not_ready");
                    ItemMouseDoubleClickHandler.current.OpenContentAction(item, Vector2.zero);
                    item.contentWindow.Center();
                }, () => item.CloseContentWindow(false), stock);
        }

        private void AddPanel(string id, string name, PixelWindow window, GameItem? item,
            Action? open, Action? close, JArray stock)
        {
            if (window == null || panels.ContainsKey(id)) return;
            var visible = PanelVisible(window);
            var view = new JObject { ["id"] = id, ["name"] = name, ["open"] = visible,
                ["owner_item"] = item == null ? null : RegisterItem(item),
                ["kind"] = window.childElement is MachineryPanelRow ? "machine" : "container",
                ["inventories"] = new JArray(), ["text"] = new JArray(), ["controls"] = new JArray() };
            var panel = new PanelTarget { Id = id, Window = window,
                Open = open, Close = close, View = view };
            panels[id] = panel;
            panelList.Add(panel);
            if (!visible) return;
            view["text"] = new JArray(window.rectTransform.GetComponentsInChildren<TextMeshProUGUI>(false)
                .Select(Text).Where(text => text.Length > 0).Distinct());
            CaptureElement(window.childElement, panel, "", name, stock, new HashSet<PixelElement>());
            view["inventories"] = new JArray(inventoryViews.Values
                .Where(inventory => inventory.Value<string>("panel") == id).Select(inventory => inventory["id"]));
            foreach (var inventoryView in inventoryViews.Values.Where(inventory => inventory.Value<string>("panel") == id))
                inventoryView["owner_item"] = view["owner_item"]?.DeepClone();
        }

        private void CaptureElement(PixelElement element, PanelTarget panel, string path, string label,
            JArray stock, HashSet<PixelElement> visited)
        {
            if (element == null || !visited.Add(element) || !element.rectTransform.gameObject.activeInHierarchy) return;
            if (element is GameInventory inventory)
            {
                var id = panel.Id == "display_case" ? "display_case" :
                    panel.Id == "poster_stash" ? "poster_stash" : "container-" + panel.Id + path;
                if (inventories.ContainsKey(id))
                {
                    inventoryViews[id]["panel"] = panel.Id;
                    inventoryViews[id]["slot_name"] = label;
                }
                else AddInventory(id, inventory, panel.Window, stock, panel.Id, label);
            }
            else if (element is MachineryPanelRow row)
            {
                for (var i = 0; i < row.Panels.Count; i++)
                    CaptureElement(row.Panels[i], panel, path + "-" + i, label, stock, visited);
            }
            else if (element is MachineryPanel section)
            {
                if (!section.isHidden) CaptureElement(section.content, panel, path, section.label.GetText(), stock, visited);
            }
            else if (element is MachineryStack stack)
            {
                for (var i = 0; i < stack.elements.Count; i++)
                    CaptureElement(stack.elements[i], panel, path + "-" + i, label, stock, visited);
            }
            else if (element is MachineryTabs tabs && tabs.count > 0)
            {
                for (var i = 0; i < tabs.count; i++)
                {
                    var index = i;
                    AddPanelControl(panel, "tab-" + panel.Id + path + "-" + i, tabs.GetCaption(i), true,
                        () => tabs.Select(index, false));
                }
                CaptureElement(tabs.GetPage(tabs.selected), panel, path + "-tab-" + tabs.selected, tabs.GetCaption(tabs.selected), stock, visited);
            }
            else if (element is MachineryButton button && !button.isHidden)
                AddPanelControl(panel, "control-" + panel.Id + path, button.hasText ? button.text : button.description,
                    !button.isDisabled && !button.requiresHold, () => {
                        if (button.isDisabled || button.isHidden || button.requiresHold)
                            throw new InvalidOperationException("control_not_available");
                        button.Select();
                    });
        }

        private void AddPanelControl(PanelTarget panel, string id, string label, bool enabled, Action callback)
        {
            enabled = enabled && !IsBusy(PlayerStore.Instance) && PlayerStore.Instance.lockAllInventoryFlag == 0;
            var view = new JObject { ["action"] = id, ["label"] = label, ["enabled"] = enabled };
            ((JArray) panel.View["controls"]!).Add(view);
            panelActions.Add(view.DeepClone());
            if (enabled) actions[id] = callback;
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

        private static bool PanelVisible(PixelWindow window) => window.IsVisible() && window.rectTransform.gameObject.activeInHierarchy;

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
            var detail = args.Value<string>("detail") ?? "description";
            if (detail == "contents" || detail == "machine")
            {
                if (detail == "contents" && inventoryViews.TryGetValue(target, out var inventory))
                    return (JObject) inventory.DeepClone();
                if (!panels.TryGetValue(target, out var panel)) throw new InvalidOperationException("panel_not_available");
                if (!PanelVisible(panel.Window)) throw new InvalidOperationException("panel_not_open");
                if (detail == "machine" && panel.View.Value<string>("kind") != "machine")
                    throw new InvalidOperationException("not_a_machine_panel");
                var result = (JObject) panel.View.DeepClone();
                result["inventories"] = new JArray(inventoryViews.Values.Where(view => view.Value<string>("panel") == target));
                return result;
            }
            if (!items.TryGetValue(target, out var item)) throw new InvalidOperationException("item_not_visible");
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
            if (action == "open_panel" || action == "close_panel")
            {
                if (PlayerStore.Instance.lockAllInventoryFlag > 0) throw new InvalidOperationException("inventory_locked");
                if (!panels.TryGetValue(args.Value<string>("target") ?? "", out var panel))
                    throw new InvalidOperationException("panel_not_available");
                var desired = action == "open_panel";
                if (PanelVisible(panel.Window) == desired) return new JObject { ["invoked"] = false, ["changed"] = false };
                var callback = desired ? panel.Open : panel.Close;
                if (callback == null) throw new InvalidOperationException("panel_operation_not_supported");
                callback();
                return new JObject { ["invoked"] = true, ["open"] = PanelVisible(panel.Window) };
            }
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
