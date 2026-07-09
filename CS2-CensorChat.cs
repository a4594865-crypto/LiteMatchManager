using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System;

namespace LiteMatchManager;

#pragma warning disable CS8618

public class LiteMatchConfig : BasePluginConfig
{
    [JsonPropertyName("MinPlayersToStart")] public int MinPlayersToStart { get; set; } = 4;
    [JsonPropertyName("MaxPlayersPerTeam")] public int MaxPlayersPerTeam { get; set; } = 2;
    [JsonPropertyName("KickUnreadyPlayerTime")] public int KickUnreadyPlayerTime { get; set; } = 360;
    [JsonPropertyName("UnreadyReminderInterval")] public int UnreadyReminderInterval { get; set; } = 60;
    [JsonPropertyName("ChatPrefix")] public string ChatPrefix { get; set; } = "[ {Green}比賽系統{White} ]";
    [JsonPropertyName("EnableChatWeaponCommands")] public bool EnableChatWeaponCommands { get; set; } = true;
    
    [JsonPropertyName("SpawnWeapons")] 
    public List<string> SpawnWeapons { get; set; } = new List<string> { "weapon_knife", "item_assaultsuit", "weapon_deagle", "weapon_awp" };
    
    [JsonPropertyName("WarmupConfigName")] public string WarmupConfigName { get; set; } = "warmup.cfg";
    [JsonPropertyName("LiveConfigName")] public string LiveConfigName { get; set; } = "live.cfg";
    [JsonPropertyName("Duel_MapChangeDelay")] public int MapChangeDelay { get; set; } = 5;
    
    [JsonPropertyName("MapList")] 
    public List<string> MapList { get; set; } = new List<string> { "Aim_redline_vieforit:3290337428", "aimpro_vieforit:3290753343" };
}

public class LiteMatchManager : BasePlugin, IPluginConfig<LiteMatchConfig>
{
    public override string ModuleName => "LiteMatchManager";
    public override string ModuleVersion => "4.0_ZeroLatency";
    public override string ModuleAuthor => "Optimized";
    public override string ModuleDescription => "極致優化版賽事系統 (0分配/0抖動)";

    public LiteMatchConfig Config { get; set; } = new LiteMatchConfig();

    // 狀態與快取
    private string _cachedPrefix = "";
    private HashSet<ulong> _readyPlayers = new();
    private Dictionary<ulong, int> _playerUnreadyTime = new(); 
    private bool _isMatchLive = false;
    private bool _isChangingMap = false; 
    private CounterStrikeSharp.API.Modules.Timers.Timer? _globalCheckTimer;

    public void OnConfigParsed(LiteMatchConfig config)
    {
        Config = config;
        // 【優化 3】只在讀取設定檔時轉換一次顏色，避免頻繁字串操作
        _cachedPrefix = config.ChatPrefix
            .Replace("{White}", ChatColors.White.ToString())
            .Replace("{Red}", ChatColors.Red.ToString())
            .Replace("{Green}", ChatColors.Green.ToString())
            .Replace("{Lime}", ChatColors.Lime.ToString())
            .Replace("{LightBlue}", ChatColors.LightBlue.ToString())
            .Replace("{Yellow}", ChatColors.Yellow.ToString())
            .Replace("{Gold}", ChatColors.Gold.ToString())
            .Replace("{Orange}", ChatColors.Orange.ToString());
    }

    public override void Load(bool hotReload)
    {
        AddCommandListener("say", OnPlayerSay);
        AddCommandListener("say_team", OnPlayerSay);
        AddCommandListener("jointeam", OnJoinTeam);
        AddCommand("css_gs", "顯示武器選單提示", OnGsCommand);
        
        RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
        {
            if (@event.Userid != null)
            {
                ulong steamId = @event.Userid.SteamID;
                _readyPlayers.Remove(steamId);
                _playerUnreadyTime.Remove(steamId);
            }
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);

        RegisterListener<Listeners.OnMapStart>(mapName => 
        {
            ResetMatchState();
            Server.ExecuteCommand($"exec {Config.WarmupConfigName}");
        });
    }

    // ==========================================
    // 【優化 1】徹底移除 LINQ 的無垃圾隊伍判定
    // ==========================================
    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return HookResult.Continue;
        if (!int.TryParse(info.GetArg(1), out int teamIndex)) return HookResult.Continue;

