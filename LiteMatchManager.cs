using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Cvars;
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
    [JsonPropertyName("PublicUnreadyReminderInterval")] public int PublicUnreadyReminderInterval { get; set; } = 15;

    [JsonPropertyName("ChatPrefix")] public string ChatPrefix { get; set; } = "[ {Green}2 v 2 對 戰 模 式{White} ]";
    [JsonPropertyName("EnableChatWeaponCommands")] public bool EnableChatWeaponCommands { get; set; } = true;
    
    [JsonPropertyName("SpawnWeapons")] 
    public List<string> SpawnWeapons { get; set; } = ["weapon_knife", "item_assaultsuit", "weapon_deagle", "weapon_awp"];
    
    [JsonPropertyName("WarmupConfigName")] public string WarmupConfigName { get; set; } = "warmup.cfg";
    [JsonPropertyName("LiveConfigName")] public string LiveConfigName { get; set; } = "live.cfg";
    [JsonPropertyName("Duel_MapChangeDelay")] public int MapChangeDelay { get; set; } = 5;
    
    [JsonPropertyName("MapList")] 
    public List<string> MapList { get; set; } = ["Aim_redline_vieforit:3290337428", "aimpro_vieforit:3290753343"];
}

public class LiteMatchManager : BasePlugin, IPluginConfig<LiteMatchConfig>
{
    public override string ModuleName => "LiteMatchManager";
    public override string ModuleVersion => "7.0_Dynamic_Matchmaking";
    public override string ModuleAuthor => "Optimized";
    public override string ModuleDescription => "全自動人數偵測 + 完美換槍 + 解除中途加入限制";

    public LiteMatchConfig Config { get; set; } = new LiteMatchConfig();

    private string _cachedPrefix = "";
    private HashSet<ulong> _readyPlayers = new(64);
    private Dictionary<ulong, int> _playerUnreadyTime = new(64); 
    private List<string> _unreadyNamesCache = new(64); 
    
    // 【新增】用來記憶每個玩家偏好的主副武器
    private Dictionary<ulong, string> _playerPrimary = new(64);
    private Dictionary<ulong, string> _playerSecondary = new(64);
    
    private bool _isMatchLive = false;
    private bool _isChangingMap = false; 
    
    private CounterStrikeSharp.API.Modules.Timers.Timer? _privateCheckTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _publicBroadcastTimer;

