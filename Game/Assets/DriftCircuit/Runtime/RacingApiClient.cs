using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DriftCircuit
{
    /// <summary>Account-linked save and settings contract backed by the Express/PostgreSQL API.</summary>
    internal interface IRacingSaveStore
    {
        bool IsSignedIn { get; }
        string Username { get; }
        string StatusText { get; }
        Task<bool> TryRestoreSessionAsync();
        Task<bool> SyncRecordAsync(CourseDefinition circuit, TrialRecord record);
        Task<bool> SyncSettingsAsync(bool showGhost, bool showBeacon);
        Task<string> FetchLeaderboardAsync();
    }

    /// <summary>In-memory store for deterministic Edit/Play Mode tests without network dependencies.</summary>
    internal sealed class InMemoryRacingSaveStore : IRacingSaveStore
    {
        private readonly Dictionary<string, TrialRecord> syncedRecords = new();
        private readonly List<(string Username, int Score)> board = new();

        public bool IsSignedIn { get; private set; }
        public string Username { get; private set; } = "Offline";
        public string StatusText => IsSignedIn ? $"Signed in: {Username}" : "Offline (local records)";
        internal bool LastGhostSetting { get; private set; }
        internal bool LastBeaconSetting { get; private set; }
        internal IReadOnlyDictionary<string, TrialRecord> SyncedRecords => syncedRecords;

        internal void SignInForTests(string username)
        {
            IsSignedIn = true;
            Username = string.IsNullOrWhiteSpace(username) ? "TestRacer" : username;
        }

        internal void SeedLeaderboard(string username, int score) => board.Add((username, score));

        public Task<bool> TryRestoreSessionAsync() => Task.FromResult(IsSignedIn);

        public Task<bool> SyncRecordAsync(CourseDefinition circuit, TrialRecord record)
        {
            if (!IsSignedIn || circuit == null || record == null)
                return Task.FromResult(false);
            syncedRecords[$"{circuit.Id}-r{circuit.Revision}"] = record;
            return Task.FromResult(true);
        }

        public Task<bool> SyncSettingsAsync(bool showGhost, bool showBeacon)
        {
            LastGhostSetting = showGhost;
            LastBeaconSetting = showBeacon;
            return Task.FromResult(IsSignedIn);
        }

        public Task<string> FetchLeaderboardAsync()
        {
            if (board.Count == 0)
                return Task.FromResult("No leaderboard entries");
            var sb = new StringBuilder();
            for (int i = 0; i < board.Count; i++)
                sb.AppendLine($"{i + 1}. {board[i].Username} ({board[i].Score})");
            return Task.FromResult(sb.ToString().TrimEnd());
        }
    }

    /// <summary>
    /// Talks to the racing Express API (`/api/auth/` and `/api/game/`), which persists
    /// accounts, slots, documents, and leaderboards in PostgreSQL. Never opens PostgreSQL directly.
    /// </summary>
    internal sealed class HttpRacingSaveStore : IRacingSaveStore, IDisposable
    {
        [Serializable]
        private sealed class TokenSession
        {
            public string access;
            public string refresh;
            public string username;
        }

        [Serializable]
        private sealed class SlotListResponse
        {
            public SlotSummary[] slots;
        }

        [Serializable]
        private sealed class SlotSummary
        {
            public int id;
            public int revision;
            public string character;
        }

        [Serializable]
        private sealed class SlotCreateResponse
        {
            public SlotSummary slot;
        }

        [Serializable]
        private sealed class MeResponse
        {
            public string username;
        }

        [Serializable]
        private sealed class SettingsPayload
        {
            public bool showGhost;
            public bool showBeacon;
        }

        private readonly HttpClient http;
        private readonly string baseUrl;
        private readonly string tokenPath;
        private readonly SemaphoreSlim refreshLock = new(1, 1);
        private TokenSession session;
        private int activeSlotId = -1;
        private int activeRevision = 0;

        public bool IsSignedIn => session != null && !string.IsNullOrEmpty(session.access);
        public string Username => IsSignedIn ? (session.username ?? "Racer") : "Offline";
        public string StatusText { get; private set; } = "Offline (local records)";

        internal HttpRacingSaveStore(string baseUrlOverride = null, string tokenPathOverride = null)
        {
            baseUrl = (baseUrlOverride ?? Environment.GetEnvironmentVariable("AXIOM_RACING_API_URL") ?? "http://localhost:8001").TrimEnd('/');
            tokenPath = tokenPathOverride ?? Path.Combine(Application.persistentDataPath, "racing-auth.json");
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        public void Dispose()
        {
            http.Dispose();
            refreshLock.Dispose();
        }

        public async Task<bool> TryRestoreSessionAsync()
        {
            try
            {
                if (!File.Exists(tokenPath))
                {
                    StatusText = "Offline (local records)";
                    return false;
                }
                session = JsonUtility.FromJson<TokenSession>(File.ReadAllText(tokenPath));
                if (session == null || string.IsNullOrEmpty(session.access))
                {
                    StatusText = "Offline (local records)";
                    return false;
                }
                using HttpResponseMessage me = await SendAuthorizedAsync(HttpMethod.Get, "/api/auth/me/", null);
                if (!me.IsSuccessStatusCode)
                {
                    session = null;
                    StatusText = "Session expired (local records)";
                    return false;
                }
                string json = await me.Content.ReadAsStringAsync();
                MeResponse profile = JsonUtility.FromJson<MeResponse>(json);
                if (!string.IsNullOrEmpty(profile?.username))
                    session.username = profile.username;
                StatusText = $"API: {Username}";
                return true;
            }
            catch
            {
                StatusText = "API offline (local records)";
                return false;
            }
        }

        public async Task<bool> SyncRecordAsync(CourseDefinition circuit, TrialRecord record)
        {
            if (!IsSignedIn || circuit == null || record == null)
                return false;
            try
            {
                if (!await EnsureSlotAsync())
                    return false;
                string docKey = $"trial_{circuit.Id.Replace('-', '_')}_r{circuit.Revision}";
                string token = Guid.NewGuid().ToString("N");
                string recordJson = JsonUtility.ToJson(record);
                int bestMs = record.BestTotal > 0d ? Mathf.RoundToInt((float)(record.BestTotal * 1000d)) : 0;
                string body = $"{{\"expectRevision\":{activeRevision},\"token\":\"{token}\"," +
                    $"\"slot\":{{\"score\":{bestMs},\"level\":1,\"character\":\"drift_circuit\"}}," +
                    $"\"docs\":[{{\"key\":\"{docKey}\",\"schemaVersion\":1,\"value\":{recordJson}}}]}}";
                using HttpResponseMessage res = await SendAuthorizedAsync(HttpMethod.Post, $"/api/game/slots/{activeSlotId}/commit/", body);
                if (!res.IsSuccessStatusCode)
                    return false;
                activeRevision++;
                StatusText = $"Synced: {Username}";
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SyncSettingsAsync(bool showGhost, bool showBeacon)
        {
            if (!IsSignedIn)
                return false;
            try
            {
                string json = JsonUtility.ToJson(new SettingsPayload { showGhost = showGhost, showBeacon = showBeacon });
                using HttpResponseMessage res = await SendAuthorizedAsync(HttpMethod.Put, "/api/game/account-docs/user_settings/", json);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> FetchLeaderboardAsync()
        {
            try
            {
                using HttpResponseMessage res = await http.GetAsync($"{baseUrl}/api/game/leaderboard/");
                if (!res.IsSuccessStatusCode)
                    return "Leaderboard unavailable";
                return await res.Content.ReadAsStringAsync();
            }
            catch
            {
                return "Leaderboard offline";
            }
        }

        private async Task<bool> EnsureSlotAsync()
        {
            if (activeSlotId > 0)
                return true;
            using HttpResponseMessage listRes = await SendAuthorizedAsync(HttpMethod.Get, "/api/game/slots/", null);
            if (!listRes.IsSuccessStatusCode)
                return false;
            string listJson = await listRes.Content.ReadAsStringAsync();
            // Express returns an array or {slots:[...]}; handle both safely.
            string wrapped = listJson.TrimStart().StartsWith("[") ? $"{{\"slots\":{listJson}}}" : listJson;
            SlotListResponse parsed = JsonUtility.FromJson<SlotListResponse>(wrapped);
            if (parsed?.slots != null && parsed.slots.Length > 0)
            {
                activeSlotId = parsed.slots[0].id;
                activeRevision = parsed.slots[0].revision;
                return true;
            }
            using HttpResponseMessage createRes = await SendAuthorizedAsync(HttpMethod.Post, "/api/game/slots/", "{\"character\":\"drift_circuit\"}");
            if (!createRes.IsSuccessStatusCode)
                return false;
            string createJson = await createRes.Content.ReadAsStringAsync();
            SlotSummary created = JsonUtility.FromJson<SlotCreateResponse>(createJson)?.slot ??
                JsonUtility.FromJson<SlotSummary>(createJson);
            if (created == null || created.id <= 0)
                return false;
            activeSlotId = created.id;
            activeRevision = created.revision;
            return true;
        }

        private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path, string jsonBody)
        {
            HttpResponseMessage response = await SendOnceAsync(method, path, jsonBody);
            if (response.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrEmpty(session?.refresh))
                return response;
            response.Dispose();
            if (!await RefreshSingleFlightAsync())
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return await SendOnceAsync(method, path, jsonBody);
        }

        private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, string jsonBody)
        {
            using var req = new HttpRequestMessage(method, baseUrl + path);
            if (!string.IsNullOrEmpty(session?.access))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.access);
            if (jsonBody != null)
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            return await http.SendAsync(req);
        }

        private async Task<bool> RefreshSingleFlightAsync()
        {
            await refreshLock.WaitAsync();
            try
            {
                if (string.IsNullOrEmpty(session?.refresh))
                    return false;
                string body = $"{{\"refresh\":\"{session.refresh}\"}}";
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/auth/refresh/")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                using HttpResponseMessage res = await http.SendAsync(req);
                if (!res.IsSuccessStatusCode)
                    return false;
                TokenSession updated = JsonUtility.FromJson<TokenSession>(await res.Content.ReadAsStringAsync());
                if (updated == null || string.IsNullOrEmpty(updated.access))
                    return false;
                session.access = updated.access;
                if (!string.IsNullOrEmpty(updated.refresh))
                    session.refresh = updated.refresh;
                File.WriteAllText(tokenPath, JsonUtility.ToJson(session));
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                refreshLock.Release();
            }
        }
    }
}
