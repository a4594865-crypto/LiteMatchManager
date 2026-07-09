using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System;

namespace LiteMatchManager;

#pragma warning disable CS8618

// ==========================================
// 1. JSON 設定檔結構 (所有你未來想改的東西都在這)
// ==========================================
public class LiteMatchConfig : BasePluginConfig
{
    [JsonPropertyName("MinPlayersToStart")]
    public int MinPlayersToStart { get; set; } = 4;           // 預設 2V2 開賽人數

    [JsonPropertyName("MaxPlayersPerTeam")]
    public int MaxPlayersPerTeam { get; set; } = 2;           // 單邊隊伍人數上限

    [JsonPropertyName("KickUnreadyPlayerTime")]
    public int KickUnreadyPlayerTime { get; set; } = 360;     // 幾秒未準備要踢出 (預設 6 分鐘)

    [JsonPropertyName("UnreadyReminderInterval")]
    public int UnreadyReminderInterval { get; set; } = 60;    // 每幾秒提示一次未準備警告

    [JsonPropertyName("ChatPrefix")]
    public string ChatPrefix { get; set; } = "[ {Green}比賽系統{White} ]"; // 系統提示前綴文字

    [JsonPropertyName("EnableChatWeaponCommands")]
    public bool EnableChatWeaponCommands { get; set; } = true; // 是否允許玩家使用 !dg, !awp 拿槍

    [JsonPropertyName("SpawnWeapons")]
    public List<string> SpawnWeapons { get; set; } = new List<string> // 玩家重生預設給予的武器清單
    {
        "weapon_knife",
        "item_assaultsuit",
        "weapon_deagle",
        "weapon_awp"
    };

    [JsonPropertyName("WarmupConfigName")]
    public string WarmupConfigName { get; set; } = "warmup.cfg"; // 暖場設定檔名稱

    [JsonPropertyName("LiveConfigName")]
    public string LiveConfigName { get; set; } = "live.cfg";     // 開賽設定檔名稱

    [JsonPropertyName("Duel_MapChangeDelay")]
    public int MapChangeDelay { get; set; } = 5;              // 換圖倒數秒數

    [JsonPropertyName("MapList")]
    public List<string> MapList { get; set; } = new List<string> // 換圖清單
    {
        "Aim_redline_vieforit:3290337428",
        "aimpro_vieforit:3290753343",
        "5e_aim_map:3250592791",
        "5e_akm4_aim_duel:3250543760"
    };
}

public class LiteMatchManager : BasePlugin, IPluginConfig<LiteMatchConfig>
{
    public override string ModuleName => "LiteMatchManager";
    public override string ModuleVersion => "3.0_UltimateConfig";
    public override string ModuleAuthor => "Optimized";
    public override string ModuleDescription => "輕量化賽事系統 (全動態設定檔支援)";

    public LiteMatchConfig Config { get; set; } = new LiteMatchConfig();

    public void OnConfigParsed(LiteMatchConfig config)
    {
        Config = config;
    }

    // ==========================================
    // 狀態追蹤參數
    // ==========================================
    // 動態解析設定檔中的前綴顏色
    private string Prefix => ReplaceColorTags(Config.ChatPrefix);

