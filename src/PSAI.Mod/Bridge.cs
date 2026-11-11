using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PSAI
{
    internal sealed class Bridge
    {
        private readonly ModLog log;
        private readonly GameAdapter game = new GameAdapter();
        private readonly Dictionary<string, string> fingerprints = new Dictionary<string, string>();
        private string session = Guid.NewGuid().ToString("N");
        private long revision;
        private string previousSnapshot = "";
        private object? world;
        private float nextHeartbeat;
        private float nextPoll;
        private bool stopped;
        private string? lastError;
        private bool heartbeatLogged;
        private double directoryMilliseconds;
        private int directoryFileCount;
        private double snapshotMilliseconds;
        private int snapshotCount;
        private long requestsProcessed;

        internal Bridge(ModLog log) { this.log = log; }

        internal void Poll()
        {
            if (stopped || Time.realtimeSinceStartup < nextPoll) return;
            nextPoll = Time.realtimeSinceStartup + 0.1f;
            try
            {
                Tick();
                if (!heartbeatLogged)
                {
                    log.Log("PSAI heartbeat active.");
                    heartbeatLogged = true;
                }
            }
            catch (Exception error)
            {
                lastError = error.Message;
                log.Error("Bridge tick failed: " + error.Message);
                nextPoll = Time.realtimeSinceStartup + 2f;
            }
        }

        internal void Stop()
        {
            if (stopped) return;
            stopped = true;
            game.RestoreOptions();
            Write("status.json", Status(false));
        }

        private void Tick()
        {
            var currentWorld = game.World;
            if (!ReferenceEquals(world, currentWorld))
            {
                game.RestoreOptions();
                game.Clear();
                world = currentWorld;
                session = Guid.NewGuid().ToString("N");
                revision = 0;
                previousSnapshot = "";
                fingerprints.Clear();
            }
            if (Time.realtimeSinceStartup >= nextHeartbeat)
            {
                nextHeartbeat = Time.realtimeSinceStartup + 1f;
                Write("status.json", Status(true));
            }
            // One request per tick keeps state-changing operations serialized.
            var listingTimer = Stopwatch.StartNew();
            var files = ModHelper.ListModDataFiles("psai");
            directoryMilliseconds = listingTimer.Elapsed.TotalMilliseconds;
            directoryFileCount = files.Count;
            var requestFile = files
                .Where(path => path.StartsWith("request-", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal).FirstOrDefault();
            if (requestFile != null) Process(requestFile);
        }

        private JObject Status(bool online)
        {
            return new JObject {
                ["protocol_version"] = "0.1", ["session_id"] = session,
                ["online"] = online, ["ready"] = game.Ready,
                ["compatible"] = game.Compatible, ["game_version"] = PlayerStore.VERSION,
                ["updated_at"] = DateTimeOffset.UtcNow.ToString("o"),
                ["last_error"] = lastError,
                ["performance"] = new JObject { ["directory_scan_ms"] = directoryMilliseconds,
                    ["directory_file_count"] = directoryFileCount, ["requests_processed"] = requestsProcessed },
                ["tools"] = new JArray("status", "observe", "inspect", "actions", "act", "result"),
                ["features"] = new JArray("visible_tooltips", "targeted_item_use", "telephone", "upgrade_payment_guard", "afterhour", "visible_image_crops",
                    "response_choices", "contact_cards", "authorized_wanted_identity", "operation_discovery", "rent_obligations",
                    "outing_state", "firearm_actions", "visible_ownership", "scripted_dialogue",
                    "customer_context", "structured_trade", "daily_news", "customer_queue", "request_timings")
            };
        }

        private JObject Snapshot()
        {
            var timer = Stopwatch.StartNew();
            var snapshot = game.Capture();
            var serialized = snapshot.ToString(Formatting.None);
            if (serialized != previousSnapshot)
            {
                previousSnapshot = serialized;
                revision++;
            }
            snapshot["revision"] = revision;
            snapshotMilliseconds += timer.Elapsed.TotalMilliseconds;
            snapshotCount++;
            return snapshot;
        }

        private void Process(string path)
        {
            JObject request;
            try
            {
                var json = ModHelper.ReadModDataFileText("psai", path);
                if (json.Length > 65536) { ModHelper.DeleteModDataFile("psai", path); return; }
                request = JObject.Parse(json);
            }
            catch (JsonException)
            {
                log.Warning("Discarding invalid JSON request " + path);
                ModHelper.DeleteModDataFile("psai", path);
                return;
            }
            if (request["command_id"]?.Type != JTokenType.String || request["session_id"]?.Type != JTokenType.String)
            {
                ModHelper.DeleteModDataFile("psai", path);
                return;
            }
            var id = request.Value<string>("command_id") ?? "";
            var requestSession = request.Value<string>("session_id") ?? "";
            if (!SafeId(id) || !SafeId(requestSession) || path != "request-" + requestSession + "-" + id + ".json")
            {
                ModHelper.DeleteModDataFile("psai", path);
                return;
            }
            var resultPath = "result-" + requestSession + "-" + id + ".json";
            var fingerprint = request.ToString(Formatting.None);
            if (ModHelper.ModDataFileExists("psai", resultPath))
            {
                var prior = JObject.Parse(ModHelper.ReadModDataFileText("psai", resultPath));
                if (prior.Value<string>("status") == "pending" && requestSession != session)
                {
                    prior["status"] = "uncertain";
                    Write(resultPath, prior);
                }
                ModHelper.DeleteModDataFile("psai", path);
                return;
            }
            if (fingerprints.TryGetValue(id, out var existing))
            {
                // Keep the original receipt. A conflicting retry never overwrites it.
                if (existing != fingerprint) log.Warning("Conflicting retry for command " + id);
                ModHelper.DeleteModDataFile("psai", path);
                return;
            }
            var result = new JObject {
                ["session_id"] = requestSession, ["command_id"] = id, ["status"] = "completed",
                ["request"] = request.DeepClone()
            };
            var invoked = false;
            var requestTimer = Stopwatch.StartNew();
            snapshotMilliseconds = 0;
            snapshotCount = 0;
            try
            {
                if (requestSession != session) throw new InvalidOperationException("stale_session");
                var tool = request.Value<string>("tool");
                var args = request["arguments"] as JObject ?? new JObject();
                var before = Snapshot();
                switch (tool)
                {
                    case "observe": result["data"] = before; break;
                    case "actions": result["data"] = before["actions"]?.DeepClone(); break;
                    case "inspect": result["data"] = game.Inspect(args); break;
                    case "act":
                        if (!game.Ready) throw new InvalidOperationException("game_not_ready");
                        if (!game.Compatible) throw new InvalidOperationException("unsupported_game_version");
                        if (request.Value<long?>("expected_revision") != revision)
                            throw new InvalidOperationException("stale_revision");
                        // Persist intent before the side effect. On restart this receipt remains uncertain.
                        Write(resultPath, new JObject {
                            ["session_id"] = session, ["command_id"] = id, ["status"] = "pending",
                            ["request"] = request.DeepClone()
                        });
                        fingerprints[id] = fingerprint;
                        invoked = true;
                        revision++;
                        result["data"] = game.Execute(args);
                        result["snapshot"] = Snapshot();
                        break;
                    default: throw new InvalidOperationException("unknown_tool");
                }
            }
            catch (Exception error)
            {
                result["status"] = invoked ? "uncertain" : "error";
                result["error"] = error.Message;
            }
            // A completed receipt means the handler returned. Animations may still be busy.
            result["revision"] = revision;
            requestsProcessed++;
            result["performance"] = new JObject {
                ["handler_ms"] = requestTimer.Elapsed.TotalMilliseconds,
                ["snapshot_ms"] = snapshotMilliseconds, ["snapshot_count"] = snapshotCount,
                ["directory_scan_ms"] = directoryMilliseconds, ["directory_file_count"] = directoryFileCount,
                ["requests_processed"] = requestsProcessed
            };
            Write(resultPath, result);
            fingerprints[id] = fingerprint;
            ModHelper.DeleteModDataFile("psai", path);
            lastError = null;
        }

        private static bool SafeId(string value) { return Regex.IsMatch(value, "^[a-z0-9-]{1,64}$"); }
        private static void Write(string path, JObject data)
        {
            ModHelper.WriteModDataFileText("psai", path, data.ToString(Formatting.None));
        }
    }
}
