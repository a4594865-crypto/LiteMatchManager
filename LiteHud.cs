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
            // 【黑魔法一：防閃爍 (Anti-Flash)】
            // 在 HUD 顯示期間，持續欺騙引擎處於 GameRestart 狀態
            // 藉此壓制原生暖身 UI，讓每秒更新的倒數計時絕對不閃爍
            if (_gameRules != null)
            {
                _gameRules.GameRestart = true;
            }

            float currentTime = Server.CurrentTime;
            
            if (currentTime >= _hudEndTime)
            {
                _isShowingHud = false; 
                
                // HUD 結束，將 GameRestart 交還給原本的邏輯或設為 false
                if (_gameRules != null)
                {
                    _gameRules.GameRestart = false; 
                }
                
                // 【黑魔法二：CSS 坍塌大法 (解決 15 秒殘影)】
                string clearHtml = "<div style='width: 0px; height: 0px;'></div>";

                foreach (var p in _hudTargetPlayers)
                {
                    if (p != null && p.IsValid) 
                    {
                        p.PrintToCenterHtml(clearHtml);
                        p.PrintToCenter(" "); // 雙重保險，推擠 Alert 頻道
                    }
                }
                
                _hudTargetPlayers.Clear(); 
            }
            else
            {
                int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
                
                // 【黑魔法三：跳過重複渲染】
                // 只有秒數跳動時才發送，不塞爆 Panorama 事件佇列
                if (remaining != _lastRemainingSeconds)
                {
                    _lastRemainingSeconds = remaining;
                    string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
                    _currentRenderedHud = _cachedHudBaseHtml + countdownLine;
                    
                    foreach (var p in _hudTargetPlayers)
                    {
                        if (p != null && p.IsValid) 
                            p.PrintToCenterHtml(_currentRenderedHud);
                    }
                }
            }
        }
    }
