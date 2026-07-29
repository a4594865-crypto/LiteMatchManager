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
        _runThisTickHud = !_runThisTickHud; // 降低一半的 Tick 處理頻率
        if (_runThisTickHud)
        {
            if (_isShowingHud)
            {
                float currentTime = Server.CurrentTime;
                
                // 【關鍵修復】如果時間到了，立即強制清除畫面
                if (currentTime >= _hudEndTime)
                {
                    _isShowingHud = false; // 標記為停止推播
                    
                    // 發送一格空白字串 " "，強制消除 CS2 原生殘留 5 秒的機制
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p != null && p.IsValid && !p.IsBot) 
                        {
                            p.PrintToCenterHtml(" "); 
                        }
                    }
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
}
