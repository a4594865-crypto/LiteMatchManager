using CounterStrikeSharp.API.Core;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LiteMatchManager;

public class LiteMatchConfig : BasePluginConfig
{
    [JsonPropertyName("MinPlayersToStart")] public int MinPlayersToStart { get; set; } = 4;
    [JsonPropertyName("MaxPlayersPerTeam")] public int MaxPlayersPerTeam { get; set; } = 3; 
    [JsonPropertyName("KickUnreadyPlayerTime")] public int KickUnreadyPlayerTime { get; set; } = 360;
    
    [JsonPropertyName("UnreadyReminderInterval")] public int UnreadyReminderInterval { get; set; } = 60;
    [JsonPropertyName("PublicUnreadyReminderInterval")] public int PublicUnreadyReminderInterval { get; set; } = 15;
    
    [JsonPropertyName("WaitingForOpponentInterval")] public int WaitingForOpponentInterval { get; set; } = 30;

    [JsonPropertyName("ChatPrefix")] public string ChatPrefix { get; set; } = "[ {Green}狙 擊 模 式{White} ]";
    [JsonPropertyName("EnableChatWeaponCommands")] public bool EnableChatWeaponCommands { get; set; } = true;
    
    [JsonPropertyName("SpawnWeapons")] 
    public List<string> SpawnWeapons { get; set; } = ["weapon_knife", "item_assaultsuit", "weapon_awp"];
    
    [JsonPropertyName("WarmupConfigName")] public string WarmupConfigName { get; set; } = "warmup.cfg";
    [JsonPropertyName("LiveConfigName")] public string LiveConfigName { get; set; } = "live.cfg";
    [JsonPropertyName("Duel_MapChangeDelay")] public int MapChangeDelay { get; set; } = 5;
    
    [JsonPropertyName("MapList")] 
    public List<string> MapList { get; set; } = ["Aim_redline_vieforit:3290337428", "aimpro_vieforit:3290753343"];

    // 各個 HUD 的專屬顯示秒數設定
    [JsonPropertyName("HudDuration_Prep")] public int HudDuration_Prep { get; set; } = 15;
    [JsonPropertyName("HudDuration_MatchAbort")] public int HudDuration_MatchAbort { get; set; } = 5;
    [JsonPropertyName("HudDuration_Round1")] public int HudDuration_Round1 { get; set; } = 8;
    
    // 共用的秒數倒數排版
    [JsonPropertyName("HudHtml_Countdown")] 
    public string HudHtml_Countdown { get; set; } = "<font class='fontSize-m' color='gray'>視窗關閉倒數：{0} 秒</font>";

    [JsonPropertyName("HudHtml_Prep1v1_Line1")] 
    public string HudHtml_Prep1v1_Line1 { get; set; } = "<font class='fontSize-l' color='white'>✦ 觸 發 1 v 1 單 挑 ✦</font>";
    
    [JsonPropertyName("HudHtml_Prep1v1_Line2")] 
    public string HudHtml_Prep1v1_Line2 { get; set; } = "<font class='fontSize-l' color='gray'>進度： </font><font class='fontSize-l' color='lime'>{0} / 2</font><font class='fontSize-l' color='gray'> ( 尚缺 {1} 人 )</font>";
    
    [JsonPropertyName("HudHtml_Prep2v2_Line1")] 
    public string HudHtml_Prep2v2_Line1 { get; set; } = "<font class='fontSize-l' color='white'>✦ 觸 發 2 v 2 團 戰 ✦</font>";
    
    [JsonPropertyName("HudHtml_Prep2v2_Line2")] 
    public string HudHtml_Prep2v2_Line2 { get; set; } = "<font class='fontSize-l' color='gray'>進度： </font><font class='fontSize-l' color='lime'>{0} / {2}</font><font class='fontSize-l' color='gray'> ( 尚缺 {1} 人 )</font>";

    [JsonPropertyName("HudHtml_Prep3v3_Line1")] 
    public string HudHtml_Prep3v3_Line1 { get; set; } = "<font class='fontSize-l' color='white'>✦ 觸 發 3 v 3 大 亂 鬥 ✦</font>";
    
    [JsonPropertyName("HudHtml_Prep3v3_Line2")] 
    public string HudHtml_Prep3v3_Line2 { get; set; } = "<font class='fontSize-l' color='gray'>進度： </font><font class='fontSize-l' color='lime'>{0} / {2}</font><font class='fontSize-l' color='gray'> ( 尚缺 {1} 人 )</font>";
    
    [JsonPropertyName("HudHtml_MatchAbort_Line1")] 
    public string HudHtml_MatchAbort_Line1 { get; set; } = "<font class='fontSize-l' color='red'>[ 警 告 ] 玩 家 逃 跑 ， 戰 鬥 終 止</font>";

    [JsonPropertyName("HudHtml_MatchAbort_Line2")] 
    public string HudHtml_MatchAbort_Line2 { get; set; } = "<font class='fontSize-l' color='white'>已 退 回 暖 身 模 式</font>";

    [JsonPropertyName("HudHtml_Round1_Line1")] 
    public string HudHtml_Round1_Line1 { get; set; } = "<font class='fontSize-l' color='gold'>✦ 戰 鬥 開 始 ✦</font>";

    [JsonPropertyName("HudHtml_Round1_Line2")] 
    public string HudHtml_Round1_Line2 { get; set; } = "<font class='fontSize-l' color='white'>率 先 取 得 </font><font class='fontSize-xxl' color='lime'><b>２０</b></font><font class='fontSize-xxl' color='white'> 勝 者 為 贏 家</font>";
}