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
// 1. 定義 JSON 設定檔結構 (自動生成)
// ==========================================
public class LiteMatchConfig : BasePluginConfig
{
    [JsonPropertyName("Duel_MapChangeDelay")]
    public int MapChangeDelay { get; set; } = 5;

    [JsonPropertyName("MapList")]
    public List<string> MapList { get; set; } = new List<string>
    {
        "Aim_redline_vieforit:3290337428",
        "aimpro_vieforit:3290753343",
        "5e_aim_map:3250592791",
        "5e_akm4_aim_duel:3250543760"
    };
}

// 繼承 IPluginConfig 來啟用設定檔功能
public class LiteMatchManager : BasePlugin, IPluginConfig<LiteMatchConfig>
{
    public override string ModuleName => "LiteMatchManager";
    public override string ModuleVersion => "2.2_Optimized";
    public override string ModuleAuthor => "Optimized";
    public override string ModuleDescription => "輕量化準備系統與自動換圖設定檔";

    public LiteMatchConfig Config { get; set; } = new LiteMatchConfig();

    // 當外掛讀取 JSON 設定檔時觸發
    public void OnConfigParsed(LiteMatchConfig config)
    {
        Config = config;
    }

    // ==========================================
    // 參數設定區塊
    // ==========================================
    private const int MinPlayersToStart = 10;       
    private const int KickTimeSeconds = 360;        
    private const int ReminderInterval = 60;        
    private string Prefix = $" [{ChatColors.Green}比賽系統{ChatColors.White}]";

    private HashSet<ulong> _readyPlayers = new();
    private Dictionary<ulong, int> _playerUnreadyTime = new(); 
    private bool _isMatchLive = false;
    private bool _isChangingMap = false; // 防止重複觸發換圖
    private CounterStrikeSharp.API.Modules.Timers.Timer? _globalCheckTimer;

    public override void Load(bool hotReload)
    {
        AddCommandListener("say", OnPlayerSay);
        AddCommandListener("say_team", OnPlayerSay);
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
            Server.ExecuteCommand("exec warmup.cfg");
        });

        _globalCheckTimer = AddTimer(ReminderInterval, CheckUnreadyPlayers, TimerFlags.REPEAT);
    }

    // ==========================================
    // 聊天指令攔截中樞
    // ==========================================
    private HookResult OnPlayerSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return HookResult.Continue;

        string text = info.GetArg(1).Trim('"').Trim().ToLower();

        // [新增] 管理員強制換圖指令 !nextmap
        if (text == "!nextmap")
        {
            if (AdminManager.PlayerHasPermissions(player, "@css/root"))
            {
                TriggerMapChange();
            }
            else
            {
                player.PrintToChat($" {Prefix} {ChatColors.Red}你沒有權限執行此指令。");
            }
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

        if (HandleWeaponCommand(player, text)) return HookResult.Handled; 

        return HookResult.Continue;
    }

    // ==========================================
    // [核心新增] 解析工作坊並執行換圖邏輯
    // ==========================================
    private void TriggerMapChange()
    {
        if (_isChangingMap || Config.MapList == null || Config.MapList.Count == 0) return;

        _isChangingMap = true;

        // 隨機挑選清單中的一張圖
        var random = new Random();
        string selectedMapString = Config.MapList[random.Next(Config.MapList.Count)];
        
        // 解析寫法："地圖名稱:工作坊ID"
        string[] parts = selectedMapString.Split(':');
        string mapName = parts[0];
        string workshopId = parts.Length > 1 ? parts[1] : "";

        // 完美還原你指定的顏色廣播格式
        Server.PrintToChatAll($" {ChatColors.Gold}正在切換地圖 {ChatColors.Lime}{mapName}{ChatColors.Gold}，{ChatColors.Orange}{Config.MapChangeDelay} 秒後 {ChatColors.Gold}開始下一場決鬥");

        // 使用 AddTimer 倒數，完全不吃效能
        AddTimer(Config.MapChangeDelay, () =>
        {
            // 如果有工作坊 ID，使用專屬讀圖指令，否則使用一般 map 指令
            if (!string.IsNullOrEmpty(workshopId))
            {
                Server.ExecuteCommand($"host_workshop_map {workshopId}");
            }
            else
            {
                Server.ExecuteCommand($"map {mapName}");
            }
        });
    }

    // ==========================================
    // 玩家準備邏輯 (與上方相同)
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
        Server.PrintToChatAll($" {Prefix} {ChatColors.Green}{player.PlayerName}{ChatColors.White} 已準備！ 目前進度: {ChatColors.Green}{_readyPlayers.Count} / {MinPlayersToStart}");
        CheckMatchStart();
    }

    private void HandlePlayerUnready(CCSPlayerController player)
    {
        ulong steamId = player.SteamID;
        if (_readyPlayers.Contains(steamId))
        {
            _readyPlayers.Remove(steamId);
            _playerUnreadyTime[steamId] = 0; 
            Server.PrintToChatAll($" {Prefix} {ChatColors.Red}{player.PlayerName}{ChatColors.White} 取消了準備！ 目前進度: {ChatColors.Red}{_readyPlayers.Count} / {MinPlayersToStart}");
        }
    }

    private void CheckMatchStart()
    {
        if (_isMatchLive) return;
        if (_readyPlayers.Count >= MinPlayersToStart)
        {
            _isMatchLive = true;
            Server.PrintToChatAll($" {Prefix} {ChatColors.Green}所有玩家已準備，比賽即將開始！");
            AddTimer(3.0f, () => {
                Server.ExecuteCommand("exec live.cfg");
            });
        }
    }

    // ==========================================
    // 重生配裝系統
    // ==========================================
    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || !player.PawnIsAlive) return HookResult.Continue;

        Server.NextFrame(() => {
            if (player == null || !player.IsValid || !player.PawnIsAlive) return;
            player.RemoveWeapons(); 
            player.GiveNamedItem("weapon_knife");        
            player.GiveNamedItem("item_assaultsuit");    
            player.GiveNamedItem("weapon_deagle");       
            player.GiveNamedItem("weapon_awp");          
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
                _playerUnreadyTime[steamId] += ReminderInterval;

                if (_playerUnreadyTime[steamId] >= KickTimeSeconds) 
                {
                    Server.NextFrame(() => {
                        Server.ExecuteCommand($"kickid {p.UserId} 你超過 6 分鐘未準備，已被系統自動踢出");
                    });
                    _playerUnreadyTime.Remove(steamId);
                }
                else
                {
                    int timeLeft = KickTimeSeconds - _playerUnreadyTime[steamId];
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