    // 手槍清單，用於極速換槍判定
    private static readonly HashSet<string> PistolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "weapon_deagle", "weapon_usp_silencer", "weapon_glock", "weapon_revolver",
        "weapon_p250", "weapon_cz75a", "weapon_tec9", "weapon_fiveseven", 
        "weapon_elite", "weapon_hkp2000"
    };

    public void OnConfigParsed(LiteMatchConfig config)
    {
        Config = config;
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
        Console.WriteLine("=================================================");
        Console.WriteLine("    LiteMatchManager v7.0 (動態開賽版) 初始化！   ");
        Console.WriteLine("=================================================");

        AddCommandListener("say", OnPlayerSay);
        AddCommandListener("say_team", OnPlayerSay);
        AddCommandListener("jointeam", OnJoinTeam);
        AddCommand("css_gs", "顯示武器選單提示", OnGsCommand);
        
        // 沒收丟槍
        AddCommandListener("drop", (player, info) => {
            return HookResult.Handled;
        });
        
        RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
        {
            var player = @event.Userid;
            if (player != null)
            {
                try 
                {
                    ulong steamId = player.SteamID;
                    if (steamId > 0)
                    {
                        _readyPlayers.Remove(steamId);
                        _playerUnreadyTime.Remove(steamId);
                        
                        // 【新增】玩家離開時，把他的武器記憶刪除
                        _playerPrimary.Remove(steamId);
                        _playerSecondary.Remove(steamId);

                        if (_isMatchLive) CheckAndResetGameImmediate();
                    }
                } 
                catch (Exception) { }
            }
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerTeam>((@event, info) =>
        {
            var player = @event.Userid;
            if (player != null && player.IsValid && player.Handle != IntPtr.Zero)
            {
                ulong steamId = player.SteamID;
                int newTeam = @event.Team; 
                
                if (!_isMatchLive)
                {
                    if (newTeam == 0 || newTeam == 1)
                    {
                        if (_readyPlayers.Contains(steamId))
                        {
                            _readyPlayers.Remove(steamId);
                            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Orange}{player.PlayerName}{ChatColors.White} 跳 去 觀 戰，已 取 消 他 的 準 備");
                        }
                        _playerUnreadyTime.Remove(steamId); 
                    }
                }
                else
                {
                    CheckAndResetGameImmediate();
                }
            }
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventCsWinPanelMatch>(OnMatchEnd);

        RegisterListener<Listeners.OnMapStart>(mapName => 
        {
            ResetMatchState();
            Console.WriteLine($"[LiteMatch] [StartWarmup] 地圖載入完成！準備執行暖身設定檔：{Config.WarmupConfigName}");
            Server.NextFrame(() => {
                Server.ExecuteCommand($"exec {Config.WarmupConfigName}");
            });
        });
    }

    // 【新增】動態計算當前需要的開賽人數
    private int GetDynamicRequiredPlayers()
    {
        int activeT = 0;
        int activeCT = 0;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && p.Handle != IntPtr.Zero && !p.IsBot && !p.IsHLTV)
            {
                if (p.TeamNum == 2) activeT++;
                if (p.TeamNum == 3) activeCT++;
            }
        }
        int total = activeT + activeCT;
        
        // 如果場上只有 1~2 人，目標人數就是 2 (準備單挑)
        // 如果場上超過 2 人 (如 3~4 人)，目標人數就是 Config 的最大設定 (準備團戰)
        return total <= 2 ? 2 : Config.MinPlayersToStart;
    }

    private void CheckAndResetGameImmediate()
    {
        Server.NextFrame(() => {
            if (!_isMatchLive || _isChangingMap) return; 
            try 
            {
                int activeT = 0, activeCT = 0;
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && p.Handle != IntPtr.Zero && !p.IsBot)
                    {
                        if (p.TeamNum == 2) activeT++;
                        if (p.TeamNum == 3) activeCT++;
                    }
                }
                if (activeT == 0 || activeCT == 0) AbortMatch();
            } 
            catch (Exception) { }
        });
    }

    private void AbortMatch()
    {
        if (!_isMatchLive) return;
        Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Orange}玩 家 離 退 對 戰 終 止，請 重 新 輸 入 {ChatColors.Lime}!R {ChatColors.Orange}對 戰");
        Server.ExecuteCommand("mp_warmup_start");
        
        var pauseConVar = ConVar.Find("mp_warmup_pausetimer");
        if (pauseConVar != null) pauseConVar.SetValue(1);
        else Server.ExecuteCommand("mp_warmup_pausetimer 1");
        
        ResetMatchState();
        Console.WriteLine($"[LiteMatch] [AbortMatch] 對戰已終止！正在切換回暖身設定檔：{Config.WarmupConfigName}");
        Server.NextFrame(() => { Server.ExecuteCommand($"exec {Config.WarmupConfigName}"); });
    }

    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return HookResult.Continue;
        if (!int.TryParse(info.GetArg(1), out int teamIndex)) return HookResult.Continue;

        // 【重新加回防護罩】只要比賽一開始，競技場大門立刻鎖死！
        // 完美防止 1v1 被第 3 人亂入，也防止 2v2 被干擾。
        if (_isMatchLive && (teamIndex == 2 || teamIndex == 3))
        {
            player.PrintToChat($" {_cachedPrefix} {ChatColors.Orange}對戰已經開始，無法中途加入！請在旁觀者模式等待");
            return HookResult.Handled; 
        }

        // 暖身階段的正常滿員檢查
        if (teamIndex == 2 || teamIndex == 3)
        {
            int currentTeamCount = 0;
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && p.Handle != IntPtr.Zero && !p.IsBot && p.TeamNum == teamIndex && p.SteamID != player.SteamID)
                {
                    currentTeamCount++;
                }
            }
            if (currentTeamCount >= Config.MaxPlayersPerTeam)
            {
                string teamName = teamIndex == 2 ? "恐怖份子 (T)" : "反恐小組 (CT)";
                player.PrintToChat($" {_cachedPrefix} {ChatColors.Orange}加 入 失 敗！{teamName} 已 經 滿 員 ( 最 多 {ChatColors.Green}{Config.MaxPlayersPerTeam}{ChatColors.Orange} 人 )");
                return HookResult.Handled; 
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return HookResult.Continue;
        string rawArg = info.GetArg(1);
        if (string.IsNullOrEmpty(rawArg)) return HookResult.Continue;

        bool isCommand = false;
        for (int i = 0; i < rawArg.Length; i++)
        {
            char c = rawArg[i];
            if (c == '"' || c == ' ') continue; 
            if (c == '!') { isCommand = true; break; }
            break; 
        }

        if (!isCommand) return HookResult.Continue; 
        string command = rawArg.Trim('"', ' ').ToLower();

        if (command == "!nextmap")
        {
            if (AdminManager.PlayerHasPermissions(player, "@css/root")) TriggerMapChange(false);
            else player.PrintToChat($" {_cachedPrefix} {ChatColors.Red}你沒有權限執行此指令。");
            return HookResult.Handled;
        }

        if (command == "!r" || command == "!ready")
        {
            if (!_isMatchLive) HandlePlayerReady(player);
            return HookResult.Continue; 
        }
        else if (command == "!unready")
        {
            if (!_isMatchLive) HandlePlayerUnready(player);
            return HookResult.Continue; 
        }

        if (Config.EnableChatWeaponCommands && HandleWeaponCommand(player, command)) return HookResult.Continue; 
        return HookResult.Continue;
    }

    private void TriggerMapChange(bool isMatchEnd = false)
    {
        if (_isChangingMap || Config.MapList == null || Config.MapList.Count == 0) return;
        _isChangingMap = true;
        var random = new Random();
        string selectedMapString = Config.MapList[random.Next(Config.MapList.Count)];
        string[] parts = selectedMapString.Split(':');
        string mapName = parts[0];
        string workshopId = parts.Length > 1 ? parts[1] : "";

        if (!isMatchEnd)
        {
            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Gold}管理員強制換圖：即將切換至 {ChatColors.Lime}{mapName}");
            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Orange}{Config.MapChangeDelay} 秒後 {ChatColors.Gold}自動載入...");
        }
        else
        {
            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Orange}{Config.MapChangeDelay} 秒後 {ChatColors.Gold}自動載入下一張地圖：{ChatColors.Lime}{mapName} ...");
        }

        AddTimer(Config.MapChangeDelay, () =>
        {
            if (!string.IsNullOrEmpty(workshopId)) Server.ExecuteCommand($"host_workshop_map {workshopId}");
            else Server.ExecuteCommand($"map {mapName}");
        });
    }

    private void HandlePlayerReady(CCSPlayerController player)
    {
        if (player.TeamNum == 0 || player.TeamNum == 1)
        {
            player.PrintToChat($" {_cachedPrefix} {ChatColors.Orange}您 無 法 從 旁 觀 者 模 式 加 入 對 戰");
            return;
        }

        ulong steamId = player.SteamID;
        if (_readyPlayers.Contains(steamId))
        {
            player.PrintToChat($" {_cachedPrefix} 你已經是 {ChatColors.Green}準備完成{ChatColors.White} 的狀態了！");
            return;
        }

        _readyPlayers.Add(steamId);
        _playerUnreadyTime.Remove(steamId); 
        
        // 廣播顯示動態人數
        int targetPlayers = GetDynamicRequiredPlayers();
        Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Green}{player.PlayerName}{ChatColors.White} 已 準 備！準 備 進 度：{ChatColors.Green}{_readyPlayers.Count} / {targetPlayers}");
        
        CheckMatchStart();
    }

    private void HandlePlayerUnready(CCSPlayerController player)
    {
        ulong steamId = player.SteamID;
        if (_readyPlayers.Contains(steamId))
        {
            _readyPlayers.Remove(steamId);
            _playerUnreadyTime[steamId] = 0; 
            
            // 廣播顯示動態人數
            int targetPlayers = GetDynamicRequiredPlayers();
            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Red}{player.PlayerName}{ChatColors.White} 取 消 了 準 備！準 備 進 度：{ChatColors.Green}{_readyPlayers.Count} / {targetPlayers}");
        }
    }

    // 【修改】純淨全自動：動態判斷 1v1 或 2v2 開賽邏輯
    private void CheckMatchStart()
    {
        if (_isMatchLive) return;

        int activeT = 0, activeCT = 0;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && p.Handle != IntPtr.Zero && !p.IsBot && !p.IsHLTV)
            {
                if (p.TeamNum == 2) activeT++;
                if (p.TeamNum == 3) activeCT++;
            }
        }
        int totalPlayers = activeT + activeCT;

        // 防呆機制：總人數必須是 2 人(單挑) 或 達標人數(團戰)
        if (totalPlayers != 2 && totalPlayers != Config.MinPlayersToStart) return;

        // 公平機制：隊伍必須絕對平衡 (1v1 或 2v2)
        if (activeT != activeCT) return;

        // 意願機制：準備人數必須大於等於場上總人數
        if (_readyPlayers.Count >= totalPlayers)
        {
            _isMatchLive = true;
            string modeText = totalPlayers == 2 ? "1 v 1 單 挑" : $"{activeT} v {activeCT} 團 戰";

            Server.PrintToChatAll($" {_cachedPrefix} 所 有 玩 家 已 準 備，{modeText} 比 賽 開 始");
            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Orange}對 戰 開 始！採 贏{ChatColors.Default} {ChatColors.Green}２４{ChatColors.Default} {ChatColors.Orange}回 合 制{ChatColors.Default}。");
            
            _privateCheckTimer?.Kill();
            _privateCheckTimer = null;
            _publicBroadcastTimer?.Kill();
            _publicBroadcastTimer = null;
            
            Console.WriteLine($"[LiteMatch] [MatchLive] 雙方準備就緒 ({modeText})！正式執行開賽設定檔：{Config.LiveConfigName}");
            Server.NextFrame(() => { Server.ExecuteCommand($"exec {Config.LiveConfigName}"); });
        }
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;
        
        ulong steamId = player.SteamID;
        
        // 【修改】不使用 Timer，改用雙重 NextFrame (等待 2 個引擎 Tick) 避開動畫衝突
        Server.NextFrame(() => {
            Server.NextFrame(() => {
                // 再次進行完整的安全檢查，確保這 2 幀過後玩家依然活著且有效
                if (player == null || !player.IsValid || player.PlayerPawn == null || !player.PlayerPawn.IsValid || !player.PawnIsAlive) return;
                
                player.RemoveWeapons(); 
                
                // 【修改】加入武器記憶讀取判斷
                foreach (var item in Config.SpawnWeapons) 
                { 
                    string weaponToGive = item;

                    // 判斷 1：如果設定檔這把槍是手槍，且玩家有自己偏好的手槍記憶
                    if (PistolNames.Contains(item))
                    {
                        if (_playerSecondary.TryGetValue(steamId, out string? prefSec)) weaponToGive = prefSec;
                    }
                    // 判斷 2：如果設定檔這把槍是主武器 (非手槍、非刀)，且玩家有偏好的主武器記憶
                    else if (item.StartsWith("weapon_") && !item.Contains("knife") && !item.Contains("bayonet"))
                    {
                        if (_playerPrimary.TryGetValue(steamId, out string? prefPri)) weaponToGive = prefPri;
                    }

                    player.GiveNamedItem(weaponToGive); 
                }
            });
        });
        
        return HookResult.Continue;
    }

    private bool HandleWeaponCommand(CCSPlayerController player, string command)
    {
        if (!player.PawnIsAlive) return false;
        ulong steamId = player.SteamID;
        
        // 【修改】換槍的同時，將玩家的選擇抄進筆記本裡
        switch (command)
        {
            // 手槍類：寫入副武器記憶
            case "!dg": _playerSecondary[steamId] = "weapon_deagle"; ReplaceWeapon(player, "weapon_deagle"); return true;
            case "!usp": _playerSecondary[steamId] = "weapon_usp_silencer"; ReplaceWeapon(player, "weapon_usp_silencer"); return true;
            case "!gk": _playerSecondary[steamId] = "weapon_glock"; ReplaceWeapon(player, "weapon_glock"); return true;
            case "!r8": _playerSecondary[steamId] = "weapon_revolver"; ReplaceWeapon(player, "weapon_revolver"); return true;
            
            // 狙擊類：寫入主武器記憶
            case "!ssg": _playerPrimary[steamId] = "weapon_ssg08"; ReplaceWeapon(player, "weapon_ssg08"); return true;
            case "!awp": _playerPrimary[steamId] = "weapon_awp"; ReplaceWeapon(player, "weapon_awp"); return true;
            
            case "!gs": OnGsCommand(player, null!); return true;
        }
        return false;
    }

    private void ReplaceWeapon(CCSPlayerController player, string newWeapon)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || pawn.WeaponServices == null || pawn.WeaponServices.MyWeapons == null) return;
        bool isRequestingPistol = PistolNames.Contains(newWeapon);

        foreach (var weaponHandle in pawn.WeaponServices.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon != null && weapon.IsValid)
            {
                string wName = weapon.DesignerName;
                if (string.IsNullOrEmpty(wName)) continue;
                if (wName.Contains("knife") || wName.Contains("bayonet") || wName.Contains("c4")) continue;

                bool isCurrentPistol = PistolNames.Contains(wName);
                if ((isRequestingPistol && isCurrentPistol) || (!isRequestingPistol && !isCurrentPistol))
                {
                    weapon.Remove();
                    // 【修改】不使用 Timer，利用下一個 Tick 發放新武器
                    Server.NextFrame(() => {
                        if (player.IsValid && player.PawnIsAlive) {
                            player.GiveNamedItem(newWeapon);
                        }
                    });
                    return; 
                }
            }
        }
        player.GiveNamedItem(newWeapon);
    }

    private void OnGsCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || !player.IsValid) return;
        player.PrintToChat($" {ChatColors.Orange}您 可 在 聊 天 欄 位 輸 入 您 要 的 武器，以 下 是 常 用 武器");
        player.PrintToChat($" [ {ChatColors.LightBlue}手槍{ChatColors.White} ]  {ChatColors.LightBlue}!dg {ChatColors.White}[ 沙鷹 ] 、{ChatColors.LightBlue}!usp {ChatColors.White}[ USP ] 、{ChatColors.LightBlue}!gk {ChatColors.White}[ 格洛克 ] 、{ChatColors.LightBlue}!r8 {ChatColors.White}[ R8 ]");
        player.PrintToChat($" [ {ChatColors.Orange}狙擊{ChatColors.White} ] {ChatColors.Orange}!ssg {ChatColors.White}[ SSG 08 鳥狙 ] 、{ChatColors.Orange}!awp {ChatColors.White}[ AWP狙擊步槍 ]");
    }

    private void CheckAndWarnUnreadyPlayers()
    {
        if (_isMatchLive) return; 

        try 
        {
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && p.Handle != IntPtr.Zero && !p.IsBot && !p.IsHLTV && (p.TeamNum == 2 || p.TeamNum == 3))
                {
                    ulong steamId = p.SteamID;
                    if (!_readyPlayers.Contains(steamId))
                    {
                        if (!_playerUnreadyTime.ContainsKey(steamId)) _playerUnreadyTime[steamId] = 0;
                        _playerUnreadyTime[steamId] += Config.UnreadyReminderInterval;

                        if (_playerUnreadyTime[steamId] >= Config.KickUnreadyPlayerTime) 
                        {
                            string kickedName = p.PlayerName;
                            Server.NextFrame(() => {
                                Server.ExecuteCommand($"kickid {p.UserId} Unready_Timeout");
                            });
                            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Lime}{kickedName} {ChatColors.White}因 未 準 備 好 而 被 踢 出");
                            _playerUnreadyTime.Remove(steamId);
                        }
                        else
                        {
                            int timeLeft = Config.KickUnreadyPlayerTime - _playerUnreadyTime[steamId];
                            p.PrintToChat($" {_cachedPrefix} 請輸入 {ChatColors.Lime}!R{ChatColors.White} 準備 ，{ChatColors.Lime}{timeLeft}{ChatColors.White} 秒未準備將被踢出");
                        }
                    }
                }
            }
        } 
        catch (Exception) { }
    }
   private void BroadcastUnreadyPlayers()
    {
        if (_isMatchLive) return; 
        try 
        {
            _unreadyNamesCache.Clear();
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && p.Handle != IntPtr.Zero && !p.IsBot && !p.IsHLTV && (p.TeamNum == 2 || p.TeamNum == 3))
                {
                    if (!_readyPlayers.Contains(p.SteamID)) _unreadyNamesCache.Add(p.PlayerName); 
                }
            }
            if (_unreadyNamesCache.Count > 0) 
            {
                // 取得動態人數
                int targetPlayers = GetDynamicRequiredPlayers();
                
                // 【新增】根據目標人數，動態決定要顯示哪一句提示
               // 【修改】將提示文字改為明確告知「以人數判斷」
               string modeHint = targetPlayers == 2 
                    ? $" [ {ChatColors.Green}動 態 判 斷{ChatColors.White} ] {ChatColors.White}目 前 場 上 {ChatColors.Green}2 {ChatColors.White}人，雙 方 輸 入 {ChatColors.Orange}!R {ChatColors.White}即 可 直 接 {ChatColors.Green}1 v 1 單 挑{ChatColors.White}"
                    : $" [ {ChatColors.Green}動 態 判 斷{ChatColors.White} ] {ChatColors.White}已觸發團戰，需滿 {ChatColors.Green}4 {ChatColors.White}人輸入 {ChatColors.Orange}!R {ChatColors.White}可開始 {ChatColors.Green}2 v 2 團戰{ChatColors.White}";
                
                Server.PrintToChatAll($" {_cachedPrefix} 尚未準備玩家：{ChatColors.Yellow}{string.Join(", ", _unreadyNamesCache)}{ChatColors.Default} | 對戰需滿 {ChatColors.Green}{targetPlayers}{ChatColors.Default} 人");
                
                // 這裡只會跟著印出符合當下情況的那一行
                Server.PrintToChatAll(modeHint); 
            }
        }
        catch (Exception) { }
    }
    private void ResetMatchState()
    {
        _isMatchLive = false;
        _isChangingMap = false;
        _readyPlayers.Clear();
        _playerUnreadyTime.Clear();
        
        _privateCheckTimer?.Kill();
        _privateCheckTimer = AddTimer(Config.UnreadyReminderInterval, CheckAndWarnUnreadyPlayers, TimerFlags.REPEAT);
        
        _publicBroadcastTimer?.Kill();
        _publicBroadcastTimer = AddTimer(Config.PublicUnreadyReminderInterval, BroadcastUnreadyPlayers, TimerFlags.REPEAT);
    }

    private HookResult OnMatchEnd(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        if (!_isMatchLive) return HookResult.Continue;
        int scoreT = 0, scoreCT = 0;
        var teamManagers = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
        foreach (var team in teamManagers)
        {
            if (team.TeamNum == 2) scoreT = team.Score;
            if (team.TeamNum == 3) scoreCT = team.Score;
        }

        string winnerName = scoreT > scoreCT ? "恐怖份子 (T)" : "反恐小組 (CT)";
        string loserName = scoreT > scoreCT ? "反恐小組 (CT)" : "恐怖份子 (T)";
        
        int winnerScore = Math.Max(scoreT, scoreCT);
        int loserScore = Math.Min(scoreT, scoreCT);
        string scoreString = $"{winnerScore} : {loserScore}";

        Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Lime}{winnerName} {ChatColors.Gold}以 {ChatColors.Green}({scoreString}) {ChatColors.Gold}的分數贏過 {ChatColors.Lime}{loserName}");

        TriggerMapChange(true);
        return HookResult.Continue;
    }
}
