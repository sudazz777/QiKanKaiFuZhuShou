using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// Uses the installed mod's Steamworks wrapper without shipping another copy of its DLLs.
// All calls, including Tick and Dispose, belong on the launcher's GUI thread.
public sealed class SteamJoinBridge : IDisposable
{
    private readonly string moduleBin;
    private bool initialized;
    private bool disposed;
    private bool wasOffline;
    private bool metadataValid;
    private bool waitingForData;
    private string candidate = string.Empty;
    private string ownedConnect = string.Empty;
    private uint expiresAt;
    private DateTime nextInit = DateTime.MinValue;
    private DateTime nextRequest = DateTime.MinValue;
    private DateTime readAfter = DateTime.MinValue;
    private DateTime nextPublish = DateTime.MinValue;
    private object lobby;

    private Type steamIdType;
    private FieldInfo steamIdValue;
    private FieldInfo appIdValue;
    private MethodInfo apiInit, apiShutdown, apiCallbacks;
    private MethodInfo userLoggedOn, userId;
    private MethodInfo requestLobbyData, getLobbyData, getLobbyOwner;
    private MethodInfo setPresence, getPresence, getServerTime, getAppId;

    public string StatusText { get; private set; }
    public bool EntryReady { get; private set; }
    public string LobbyId { get; private set; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string directory);

    public SteamJoinBridge(string moduleBin)
    {
        this.moduleBin = moduleBin;
        StatusText = "Steam 好友加入入口：等待服务器大厅";
        LobbyId = string.Empty;
    }

    // The caller supplies the latest lobby from this running server's log, never a client lobby.
    // Requests and callbacks are asynchronous; this method has no waits or polling loops.
    public void Tick(string candidateLobbyId)
    {
        if (disposed) return;
        DateTime now = DateTime.UtcNow;
        string supplied = (candidateLobbyId ?? string.Empty).Trim();
        ulong parsed;
        if (supplied.Length == 0)
        {
            ClearOwnedEntry();
            ResetCandidate(string.Empty);
            StatusText = "Steam 好友加入入口：等待服务器大厅";
            PumpIfInitialized(now);
            return;
        }
        if (!ulong.TryParse(supplied, out parsed) || parsed == 0)
        {
            ClearOwnedEntry();
            ResetCandidate(string.Empty);
            StatusText = "Steam 好友加入入口：大厅编号无效";
            PumpIfInitialized(now);
            return;
        }

        string normalized = parsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(candidate, normalized, StringComparison.Ordinal))
        {
            ClearOwnedEntry();
            ResetCandidate(normalized);
        }

