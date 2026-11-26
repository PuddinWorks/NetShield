using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NetShield
{
    [DisallowMultipleComponent]
    [AddComponentMenu("NetShield/NetShield")]

    public sealed class NetShield : MonoBehaviour
    {
        public NetShieldSettings settings = new NetShieldSettings();
        public List<GameObject> enableObjects = new List<GameObject>();
        public List<GameObject> disableObjects = new List<GameObject>();
        public List<GameObject> toggleObjects = new List<GameObject>();
        public NetShieldResultEvent onChecked = new NetShieldResultEvent();
        public NetShieldResultEvent onDetected = new NetShieldResultEvent();
        public NetShieldResultEvent onAction = new NetShieldResultEvent();
        public NetShieldResultEvent onCleared = new NetShieldResultEvent();
        public NetShieldResultEvent onFailed = new NetShieldResultEvent();
        public NetShieldResult LastResult { get; private set; }
        public bool IsChecking { get; private set; }
        public event Action<NetShieldResult> Checked;
        public event Action<NetShieldResult> Detected;
        readonly List<string> history = new List<string>();
        public string[] History => history.ToArray();
        public void ClearHistory() { history.Clear(); }
        static NetShield persistentManager;
        float nextCheck, lastAction = float.NegativeInfinity;
        bool wasDetected, pending, triggered;
        int actionGeneration;
        string lastAddresses;
        bool Ready => settings.enabled;

        void Awake()
        {
            if (!Ready || !settings.persistent) return;
            if (persistentManager != null && persistentManager != this) { enabled = false; return; }
            persistentManager = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }
        void OnDestroy() { if (persistentManager == this) persistentManager = null; }
        void OnDisable() { actionGeneration++; pending = false; }
        async void Start()
        {
            nextCheck = Time.unscaledTime + Mathf.Max(5, settings.checkInterval);
            if (Ready && settings.publicLookup && !settings.offline && settings.checkOnStart) await CheckConnectionAsync();
        }
        void Update()
        {
            if (!Ready || !settings.continuous || !settings.publicLookup || settings.offline || IsChecking || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + Mathf.Max(5, settings.checkInterval);
            CheckConnection();
        }
        public async void CheckConnection() { await CheckConnectionAsync(); }
        public async Task<NetShieldResult> CheckConnectionAsync()
        {
            if (!settings.enabled || !settings.publicLookup || settings.offline) return new NetShieldResult { status = DetectionStatus.Disabled };
            if (IsChecking) return LastResult ?? new NetShieldResult { status = DetectionStatus.Incomplete };
            IsChecking = true;
            try
            {
                var db = NetShieldDatabase.Shared; db.Load();
                Task refresh = settings.autoUpdate ? db.RefreshAsync(settings) : Task.CompletedTask;
                var tasks = new List<Task<string>>();
                if (settings.ipv4) tasks.Add(PublicAddress("https://api.ipify.org", System.Net.Sockets.AddressFamily.InterNetwork));
                if (settings.ipv6) tasks.Add(PublicAddress("https://api6.ipify.org", System.Net.Sockets.AddressFamily.InterNetworkV6));
                var addresses = await Task.WhenAll(tasks); await refresh;
                if (this == null || !isActiveAndEnabled || !Ready) return new NetShieldResult { status = DetectionStatus.Disabled };
                var valid = addresses.Where(x => x != null).Distinct().OrderBy(x => x).ToArray();
                var key = string.Join(", ", valid);
                var results = valid.Select(x => db.Check(x, settings)).ToArray();
                var detected = results.Where(x => x.Detected).ToArray();
                NetShieldResult result;
                if (detected.Length > 0)
                    result = new NetShieldResult { address = string.Join(", ", detected.Select(x => x.address)), status = DetectionStatus.Detected, complete = detected.All(x => x.complete), matches = detected.SelectMany(x => x.matches).Distinct().ToArray(), activeMatches = detected.SelectMany(x => x.activeMatches).Distinct().ToArray() };
                else if (results.Length == 0) result = new NetShieldResult { status = tasks.Count == 0 ? DetectionStatus.Disabled : DetectionStatus.Incomplete };
                else result = results.FirstOrDefault(x => x.status == DetectionStatus.Incomplete) ?? results.FirstOrDefault(x => x.status == DetectionStatus.NoMatch) ?? results[0];
                bool same = key.Length > 0 && key == lastAddresses && LastResult != null && LastResult.status == result.status && LastResult.complete == result.complete && LastResult.activeMatches.SequenceEqual(result.activeMatches);
                lastAddresses = key;
                return Publish(result, !(settings.onlyOnChange && same));
            }
            catch (Exception)
            {
                if (this == null || !Ready) return new NetShieldResult { status = DetectionStatus.Disabled };
                return Publish(new NetShieldResult { status = DetectionStatus.Incomplete }, true);
            }
            finally { if (this != null) IsChecking = false; }
        }
        async Task<string> PublicAddress(string url, System.Net.Sockets.AddressFamily family)
        {
            try { string text = await NetShieldDatabase.GetTextAsync(url, settings.timeoutSeconds); return NetShieldDatabase.TryAddress(text, out IPAddress address) && address.AddressFamily == family ? address.ToString() : null; }
            catch { return null; }
        }
        public NetShieldResult InspectAddress(string address) => NetShieldDatabase.Shared.Check(address, settings);
        public async Task<NetShieldResult> InspectAddressAsync(string address)
        {
            if (settings.enabled && settings.autoUpdate && !settings.offline) await NetShieldDatabase.Shared.RefreshAsync(settings);
            return InspectAddress(address);
        }
        public NetShieldResult CheckAddressAndApply(string address)
        {
            var result = InspectAddress(address);
            return Application.isPlaying && Ready ? Publish(result, true) : result;
        }
        NetShieldResult Publish(NetShieldResult result, bool respond)
        {
            if (!Ready) return result;
            LastResult = result;
            if (settings.history)
            {
                history.Add(DateTime.Now.ToString("HH:mm:ss") + " - " + result.status + (result.activeMatches.Length > 0 ? " - " + string.Join(", ", result.activeMatches) : ""));
                while (history.Count > Mathf.Clamp(settings.historyLimit, 1, 500)) history.RemoveAt(0);
            }
            onChecked.Invoke(result); Checked?.Invoke(result);
            bool failed = result.status == DetectionStatus.Incomplete || (result.Detected && settings.requireFresh && !result.complete);
            if (failed)
            {
                if (pending && settings.requireFresh) { actionGeneration++; pending = false; }
                onFailed.Invoke(result);
                if (respond && settings.failClosed) Schedule(result, settings.failureActions.ToArray());
                return result;
            }
            if (result.Detected)
            {
                wasDetected = true;
                onDetected.Invoke(result); Detected?.Invoke(result);
                if (respond) ApplyAction(result);
            }
            else if (result.status == DetectionStatus.NoMatch || result.status == DetectionStatus.Whitelisted)
            {
                if (wasDetected) onCleared.Invoke(result);
                wasDetected = false; triggered = false; actionGeneration++; pending = false;
            }
            return result;
        }
        public void ApplyAction(NetShieldResult result)
        {
            if (!Application.isPlaying || !Ready || result == null || !result.Detected || (settings.requireFresh && !result.complete)) return;
            var rules = settings.actionRules.Where(r => r != null && result.activeMatches.Any(m => string.Equals(m, r.match, StringComparison.OrdinalIgnoreCase) || (r.match == "VPN" && (NetShieldDatabase.IsProvider(m) || m == NetShieldDatabase.GenericVPN)))).ToArray();
            var actions = new List<DetectionAction>();
            foreach (string match in result.activeMatches)
            {
                var specific = rules.LastOrDefault(r => string.Equals(r.match, match, StringComparison.OrdinalIgnoreCase));
                var group = (NetShieldDatabase.IsProvider(match) || match == NetShieldDatabase.GenericVPN) ? rules.LastOrDefault(r => r.match == "VPN") : null;
                var chosen = specific ?? group;
                if (chosen != null) actions.AddRange(chosen.actions);
                else { actions.Add(settings.action); actions.AddRange(settings.additionalActions); }
            }
            if (result.activeMatches.Length == 0) { actions.Add(settings.action); actions.AddRange(settings.additionalActions); }
            Schedule(result, actions.Distinct().ToArray());
        }
        async void Schedule(NetShieldResult result, DetectionAction[] actions)
        {
            if (!Application.isPlaying || !Ready || pending || (settings.once && triggered) || Time.unscaledTime - lastAction < Mathf.Max(0, settings.cooldown)) return;
            if (actions.All(x => x == DetectionAction.DoNothing)) return;
            pending = true;
            int generation = ++actionGeneration;
            try
            {
                if (settings.actionDelay > 0) await Task.Delay(TimeSpan.FromSeconds(Mathf.Clamp(settings.actionDelay, 0, 600)));
                if (this == null || !isActiveAndEnabled || !Ready || !Application.isPlaying || generation != actionGeneration) return;
                triggered = true; lastAction = Time.unscaledTime;
                foreach (var action in actions.OrderBy(x => x == DetectionAction.QuitGame ? 2 : x == DetectionAction.LoadScene ? 1 : 0))
                {
                    if (this == null) break;
                    Execute(action, result);
                }
            }
            catch (Exception e) { if (this != null) Debug.LogError("[NetShield] Action failed: " + e.GetType().Name, this); }
            finally { if (this != null && generation == actionGeneration) pending = false; }
        }
        void Execute(DetectionAction action, NetShieldResult result)
        {
            switch (action)
            {
                case DetectionAction.JustLog: Debug.Log("[NetShield] " + result.status + " - " + string.Join(", ", result.activeMatches), this); break;
                case DetectionAction.Objects:
                    var targets = new Dictionary<GameObject, bool>();
                    foreach (var obj in enableObjects) if (obj != null) targets[obj] = true;
                    foreach (var obj in disableObjects) if (obj != null) targets[obj] = false;
                    foreach (var obj in toggleObjects) if (obj != null) targets[obj] = !obj.activeSelf;
                    foreach (var pair in targets) if (pair.Key != null) pair.Key.SetActive(pair.Value);
                    break;
                case DetectionAction.UnityEvent: onAction.Invoke(result); break;
                case DetectionAction.LoadScene:
                    if (!string.IsNullOrWhiteSpace(settings.sceneName) && Application.CanStreamedLevelBeLoaded(settings.sceneName)) SceneManager.LoadScene(settings.sceneName);
                    else Debug.LogError("[NetShield] Configure a scene included in the build.", this);
                    break;
                case DetectionAction.QuitGame:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#elif !UNITY_WEBGL
                    Application.Quit();
#endif
                    break;
            }
        }
        public void SimulateDetection()
        {
            if (Application.isPlaying && Ready) Publish(new NetShieldResult { status = DetectionStatus.Detected, complete = true, activeMatches = new[] { "Simulation" }, matches = new[] { "Simulation" } }, true);
        }
    }
}
