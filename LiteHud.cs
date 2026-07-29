using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;
using System.Collections.Generic;

namespace LiteMatchManager;

// 💡 注意這裡：必須是 public partial class，絕對不能是 private
public partial class LiteMatchManager
{
    private bool _isShowingHud = false;
    private float _hudEndTime = 0f;
    private string _cachedHudBaseHtml = ""; 
    private int _lastRemainingSeconds = -1;
    
    // 【極限優化核心】：宣告一個專屬發送名單
    private List<CCSPlayerController> _hudTargetPlayers = new List<CCSPlayerController>();

    private void ShowHudWithCountdown(string baseHtml, int durationSeconds)
    {
        _cachedHudBaseHtml = baseHtml;
        _hudEndTime = Server.CurrentTime + durationSeconds;
        _isShowingHud = true;
        _lastRemainingSeconds = -1; 
        
        // 【建立名單】：一開始就點名，不用每次 Tick 都重新尋找玩家
        _hudTargetPlayers.Clear(); 
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
        
        // 【階段一：時間到，完全停止發送】
        if (currentTime >= _hudEndTime)
        {
            _isShowingHud = false; // 停止 HUD 邏輯
            _hudTargetPlayers.Clear(); // 顯示結束後，清空名單釋放伺服器資源
            
            // 💡 什麼都不發送，讓 CS2 引擎自然且乾淨地關閉 HUD
            return;
        }
        
        // 【階段二：倒數中，只有「秒數改變」才對名單發送】
        int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
        
        // 只有當數字跳動時 (例如 15 變 14)，才組裝字串並發送
        if (remaining != _lastRemainingSeconds)
        {
            _lastRemainingSeconds = remaining;
            string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
            string currentRenderedHud = _cachedHudBaseHtml + countdownLine;
            
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
