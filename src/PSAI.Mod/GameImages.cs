using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PSAI
{
    internal static class GameImages
    {
        private sealed class RequestState
        {
            internal Func<Rect> Bounds = null!;
            internal JObject? Result;
            internal object Context = null!;
            internal float RequestedAt;
        }
        private static readonly Dictionary<string, RequestState> requests = new Dictionary<string, RequestState>();
        private static readonly Queue<RequestState> pending = new Queue<RequestState>();
        private static int generation;

        internal static void Reset() { requests.Clear(); pending.Clear(); generation++; }

        internal static JObject Request(string target, Func<Rect> bounds, object context)
        {
            if (requests.TryGetValue(target, out var existing) && ReferenceEquals(existing.Context, context))
            {
                if (existing.Result == null && Time.realtimeSinceStartup - existing.RequestedAt > 5f)
                {
                    requests.Remove(target);
                    return new JObject { ["image_status"] = "error", ["error"] = "image_capture_timeout" };
                }
                if (existing.Result == null) return new JObject { ["image_status"] = "pending", ["target"] = target };
                requests.Remove(target);
                return (JObject) existing.Result.DeepClone();
            }
            var request = new RequestState { Bounds = bounds, Context = context, RequestedAt = Time.realtimeSinceStartup };
            requests[target] = request;
            pending.Enqueue(request);
            return new JObject { ["image_status"] = "pending", ["target"] = target };
        }

        internal static void Pump(MonoBehaviour runner)
        {
            if (pending.Count > 0) runner.StartCoroutine(Capture(pending.Dequeue(), generation));
        }

        private static IEnumerator Capture(RequestState request, int version)
        {
            yield return new WaitForEndOfFrame();
            if (version != generation) yield break;
            Texture2D? image = null;
            try
            {
                var bounds = request.Bounds();
                var x = Mathf.Clamp(Mathf.FloorToInt(bounds.xMin), 0, Screen.width);
                var y = Mathf.Clamp(Mathf.FloorToInt(bounds.yMin), 0, Screen.height);
                var width = Mathf.Clamp(Mathf.CeilToInt(bounds.xMax) - x, 0, Screen.width - x);
                var height = Mathf.Clamp(Mathf.CeilToInt(bounds.yMax) - y, 0, Screen.height - y);
                if (width == 0 || height == 0) throw new InvalidOperationException("image_target_not_visible");
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(x, y, width, height), 0, 0);
                image.Apply();
                request.Result = new JObject { ["image_status"] = "ready", ["captured_at"] = DateTimeOffset.UtcNow.ToString("o"),
                    ["image"] = new JObject { ["mime_type"] = "image/png", ["data"] = Convert.ToBase64String(image.EncodeToPNG()) } };
            }
            catch (Exception error) { request.Result = new JObject { ["image_status"] = "error", ["error"] = error.Message }; }
            finally { if (image != null) UnityEngine.Object.Destroy(image); }
        }

        internal static Rect VisibleRect(RectTransform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return Rect.zero;
            var canvas = target.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var first = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var last = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(Mathf.Min(first.x, last.x), Mathf.Min(first.y, last.y), Mathf.Max(first.x, last.x), Mathf.Max(first.y, last.y));
        }

        internal static Rect VisibleRect(SpriteRenderer target)
        {
            if (target == null || !target.enabled || !target.gameObject.activeInHierarchy || Camera.main == null) return Rect.zero;
            var min = Camera.main.WorldToScreenPoint(target.bounds.min);
            var max = Camera.main.WorldToScreenPoint(target.bounds.max);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }
    }
}
