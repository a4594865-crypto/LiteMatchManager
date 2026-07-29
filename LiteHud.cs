using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;

namespace LiteMatchManager;

public partial class LiteMatchManager
{
    private bool _isShowingHud = false;
    private float _hudEndTime = 0f;
    private string _cachedHudBaseHtml = ""; 
    private int _lastRemainingSeconds = -1;

    private void ShowHudWithCountdown(string baseHtml, int durationSeconds)
    {
        _cachedHudBaseHtml = baseHtml;
        _hudEndTime = Server.CurrentTime + durationSeconds;
        _isShowingHud = true;
        _lastRemainingSeconds = -1; // 強制重置，讓 OnTick 立即更新
    }

    private void HandleHudTick()
    {
        // 現在我們連 _runThisTickHud 減半都不需要了，因為我們只在「關鍵時刻」才發送封包
        if (_isShowingHud)
        {
            float currentTime = Server.CurrentTime;
            
            // 【階段一：時間到，強制清除畫面】
            if (currentTime >= _hudEndTime)
            {
                _isShowingHud = false; // 停止 HUD 邏輯
                
                // 只在結束的這一瞬間，發送一次空白字串，強制消除殘留畫面
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot) 
                    {
                        p.PrintToCenterHtml(" "); 
                    }
                }
            }
            // 【階段二：倒數中，只有「秒數改變」才發送】
            else
            {
                int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
                
                // ★ 效能優化核心：只有當數字從 15 變 14 時，才組裝字串並發送
                if (remaining != _lastRemainingSeconds)
                {
                    _lastRemainingSeconds = remaining;
                    string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
                    string currentRenderedHud = _cachedHudBaseHtml + countdownLine;
                    
                    // 每秒只會執行到這裡「1次」，大幅節省伺服器與網路效能
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p != null && p.IsValid && !p.IsBot) 
                            p.PrintToCenterHtml(currentRenderedHud);
                    }
                }
            }
        }
    }
}