        try
        {
            if (!initialized)
            {
                if (now < nextInit) return;
                if (!Initialize(now)) return;
            }

            apiCallbacks.Invoke(null, null);
            if (!(bool)userLoggedOn.Invoke(null, null))
            {
                wasOffline = true;
                metadataValid = false;
                EntryReady = false;
                LobbyId = string.Empty;
                StatusText = "Steam 好友加入入口：Steam 离线，等待重新连接";
                return;
            }
            if (wasOffline)
            {
                wasOffline = false;
                nextRequest = DateTime.MinValue;
                nextPublish = DateTime.MinValue;
            }

            if (lobby == null) lobby = Activator.CreateInstance(steamIdType, new object[] { parsed });
            if (now >= nextRequest)
            {
                nextRequest = now.AddSeconds(10);
                waitingForData = (bool)requestLobbyData.Invoke(null, new object[] { lobby });
                readAfter = now.AddMilliseconds(500);
                if (!waitingForData && !metadataValid)
                    StatusText = "Steam 好友加入入口：大厅资料请求未完成，将自动重试";
                else if (!metadataValid)
                    StatusText = "Steam 好友加入入口：正在核对正式服务器大厅";
            }

            if (waitingForData && now >= readAfter)
            {
                // Steam may still be filling its cache. Empty data retries without a blocking wait.
                readAfter = now.AddMilliseconds(500);
                ValidateLobby();
            }
            if (!metadataValid) return;

            uint steamTime = Convert.ToUInt32(getServerTime.Invoke(null, null));
            if (steamTime == 0 || expiresAt <= steamTime)
            {
                metadataValid = false;
                EntryReady = false;
                LobbyId = string.Empty;
                ClearOwnedEntry();
                StatusText = steamTime == 0
                    ? "Steam 好友加入入口：等待 Steam 时间与大厅有效期确认"
                    : "Steam 好友加入入口：大厅已过期，等待服务器刷新";
                return;
            }

            if (now < nextPublish) return;
            nextPublish = now.AddSeconds(5);
            string target = "+connect_lobby " + candidate;
            bool accepted = (bool)setPresence.Invoke(null, new object[] { "connect", target });
            if (accepted) ownedConnect = target;
            object me = userId.Invoke(null, null);
            string actual = Convert.ToString(getPresence.Invoke(null, new object[] { me, "connect" }));
            EntryReady = accepted && string.Equals(actual, target, StringComparison.Ordinal);
            StatusText = EntryReady
                ? "Steam 好友加入入口：已设置，好友实际加入仍待验证"
                : "Steam 好友加入入口：设置未确认，将自动重试";
        }
        catch (Exception error)
        {
            RecoverFromError(error, now);
        }
    }

    private bool Initialize(DateTime now)
    {
        nextInit = now.AddSeconds(10);
        if (apiInit == null) LoadApi();
        // These variables affect this process and its children, not the user's global settings.
        Environment.SetEnvironmentVariable("SteamAppId", "261550", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("SteamGameId", "261550", EnvironmentVariableTarget.Process);
        if (!(bool)apiInit.Invoke(null, null))
        {
            StatusText = "Steam 好友加入入口：初始化未完成，请保持 Steam 登录在线";
            return false;
        }
        initialized = true;
        object app = getAppId.Invoke(null, null);
        if (Convert.ToUInt32(appIdValue.GetValue(app)) != 261550)
            throw new InvalidOperationException("当前 Steam 应用不是骑马与砍杀 2");
        nextRequest = DateTime.MinValue;
        nextPublish = DateTime.MinValue;
        StatusText = "Steam 好友加入入口：正在读取服务器大厅";
        return true;
    }

    private void LoadApi()
    {
        string native = Path.Combine(moduleBin, "steam_api64.dll");
        string managed = Path.Combine(moduleBin, "Steamworks.NET.dll");
        if (!File.Exists(native) || !File.Exists(managed))
            throw new FileNotFoundException("找不到模组自带的 Steam 接口文件");
        if (!SetDllDirectory(moduleBin))
            throw new InvalidOperationException("无法载入模组 Steam 接口目录");
        Assembly assembly = Assembly.LoadFrom(managed);
        Type api = RequireType(assembly, "Steamworks.SteamAPI");
        Type user = RequireType(assembly, "Steamworks.SteamUser");
        Type friends = RequireType(assembly, "Steamworks.SteamFriends");
        Type matching = RequireType(assembly, "Steamworks.SteamMatchmaking");
        Type utils = RequireType(assembly, "Steamworks.SteamUtils");
        steamIdType = RequireType(assembly, "Steamworks.CSteamID");
        steamIdValue = RequireField(steamIdType, "m_SteamID");
        appIdValue = RequireField(RequireType(assembly, "Steamworks.AppId_t"), "m_AppId");
        apiShutdown = RequireMethod(api, "Shutdown", Type.EmptyTypes);
        apiCallbacks = RequireMethod(api, "RunCallbacks", Type.EmptyTypes);
        userLoggedOn = RequireMethod(user, "BLoggedOn", Type.EmptyTypes);
        userId = RequireMethod(user, "GetSteamID", Type.EmptyTypes);
        requestLobbyData = RequireMethod(matching, "RequestLobbyData", new Type[] { steamIdType });
        getLobbyData = RequireMethod(matching, "GetLobbyData", new Type[] { steamIdType, typeof(string) });
        getLobbyOwner = RequireMethod(matching, "GetLobbyOwner", new Type[] { steamIdType });
        setPresence = RequireMethod(friends, "SetRichPresence", new Type[] { typeof(string), typeof(string) });
        getPresence = RequireMethod(friends, "GetFriendRichPresence", new Type[] { steamIdType, typeof(string) });
        getServerTime = RequireMethod(utils, "GetServerRealTime", Type.EmptyTypes);
        getAppId = RequireMethod(utils, "GetAppID", Type.EmptyTypes);
        // Assign Init last so a partially failed reflection load is retried as a whole.
        apiInit = RequireMethod(api, "Init", Type.EmptyTypes);
    }

    private void ValidateLobby()
    {
        string kind = LobbyData("coop_lobby_type");
        string server = LobbyData("coop_server_steamid");
        string visibility = LobbyData("coop_visibility");
        string expiry = LobbyData("coop_advertisement_expires_at");
        object me = userId.Invoke(null, null);
        ulong myId = Convert.ToUInt64(steamIdValue.GetValue(me));
        object owner = getLobbyOwner.Invoke(null, new object[] { lobby });
        ulong ownerId = Convert.ToUInt64(steamIdValue.GetValue(owner));
        if (kind.Length == 0 || server.Length == 0 || visibility.Length == 0 ||
            expiry.Length == 0 || myId == 0 || ownerId == 0)
        {
            EntryReady = false;
            metadataValid = false;
            LobbyId = string.Empty;
            ClearOwnedEntry();
            StatusText = "Steam 好友加入入口：等待 Steam 返回完整大厅资料";
            return;
        }
        ulong serverId;
        uint expiryValue;
        string problem = null;
        if (ownerId != myId) problem = "当前大厅不属于登录账号，未设置加入入口";
        else if (!string.Equals(kind, "standalone", StringComparison.Ordinal))
            problem = "等待正式专用服务器大厅，临时房间不适用";
        else if (!ulong.TryParse(server, out serverId) || serverId == 0)
            problem = "大厅尚未提供有效的 Steam 服务器身份";
        else if (!string.Equals(visibility, "public", StringComparison.Ordinal))
            problem = "大厅尚未公开，请应用公开设置并重启服务器";
        else if (!uint.TryParse(expiry, out expiryValue))
            problem = "大厅有效期资料无效，等待服务器刷新";
        if (problem != null)
        {
            metadataValid = false;
            EntryReady = false;
            LobbyId = string.Empty;
            ClearOwnedEntry();
            StatusText = "Steam 好友加入入口：" + problem;
            return;
        }
        expiresAt = uint.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture);
        metadataValid = true;
        waitingForData = false;
        LobbyId = candidate;
    }

    private string LobbyData(string key)
    {
        return Convert.ToString(getLobbyData.Invoke(null, new object[] { lobby, key })) ?? string.Empty;
    }

    private void ResetCandidate(string value)
    {
        candidate = value;
        lobby = null;
        metadataValid = false;
        waitingForData = false;
        EntryReady = false;
        LobbyId = string.Empty;
        nextRequest = DateTime.MinValue;
        nextPublish = DateTime.MinValue;
    }

    private void ClearOwnedEntry()
    {
        if (ownedConnect.Length == 0) return;
        try
        {
            if (initialized)
            {
                object me = userId.Invoke(null, null);
                string current = Convert.ToString(getPresence.Invoke(null, new object[] { me, "connect" }));
                if (string.Equals(current, ownedConnect, StringComparison.Ordinal))
                    setPresence.Invoke(null, new object[] { "connect", string.Empty });
            }
        }
        catch { }
        finally { ownedConnect = string.Empty; }
    }

    private void PumpIfInitialized(DateTime now)
    {
        try { if (initialized) apiCallbacks.Invoke(null, null); }
        catch (Exception error) { RecoverFromError(error, now); }
    }

    private void RecoverFromError(Exception error, DateTime now)
    {
        EntryReady = false;
        metadataValid = false;
        LobbyId = string.Empty;
        ClearOwnedEntry();
        Shutdown();
        nextInit = now.AddSeconds(10);
        Exception actual = error;
        while (actual.InnerException != null) actual = actual.InnerException;
        string message = (actual.Message ?? "接口暂时不可用").Replace('\r', ' ').Replace('\n', ' ');
        if (message.Length > 100) message = message.Substring(0, 100);
        StatusText = "Steam 好友加入入口：暂时不可用，将自动重试（" + message + "）";
    }

    private void Shutdown()
    {
        if (!initialized) return;
        try { apiShutdown.Invoke(null, null); }
        catch { }
        finally { initialized = false; }
    }

    private static Type RequireType(Assembly assembly, string name)
    {
        Type found = assembly.GetType(name, false);
        if (found == null) throw new MissingMemberException("当前模组缺少 Steam 接口：" + name);
        return found;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo found = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (found == null) throw new MissingMemberException("当前模组 Steam 字段不匹配：" + name);
        return found;
    }

    private static MethodInfo RequireMethod(Type type, string name, Type[] parameters)
    {
        MethodInfo found = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static,
            null, parameters, null);
        if (found == null) throw new MissingMethodException("当前模组 Steam 方法不匹配：" + name);
        return found;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ClearOwnedEntry();
        EntryReady = false;
        LobbyId = string.Empty;
        Shutdown();
        StatusText = "Steam 好友加入入口：助手已关闭";
    }
}
