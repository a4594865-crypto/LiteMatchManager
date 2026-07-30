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
        if (_isShowingHud)
        {
            float currentTime = Server.CurrentTime;
            
            if (currentTime >= _hudEndTime)
            {
                _isShowingHud = false; 
                
                // 【黑魔法三：GameRestart 強制抹除】
                // 欺騙引擎目前正在 Restart，瞬間殺死所有 Center HTML (show_survival_respawn_status) 殘留面板
                if (_gameRules != null)
                {
                    _gameRules.GameRestart = true;
                    // 在下一個 Frame 立刻還原，避免影響遊戲實際進程
                    Server.NextFrame(() => {
                        if (_gameRules != null) _gameRules.GameRestart = false;
                    });
                }
                
                _hudTargetPlayers.Clear(); 
            }
            else
            {
                int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
                
                // 【黑魔法一 & 二：跳過重複發送 (Skip Identical Refreshes)】
                // 只有當「秒數真的改變」時，才發送一次 HTML
                // 完全消滅 Panorama UI 的事件積壓，告別那多出來的 5 秒延遲
                if (remaining != _lastRemainingSeconds)
                {
                    _lastRemainingSeconds = remaining;
                    string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
                    _currentRenderedHud = _cachedHudBaseHtml + countdownLine;
                    
                    // 1 秒只發送 1 次
                    foreach (var p in _hudTargetPlayers)
                    {
                        if (p != null && p.IsValid) 
                            p.PrintToCenterHtml(_currentRenderedHud);
                    }
                }
            }
        }
