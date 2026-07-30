using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils; // 確保有 Utilities 可以抓玩家和狀態
using System;
using System.Linq;

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
    
    // 專門給 HUD 用的 GameRules 快取
    private CCSGameRulesProxy? _gameRulesProxyHud; 

    // HUD 推播觸發器
    public void ShowHudWithCountdown(string baseHtml, int durationSeconds)
    {
        _cachedHudBaseHtml = baseHtml;
        _hudEndTime = Server.CurrentTime + durationSeconds;
        _isShowingHud = true;
        _lastRemainingSeconds = -1; // 強制重置，讓 OnTick 立即更新字串
    }

    // 專門處理 HUD 的 Tick 邏輯 (會被主檔案的 OnTick 呼叫)[cite: 2]
    private void HandleHudTick()
    {
        _runThisTickHud = !_runThisTickHud; // 降低一半的 Tick 處理頻率[cite: 2]
        if (!_runThisTickHud) return;

        // 1. 處理 HUD 顯示
        if (_isShowingHud)
        {
            float currentTime = Server.CurrentTime;
            if (currentTime >= _hudEndTime)
            {
                _isShowingHud = false; // 時間到，停止推播 HUD[cite: 2]
                
                // 時間到時，自動清空所有玩家的畫面，避免殘留
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot) 
                        p.PrintToCenter(" ");
                }
            }
            else
            {
                // 計算剩餘的整數秒數[cite: 2]
                int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
                
                // 只有當秒數「改變」時，才重新組裝字串[cite: 2]
                if (remaining != _lastRemainingSeconds)
                {
                    _lastRemainingSeconds = remaining;
                    string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
                    _currentRenderedHud = _cachedHudBaseHtml + countdownLine;
                }
                
                // 將最新的 HTML 畫面推給所有有效玩家[cite: 2]
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot) 
                        p.PrintToCenterHtml(_currentRenderedHud);
                }
            }
        }

        // 2. 黑魔法：處理殘影與引擎狀態 (完美移植版)
        var proxy = GetGameRulesProxyForHud();
        if (proxy == null || !proxy.IsValid) return;

        var gameRules = proxy.GameRules;
        if (gameRules == null) return;

        // 🛡️ 最關鍵的防護罩：如果是暖身期間，絕對不要動 GameRestart！
        if (gameRules.WarmupPeriod) return;

        float serverTime = Server.CurrentTime;
        float restartTime = gameRules.RestartRoundTime;
        
        bool expectedState = restartTime < serverTime;

        if (gameRules.GameRestart != expectedState)
        {
            gameRules.GameRestart = expectedState;
            Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
        }
    }

    // 取得 GameRulesProxy 的輔助方法
    private CCSGameRulesProxy? GetGameRulesProxyForHud()
    {
        if (_gameRulesProxyHud != null && _gameRulesProxyHud.IsValid) return _gameRulesProxyHud;
        
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            _gameRulesProxyHud = entity;
            return _gameRulesProxyHud;
        }
        return null;
    }
}
