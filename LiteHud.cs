using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;
using System.Collections.Generic; // 為了使用名單 (List) 功能

namespace LiteMatchManager;

public partial class LiteMatchManager
{
    private bool _isShowingHud = false;
    private float _hudEndTime = 0f;
    private string _cachedHudBaseHtml = ""; 
    private int _lastRemainingSeconds = -1;
    
    // 【極限優化核心】：宣告一個專屬發送名單 (參考 ServerGraphic)
    private List<CCSPlayerController> _hudTargetPlayers = new List<CCSPlayerController>();

    private void ShowHudWithCountdown(string baseHtml, int durationSeconds)
    {
        _cachedHudBaseHtml = baseHtml;
        _hudEndTime = Server.CurrentTime + durationSeconds;
        _isShowingHud = true;
        _lastRemainingSeconds = -1; 
        
        // 【建立名單】：一開始就點名，不用每次 Tick 都重新尋找玩家
        _hudTargetPlayers.Clear(); // 確保名單是乾淨的
        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && !p.IsBot && !p.IsHLTV)
            {
                _hudTargetPlayers.Add(p); // 加入發送名單
            }
        }
    }

    private void HandleHudTick()
    {
        // 如果沒有要顯示，直接 return，不佔用任何運算資源
        if (!_isShowingHud) return;

        float currentTime = Server.CurrentTime;
        
        // 【階段一：時間到，強制清除畫面並釋放資源】
        if (currentTime >= _hudEndTime)
        {
            _isShowingHud = false; // 停止 HUD 邏輯
            
            // 修正「遮擋框」：不能發送空白鍵 " "，這會產生黑底框。
            // 必須發送完全空字串 "" (裡面什麼都不要有)，才能讓 CS2 徹底撤銷該 UI 元素
           foreach (var p in _hudTargetPlayers)
            {
                if (p != null && p.IsValid) 
                {
                    // ✅ 發送長寬皆為 0 的區塊，並塞入零寬度空白字元 (&#8203;) 強制刷新引擎
                    p.PrintToCenterHtml("<div style='width: 0px; height: 0px;'>&#8203;</div>"); 
                }
            }
            
            // 顯示結束後，清空名單釋放伺服器資源 (同步 ServerGraphic 邏輯)
            _hudTargetPlayers.Clear(); 
        }
        // 【階段二：倒數中，只有「秒數改變」才對名單發送】
        else
        {
            int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
            
            // 只有當數字跳動時 (例如 15 變 14)，才組裝字串並發送
            if (remaining != _lastRemainingSeconds)
            {
                _lastRemainingSeconds = remaining;
                string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
                string currentRenderedHud = _cachedHudBaseHtml + countdownLine;
                
                // 【效能解放】：不再呼叫 Utilities.GetPlayers()，不產生記憶體垃圾
                // 直接對著已經點好名的名單發送！
                foreach (var p in _hudTargetPlayers)
                {
                    // 僅保留最後一道安全防線：防範這幾秒內剛好有人斷線
                    if (p != null && p.IsValid) 
                    {
                        p.PrintToCenterHtml(currentRenderedHud);
                    }
                }
            }
        }
    }
}
