using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PSAI
{
    internal sealed partial class GameAdapter
    {
        private void CaptureCustomerInteraction(JObject snapshot, JArray screens, JArray available)
        {
            var store = PlayerStore.Instance;
            var client = store.isClientArrived ? store.currentClientInstance?.GetClientBlueprint() : null;
            var dialogue = DialogUIManager.Instance;
            var choices = available.OfType<JObject>().Where(action =>
                (action.Value<string>("action") ?? "").StartsWith("choice-", StringComparison.Ordinal)).ToArray();
            var active = client != null && (dialogue.currentDialog != null || dialogue.isWaitingForManualAdvance);
            var eventDialogue = client != null && client.clientIntent.ToString() == "DIALOGUE";
            var tradeActions = new HashSet<string>(screens.OfType<JObject>().Where(screen =>
                screen.Value<string>("id") == "trade" || screen.Value<string>("id") == "bargain")
                .SelectMany(screen => ((JArray) screen["action_ids"]!).Values<string>()).OfType<string>());
            tradeActions.Add("open_trade");
            var primary = new JArray();
            var secondary = new JArray();
            foreach (var action in available.OfType<JObject>())
            {
                var id = action.Value<string>("action") ?? "";
                var channel = IsDialogueAction(id) ? "dialogue" : tradeActions.Contains(id) ? "trade" :
                    id == "dismiss_customer" || id == "call_next" ? "customer_management" : "other";
                if (channel == "other") continue;
                action["channel"] = channel;
                var recommended = client != null && action.Value<bool>("enabled") && (choices.Length > 0 ? id.StartsWith("choice-", StringComparison.Ordinal) :
                    active ? id == "dialogue_advance" : !eventDialogue && channel == "trade");
                action["recommended"] = recommended;
                if (recommended) primary.Add(id);
                else if (action.Value<bool>("enabled")) secondary.Add(id);
                if ((eventDialogue || choices.Length > 0) && channel != "dialogue")
                    action["context_note"] = "当前优先处理对白选项；普通交易或送客可能结束或中断该次事件。";
            }
            snapshot["customer_interaction"] = new JObject {
                ["customer_id"] = client == null ? null : firearmTarget,
                ["kind"] = client == null ? "none" : eventDialogue ? "event_dialogue" : choices.Length > 0 ? "dialogue_choice" : active ? "dialogue" : "trade",
                ["client_intent"] = client?.clientIntent.ToString(), ["dialogue_active"] = active,
                ["has_visible_choices"] = choices.Length > 0,
                ["primary_action_ids"] = primary, ["secondary_action_ids"] = secondary,
                ["instruction"] = "对白期间优先读完整选项并使用 primary_action_ids；送客不是事件拒绝选项的同义操作。次要动作保留原版可用性，由当前用户目标决定是否使用。"
            };
            if (snapshot["dialogue_state"] is JObject state)
            {
                state["has_visible_choices"] = choices.Length > 0;
                state["primary_action_ids"] = new JArray(primary.Values<string>().Where(id => IsDialogueAction(id!)));
            }
            if (snapshot["trade"] is JObject trade)
                trade["interaction_context"] = eventDialogue ? "dialogue_only" : "customer_trade";
        }
    }
}
