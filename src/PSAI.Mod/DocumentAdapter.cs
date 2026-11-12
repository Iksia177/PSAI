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
        private readonly Dictionary<string, JObject> documentViews = new Dictionary<string, JObject>();
        private readonly Dictionary<string, Tuple<GameObject, string, object>> documentContexts = new Dictionary<string, Tuple<GameObject, string, object>>();

        private void CaptureDocuments(JObject snapshot, JArray screens, JArray available)
        {
            var paper = PaperUIManager.Instance;
            if (paper != null)
            {
                AddDocument("paper", paper.panel, paper.CloseUI, screens, available);
                var book = AddDocument("book", paper.bookPanel, paper.CloseBook, screens, available);
                if (book != null)
                {
                    book["title"] = PromptText(paper.bookTitle);
                    book["subtitle"] = PromptText(paper.bookSubtitle);
                    book["page_text"] = PromptText(paper.bookPage);
                    book["body"] = PromptText(paper.bookBody);
                    book["index_visible"] = paper.indexGroup != null && paper.indexGroup.activeInHierarchy;
                    if (PaperUIManager.currentBook != null)
                    {
                        book["item_id"] = RegisterItem(PaperUIManager.currentBook);
                        book["name"] = PaperUIManager.currentBook.GetDisplayName(true);
                    }
                    AddDocumentButton(book, paper.leftButton?.gameObject, "book_previous", "上一页", paper.BookLeftClick, available);
                    AddDocumentButton(book, paper.rightButton?.gameObject, "book_next", "下一页", paper.BookRightClick, available);
                }
                AddDocument("cigarette_guide", paper.cigaretteGuidePanel, paper.CloseCigaretteGuide, screens, available);
                AddDocument("stamp_guide", paper.foodStampGuidePanel, paper.CloseStampGuide, screens, available);
                AddDocument("injector_guide", paper.injectorGuidePanel, paper.CloseInjectorGuide, screens, available);
                AddDocument("cigarette_colors", paper.cigaretteColorPanel, paper.CloseCigaretteColor, screens, available);
                AddDocument("code_book", paper.codeBookPanel, paper.CloseCodeBook, screens, available);
                AddDocument("augmentation_guide", paper.augIdGuide, paper.CloseAugIdGuide, screens, available);
                AddDocument("lottery_info", paper.lotteryInfo, paper.CloseLotteryInfo, screens, available);
                AddDocument("contract", paper.contractPanel, paper.CloseContract, screens, available);
                AddDocument("deed", paper.deedPanel, paper.CloseDeed, screens, available);
                AddDocument("procurement_slip", paper.slipPanel, paper.CloseSlipUI, screens, available);
                AddDocument("general_slip", paper.generalSlipPanel, paper.CloseGeneralSlipUI, screens, available);
            }
            var multi = MultiPageUIManager.Instance;
            if (multi != null && multi.panel != null && multi.panel.activeInHierarchy)
            {
                var current = multi.books?.FirstOrDefault(root => root != null && root.activeInHierarchy);
                GameObject? page = null;
                var index = -1;
                if (current != null)
                    for (var i = 0; i < current.transform.childCount; i++)
                        if (current.transform.GetChild(i).gameObject.activeInHierarchy)
                        {
                            page = current.transform.GetChild(i).gameObject;
                            index = i;
                            break;
                        }
                var view = AddDocument("multipage_guide", multi.panel, multi.CloseUI, screens, available, page);
                if (view != null)
                {
                    view["page_number"] = index < 0 ? JValue.CreateNull() : new JValue(index + 1);
                    view["page_count"] = current == null ? JValue.CreateNull() : new JValue(current.transform.childCount);
                    view["page_text"] = PromptText(multi.pageNumberText);
                    AddDocumentButton(view, multi.buttonLeft, "multipage_previous", "上一页", multi.FlipLeftClicked, available);
                    AddDocumentButton(view, multi.buttonRight, "multipage_next", "下一页", multi.FlipRightClicked, available);
                }
            }
            foreach (var id in documentContexts.Keys.Where(id => !documentViews.ContainsKey(id)).ToArray()) documentContexts.Remove(id);
            snapshot["documents"] = new JArray(documentViews.Values);
        }

        private JObject? AddDocument(string id, GameObject? root, Action close, JArray screens, JArray available, GameObject? page = null)
        {
            if (root == null || !root.activeInHierarchy) return null;
            var screen = screens.OfType<JObject>().FirstOrDefault(value => value.Value<string>("id") == id);
            if (screen == null)
            {
                AddScreen(id, root, screens, available);
                screen = screens.OfType<JObject>().Last(value => value.Value<string>("id") == id);
            }
            var body = page ?? root;
            var bodyText = body.GetComponentsInChildren<TMP_Text>(false)
                .Where(field => field.GetComponentInParent<Button>() == null).Select(PromptText)
                .Concat(body.GetComponentsInChildren<UnityEngine.UI.Text>(false)
                    .Where(field => field.isActiveAndEnabled && field.GetComponentInParent<Button>() == null).Select(field => field.text))
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
            var text = body.GetComponentsInChildren<TMP_Text>(false).Select(PromptText)
                .Concat(body.GetComponentsInChildren<UnityEngine.UI.Text>(false).Where(field => field.isActiveAndEnabled).Select(field => field.text))
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
            var graphics = body.GetComponentsInChildren<Image>(false).Count(image => image.isActiveAndEnabled && image.sprite != null && image.color.a > 0);
            var signature = body.GetInstanceID() + ":" + string.Join("\n", text);
            if (!documentContexts.TryGetValue(id, out var previous) || previous.Item1 != root || previous.Item2 != signature)
                documentContexts[id] = Tuple.Create(root, signature, new object());
            var context = documentContexts[id];
            imageContexts[id] = context.Item3;
            imageTargets[id] = () => {
                if (root == null || !root.activeInHierarchy || body == null || !body.activeInHierarchy ||
                    !documentContexts.TryGetValue(id, out var current) || !ReferenceEquals(current.Item3, context.Item3))
                    throw new InvalidOperationException("document_page_changed_or_closed");
                return GameImages.VisibleRect(body.GetComponent<RectTransform>() ?? root.GetComponent<RectTransform>());
            };
            var view = new JObject {
                ["id"] = id, ["text"] = new JArray(text), ["graphic_count"] = graphics,
                ["body_text"] = new JArray(bodyText),
                ["text_status"] = bodyText.Length == 0 ? "no_visible_body_text" : "visible_text",
                ["image_target"] = id, ["action_ids"] = screen["action_ids"]!.DeepClone(),
                ["source"] = "current_visible_document_page",
                ["reading_note"] = "仅提供当前可见页。图案或排版文字不足时按需 inspect image；没有文本不等于文档没有内容。"
            };
            documentViews[id] = view;
            screen["text"] = new JArray(text);
            screen["document_id"] = id;
            var closeId = id == "multipage_guide" ? "close_multipage_guide" : "close_" + id;
            if (!available.OfType<JObject>().Any(action => action.Value<string>("action") == closeId))
                AddAction(available, closeId, "关闭当前文档", () => {
                    if (root == null || !root.activeInHierarchy) throw new InvalidOperationException("document_not_open");
                    close();
                });
            ((JArray) view["action_ids"]!).Add(closeId);
            return view;
        }

        private void AddDocumentButton(JObject view, GameObject? root, string id, string label, Action fallback, JArray available)
        {
            if (root == null || !root.activeInHierarchy) return;
            var button = root.GetComponent<Button>();
            var enabled = button == null || (button.isActiveAndEnabled && button.IsInteractable());
            available.Add(new JObject { ["action"] = id, ["label"] = label, ["enabled"] = enabled });
            ((JArray) view["action_ids"]!).Add(id);
            if (enabled) actions[id] = () => {
                if (root == null || !root.activeInHierarchy || (button != null && (!button.isActiveAndEnabled || !button.IsInteractable())))
                    throw new InvalidOperationException("document_navigation_not_available");
                if (button != null) button.onClick.Invoke(); else fallback();
            };
        }

        private JObject InspectDocument(string target)
        {
            if (!documentViews.TryGetValue(target, out var view)) throw new InvalidOperationException("document_not_open");
            return (JObject) view.DeepClone();
        }
    }
}