        if (teamIndex == 2 || teamIndex == 3)
        {
            int currentTeamCount = 0;
            // 傳統迴圈，效能最高，不會產生 GC 垃圾
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && !p.IsBot && p.TeamNum == teamIndex && p.SteamID != player.SteamID)
                {
                    currentTeamCount++;
                }
            }
            
            if (currentTeamCount >= Config.MaxPlayersPerTeam)
            {
                string teamName = teamIndex == 2 ? "恐怖份子 (T)" : "反恐小組 (CT)";
                player.PrintToChat($" {_cachedPrefix} {ChatColors.Red}加入失敗！{teamName} 已經滿員 (最多 {Config.MaxPlayersPerTeam} 人)。");
                return HookResult.Handled; 
            }
        }
        return HookResult.Continue;
    }

    // ==========================================
    // 【優化 2】攔截器提早返回，節省字串創建
    // ==========================================
    private HookResult OnPlayerSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return HookResult.Continue;

        string rawText = info.GetArg(1).Trim('"');
        if (string.IsNullOrEmpty(rawText)) return HookResult.Continue;

        // 首字元非指令符號，直接放行，0 效能損耗
        if (rawText[0] != '!' && rawText[0] != '.') return HookResult.Continue;

        string command = rawText.ToLower();

        if (command == "!nextmap")
        {
            if (AdminManager.PlayerHasPermissions(player, "@css/root")) TriggerMapChange();
            else player.PrintToChat($" {_cachedPrefix} {ChatColors.Red}你沒有權限執行此指令。");
            return HookResult.Handled;
        }

        if (command == "!r" || command == "!ready" || command == ".r" || command == ".ready")
        {
            if (!_isMatchLive) HandlePlayerReady(player);
            return HookResult.Handled; 
        }
        else if (command == "!unready" || command == ".unready")
        {
            if (!_isMatchLive) HandlePlayerUnready(player);
            return HookResult.Handled;
        }

        if (Config.EnableChatWeaponCommands && HandleWeaponCommand(player, command)) 
        {
            return HookResult.Handled; 
        }

        return HookResult.Continue;
    }

    private void TriggerMapChange()
    {
        if (_isChangingMap || Config.MapList == null || Config.MapList.Count == 0) return;
        _isChangingMap = true;

        var random = new Random();
        string selectedMapString = Config.MapList[random.Next(Config.MapList.Count)];
        
        string[] parts = selectedMapString.Split(':');
        string mapName = parts[0];
        string workshopId = parts.Length > 1 ? parts[1] : "";

        Server.PrintToChatAll($" {ChatColors.Gold}正在切換地圖 {ChatColors.Lime}{mapName}{ChatColors.Gold}，{ChatColors.Orange}{Config.MapChangeDelay} 秒後 {ChatColors.Gold}開始下一場決鬥");

        AddTimer(Config.MapChangeDelay, () =>
        {
            if (!string.IsNullOrEmpty(workshopId)) Server.ExecuteCommand($"host_workshop_map {workshopId}");
            else Server.ExecuteCommand($"map {mapName}");
        });
    }

    private void HandlePlayerReady(CCSPlayerController player)
    {
        ulong steamId = player.SteamID;
        if (_readyPlayers.Contains(steamId))
        {
            player.PrintToChat($" {_cachedPrefix} 你已經是 {ChatColors.Green}準備完成{ChatColors.White} 的狀態了！");
            return;
        }

        _readyPlayers.Add(steamId);
        _playerUnreadyTime.Remove(steamId); 
        Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Green}{player.PlayerName}{ChatColors.White} 已準備！ 目前進度: {ChatColors.Green}{_readyPlayers.Count} / {Config.MinPlayersToStart}");
        CheckMatchStart();
    }

    private void HandlePlayerUnready(CCSPlayerController player)
    {
        ulong steamId = player.SteamID;
        if (_readyPlayers.Contains(steamId))
        {
            _readyPlayers.Remove(steamId);
            _playerUnreadyTime[steamId] = 0; 
            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Red}{player.PlayerName}{ChatColors.White} 取消了準備！ 目前進度: {ChatColors.Red}{_readyPlayers.Count} / {Config.MinPlayersToStart}");
        }
    }

    private void CheckMatchStart()
    {
        if (_isMatchLive) return;
        if (_readyPlayers.Count >= Config.MinPlayersToStart)
        {
            _isMatchLive = true;
            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Green}所有玩家已準備，比賽即將開始！");
            
            // 【優化 4】滿人開賽後，徹底銷毀檢查未準備的計時器，釋放伺服器線程
            _globalCheckTimer?.Kill();
            _globalCheckTimer = null;

            AddTimer(3.0f, () => {
                Server.ExecuteCommand($"exec {Config.LiveConfigName}");
            });
        }
    }

    // ==========================================
    // 【優化 5】嚴格的 Pawn 雙層防崩潰驗證
    // ==========================================
    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        Server.NextFrame(() => {
            // 加入 PlayerPawn.Value 驗證，防止斷線瞬間觸發 Access Violation 導致伺服器崩潰
            if (player == null || !player.IsValid || player.PlayerPawn == null || !player.PlayerPawn.IsValid || !player.PawnIsAlive) return;
            
            player.RemoveWeapons(); 
            
            foreach (var item in Config.SpawnWeapons)
            {
                player.GiveNamedItem(item);
            }
        });
        return HookResult.Continue;
    }

    private bool HandleWeaponCommand(CCSPlayerController player, string command)
    {
        if (!player.PawnIsAlive) return false;
        switch (command)
        {
            case "!dg": player.GiveNamedItem("weapon_deagle"); return true;
            case "!usp": player.GiveNamedItem("weapon_usp_silencer"); return true;
            case "!gk": player.GiveNamedItem("weapon_glock"); return true;
            case "!r8": player.GiveNamedItem("weapon_revolver"); return true;
            case "!ssg": player.GiveNamedItem("weapon_ssg08"); return true;
            case "!awp": player.GiveNamedItem("weapon_awp"); return true;
            case "!gs": OnGsCommand(player, null!); return true;
        }
        return false;
    }

    private void OnGsCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || !player.IsValid) return;
        player.PrintToChat($" {ChatColors.Orange}您 可 在 聊 天 欄 位 輸 入 您 要 的 武器，以 下 是 常 用 武器");
        player.PrintToChat($" ----------------------------------------------------------------------");
        player.PrintToChat($" [ {ChatColors.LightBlue}手槍{ChatColors.White} ]  {ChatColors.LightBlue}!dg {ChatColors.White}[ 沙鷹 ] 、{ChatColors.LightBlue}!usp {ChatColors.White}[ USP ] 、{ChatColors.LightBlue}!gk {ChatColors.White}[ 格洛克 ] 、{ChatColors.LightBlue}!r8 {ChatColors.White}[ R8 ]");
        player.PrintToChat($" [ {ChatColors.Orange}狙擊{ChatColors.White} ] {ChatColors.Orange}!ssg {ChatColors.White}[ SSG 08 鳥狙 ] 、{ChatColors.Orange}!awp {ChatColors.White}[ AWP狙擊步槍 ]");
    }

    // ==========================================
    // 防卡頓全域掃描 (已替換掉 LINQ)
    // ==========================================
    private void CheckUnreadyPlayers()
    {
        if (_isMatchLive) return; 
        
        List<string> unreadyNames = new();

        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && !p.IsBot && !p.IsHLTV && (p.TeamNum == 2 || p.TeamNum == 3))
            {
                ulong steamId = p.SteamID;
                if (!_readyPlayers.Contains(steamId))
                {
                    unreadyNames.Add(p.PlayerName);
                    if (!_playerUnreadyTime.ContainsKey(steamId)) _playerUnreadyTime[steamId] = 0;
                    
                    _playerUnreadyTime[steamId] += Config.UnreadyReminderInterval;

                    if (_playerUnreadyTime[steamId] >= Config.KickUnreadyPlayerTime) 
                    {
                        Server.NextFrame(() => {
                            // 簡化 Kick 指令，防範中文編碼可能導致的控制台錯誤
                            Server.ExecuteCommand($"kickid {p.UserId} Unready_Timeout");
                        });
                        _playerUnreadyTime.Remove(steamId);
                    }
                    else
                    {
                        int timeLeft = Config.KickUnreadyPlayerTime - _playerUnreadyTime[steamId];
                        p.PrintToChat($" {_cachedPrefix} {ChatColors.Red}[警告] {ChatColors.White}你尚未準備，請輸入 {ChatColors.Green}!R{ChatColors.White}。再過 {ChatColors.Red}{timeLeft}{ChatColors.White} 秒未準備將被踢出！");
                    }
                }
            }
        }
        
        if (unreadyNames.Count > 0) Server.PrintToChatAll($" {_cachedPrefix} 目前未準備玩家: {ChatColors.Yellow}{string.Join(", ", unreadyNames)}");
    }

    private void ResetMatchState()
    {
        _isMatchLive = false;
        _isChangingMap = false;
        _readyPlayers.Clear();
        _playerUnreadyTime.Clear();
        
        // 換地圖時重新啟動未準備偵測計時器
        _globalCheckTimer?.Kill();
        _globalCheckTimer = AddTimer(Config.UnreadyReminderInterval, CheckUnreadyPlayers, TimerFlags.REPEAT);
    }
}
