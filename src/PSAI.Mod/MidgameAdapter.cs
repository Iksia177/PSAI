using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PSAI
{
    internal sealed partial class GameAdapter
    {
        private readonly Dictionary<string, Action<JObject>> parameterActions = new Dictionary<string, Action<JObject>>();
        private readonly Dictionary<string, Func<string>> visibleTooltips = new Dictionary<string, Func<string>>();
        private readonly Dictionary<string, Func<Rect>> imageTargets = new Dictionary<string, Func<Rect>>();
        private readonly Dictionary<string, object> imageContexts = new Dictionary<string, object>();

        private void ClearMidgame() { ClearMidgameViews(); inspectionItem = null; inspectionImageContext = null; ClearFirearm(); GameImages.Reset(); }
        private void ClearMidgameViews() { parameterActions.Clear(); visibleTooltips.Clear(); imageTargets.Clear(); imageContexts.Clear(); inspectionView = null; }

        private void CaptureMidgame(JObject snapshot, JArray screens, JArray available, JArray stock)
        {
            var store = PlayerStore.Instance;
            var map = MapUIManager.Instance;
            CaptureInteractions(snapshot, screens, available);
            CaptureInspection(snapshot, screens, available);
            var confirmation = GenericConfirmationUIManager.Instance;
            AddScreen("generic_confirmation", confirmation?.panel, screens, available);
            if (confirmation != null && confirmation.panel.activeInHierarchy)
            {
                if (confirmation.confirmButton.activeInHierarchy)
                    AddAction(available, "confirm_prompt", Text(confirmation.confirmButtonTMP), confirmation.ConfirmButtonPressed);
                if (confirmation.cancelButton.activeInHierarchy)
                    AddAction(available, "cancel_prompt", Text(confirmation.cancelButtonTMP), confirmation.CancelButtonPressed);
            }
            snapshot["save_context"] = new JObject { ["slot"] = store.saveSlotId, ["run"] = store.runID,
                ["upgrade_days_since_review"] = store.daySinceLastBrowseUpgrade,
                ["upgrade_history_requires_current_state_check"] = true,
                ["upgrade_history"] = ReadUpgradeHistory() };
            snapshot["location"] = map == null || !map.IsOutside ? "store" : map.Current?.Id;
            var phone = PhoneUIManager.Instance;
            AddScreen("phone", phone?.phonePanel, screens, available);
            AddScreen("phone_book", phone?.phoneBookUIManager?.layoutGroup, screens, available);
            if (phone != null && phone.phonePanel.activeInHierarchy)
            {
                snapshot["phone"] = new JObject { ["number"] = Text(phone.numberDisplay), ["status"] = Text(phone.statusDisplay) };
                AddAction(available, "close_phone", "Hang up and close the telephone", phone.CloseUI);
                AddAction(available, "hang_up", "Hang up the current telephone call", phone.StopCall);
                available.Add(new JObject { ["action"] = "dial_phone", ["label"] = "Dial a number using the normal telephone", ["parameters"] = new JArray("number"), ["enabled"] = !IsBusy(store) });
            }
            if (!IsBusy(store))
            {
                var phoneObject = FindCached(ref cachedPhone);
                if (phoneObject != null && phoneObject.isActiveAndEnabled && phoneObject.gameObject.activeInHierarchy)
                    AddAction(available, "open_phone", "Pick up the telephone", phoneObject.OnClick);
            }
            CapturePhoneDialogue(snapshot, available);
            var paper = PaperUIManager.Instance;
            if (paper != null)
            {
                AddScreen("wanted_paper", paper.wantedPanel, screens, available);
                AddScreen("paper", paper.panel, screens, available);
                AddScreen("book", paper.bookPanel, screens, available);
                if (paper.wantedPanel.activeInHierarchy)
                {
                    AddAction(available, "close_wanted", "Close the wanted document", paper.CloseWantedPaper);
                    imageTargets["wanted_paper"] = () => GameImages.VisibleRect(paper.wantedPanel.GetComponent<RectTransform>());
                    imageContexts["wanted_paper"] = paper.wantedPanel;
                }
                if (paper.panel.activeInHierarchy) AddAction(available, "close_paper", "Close the current paper", paper.CloseUI);
                if (paper.bookPanel.activeInHierarchy) AddAction(available, "close_book", "Close the current book", paper.CloseBook);
                var wantedDocument = items.Values.FirstOrDefault(item => item.GetDisplayName(true) == "治安部通缉名单");
                if (wantedDocument != null && !IsBusy(store)) AddAction(available, "open_wanted", "Read the acquired wanted document", paper.OpenWantedPaper);
            }
            var security = StoreSecButton.Instance;
            if (security != null && security.gameObject.activeInHierarchy && security.IsTooltipEnabled())
            {
                visibleTooltips["security"] = security.GetTooltip;
                snapshot["security"] = new JObject { ["prompt"] = security.GetTooltip(), ["identity_basis"] = "original_identity_fields_and_visible_evidence" };
                if (!IsBusy(store) && store.currentClientInstance != null && security.IsClientArrestable())
                {
                    AddAction(available, "report_customer", "Use the security button after assessing visible evidence", security.OnClick);
                    parameterActions["report_customer"] = args => {
                        if (string.IsNullOrWhiteSpace(args.Value<string>("evidence"))) throw new InvalidOperationException("report_evidence_required");
                        if (!store.IsClientPresent() || !security.IsClientArrestable()) throw new InvalidOperationException("report_not_available");
                        security.OnClick();
                    };
                }
            }
            var customer = StoreClientMono.Instance;
            var observedClient = store.currentClientInstance;
            if (customer != null && customer.image != null && customer.image.enabled && customer.image.gameObject.activeInHierarchy && observedClient != null && store.IsClientPresent())
            {
                imageContexts["customer_portrait"] = observedClient;
                imageTargets["customer_portrait"] = () => ReferenceEquals(store.currentClientInstance, observedClient) ? GameImages.VisibleRect(customer.image) : Rect.zero;
            }

            foreach (var tooltip in UnityEngine.Object.FindObjectsOfType<TooltipInterface>())
            {
                if (!tooltip.isActiveAndEnabled || !tooltip.IsTooltipEnabled()) continue;
                var id = "fixture-" + tooltip.GetInstanceID();
                visibleTooltips[id] = tooltip.GetTooltip;
            }
            snapshot["image_targets"] = new JArray(imageTargets.Keys);

            var clientele = ClienteleUIManager.Instance;
            AddScreen("clientele", clientele?.panel, screens, available);
            var display = FindCached(ref cachedDisplay);
            if (clientele != null && display != null && display.gameObject.activeInHierarchy)
            {
                if (clientele.panel.activeInHierarchy)
                {
                    AddAction(available, "close_clientele", "Close the display-case customer effects", clientele.CloseUI);
                    visibleTooltips["display_case"] = () => string.Join("\n", clientele.panel.GetComponentsInChildren<TextMeshProUGUI>(false).Select(Text).Where(text => text.Length > 0));
                }
                else AddAction(available, "open_clientele", "Read the display-case customer effects", display.OnClick);
            }
            snapshot["facilities"] = new JArray(visibleTooltips.Keys.Select(id => new JObject { ["id"] = id, ["detail"] = "tooltip" }));

            var wild = WildUIManager.Instance;
            AddScreen("upgrades_legacy", wild?.panel, screens, available);
            var network = CustomUIManager.Instance?.GetWindow(WildNetworkUI.WINDOW_ID);
            AddScreen("upgrades", network?.root, screens, available);
            if (network != null && network.IsVisible)
            {
                foreach (var slot in network.root.GetComponentsInChildren<WildNetworkSlot>(false))
                {
                    if (string.IsNullOrEmpty(slot.id)) continue;
                    var id = "upgrade-slot-" + slot.GetInstanceID();
                    var label = string.Join(" ", slot.GetComponentsInChildren<TextMeshProUGUI>(false).Select(Text).Where(text => text.Length > 0));
                    AddAction(available, id, label.Length == 0 ? "Select the visible upgrade slot" : label, () => slot.onClick?.Invoke(slot.id));
                }
                AddAction(available, "close_upgrades", "Close the upgrade network", WildNetworkUI.Close);
            }
            if (wild != null && wild.panel.activeInHierarchy) AddAction(available, "close_upgrades_legacy", "Close the upgrade network", wild.CloseUI);

            if (map == null) return;
            AddScreen("outside_map", map.mapPanel, screens, available);
            AddScreen("outside_map_controls", CustomUIManager.Instance?.GetWindow(MapUIManager.PANEL_WINDOW_ID)?.root, screens, available);
            AddScreen("outside_confirmation", map.confirmationPanel, screens, available);
            AddScreen("food_shop", CommissaryUIManager.Instance?.panel, screens, available);
            if (map.Current != null && map.IsOutside)
            {
                AddScreen("location_panel", map.Current.PanelObject, screens, available);
                AddScreen("location_controls", CustomUIManager.Instance?.GetWindow("afterhour_location")?.root, screens, available);
                AddAction(available, "return_store", "Return using the normal leave confirmation", map.Return);
            }
            if (map.IsOutside)
            {
                // The loose afterhour inventory is the ground; only pocket slots travel home.
                AddInventory("afterhour_ground", EmporiumEntry.Instance.afterhourInventory, EmporiumEntry.Instance.afterhourWindow, stock);
                AddInventory("afterhour_left", EmporiumEntry.Instance.afterhourPocketSlotInvLeft, EmporiumEntry.Instance.afterhourWindow, stock, null, "口袋1");
                AddInventory("afterhour_right", EmporiumEntry.Instance.afterhourPocketSlotInvRight, EmporiumEntry.Instance.afterhourWindow, stock, null, "口袋2");
                AddInventory("afterhour_backpack", EmporiumEntry.Instance.afterhourPocketSlotInvBackpack, EmporiumEntry.Instance.afterhourWindow, stock, null, "背包槽");
                AddInventory("bazaar", EmporiumEntry.Instance.bazarLeftinvElement, EmporiumEntry.Instance.bazarLeftInvWindow, stock);
            }
            var destinations = new JArray();
            if (map.mapPanel != null && map.mapPanel.activeInHierarchy)
            {
                foreach (var location in AfterhourLocation.GetDiscovered())
                {
                    var destination = location;
                    var id = "travel-" + location.Id;
                    var enabled = location.IsAvailable() && !map.IsInTransit;
                    destinations.Add(new JObject { ["id"] = location.Id, ["name"] = location.GetTitle(), ["available"] = enabled });
                    available.Add(new JObject { ["action"] = id, ["label"] = location.GetTitle(), ["enabled"] = enabled });
                    if (enabled) actions[id] = () => { if (!destination.IsDiscovered() || !destination.IsAvailable()) throw new InvalidOperationException("destination_unavailable"); map.Travel(destination); };
                }
            }
            snapshot["destinations"] = destinations;
            var door = FindCached(ref cachedDoor);
            if (CanStartOuting(door))
                AddAction(available, "open_outside_confirmation", "Use the original door to begin an outing", () => {
                    if (!CanStartOuting(door)) throw new InvalidOperationException("outing_not_available");
                    door!.OnClick();
                });
            // Some built-in locations use legacy panels instead of PanelObject.
            AddScreen("scavenging", map.UIScavengingPanel, screens, available);
            AddScreen("technology_shop", map.UIUpgradeMerchantPanel, screens, available);
            AddScreen("commissary", map.UICommissaryPanel, screens, available);
        }

        private void CapturePhoneDialogue(JObject snapshot, JArray available)
        {
            var dialogue = DialoguePhoneUIManager.Instance;
            if (dialogue == null || dialogue.currentDialog == null) return;
            snapshot["phone_dialogue"] = new JObject { ["customer"] = Text(dialogue.clientBubble?.textfield),
                ["player"] = Text(dialogue.playerBubble?.textfield), ["name"] = Text(dialogue.clientNameTMP) };
            if (IsBusy(PlayerStore.Instance)) return;
            AddAction(available, "phone_advance", "Advance the displayed telephone dialogue", dialogue.OnAdvancePressed);
            foreach (var bubble in dialogue.dialogChoiceBubbles)
            {
                if (bubble == null || !bubble.gameObject.activeInHierarchy) continue;
                var index = bubble.index;
                if (index < 0 || index >= dialogue.currentDialog.choices.Count) continue;
                var enabled = dialogue.currentDialog.IsChoiceEnabledAt(index);
                var id = "phone-choice-" + index;
                var choiceItem = dialogue.currentDialog.choices[index].Item1;
                available.Add(new JObject { ["action"] = id, ["label"] = Text(bubble.TMP), ["enabled"] = enabled,
                    ["tooltip_target"] = choiceItem == null ? null : RegisterItem(choiceItem),
                    ["fully_displayed"] = bubble.TMP != null && bubble.TMP.GetParsedText().Length <= bubble.TMP.maxVisibleCharacters });
                if (enabled) actions[id] = bubble.OnDialogChoiceClick;
            }
        }

        private void RegisterPaymentButton(Button button, string action, JArray available)
        {
            var frame = button.GetComponentInParent<WildNetworkFrame>();
            var wild = WildUIManager.Instance;
            var modern = frame != null && button == frame.useButton;
            var legacy = wild != null && button == wild.unlockButton;
            if (!modern && !legacy) return;
            Func<int> currentCost = () => modern ? UpgradePayment.ReadPrice(frame!.usePrice.text) : PlayerStore.Instance.networkUpgrade[wild!.currentSelectedId].GetCost();
            var policy = new JObject { ["allow_cash_default"] = false };
            try
            {
                policy["cost"] = currentCost();
                policy["favor"] = PlayerStore.Instance.wildFavor;
                policy["default_payment_allowed"] = PlayerStore.Instance.wildFavor >= policy.Value<int>("cost");
            }
            catch (InvalidOperationException error) { policy["error"] = error.Message; }
            available.OfType<JObject>().Last(option => option.Value<string>("action") == action)["payment"] = policy;
            parameterActions[action] = args => {
                if (!button.isActiveAndEnabled || !button.IsInteractable()) throw new InvalidOperationException("button_not_available");
                // TMP's parsed mesh can lag behind a selection made this frame.
                // Validate the current source text used by the original price label.
                var cost = currentCost();
                var title = modern ? Text(frame!.infoTitle) : Text(wild!.title);
                UpgradePayment.Validate(cost, PlayerStore.Instance.wildFavor, PlayerStore.Instance.playerCash, args.Value<bool?>("allow_cash") == true);
                var history = ReadUpgradeHistory();
                var cash = PlayerStore.Instance.playerCash;
                var favor = PlayerStore.Instance.wildFavor;
                button.onClick.Invoke();
                var entry = new JObject { ["day"] = StoreStation.Instance.dayCounter, ["title"] = title,
                    ["favor_spent"] = favor - PlayerStore.Instance.wildFavor, ["cash_spent"] = cash - PlayerStore.Instance.playerCash };
                if (cost == 0 || entry.Value<int>("favor_spent") > 0 || entry.Value<int>("cash_spent") > 0)
                {
                    ((JArray) history["purchases"]!).Add(entry);
                    ModHelper.WriteModDataFileText("psai", HistoryPath(), history.ToString());
                }
            };
        }

        private static string HistoryPath()
        {
            var store = PlayerStore.Instance;
            var run = Regex.Replace(store.runID ?? "", "[^a-zA-Z0-9-]", "_");
            return "upgrade-history-" + store.saveSlotId + "-" + store.runNumber + "-" + run + ".json";
        }

        private static JObject ReadUpgradeHistory()
        {
            if (!ModHelper.ModDataFileExists("psai", HistoryPath())) return new JObject { ["purchases"] = new JArray() };
            try
            {
                var history = JObject.Parse(ModHelper.ReadModDataFileText("psai", HistoryPath()));
                if (history["purchases"] is JArray) return history;
            }
            catch (Newtonsoft.Json.JsonException) { }
            return new JObject { ["purchases"] = new JArray(), ["error"] = "upgrade_history_unreadable" };
        }

        private JObject? InspectMidgame(string target, string detail, JObject args)
        {
            if (detail == "appraisal") return InspectAppraisal(target);
            if (detail == "operations" || detail == "capacity" || detail == "liquid") return InspectInteractions(target, detail);
            if (detail == "image")
            {
                if (!imageTargets.TryGetValue(target, out var bounds)) throw new InvalidOperationException("image_target_not_visible");
                return GameImages.Request(target, bounds, imageContexts[target]);
            }
            if (detail == "tooltip")
            {
                if (visibleTooltips.TryGetValue(target, out var tooltip)) return new JObject { ["id"] = target, ["text"] = tooltip() };
                if (target == "display_case") throw new InvalidOperationException("open_clientele_required");
                if (!items.TryGetValue(target, out var item) || !(item is GameItemElement element)) throw new InvalidOperationException("item_not_visible");
                return new JObject { ["id"] = target, ["text"] = element.GetTooltip().nakedText,
                    ["owned"] = GeneralHelper.IsItemEffectivelyOwned(item),
                    ["can_remove"] = item.MayRemove(), ["can_activate"] = item.MayActivateSlotItem() && item.CanActivateSlotItem(),
                    ["can_toggle"] = item.MayToggleSlotItem() && item.CanToggleSlotItem() };
            }
            if (detail != "use_conditions") return null;
            if (!items.TryGetValue(args.Value<string>("item") ?? "", out var source) || !items.TryGetValue(target, out var destination))
                throw new InvalidOperationException("item_not_visible");
            var may = source.MayTarget(destination);
            return new JObject { ["item"] = args["item"], ["target"] = target, ["may_use"] = may, ["can_use"] = may && source.CanTarget(destination) };
        }

        private JObject? ExecuteMidgame(JObject args)
        {
            var action = args.Value<string>("action") ?? "";
            if (parameterActions.TryGetValue(action, out var callback)) { callback(args); return new JObject { ["invoked"] = true }; }
            if (action == "dial_phone")
            {
                var phone = PhoneUIManager.Instance;
                var number = args.Value<string>("number") ?? "";
                if (phone == null || !phone.phonePanel.activeInHierarchy) throw new InvalidOperationException("phone_not_open");
                if (!Regex.IsMatch(number, "^[0-9]{1,9}$")) throw new InvalidOperationException("invalid_phone_number");
                phone.StopCall();
                foreach (var digit in number) phone.PressNumber(digit - '0');
                phone.StartCalling();
                return new JObject { ["invoked"] = true, ["number"] = number };
            }
            if (action != "use_item_on" && action != "toggle_item") return null;
            if (PlayerStore.Instance.lockAllInventoryFlag > 0) throw new InvalidOperationException("inventory_locked");
            if (!items.TryGetValue(args.Value<string>("item") ?? "", out var item)) throw new InvalidOperationException("item_not_visible");
            if (action == "toggle_item")
            {
                if (!item.MayToggleSlotItem() || !item.CanToggleSlotItem()) throw new InvalidOperationException("toggle_not_allowed");
                item.ToggleSlotItem();
            }
            else
            {
                if (!items.TryGetValue(args.Value<string>("target") ?? "", out var target)) throw new InvalidOperationException("target_not_visible");
                if (!item.MayTarget(target) || !item.CanTarget(target)) throw new InvalidOperationException("use_not_allowed");
                item.Target(target);
            }
            return new JObject { ["invoked"] = true };
        }
    }
}
