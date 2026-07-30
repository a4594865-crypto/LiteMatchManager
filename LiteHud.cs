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

    // 新增：用來記錄是否已經發送過「空字串」清除畫面
    private bool _hasSentFinalMessage = false; 

    // HUD 推播觸發器
    private void ShowHudWithCountdown(string baseHtml, int durationSeconds)
    {
        _cachedHudBaseHtml = baseHtml;
        _hudEndTime = Server.CurrentTime + durationSeconds;
        _isShowingHud = true;
        _hasSentFinalMessage = false; // 每次呼叫新 HUD 時都要重置
        _lastRemainingSeconds = -1; // 強制重置，讓 OnTick 立即更新字串
    }

    // 專門處理 HUD 的 Tick 邏輯 (會被主檔案的 OnTick 呼叫)
    private void HandleHudTick()
    {
        _runThisTickHud = !_runThisTickHud; // 降低一半的 Tick 處理頻率
        if (_runThisTickHud && _isShowingHud)
        {
            float currentTime = Server.CurrentTime;
            
            // 當前時間已經大於等於設定的結束時間 (時間到！)
            if (currentTime >= _hudEndTime)
            {
                if (!_hasSentFinalMessage)
                {
                    // 【塌陷覆蓋大法】：發送空字串，讓黑框失去支撐瞬間縮小為 0
                    string clearHtml = ""; 
                    
                    // 將清除指令推給所有有效玩家
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p != null && p.IsValid && !p.IsBot) 
                            p.PrintToCenterHtml(clearHtml);
                    }
                    
                    _hasSentFinalMessage = true; // 標記已發送，確保只發送一次
                }
                
                _isShowingHud = false; // 時間到，停止推播 HUD
            }
            else
            {
                // 計算剩餘的整數秒數
                int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
                
                // 只有當秒數「改變」時，才重新組裝字串
                if (remaining != _lastRemainingSeconds)
                {
                    _lastRemainingSeconds = remaining;
                    string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
                    _currentRenderedHud = _cachedHudBaseHtml + countdownLine;
                }
                
                // 將最新的 HTML 畫面推給所有有效玩家
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot) 
                        p.PrintToCenterHtml(_currentRenderedHud);
                }
            }
        }
    }
}