    private HashSet<ulong> _readyPlayers = new();
    private Dictionary<ulong, int> _playerUnreadyTime = new(); 
    private bool _isMatchLive = false;
    private bool _isChangingMap = false; 

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
            // 讀取設定檔中定義的暖場 cfg (預設 warmup.cfg)
            Server.ExecuteCommand($"exec {Config.WarmupConfigName}");
        });

        // 使用設定檔中的廣播秒數啟動全域計時器
        AddTimer(Config.UnreadyReminderInterval, CheckUnreadyPlayers, TimerFlags.REPEAT);
    }

    // ==========================================
    // 顏色標籤轉換器
    // ==========================================
    private string ReplaceColorTags(string input)
    {
        return input
            .Replace("{White}", ChatColors.White.ToString())
            .Replace("{Red}", ChatColors.Red.ToString())
            .Replace("{Green}", ChatColors.Green.ToString())
            .Replace("{Lime}", ChatColors.Lime.ToString())
            .Replace("{LightBlue}", ChatColors.LightBlue.ToString())
            .Replace("{Yellow}", ChatColors.Yellow.ToString())
            .Replace("{Gold}", ChatColors.Gold.ToString())
            .Replace("{Orange}", ChatColors.Orange.ToString());
    }

    // ==========================================
    // 攔截加入隊伍 (人數限制)
    // ==========================================
    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return HookResult.Continue;
        if (!int.TryParse(info.GetArg(1), out int teamIndex)) return HookResult.Continue;

        if (teamIndex == 2 || teamIndex == 3)
        {
            int currentTeamCount = Utilities.GetPlayers().Count(p => 
                p != null && p.IsValid && !p.IsBot && p.TeamNum == teamIndex && p.SteamID != player.SteamID);
            
            if (currentTeamCount >= Config.MaxPlayersPerTeam)
            {
                string teamName = teamIndex == 2 ? "恐怖份子 (T)" : "反恐小組 (CT)";
                player.PrintToChat($" {Prefix} {ChatColors.Red}加入失敗！{teamName} 已經滿員 (最多 {Config.MaxPlayersPerTeam} 人)。");
                return HookResult.Handled; 
            }
        }
        return HookResult.Continue;
    }

    // ==========================================
    // 聊天指令攔截中樞
    // ==========================================
    private HookResult OnPlayerSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return HookResult.Continue;

        string text = info.GetArg(1).Trim('"').Trim().ToLower();

        if (text == "!nextmap")
        {
            if (AdminManager.PlayerHasPermissions(player, "@css/root")) TriggerMapChange();
            else player.PrintToChat($" {Prefix} {ChatColors.Red}你沒有權限執行此指令。");
            return HookResult.Handled;
        }

        if (text == "!r" || text == "!ready")
        {
            if (!_isMatchLive) HandlePlayerReady(player);
            return HookResult.Handled; 
        }
        else if (text == "!unready")
        {
            if (!_isMatchLive) HandlePlayerUnready(player);
            return HookResult.Handled;
        }

        // 只有在設定檔允許的情況下，才攔截武器指令
        if (Config.EnableChatWeaponCommands && HandleWeaponCommand(player, text)) 
        {
            return HookResult.Handled; 
        }

        return HookResult.Continue;
    }

    // ==========================================
    // 換圖邏輯
    // ==========================================
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

    // ==========================================
    // 玩家準備邏輯
    // ==========================================
    private void HandlePlayerReady(CCSPlayerController player)
    {
        ulong steamId = player.SteamID;
        if (_readyPlayers.Contains(steamId))
        {
            player.PrintToChat($" {Prefix} 你已經是 {ChatColors.Green}準備完成{ChatColors.White} 的狀態了！");
            return;
        }

        _readyPlayers.Add(steamId);
        _playerUnreadyTime.Remove(steamId); 
        Server.PrintToChatAll($" {Prefix} {ChatColors.Green}{player.PlayerName}{ChatColors.White} 已準備！ 目前進度: {ChatColors.Green}{_readyPlayers.Count} / {Config.MinPlayersToStart}");
        CheckMatchStart();
    }

    private void HandlePlayerUnready(CCSPlayerController player)
    {
        ulong steamId = player.SteamID;
        if (_readyPlayers.Contains(steamId))
        {
            _readyPlayers.Remove(steamId);
            _playerUnreadyTime[steamId] = 0; 
            Server.PrintToChatAll($" {Prefix} {ChatColors.Red}{player.PlayerName}{ChatColors.White} 取消了準備！ 目前進度: {ChatColors.Red}{_readyPlayers.Count} / {Config.MinPlayersToStart}");
        }
    }

    private void CheckMatchStart()
    {
        if (_isMatchLive) return;
        if (_readyPlayers.Count >= Config.MinPlayersToStart)
        {
            _isMatchLive = true;
            Server.PrintToChatAll($" {Prefix} {ChatColors.Green}所有玩家已準備，比賽即將開始！");
            AddTimer(3.0f, () => {
                // 讀取設定檔中定義的開賽 cfg (預設 live.cfg)
                Server.ExecuteCommand($"exec {Config.LiveConfigName}");
            });
        }
    }

    // ==========================================
    // 迴圈給予重生裝備 (完全對應 JSON 清單)
    // ==========================================
    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || !player.PawnIsAlive) return HookResult.Continue;

        Server.NextFrame(() => {
            if (player == null || !player.IsValid || !player.PawnIsAlive) return;
            
            player.RemoveWeapons(); // 沒收預設裝備
            
            // 根據 JSON 中的清單，迴圈給予裝備
            foreach (var item in Config.SpawnWeapons)
            {
                player.GiveNamedItem(item);
            }
        });
        return HookResult.Continue;
    }

    // ==========================================
    // 聊天給槍邏輯
    // ==========================================
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
    // 防卡頓全域掃描與踢除
    // ==========================================
    private void CheckUnreadyPlayers()
    {
        if (_isMatchLive) return; 
        var players = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV && (p.TeamNum == 2 || p.TeamNum == 3)).ToList();
        List<string> unreadyNames = new();

        foreach (var p in players)
        {
            ulong steamId = p.SteamID;
            if (!_readyPlayers.Contains(steamId))
            {
                unreadyNames.Add(p.PlayerName);
                if (!_playerUnreadyTime.ContainsKey(steamId)) _playerUnreadyTime[steamId] = 0;
                _playerUnreadyTime[steamId] += Config.UnreadyReminderInterval; // 動態抓取設定檔的廣播秒數

                if (_playerUnreadyTime[steamId] >= Config.KickUnreadyPlayerTime) // 動態抓取設定檔的踢出秒數
                {
                    Server.NextFrame(() => {
                        Server.ExecuteCommand($"kickid {p.UserId} 你超過 {Config.KickUnreadyPlayerTime / 60} 分鐘未準備，已被系統自動踢出");
                    });
                    _playerUnreadyTime.Remove(steamId);
                }
                else
                {
                    int timeLeft = Config.KickUnreadyPlayerTime - _playerUnreadyTime[steamId];
                    p.PrintToChat($" {Prefix} {ChatColors.Red}[警告] {ChatColors.White}你尚未準備，請輸入 {ChatColors.Green}!R{ChatColors.White}。再過 {ChatColors.Red}{timeLeft}{ChatColors.White} 秒未準備將被踢出！");
                }
            }
        }
        if (unreadyNames.Count > 0) Server.PrintToChatAll($" {Prefix} 目前未準備玩家: {ChatColors.Yellow}{string.Join(", ", unreadyNames)}");
    }

    private void ResetMatchState()
    {
        _isMatchLive = false;
        _isChangingMap = false;
        _readyPlayers.Clear();
        _playerUnreadyTime.Clear();
    }
}
