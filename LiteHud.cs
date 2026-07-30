using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;
using System.Linq;

namespace LiteMatchManager;

public partial class LiteMatchManager
{
    // === 動態 HUD 控制變數 ===
    private bool _isShowingHud = false;
    private float _hudEndTime = 0f;
    private string _cachedHudBaseHtml = ""; 
    private string _currentRenderedHud = ""; 
    private int _lastRemainingSeconds = -1;
    private bool _runThisTickHud = false; 
    private bool _hasSentFinalMessage = false; 

    private void ShowHudWithCountdown(string baseHtml, int durationSeconds)
    {
        _cachedHudBaseHtml = baseHtml;
        _hudEndTime = Server.CurrentTime + durationSeconds;
        _isShowingHud = true;
        _hasSentFinalMessage = false; 
        _lastRemainingSeconds = -1; 
    }

    private void HandleHudTick()
    {
        // ==========================================
        // 1. 底層 UI 狀態修復 Hack (每 Tick 執行)
        // ==========================================
        if (!_gameRulesInitialized)
        {
            InitializeGameRules();
        }
        
        if (_gameRules != null)
        {
            // 強制覆寫 GameRestart 狀態，欺騙客戶端引擎刷新 UI
            _gameRules.GameRestart = _gameRules.RestartRoundTime < Server.CurrentTime;
        }

        // ==========================================
        // 2. HUD 倒數與發送邏輯
        // ==========================================
        _runThisTickHud = !_runThisTickHud; 
        if (_runThisTickHud && _isShowingHud)
        {
            float currentTime = Server.CurrentTime;
            
            // 時間到！
            if (currentTime >= _hudEndTime)
            {
                if (!_hasSentFinalMessage)
                {
                    string clearHtml = ""; 
                    
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p != null && p.IsValid && !p.IsBot) 
                            p.PrintToCenterHtml(clearHtml);
                    }
                    
                    _hasSentFinalMessage = true; 
                }
                
                _isShowingHud = false; 
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
                
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot) 
                        p.PrintToCenterHtml(_currentRenderedHud);
                }
            }
        }
    }
}
