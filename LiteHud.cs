using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;

namespace LiteMatchManager;

public partial class LiteMatchManager
{
    // === 動態 HUD 秒數的控制變數 ===
    private bool _isShowingHud = false;
    private float _hudEndTime = 0f;
    private string _cachedHudBaseHtml = ""; 
    private string _currentRenderedHud = ""; 
    private int _lastRemainingSeconds = -1;
    private bool _runThisTickHud = false; 

    // HUD 推播觸發器
    private void ShowHudWithCountdown(string baseHtml, int durationSeconds)
    {
        _cachedHudBaseHtml = baseHtml;
        _hudEndTime = Server.CurrentTime + durationSeconds;
        _isShowingHud = true;
        _lastRemainingSeconds = -1; // 強制重置，讓 OnTick 立即更新字串
    }

    // 專門處理 HUD 的 Tick 邏輯 (會被主檔案的 OnTick 呼叫)
    private void HandleHudTick()
    {
        _runThisTickHud = !_runThisTickHud; // 降低一半的 Tick 處理頻率[cite: 2]
        if (_runThisTickHud && _isShowingHud)
        {
            float currentTime = Server.CurrentTime;
            
            if (currentTime >= _hudEndTime)
            {
                _isShowingHud = false; 
                
                // 【黑魔法：CSS 坍塌大法】
                // 迫使 Panorama UI 的背景黑框根據內容縮小到 0x0
                // 這樣即使它在背景默默衰減 5 秒，玩家也完全看不見任何框
                string clearHtml = "<div style='width: 0px; height: 0px;'></div>";

                foreach (var p in _hudTargetPlayers)
                {
                    if (p != null && p.IsValid) 
                    {
                        // 1. 發送 0x0 的 HTML 坍塌外框
                        p.PrintToCenterHtml(clearHtml);
                        
                        // 2. 【雙重保險】利用普通的 PrintToCenter 發送空字串
                        // 在某些 Source 2 的 UI 狀態下，純文字的 Alert 頻道會強制中斷 HTML 頻道的佔用
                        p.PrintToCenter(" "); 
                    }
                }
                
                _hudTargetPlayers.Clear(); 
            }
            else
            {
                int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
                
                if (remaining != _lastRemainingSeconds)
                {
                    _lastRemainingSeconds = remaining;
                    string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
                    _currentRenderedHud = _cachedHudBaseHtml + countdownLine;
                }
                
                foreach (var p in _hudTargetPlayers)
                {
                    if (p != null && p.IsValid) 
                        p.PrintToCenterHtml(_currentRenderedHud);
                }
            }
        }
    }
}
