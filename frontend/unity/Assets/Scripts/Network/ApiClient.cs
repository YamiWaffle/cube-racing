using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace CubeRacing
{
    public class ApiException : Exception
    {
        public long   StatusCode   { get; }
        public string ResponseBody { get; }
        public ApiException(long statusCode, string body)
            : base($"API {statusCode}: {body}") { StatusCode = statusCode; ResponseBody = body; }
    }

    public class ApiClient
    {
        private readonly string  _baseUrl;
        private Func<string>     _tokenProvider;

        public ApiClient(string baseUrl) => _baseUrl = baseUrl.TrimEnd('/');

        public void SetTokenProvider(Func<string> provider) => _tokenProvider = provider;

        public UniTask<CreatePlayerResponse> CreatePlayerAsync(string nickname, CancellationToken ct = default)
            => PostAsync<CreatePlayerResponse>("/api/players",
               JsonConvert.SerializeObject(new { nickname }), withAuth: false, ct);

        public UniTask<CurrentSessionResponse> GetCurrentSessionAsync(CancellationToken ct = default)
            => GetAsync<CurrentSessionResponse>("/api/sessions/current", ct);

        public async UniTask PlaceBetAsync(Guid sessionId, int npcId, int amount, CancellationToken ct = default)
            => await PostAsync<object>($"/api/sessions/{sessionId}/bets",
               JsonConvert.SerializeObject(new { npcId, amount }), withAuth: true, ct);

        public UniTask<List<LeaderboardEntry>> GetLeaderboardAsync(CancellationToken ct = default)
            => GetAsync<List<LeaderboardEntry>>("/api/leaderboard", ct);

        public UniTask<Dictionary<string, List<int>>> GetCurrentSquaresAsync(CancellationToken ct = default)
            => GetAsync<Dictionary<string, List<int>>>("/api/sessions/current/squares", ct);

        private async UniTask<T> GetAsync<T>(string path, CancellationToken ct)
        {
            using var req = UnityWebRequest.Get(_baseUrl + path);
            AddAuthHeader(req);
            await req.SendWebRequest().ToUniTask(cancellationToken: ct);
            EnsureSuccess(req);
            return JsonConvert.DeserializeObject<T>(req.downloadHandler.text);
        }

        private async UniTask<T> PostAsync<T>(string path, string json, bool withAuth, CancellationToken ct)
        {
            using var req = new UnityWebRequest(_baseUrl + path, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (withAuth) AddAuthHeader(req);
            await req.SendWebRequest().ToUniTask(cancellationToken: ct);
            EnsureSuccess(req);
            if (typeof(T) == typeof(object)) return default;
            return JsonConvert.DeserializeObject<T>(req.downloadHandler.text);
        }

        private void AddAuthHeader(UnityWebRequest req)
        {
            var token = _tokenProvider?.Invoke();
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        private static void EnsureSuccess(UnityWebRequest req)
        {
            if (req.result != UnityWebRequest.Result.Success)
                throw new ApiException(req.responseCode, req.downloadHandler?.text ?? req.error);
        }
    }
}
