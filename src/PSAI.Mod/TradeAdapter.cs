using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace PSAI
{
    internal sealed partial class GameAdapter
    {
        private StoreDoor? cachedDoor;
        private StoreBedroll? cachedBed;
        private StorePhoneObject? cachedPhone;
        private StoreClienteleDisplay? cachedDisplay;
        private Shotgun? cachedGun;

        private static T? FindCached<T>(ref T? cached) where T : UnityEngine.Object
        {
            if (cached == null) cached = UnityEngine.Object.FindObjectOfType<T>();
            return cached;
        }

        private void ClearSceneReferences()
        {
            cachedDoor = null; cachedBed = null; cachedPhone = null; cachedDisplay = null; cachedGun = null;
        }

        private static string InventoryName(string id, string fallback)
        {
            switch (id)
            {
                case "inventory": return "主库存";
                case "counter": return "前柜台（顾客放货处）";
                case "back_counter": return "交易备货台（出售物品放这里）";
                case "display_case": return "展示柜";
                case "documents": return "文档柜";
                case "trash_fixture": return "垃圾桶";
                default: return fallback;
            }
        }

        private void CaptureCustomer(JObject snapshot)
        {
            var store = PlayerStore.Instance;
            var current = store.currentClientInstance;
            if (!ReferenceEquals(current, firearmClient))
            {
                firearmClient = current;
                firearmTarget = current == null ? "" : "customer-" + (++nextFirearmTarget);
            }
            var client = current?.GetClientBlueprint();
            snapshot["customer_context"] = client == null ? null : new JObject {
                ["id"] = firearmTarget, ["game_id"] = client.identifier,
                ["name"] = store.isClientArrived ? FullText(DialogUIManager.Instance.clientNameTMP) : "",
                ["arrived"] = store.isClientArrived,
                ["wanted"] = client.isWanted, ["wanted_poster_id"] = client.wantedPosterId,
                ["stealth_wanted"] = client.isStealthWanted,
                ["identity_source"] = "user_authorized_original_identity_fields"
            };
            snapshot["customer_queue"] = new JObject {
                ["remaining"] = store.storeClientManager?.clientStack.Count ?? 0,
                ["includes_current"] = false, ["current_present"] = current != null,
                ["can_call_next"] = (int) store.storeState == 1 && !IsBusy(store) && store.CanNextClient(),
                ["source"] = "original_client_stack_including_queued_events"
            };
        }

        private void CaptureTrade(JObject snapshot, JArray screens, JArray available)
        {
            var news = NewsUIManager.Instance;
            AddScreen("daily_news", news?.uiGameObject, screens, available);
            if (news != null && news.uiGameObject.activeInHierarchy)
                AddAction(available, "close_daily_news", "Close the newspaper", () => {
                    if (!news.uiGameObject.activeInHierarchy) throw new InvalidOperationException("news_not_open");
                    news.CloseUI();
                });

            var store = PlayerStore.Instance;
            var client = store.currentClientInstance?.GetClientBlueprint();
            if (client == null || !store.isClientArrived) { snapshot["trade"] = null; return; }
            var ui = NegociationUIManager.Instance;
            var open = ui != null && ui.IsOpen;
            var texts = new JArray();
            foreach (var screen in screens.OfType<JObject>().Where(screen =>
                screen.Value<string>("id") == "trade" || screen.Value<string>("id") == "bargain" ||
                screen.Value<string>("id") == "generic_confirmation"))
            {
                screen["customer_id"] = firearmTarget;
                texts.Add(screen.DeepClone());
            }
            var candidates = new JArray();
            var backItems = new JArray();
            var sellable = ui == null ? new System.Collections.Generic.HashSet<GameItem>() :
                new System.Collections.Generic.HashSet<GameItem>(ui.GetSellableItem());
            var seen = new System.Collections.Generic.HashSet<string>();
            // Only inventory entries can become candidates. Dialogue tooltip targets are deliberately excluded.
            foreach (var inventory in inventoryViews.Values)
            foreach (var entry in ((JArray) inventory["items"]!).OfType<JObject>())
            {
                var id = entry.Value<string>("id")!;
                if (!seen.Add(id)) continue;
                var item = items[id];
                var owned = GeneralHelper.IsItemEffectivelyOwned(item);
                var matches = owned && client.IsClientBuyingThisItem(item);
                var view = (JObject) entry.DeepClone();
                view["inventory"] = inventory["id"]!.DeepClone();
                view["inventory_name"] = inventory["name"]?.DeepClone();
                view["matches_demand"] = matches;
                view["can_remove"] = item.MayRemove();
                view["budget_sufficient"] = matches && (!client.useClientBudget || client.HasBudgetLeftToBuy(item));
                view["in_sell_offer"] = sellable.Contains(item);
                view["negotiated_value"] = item.GetNegociatedValue();
                // This native explanation depends on the back counter and is only valid there.
                if (inventory.Value<string>("id") == "back_counter")
                {
                    view["refusal_reason"] = owned && ui != null ? ui.GetNotBuyingReason(item) : "";
                    backItems.Add(view.DeepClone());
                }
                if (owned) candidates.Add(view);
            }
            var rows = new JArray();
            NegotiationTabletFrame? frame = null;
            if (open)
            {
                frame = ui!.uiObject.GetComponentInChildren<NegotiationTabletFrame>(false);
                foreach (var row in ui.uiObject.GetComponentsInChildren<TabletListRow>(false))
                {
                    var labels = row.GetComponentsInChildren<TextMeshProUGUI>(false).Select(FullText).Where(text => text.Length > 0).ToArray();
                    var title = labels.FirstOrDefault() ?? "";
                    var ids = backItems.OfType<JObject>().Where(item =>
                        items[item.Value<string>("id")!].GetDisplayName(false) == title || item.Value<string>("name") == title)
                        .Select(item => item.Value<string>("id")!).ToArray();
                    var action = "trade-row-" + row.GetInstanceID();
                    var enabled = !IsBusy(store) && row.isActiveAndEnabled && row.selectable && row.onClick != null && !NegociationUIManager.IsTradeLocked();
                    rows.Add(new JObject { ["action"] = action, ["text"] = new JArray(labels),
                        ["item_id"] = ids.Length == 1 ? ids[0] : null, ["matching_item_ids"] = new JArray(ids),
                        ["selected"] = row.selected, ["enabled"] = enabled });
                    if (!enabled) continue;
                    var expected = store.currentClientInstance;
                    AddAction(available, action, string.Join(" ", labels), () => {
                        if (!ReferenceEquals(store.currentClientInstance, expected) || !ui.IsOpen ||
                            !row.isActiveAndEnabled || !row.selectable || row.onClick == null || NegociationUIManager.IsTradeLocked())
                            throw new InvalidOperationException("trade_row_not_available");
                        row.onClick();
                    });
                }
            }
            snapshot["trade"] = new JObject {
                ["customer_id"] = firearmTarget, ["open"] = open,
                ["customer_intent"] = client.clientIntent.ToString(), ["mode_text"] = FullText(frame?.sectionHeader),
                ["requirements_text"] = frame == null ? new JArray() : new JArray(frame.infoHost.GetComponentsInChildren<TextMeshProUGUI>(false).Select(FullText).Where(text => text.Length > 0)),
                ["budget"] = client.useClientBudget ? new JValue(client.GetBudget()) : JValue.CreateNull(),
                ["budget_limited"] = client.useClientBudget,
                ["offer_inventory"] = "back_counter", ["offer_inventory_name"] = InventoryName("back_counter", ""),
                ["visible_owned_items"] = candidates,
                ["sell_candidates"] = new JArray(candidates.OfType<JObject>().Where(item => item.Value<bool>("matches_demand") &&
                    (item.Value<bool>("can_remove") || item.Value<bool>("in_sell_offer"))).Select(item => item.DeepClone())),
                ["counter_items"] = backItems,
                ["sell_offer_items"] = new JArray(backItems.OfType<JObject>().Where(item => item.Value<bool>("in_sell_offer")).Select(item => item.DeepClone())),
                ["rows"] = rows, ["selected_rows"] = new JArray(rows.OfType<JObject>().Where(row => row.Value<bool>("selected")).Select(row => row.DeepClone())),
                ["screens"] = texts,
                ["action_ids"] = new JArray(texts.OfType<JObject>().SelectMany(screen =>
                    ((JArray?) screen["action_ids"] ?? new JArray()).Values<string>()).Concat(rows.OfType<JObject>()
                    .Where(row => row.Value<bool>("enabled")).Select(row => row.Value<string>("action"))).Distinct()),
                ["source"] = "visible_inventory_instances_and_original_trade_conditions"
            };
        }
    }
}
