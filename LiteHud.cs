using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;
using System.Linq;

namespace LiteMatchManager;

public partial class LiteMatchManager
{
    // === GameRules 底層 Hack 變數 ===
    private CCSGameRules? _gameRules;
    private bool _gameRulesInitialized = false;

    // === 動態 HUD 控制變數 ===
    private bool _isShowingHud = false;
    private float _hudEndTime = 0f;
    private string _cachedHudBaseHtml = ""; 
    private string _currentRenderedHud = ""; 
    private int _lastRemainingSeconds = -1;
    private bool _runThisTickHud = false; 
    private bool _hasSentFinalMessage = false; 

    // 初始化 GameRules 代理實體
    private void InitializeGameRules()
    {
        if (_gameRulesInitialized) return;
        
        // 抓取全域的 cs_gamerules 實體 
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        _gameRules = gameRulesProxy?.GameRules;
        _gameRulesInitialized = _gameRules != null;
    }

    // ★重要：請確保在你的 OnMapStart 事件中有加上這行，以防換圖時實體指標失效
    // _gameRulesInitialized = false;

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
                    // 此時發送空字串，由於上面的 GameRules 狀態已被強制刷新，
                    // 引擎很有可能會判定為狀態重置，將文字與「黑底框」一併瞬間擊碎！
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
