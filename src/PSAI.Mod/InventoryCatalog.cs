using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PSAI
{
    internal sealed partial class GameAdapter
    {
        private readonly Dictionary<GameItem, string> catalogIds = new Dictionary<GameItem, string>();
        private readonly Dictionary<GameItem, JObject> catalogViews = new Dictionary<GameItem, JObject>();
        private readonly HashSet<GameItem> catalogVisibleItems = new HashSet<GameItem>();
        private long nextCatalogId;

        private void CaptureInventoryCatalog(JObject snapshot)
        {
            catalogViews.Clear(); catalogVisibleItems.Clear();
            foreach (var inventory in inventories.Values)
                foreach (var item in inventory.childItems) catalogVisibleItems.Add(item);
            var records = new JArray();
            var visited = new HashSet<GameInventory>();
            foreach (var pair in inventories.OrderBy(pair => inventoryViews[pair.Key].Value<string>("owner_item") == null ? 0 : 1).ThenBy(pair => pair.Key, StringComparer.Ordinal).ToArray())
            {
                var ownerId = inventoryViews[pair.Key].Value<string>("owner_item");
                if (ownerId != null && items.TryGetValue(ownerId, out var owner) && !GeneralHelper.IsItemEffectivelyOwned(owner)) continue;
                WalkCatalog(pair.Value, pair.Key, inventoryViews[pair.Key].Value<string>("name") ?? pair.Key,
                    new JArray(), new JArray(), records, visited);
            }
            foreach (var stale in catalogIds.Keys.Where(item => !catalogViews.ContainsKey(item)).ToArray()) catalogIds.Remove(stale);
            snapshot["inventory_catalog"] = new JObject { ["items"] = records, ["read_only"] = true,
                ["scope"] = "known_inventory_and_owned_normal_storage",
                ["instruction"] = "包含关闭的普通自有储物容器内容。catalog_id只读，不能用于move_item；按next_open_target打开必要容器后重新observe，取得item_id和实际库存ID再取货。" };
        }

        private void WalkCatalog(GameInventory inventory, string rootId, string name, JArray path, JArray containers,
            JArray records, HashSet<GameInventory> visited)
        {
            if (!visited.Add(inventory)) return;
            foreach (var item in inventory.childItems.ToArray())
            {
                if (!GeneralHelper.IsItemEffectivelyOwned(item) || catalogViews.ContainsKey(item)) continue;
                if (!catalogIds.TryGetValue(item, out var id)) catalogIds[item] = id = "stock-" + (++nextCatalogId);
                var itemId = catalogVisibleItems.Contains(item) && itemIds.TryGetValue(item, out var visibleId) ? visibleId : null;
                var visible = itemId != null;
                var location = new JArray(path.Select(value => value.DeepClone()));
                if (location.Count == 0) location.Add(name);
                var next = visible ? null : containers.OfType<JObject>().FirstOrDefault(parent => parent.Value<bool>("requires_open") && parent.Value<string>("panel_id") != null);
                var view = new JObject { ["catalog_id"] = id, ["item_id"] = itemId,
                    ["name"] = item.GetDisplayName(true), ["count"] = item.unitCount, ["owned"] = true,
                    ["visible"] = visible, ["root_inventory"] = rootId, ["location_path"] = location,
                    ["container_path"] = containers.DeepClone(), ["requires_open"] = !visible,
                    ["next_open_target"] = next?["panel_id"]?.DeepClone(),
                    ["can_move_now"] = visible && !IsBusy(PlayerStore.Instance) && PlayerStore.Instance.lockAllInventoryFlag == 0 && item.MayRemove() };
                records.Add(view); catalogViews[item] = view;
                // Ordinary grid storage only. Never inspect closed machinery or unopened tracked packages.
                if (item.contentWindow?.childElement is GameGridInventory contents)
                {
                    if (!StorageCatalogPolicy.CanReadContents(true, true, item.IsTag("TRACK_OPENED_TAG"), item.IsTag("OPENED_TAG")))
                    {
                        view["contents_status"] = "unopened_tracked_container";
                        continue;
                    }
                    view["contents_status"] = "catalogued_normal_storage";
                    var childPath = (JArray) location.DeepClone(); childPath.Add(item.GetDisplayName(true));
                    var parents = (JArray) containers.DeepClone();
                    var containerOpen = visible && panels.TryGetValue(itemId!, out var panel) && panel.View.Value<bool>("open");
                    parents.Add(new JObject { ["catalog_id"] = id, ["name"] = item.GetDisplayName(true),
                        ["panel_id"] = visible && panels.ContainsKey(itemId!) ? itemId : null,
                        ["requires_open"] = !containerOpen });
                    WalkCatalog(contents, rootId, name, childPath, parents, records, visited);
                }
            }
        }

        private void AddCatalogTradeCandidates(JArray candidates, StoreClient client, BarterCondition[]? conditions, bool barter)
        {
            foreach (var pair in catalogViews.Where(pair => !pair.Value.Value<bool>("visible")))
            {
                var item = pair.Key;
                var view = (JObject) pair.Value.DeepClone();
                view["id"] = null; view["inventory"] = null;
                view["inventory_name"] = ((JArray) view["location_path"]!).Last?.DeepClone();
                var matches = client.IsClientBuyingThisItem(item);
                view["matches_demand"] = matches; view["can_remove"] = item.MayRemove();
                view["in_sell_offer"] = false;
                view["budget_sufficient"] = matches && (!client.useClientBudget || client.HasBudgetLeftToBuy(item));
                view["negotiated_value"] = item.GetNegociatedValue();
                if (barter)
                {
                    var indices = conditions == null ? null : new JArray(conditions.Select((condition, index) => new { condition, index })
                        .Where(entry => entry.condition.IsItemValid(item)).Select(entry => entry.index));
                    view["barter_condition_indices"] = indices;
                    view["matches_demand"] = indices == null ? JValue.CreateNull() : new JValue(indices.Count > 0);
                    view["budget_sufficient"] = null;
                }
                candidates.Add(view);
            }
        }
    }
}
