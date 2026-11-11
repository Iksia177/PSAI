using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PSAI
{
    internal sealed partial class GameAdapter
    {
        private void CaptureInteractions(JObject snapshot, JArray screens, JArray available)
        {
            var store = PlayerStore.Instance;
            var door = FindCached(ref cachedDoor);
            snapshot["outing"] = new JObject {
                ["day"] = StoreStation.Instance.dayCounter, ["last_outing_day"] = store.lastAfterhourOutingDay,
                ["already_went_out_tonight"] = store.lastAfterhourOutingDay == StoreStation.Instance.dayCounter,
                ["visits_remaining"] = store.visitLeftTonight, ["can_depart_now"] = CanStartOuting(door),
                ["unlocked"] = store.isAfterhoursIntroduced, ["development_override"] = store.devMode,
                ["blocked_reasons"] = OutingBlockers(door),
                ["prompt"] = door == null ? null : door.GetTooltip(),
                ["source"] = "original_outing_state_and_door_conditions"
            };
            CaptureFirearm(snapshot, screens, available);
            snapshot["obligations"] = new JObject {
                ["rent_due_in_days"] = store.dayUntilRent, ["rent_amount"] = store.GetRentValue(),
                ["rent_arrears"] = store.temporaryRent, ["can_pay_rent"] = store.CanPayRent(),
                ["property_paid"] = store.IsPropertyPaid, ["paying_mortgage"] = store.isPayingMortgage,
                ["mortgage_weeks_remaining"] = store.mortgageWeekLeft,
                ["mortgage_weekly_payment"] = store.mortgageWeeklyPayment,
                ["source"] = "original_account_state"
            };
            AddScreen("rent_reminder", StartOfDayUIManager.Instance?.rentReminder, screens, available);
            var commissary = CommissaryUIManager.Instance;
            if (commissary != null && commissary.panel.activeInHierarchy)
                snapshot["food_products"] = new JArray(commissary.panel.GetComponentsInChildren<CommissaryElement>(false)
                    .Where(product => product.isActiveAndEnabled && !string.IsNullOrEmpty(product.id)).Select(product => new JObject {
                        ["name"] = Text(product.productName), ["price"] = Text(product.price), ["stock"] = Text(product.stock),
                        ["can_buy"] = CommissaryUIManager.CanBuy(product.id),
                        ["action"] = product.buyButton == null ? null : "button-" + product.buyButton.GetInstanceID()
                    }));
            var client = store.IsClientPresent() ? store.currentClientInstance?.GetClientBlueprint() : null;
            if (client != null)
                snapshot["customer_identity"] = new JObject {
                    ["wanted"] = client.isWanted, ["wanted_poster_id"] = client.wantedPosterId,
                    ["stealth_wanted"] = client.isStealthWanted, ["source"] = "original_identity_fields",
                    ["authorization"] = "user_allowed_internal_wanted_identity"
                };

            CaptureResponse(snapshot, screens, available);
            var paper = PaperUIManager.Instance;
            if (paper == null) return;
            var cards = new JArray();
            CaptureCard("brewer_card", paper.cardBrewer, paper.CloseBrewerCard, StorePhoneClient.RED_IMP_REP_NUMBER, cards, screens, available);
            CaptureCard("mining_card", paper.cardMining, paper.CloseMiningCard, StorePhoneClient.MINING_NUMBER, cards, screens, available);
            CaptureCard("chem_card", paper.cardChem, paper.CloseChemCard, StorePhoneClient.CHEM_NUMBER, cards, screens, available);
            CaptureCard("pharma_card", paper.cardPharma, paper.ClosePharmaCard, StorePhoneClient.PHARMA_NUMBER, cards, screens, available);
            CaptureCard("rev_card", paper.cardRev, () => paper.CloseCard("card_rev"), StorePhoneClient.REV_MERCHANT_NUMBER, cards, screens, available);
            CaptureCard("joe_card", paper.JoeWildBusinessCard, paper.ToggleJoeCard, StorePhoneClient.WILD_CLIENT_NUMBER, cards, screens, available);
            var joe = CustomUIManager.Instance?.GetWindow(PaperUIManager.JOE_CARD_UI_ID);
            if (joe != null)
                CaptureCard("joe_card_custom", joe.root, joe.Close, StorePhoneClient.WILD_CLIENT_NUMBER, cards, screens, available);
            snapshot["contact_cards"] = cards;
            CapturePaper("contract", paper.contractPanel, paper.CloseContract, screens, available);
            CapturePaper("deed", paper.deedPanel, paper.CloseDeed, screens, available);
            CapturePaper("procurement_slip", paper.slipPanel, paper.CloseSlipUI, screens, available);
            CapturePaper("general_slip", paper.generalSlipPanel, paper.CloseGeneralSlipUI, screens, available);
            CapturePaper("security_card", paper.secCardPanel, paper.CloseSecCard, screens, available);
            CapturePaper("security_slip", paper.secSlip, () => paper.CloseCard("sec_slip"), screens, available);
        }

        private void CapturePaper(string id, GameObject root, Action close, JArray screens, JArray available)
        {
            if (root == null || !root.activeInHierarchy) return;
            AddScreen(id, root, screens, available);
            AddAction(available, "close_" + id, "Close " + id, () => {
                if (root == null || !root.activeInHierarchy) throw new InvalidOperationException("panel_not_open");
                close();
            });
        }

        private void CaptureCard(string id, GameObject root, Action close, long number, JArray cards, JArray screens, JArray available)
        {
            if (root == null || !root.activeInHierarchy) return;
            CapturePaper(id, root, close, screens, available);
            cards.Add(new JObject { ["id"] = id, ["number"] = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["source"] = "original_contact_for_open_card", ["close_action"] = "close_" + id });
        }

        private void CaptureResponse(JObject snapshot, JArray screens, JArray available)
        {
            var entry = EmporiumEntry.Instance;
            var diagnostics = new JArray();
            snapshot["response_windows"] = diagnostics;
            foreach (var closable in new[] { false, true })
            {
                var window = closable ? entry.responseWindowClosable : entry.responseWindow;
                var inventory = closable ? entry.responseInventoryClosable : entry.responseInventory;
                if (window == null) continue;
                var visible = window.IsVisible();
                var id = closable ? "response_closable" : "response";
                diagnostics.Add(new JObject { ["id"] = id, ["visible"] = visible,
                    ["hierarchy_visible"] = window.rectTransform.gameObject.activeInHierarchy,
                    ["inventory_hierarchy_visible"] = inventory != null && ((PixelElement) inventory).rectTransform.gameObject.activeInHierarchy });
                if (!visible) continue;
                var options = new JArray();
                var view = new JObject { ["id"] = id, ["title"] = window.titleString, ["choices"] = options };
                screens.Add(view);
                if (closable) AddAction(available, "close_response", "Cancel the current selection", () => {
                    if (!window.IsVisible()) throw new InvalidOperationException("panel_not_open");
                    var cancel = window.closeCallback ?? throw new InvalidOperationException("cancel_not_available");
                    cancel();
                });
                if (entry.choices == null) continue;
                var backpacks = window.titleString == LocHelper.GetLocalizedMechanic("response_afterhours_title", Array.Empty<object>())
                    ? entry.GetAllOwnedItemsByTag("BACKPACK_TAG") : null;
                for (var index = 0; index < entry.choices.Count; index++)
                {
                    var item = entry.choices[index].Item1;
                    if (item == null || item.parentInventory != inventory) continue;
                    var target = RegisterItem(item);
                    var enabled = item.MayActivateSlotItem() && item.CanActivateSlotItem();
                    var action = "response-choice-" + target;
                    var option = new JObject { ["target"] = target, ["label"] = item.GetDisplayName(true),
                        ["description"] = item.shortDescription, ["enabled"] = enabled, ["action"] = action };
                    if (backpacks != null && entry.choices.Count == backpacks.Count + 1 && index > 0)
                    {
                        var backpack = backpacks[index - 1];
                        option["backpack"] = RegisterItem(backpack);
                        option["capacity"] = GridCapacity(backpack);
                    }
                    options.Add(option);
                    available.Add(new JObject { ["action"] = action, ["label"] = item.GetDisplayName(true), ["enabled"] = enabled, ["tooltip_target"] = target });
                    if (enabled) actions[action] = () => {
                        if (!window.IsVisible() || item.parentInventory != inventory || !item.MayActivateSlotItem() || !item.CanActivateSlotItem())
                            throw new InvalidOperationException("choice_not_available");
                        item.ActivateSlotItem();
                    };
                }
            }
        }

        private JObject InspectInteractions(string target, string detail)
        {
            if (!items.TryGetValue(target, out var item)) throw new InvalidOperationException("item_not_visible");
            if (detail == "operations")
            {
                var targets = new JArray();
                foreach (var pair in items.Where(pair => pair.Key != target).ToArray())
                    if (item.MayTarget(pair.Value)) targets.Add(new JObject { ["target"] = pair.Key, ["name"] = pair.Value.GetDisplayName(true), ["can_use"] = item.CanTarget(pair.Value) });
                return new JObject { ["id"] = target, ["can_remove"] = item.MayRemove(),
                    ["can_activate"] = item.MayActivateSlotItem() && item.CanActivateSlotItem(),
                    ["can_toggle"] = item.MayToggleSlotItem() && item.CanToggleSlotItem(), ["use_targets"] = targets };
            }
            if (detail == "capacity")
            {
                var capacity = GridCapacity(item);
                if (capacity == null) throw new InvalidOperationException("grid_capacity_unavailable");
                capacity["id"] = target;
                return capacity;
            }
            // Water quality and volume remain in the original contextual tooltip.
            if (detail == "liquid" && item is GameItemElement element)
                return new JObject { ["id"] = target, ["text"] = element.GetTooltip().nakedText, ["source"] = "original_tooltip" };
            throw new InvalidOperationException("detail_not_supported");
        }

        private static JObject? GridCapacity(GameItem item)
        {
            var grid = item.contentWindow?.childElement as GameGridInventory;
            if (grid == null) return null;
            var shape = grid.inventoryShape;
            var cells = InventoryCapacity.CountUsableCells(shape.width, shape.height, shape.GetLocal);
            return new JObject { ["total_cells"] = cells, ["width"] = shape.width, ["height"] = shape.height,
                ["source"] = "original_inventory_shape", ["includes_nested_containers"] = false };
        }

        private static bool CanStartOuting(StoreDoor? door)
            => OutingBlockers(door).Count == 0;

        private static JArray OutingBlockers(StoreDoor? door)
        {
            var store = PlayerStore.Instance;
            var map = MapUIManager.Instance;
            var entry = EmporiumEntry.Instance;
            var reasons = new JArray();
            if (door == null || !door.isActiveAndEnabled) reasons.Add("店门当前不可用");
            if (map == null) reasons.Add("地图尚未就绪");
            else
            {
                if (map.IsOutside) reasons.Add("当前已在店外");
                if (map.IsInTransit) reasons.Add("当前正在移动");
                if (map.confirmationPanel != null && map.confirmationPanel.activeInHierarchy) reasons.Add("正在确认出发");
            }
            if (TransitUI.IsOpen) reasons.Add("当前正在转场");
            if ((int) store.storeState != 0) reasons.Add("需要先结束营业");
            if ((entry.responseWindow?.IsVisible() ?? false) || (entry.responseWindowClosable?.IsVisible() ?? false))
                reasons.Add("需要先处理当前选择窗口");
            if (!store.devMode)
            {
                if (!store.isAfterhoursIntroduced) reasons.Add("外出尚未解锁");
                if (store.visitLeftTonight <= 0) reasons.Add("当晚外出次数已用尽");
                if (HealthData.IsSeriouslyWounded()) reasons.Add("伤势过重");
                if (StoreStation.Instance.storeEventManager.IsEventActive("crackdown")) reasons.Add("封锁事件阻止外出");
                if (StartingPerkContent.IsAgoraphobiaResting()) reasons.Add("当前特性需要休息");
            }
            return reasons;
        }

        private void AddMountedBackpack(GameItem item, JArray stock)
        {
            var id = RegisterItem(item);
            if (panels.ContainsKey(id)) return;
            var window = EmporiumEntry.Instance.afterhourWindow;
            var inventoryId = "afterhour_backpack_contents";
            if (item.contentWindow.childElement is GameInventory inventory)
                AddInventory(inventoryId, inventory, window, stock, id, "背包内部");
            var visible = inventoryViews.TryGetValue(inventoryId, out var contents);
            if (visible) contents!["owner_item"] = id;
            var view = new JObject {
                ["id"] = id, ["name"] = item.GetDisplayName(true), ["owner_item"] = id,
                ["kind"] = "container", ["presentation"] = "embedded", ["open"] = visible,
                ["inventories"] = visible ? new JArray(inventoryId) : new JArray(),
                ["text"] = new JArray(), ["controls"] = new JArray(),
                ["can_open"] = false, ["can_close"] = false
            };
            var panel = new PanelTarget { Id = id, Window = window, View = view };
            panels[id] = panel;
            panelList.Add(panel);
        }

        private void AddValueControl(PanelTarget panel, string id, string label, bool enabled, JObject state, Action<JObject> callback)
        {
            enabled = enabled && !IsBusy(PlayerStore.Instance) && PlayerStore.Instance.lockAllInventoryFlag == 0;
            var view = new JObject { ["action"] = id, ["label"] = label, ["enabled"] = enabled, ["state"] = state,
                ["parameters"] = state["low"] != null ? new JArray("low", "high") : new JArray("value") };
            ((JArray) panel.View["controls"]!).Add(view);
            panelActions.Add(view.DeepClone());
            if (enabled) parameterActions[id] = args => {
                if (!PanelVisible(panel.Window) || PlayerStore.Instance.lockAllInventoryFlag > 0) throw new InvalidOperationException("control_not_available");
                callback(args);
            };
        }

        private static float ReadControlValue(JObject args, string key, float min, float max)
        {
            var value = args.Value<float?>(key) ?? throw new InvalidOperationException("missing_control_value");
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new InvalidOperationException("invalid_control_value");
            return value;
        }
    }
}
