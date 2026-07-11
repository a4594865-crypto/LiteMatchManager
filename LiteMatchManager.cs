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
    
    [JsonPropertyName("WaitingForOpponentInterval")] public int WaitingForOpponentInterval { get; set; } = 30;

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
    public override string ModuleVersion => "7.7_Absolute_Perfection";
    public override string ModuleAuthor => "Optimized";
    public override string ModuleDescription => "全動態開賽 + 終極防護 + 狀態機漏洞修復";

    public LiteMatchConfig Config { get; set; } = new LiteMatchConfig();

    private string _cachedPrefix = "";
    private HashSet<ulong> _readyPlayers = new(64);
    private Dictionary<ulong, int> _playerUnreadyTime = new(64); 
    private List<string> _unreadyNamesCache = new(64); 
    
    private Dictionary<ulong, string> _playerPrimary = new(64);
    private Dictionary<ulong, string> _playerSecondary = new(64);
    
    private bool _isMatchLive = false;
    private bool _isChangingMap = false; 
    private int _liveMatchTargetPlayers = 0; 
    
    private CounterStrikeSharp.API.Modules.Timers.Timer? _privateCheckTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _publicBroadcastTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _waitingTimer;

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
        Console.WriteLine("    LiteMatchManager v7.7 (絕對完美版) 初始化！ ");
        Console.WriteLine("=================================================");

        AddCommandListener("say", OnPlayerSay);
        AddCommandListener("say_team", OnPlayerSay);
        AddCommandListener("jointeam", OnJoinTeam);
        AddCommand("css_gs", "顯示武器選單提示", OnGsCommand);
        
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
                        _playerPrimary.Remove(steamId);
                        _playerSecondary.Remove(steamId);

                        if (_isMatchLive) 
                        {
                            CheckAndResetGameImmediate();
                        }
                        else 
                        {
                            Server.NextFrame(() => {
                                CheckMatchStart(); 
                            });
                        }
                    }
                } 
                catch (Exception) { }
            }
            return HookResult.Continue;
        });

        // 【終極修復核心】最嚴謹的隊伍變更狀態機
        RegisterEventHandler<EventPlayerTeam>((@event, info) =>
        {
            var player = @event.Userid;
            if (player != null && player.IsValid && player.Handle != IntPtr.Zero)
            {
                ulong steamId = player.SteamID;
                int newTeam = @event.Team; 
                
                // 1. 無論比賽是否開始，只要跳觀戰或未分配 (Team 0, 1)，一律徹底拔除參賽資格
                if (newTeam == 0 || newTeam == 1) 
                {
                    if (_readyPlayers.Contains(steamId))
                    {
                        _readyPlayers.Remove(steamId);
                        if (!_isMatchLive)
                            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Orange}{player.PlayerName}{ChatColors.White} 跳 去 觀 戰，已 取 消 準 備");
                        else
                            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Orange}{player.PlayerName}{ChatColors.White} 退 出 了 戰 鬥 (移 至 觀 戰)");
                    }
                    _playerUnreadyTime.Remove(steamId); 
                }

                // 2. 依照當前比賽狀態進行後續邏輯處理
                if (!_isMatchLive)
                {
                    // 暖身階段：延遲一幀，等待引擎將玩家陣營確實轉換完畢後，精準計算是否可開賽
                    Server.NextFrame(() => {
                        CheckMatchStart(); 
                    });
                }
                else
                {
                    // 比賽階段：有人嘗試加入 T(2) 或 CT(3)
                    if (newTeam == 2 || newTeam == 3)
                    {
                        if (_liveMatchTargetPlayers == 2 && !_readyPlayers.Contains(steamId))
                        {
                            // 1v1 局：非參賽者絕對不准加入，強制踢回觀戰
                            Server.NextFrame(() => {
                                if (player.IsValid) {
                                    player.ChangeTeam(CsTeam.Spectator);
                                    player.PrintToChat($" {_cachedPrefix} {ChatColors.Orange}1 v 1 單 挑 已 開 始，無 法 中 途 加 入！已 移 至 觀 戰。");
                                }
                            });
                        }
                        else if (_liveMatchTargetPlayers > 2 && !_readyPlayers.Contains(steamId))
                        {
                            // 2v2 局：允許替補，檢查人數是否已滿 (極度輕量迴圈)
                            int currentCount = 0;
                            foreach (var p in Utilities.GetPlayers())
                            {
                                if (p != null && p.IsValid && !p.IsBot && p.TeamNum == newTeam && p.SteamID != steamId)
                                    currentCount++;
                            }
                            
                            if (currentCount >= Config.MaxPlayersPerTeam)
                            {
                                Server.NextFrame(() => {
                                    if (player.IsValid) {
                                        player.ChangeTeam(CsTeam.Spectator);
                                        player.PrintToChat($" {_cachedPrefix} {ChatColors.Orange}該 隊 伍 已 滿 員，已 移 至 觀 戰。");
                                    }
                                });
                            }
                            else
                            {
                                // 替補合法加入，重新賦予參賽資格保護
                                _readyPlayers.Add(steamId);
                            }
                        }
                    }
                    
                    // 檢查是否有人離開(跳觀戰/斷線)導致人數不足而需要中斷比賽
                    CheckAndResetGameImmediate();
                }
            }
            return HookResult.Continue;
        }, HookMode.Post);

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

        if (teamIndex == 2 || teamIndex == 3)
        {
            if (_isMatchLive)
            {
                if (_liveMatchTargetPlayers == 2)
                {
                    if (!_readyPlayers.Contains(player.SteamID))
                    {
                        player.PrintToChat($" {_cachedPrefix} {ChatColors.Orange}1 v 1 單 挑 已 經 開 始，無 法 中 途 加 入！請 觀 戰");
                        return HookResult.Handled;
                    }
                    else
                    {
                        player.PrintToChat($" {_cachedPrefix} {ChatColors.Orange}對 戰 進 行 中，無 法 切 換 隊 伍！");
                        return HookResult.Handled;
                    }
                }
                else
                {
                    if (_readyPlayers.Contains(player.SteamID) && player.TeamNum >= 2)
                    {
                        player.PrintToChat($" {_cachedPrefix} {ChatColors.Orange}對 戰 進 行 中，無 法 切 換 隊 伍！");
                        return HookResult.Handled;
                    }
                }
            }

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
        
        int targetPlayers = GetDynamicRequiredPlayers();
        Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Green}{player.PlayerName}{ChatColors.White} 已 準 備！準 備 進 度：{ChatColors.Green}{_readyPlayers.Count} / {targetPlayers}");
        
        CheckMatchStart();

        if (!_isMatchLive)
        {
            int activePlayers = 0;
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && !p.IsBot && !p.IsHLTV && (p.TeamNum == 2 || p.TeamNum == 3))
                {
                    activePlayers++;
                }
            }

            if (activePlayers > 0 && activePlayers == _readyPlayers.Count)
            {
                BroadcastWaitingMessage();
                
                _waitingTimer?.Kill();
                _waitingTimer = AddTimer(Config.WaitingForOpponentInterval, BroadcastWaitingMessage, TimerFlags.REPEAT);
            }
        }
    }

    private void HandlePlayerUnready(CCSPlayerController player)
    {
        ulong steamId = player.SteamID;
        if (_readyPlayers.Contains(steamId))
        {
            _readyPlayers.Remove(steamId);
            _playerUnreadyTime[steamId] = 0; 
            
            int targetPlayers = GetDynamicRequiredPlayers();
            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Red}{player.PlayerName}{ChatColors.White} 取 消 了 準 備！準 備 進 度：{ChatColors.Green}{_readyPlayers.Count} / {targetPlayers}");
        }
    }

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

        if (totalPlayers != 2 && totalPlayers != Config.MinPlayersToStart) return;
        if (activeT != activeCT) return;

        if (_readyPlayers.Count >= totalPlayers && totalPlayers > 0)
        {
            _isMatchLive = true;
            _liveMatchTargetPlayers = totalPlayers; 
            
            string modeText = totalPlayers == 2 ? "1 v 1 單 挑" : $"{activeT} v {activeCT} 團 戰";

            Server.PrintToChatAll($" {_cachedPrefix} 所 有 玩 傢 已 準 備，{modeText} 比 賽 開 始");
            Server.PrintToChatAll($" {_cachedPrefix} {ChatColors.Orange}對 戰 開 始！採 贏{ChatColors.Default} {ChatColors.Green}２４{ChatColors.Default} {ChatColors.Orange}回 合 制{ChatColors.Default}。");
            
            _privateCheckTimer?.Kill();
            _privateCheckTimer = null;
            _publicBroadcastTimer?.Kill();
            _publicBroadcastTimer = null;
            _waitingTimer?.Kill();
            _waitingTimer = null;
            
            Console.WriteLine($"[LiteMatch] [MatchLive] 雙方準備就緒 ({modeText})！正式執行開賽設定檔：{Config.LiveConfigName}");
            Server.NextFrame(() => { Server.ExecuteCommand($"exec {Config.LiveConfigName}"); });
        }
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;
        
        ulong steamId = player.SteamID;

        // 【二次防護】玩家重生時，如果比賽中且身分不合法，沒收發槍並踢回觀戰
        if (_isMatchLive && (player.TeamNum == 2 || player.TeamNum == 3))
        {
            if (!_readyPlayers.Contains(steamId))
            {
                Server.NextFrame(() => {
                    if (player.IsValid) {
                        player.ChangeTeam(CsTeam.Spectator);
                        player.PrintToChat($" {_cachedPrefix} {ChatColors.Orange}比 賽 已 開 始，非 參 賽 者 無 法 偷 渡 加 入！已 移 至 觀 戰。");
                    }
                });
                return HookResult.Continue; 
            }
        }
        
        Server.NextFrame(() => {
            Server.NextFrame(() => {
                if (player == null || !player.IsValid || player.PlayerPawn == null || !player.PlayerPawn.IsValid || !player.PawnIsAlive) return;
                
                player.RemoveWeapons(); 
                
                foreach (var item in Config.SpawnWeapons) 
                { 
                    string weaponToGive = item;

                    if (PistolNames.Contains(item))
                    {
                        if (_playerSecondary.TryGetValue(steamId, out string? prefSec)) weaponToGive = prefSec;
                    }
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
        
        switch (command)
        {
            case "!dg": _playerSecondary[steamId] = "weapon_deagle"; ReplaceWeapon(player, "weapon_deagle"); return true;
            case "!usp": _playerSecondary[steamId] = "weapon_usp_silencer"; ReplaceWeapon(player, "weapon_usp_silencer"); return true;
            case "!gk": _playerSecondary[steamId] = "weapon_glock"; ReplaceWeapon(player, "weapon_glock"); return true;
            case "!r8": _playerSecondary[steamId] = "weapon_revolver"; ReplaceWeapon(player, "weapon_revolver"); return true;
            
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
        player.PrintToChat($" {ChatColors.Orange}您 可 在 聊 天 欄 位 輸 入 您 要 的 武 器，以 下 是 常 用 武 器");
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

    private void BroadcastWaitingMessage()
    {
        if (_isMatchLive) return;
        
        int totalPlayers = 0;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && p.Handle != IntPtr.Zero && !p.IsBot && !p.IsHLTV && (p.TeamNum == 2 || p.TeamNum == 3))
            {
                totalPlayers++;
            }
        }

        if (totalPlayers > 0 && totalPlayers == _readyPlayers.Count)
        {
            string modeHint = "";

            if (totalPlayers == 1)
            {
                modeHint = $" [ {ChatColors.Green}動 態 判 斷{ChatColors.White} ] {ChatColors.White}場 上 {ChatColors.Green}1 {ChatColors.White}人，等 對 手 加 入 {ChatColors.Green}1 v 1 {ChatColors.White}或 {ChatColors.Green}2 v 2 {ChatColors.White}對 戰";
            }
            else
            {
                modeHint = $" [ {ChatColors.Green}動 態 判 斷{ChatColors.White} ] {ChatColors.White}場 上 {ChatColors.Green}{totalPlayers} {ChatColors.White}人，等 對 手 加 入 {ChatColors.Green}2 v 2 {ChatColors.White}團 戰";
            }

            Server.PrintToChatAll(modeHint);
        }
    }

    private void BroadcastUnreadyPlayers()
    {
        if (_isMatchLive) return; 
        try 
        {
            _unreadyNamesCache.Clear();
            int totalPlayers = 0; 
            
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && p.Handle != IntPtr.Zero && !p.IsBot && !p.IsHLTV && (p.TeamNum == 2 || p.TeamNum == 3))
                {
                    totalPlayers++; 
                    if (!_readyPlayers.Contains(p.SteamID)) _unreadyNamesCache.Add(p.PlayerName); 
                }
            }
            
            if (totalPlayers > 0 && totalPlayers == _readyPlayers.Count) return;

            if (_unreadyNamesCache.Count > 0 || totalPlayers >= 2) 
            {
                int targetPlayers = GetDynamicRequiredPlayers();
                string modeHint = "";

                if (totalPlayers == 2)
                {
                    modeHint = $" [ {ChatColors.Green}動 態 判 斷{ChatColors.White} ] {ChatColors.White}目 前 場 上 {ChatColors.Green}2 {ChatColors.White}人，雙 方 輸 入 {ChatColors.Orange}!R {ChatColors.White}即 可 直 接 {ChatColors.Green}1 v 1 單 挑{ChatColors.White}";
                }
                else if (totalPlayers > 2)
                {
                    modeHint = $" [ {ChatColors.Green}動 態 判 斷{ChatColors.White} ] {ChatColors.White}已觸發團戰，需滿 {ChatColors.Green}{Config.MinPlayersToStart} {ChatColors.White}人輸入 {ChatColors.Orange}!R {ChatColors.White}可開始 {ChatColors.Green}2 v 2 團戰{ChatColors.White}";
                }
                
                if (_unreadyNamesCache.Count > 0)
                {
                    Server.PrintToChatAll($" {_cachedPrefix} 尚未準備玩家：{ChatColors.Yellow}{string.Join(", ", _unreadyNamesCache)}{ChatColors.Default} | 對戰需滿 {ChatColors.Green}{targetPlayers}{ChatColors.Default} 人");
                }
                
                if (!string.IsNullOrEmpty(modeHint))
                {
                    Server.PrintToChatAll(modeHint); 
                }
            }
        }
        catch (Exception) { }
    }

    private void ResetMatchState()
    {
        _isMatchLive = false;
        _isChangingMap = false;
        _liveMatchTargetPlayers = 0; 
        _readyPlayers.Clear();
        _playerUnreadyTime.Clear();
        
        _privateCheckTimer?.Kill();
        _privateCheckTimer = AddTimer(Config.UnreadyReminderInterval, CheckAndWarnUnreadyPlayers, TimerFlags.REPEAT);
        
        _publicBroadcastTimer?.Kill();
        _publicBroadcastTimer = AddTimer(Config.PublicUnreadyReminderInterval, BroadcastUnreadyPlayers, TimerFlags.REPEAT);

        _waitingTimer?.Kill();
        _waitingTimer = AddTimer(Config.WaitingForOpponentInterval, BroadcastWaitingMessage, TimerFlags.REPEAT);
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
