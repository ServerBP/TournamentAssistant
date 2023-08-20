﻿#pragma warning disable IDE0052
using BeatSaberMarkupLanguage;
using HMUI;
using IPA.Utilities.Async;
using System;
using System.Linq;
using System.Threading.Tasks;
using TournamentAssistant.UI.ViewControllers;
using TournamentAssistant.Utilities;
using TournamentAssistantShared;
using TournamentAssistantShared.Models;
using UnityEngine;
using UnityEngine.UI;
using static TournamentAssistantShared.Models.GameplayModifiers;
using static TournamentAssistantShared.Models.PlayerSpecificSettings;

namespace TournamentAssistant.UI.FlowCoordinators
{
    class QualifierCoordinator : FlowCoordinator
    {
        public event Action DidFinishEvent;

        public QualifierEvent Event { get; set; }
        public CoreServer Server { get; set; }
        public PluginClient Client { get; set; }

        private SongSelection _songSelection;
        private SongDetail _songDetail;
        private RemainingAttempts _bottomText;

        private QualifierEvent.QualifierMap _currentMap;
        private IBeatmapLevel _lastPlayedBeatmapLevel;
        private BeatmapCharacteristicSO _lastPlayedCharacteristic;
        private BeatmapDifficulty _lastPlayedDifficulty;

        private PlayerDataModel _playerDataModel;
        private MenuLightsManager _menuLightsManager;
        private SoloFreePlayFlowCoordinator _soloFreePlayFlowCoordinator;

        private GameplaySetupViewController _gameplaySetupViewController;
        private GameplayModifiersPanelController _gameplayModifiersPanelController;

        private CustomLeaderboard _customLeaderboard;
        private ResultsViewController _resultsViewController;
        private MenuLightsPresetSO _scoreLights;
        private MenuLightsPresetSO _redLights;
        private MenuLightsPresetSO _defaultLights;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle(Plugin.GetLocalized("qualifier_room"), ViewController.AnimationType.None);
                showBackButton = true;

                _playerDataModel = Resources.FindObjectsOfTypeAll<PlayerDataModel>().First();
                _menuLightsManager = Resources.FindObjectsOfTypeAll<MenuLightsManager>().First();
                _soloFreePlayFlowCoordinator = Resources.FindObjectsOfTypeAll<SoloFreePlayFlowCoordinator>().First();
                _resultsViewController = Resources.FindObjectsOfTypeAll<ResultsViewController>().First();
                _scoreLights = _soloFreePlayFlowCoordinator.GetField<MenuLightsPresetSO>("_resultsClearedLightsPreset");
                _redLights = _soloFreePlayFlowCoordinator.GetField<MenuLightsPresetSO>("_resultsFailedLightsPreset");
                _defaultLights = _soloFreePlayFlowCoordinator.GetField<MenuLightsPresetSO>("_defaultLightsPreset");

                _songSelection = BeatSaberUI.CreateViewController<SongSelection>();
                _songSelection.SongSelected += SongSelection_SongSelected;

                _songDetail = BeatSaberUI.CreateViewController<SongDetail>();
                _songDetail.PlayPressed += SongDetail_didPressPlayButtonEvent;
                _songDetail.DisableCharacteristicControl = true;
                _songDetail.DisableDifficultyControl = true;
                _songDetail.DisablePlayButton = false;

                _customLeaderboard = BeatSaberUI.CreateViewController<CustomLeaderboard>();

                _gameplaySetupViewController = Resources.FindObjectsOfTypeAll<GameplaySetupViewController>().First();
                _gameplayModifiersPanelController = Resources.FindObjectsOfTypeAll<GameplayModifiersPanelController>().First();
            }
            if (addedToHierarchy)
            {
                _songSelection.SetSongs(Event.QualifierMaps);
                ProvideInitialViewControllers(_songSelection, bottomScreenViewController: _bottomText);
            }
        }

        private void DisableDisallowedModifierToggles(GameplayModifiersPanelController controller)
        {
            var toggles = controller.GetField<GameplayModifierToggle[]>("_gameplayModifierToggles");
            var disallowedToggles = toggles.Where(x => x.name != "ProMode");

            foreach (var toggle in disallowedToggles)
            {
                toggle.gameObject.SetActive(false);
            }
        }

        private void ReenableDisallowedModifierToggles(GameplayModifiersPanelController controller)
        {
            var toggles = controller.GetField<GameplayModifierToggle[]>("_gameplayModifierToggles");

            foreach (var toggle in toggles)
            {
                toggle.gameObject.SetActive(true);
            }
        }

        private void SongDetail_didPressPlayButtonEvent(IBeatmapLevel level, BeatmapCharacteristicSO characteristic, BeatmapDifficulty difficulty)
        {
            _lastPlayedBeatmapLevel = level;
            _lastPlayedCharacteristic = characteristic;
            _lastPlayedDifficulty = difficulty;

            var playerData = Resources.FindObjectsOfTypeAll<PlayerDataModel>().First().playerData;
            var playerSettings = playerData.playerSpecificSettings;

            //Override defaults if we have forced options enabled
            if (_currentMap.GameplayParameters.PlayerSettings.Options != PlayerOptions.NoPlayerOptions)
            {
                playerSettings = new PlayerSpecificSettings(
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.LeftHanded),
                        _currentMap.GameplayParameters.PlayerSettings.PlayerHeight,
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.AutoPlayerHeight),
                        _currentMap.GameplayParameters.PlayerSettings.SfxVolume,
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.ReduceDebris),
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.NoHud),
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.NoFailEffects),
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.AdvancedHud),
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.AutoRestart),
                        _currentMap.GameplayParameters.PlayerSettings.SaberTrailIntensity,
                        (NoteJumpDurationTypeSettings)_currentMap.GameplayParameters.PlayerSettings.note_jump_duration_type_settings,
                        _currentMap.GameplayParameters.PlayerSettings.NoteJumpFixedDuration,
                        _currentMap.GameplayParameters.PlayerSettings.NoteJumpStartBeatOffset,
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.HideNoteSpawnEffect),
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.AdaptiveSfx),
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.ArcsHapticFeedback),
                        (ArcVisibilityType)_currentMap.GameplayParameters.PlayerSettings.arc_visibility_type,
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.StaticLights) ? EnvironmentEffectsFilterPreset.NoEffects : EnvironmentEffectsFilterPreset.AllEffects,
                        _currentMap.GameplayParameters.PlayerSettings.Options.HasFlag(PlayerOptions.StaticLights) ? EnvironmentEffectsFilterPreset.NoEffects : EnvironmentEffectsFilterPreset.AllEffects
                    );
            }

            var songSpeed = GameplayModifiers.SongSpeed.Normal;
            if (_currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.SlowSong)) songSpeed = GameplayModifiers.SongSpeed.Slower;
            if (_currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.FastSong)) songSpeed = GameplayModifiers.SongSpeed.Faster;
            if (_currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.SuperFastSong)) songSpeed = GameplayModifiers.SongSpeed.SuperFast;

            var gameplayModifiers = new GameplayModifiers(
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.BatteryEnergy) ? GameplayModifiers.EnergyType.Battery : GameplayModifiers.EnergyType.Bar,
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.NoFail),
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.InstaFail),
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.FailOnClash),
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.NoObstacles) ? GameplayModifiers.EnabledObstacleType.NoObstacles : GameplayModifiers.EnabledObstacleType.All,
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.NoBombs),
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.FastNotes),
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.StrictAngles),
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.DisappearingArrows),
                songSpeed,
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.NoArrows),
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.GhostNotes),
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.ProMode) || playerData.gameplayModifiers.proMode, // Allow players to override promode setting
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.ZenMode),
                _currentMap.GameplayParameters.GameplayModifiers.Options.HasFlag(GameOptions.SmallCubes)
            );

            var colorScheme = playerData.colorSchemesSettings.overrideDefaultColors ? playerData.colorSchemesSettings.GetSelectedColorScheme() : null;

            //Disable scores if we need to
            if (((QualifierEvent.EventSettings)Event.Flags).HasFlag(QualifierEvent.EventSettings.DisableScoresaberSubmission)) BS_Utils.Gameplay.ScoreSubmission.DisableSubmission(Constants.NAME);

            //Enable anti-pause if we need to
            if (_currentMap.DisablePause)
            {
                Plugin.DisablePause = true;
            }

            Task.Run(() => PlayerUtils.GetPlatformUserData((username, platformId) => InitiateAttemptWhenResolved(username, platformId)));

            SongUtils.PlaySong(level, characteristic, difficulty, playerData.overrideEnvironmentSettings, colorScheme, gameplayModifiers, playerSettings, SongFinished);
        }

        private async void SongSelection_SongSelected(QualifierEvent.QualifierMap map)
        {
            _currentMap = map;

            var loadedLevel = await SongUtils.LoadSong(map.GameplayParameters.Beatmap.LevelId);
            PresentViewController(_songDetail, () =>
            {
                _songDetail.SetSelectedSong(loadedLevel);
                _songDetail.SetSelectedDifficulty(map.GameplayParameters.Beatmap.Difficulty);
                _songDetail.SetSelectedCharacteristic(map.GameplayParameters.Beatmap.Characteristic.SerializedName);

                // Disable play button until we get info about remaining attempts
                _songDetail.DisablePlayButton = true;

                _gameplaySetupViewController.Setup(true, true, true, false, PlayerSettingsPanelController.PlayerSettingsPanelLayout.Singleplayer);
                SetLeftScreenViewController(_gameplaySetupViewController, ViewController.AnimationType.In);

                DisableDisallowedModifierToggles(_gameplayModifiersPanelController);

                //TODO: Review whether this could cause issues. Probably need debouncing or something similar
                Task.Run(() => PlayerUtils.GetPlatformUserData(RequestLeaderboardAndAttemptsWhenResolved));
                SetRightScreenViewController(_customLeaderboard, ViewController.AnimationType.In);

                _bottomText = BeatSaberUI.CreateViewController<RemainingAttempts>();
                SetBottomScreenViewController(_bottomText, ViewController.AnimationType.In);
            });
        }

        private void ResultsViewController_continueButtonPressedEvent(ResultsViewController results)
        {
            _resultsViewController.continueButtonPressedEvent -= ResultsViewController_continueButtonPressedEvent;
            _menuLightsManager.SetColorPreset(_defaultLights, true);
            DismissViewController(_resultsViewController);
        }

        private void ResultsViewController_restartButtonPressedEvent(ResultsViewController results)
        {
            _resultsViewController.continueButtonPressedEvent -= ResultsViewController_continueButtonPressedEvent;
            _menuLightsManager.SetColorPreset(_defaultLights, true);
            DismissViewController(_resultsViewController, finishedCallback: () => SongDetail_didPressPlayButtonEvent(_lastPlayedBeatmapLevel, _lastPlayedCharacteristic, _lastPlayedDifficulty));
        }

        public void SongFinished(StandardLevelScenesTransitionSetupDataSO standardLevelScenesTransitionSetupData, LevelCompletionResults results)
        {
            standardLevelScenesTransitionSetupData.didFinishEvent -= SongFinished;

            var map = standardLevelScenesTransitionSetupData.difficultyBeatmap;
            var transformedMap = standardLevelScenesTransitionSetupData.transformedBeatmapData;
            var localPlayer = _playerDataModel.playerData;
            var localResults = localPlayer.GetPlayerLevelStatsData(map.level.levelID, map.difficulty, map.parentDifficultyBeatmapSet.beatmapCharacteristic);
            var highScore = localResults.highScore < results.modifiedScore;

            if (results.levelEndAction == LevelCompletionResults.LevelEndAction.Restart) SongDetail_didPressPlayButtonEvent(_lastPlayedBeatmapLevel, _lastPlayedCharacteristic, _lastPlayedDifficulty);
            else if (results.levelEndStateType != LevelCompletionResults.LevelEndStateType.Incomplete)
            {
                if (results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared)
                {
                    Task.Run(() => PlayerUtils.GetPlatformUserData((username, platformId) => SubmitScoreWhenResolved(username, platformId, results)));

                    _menuLightsManager.SetColorPreset(_scoreLights, true);
                    _resultsViewController.Init(results, transformedMap, map, false, highScore);
                    _resultsViewController.continueButtonPressedEvent += ResultsViewController_continueButtonPressedEvent;
                    _resultsViewController.restartButtonPressedEvent += ResultsViewController_restartButtonPressedEvent;
                }
                else if (results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed)
                {
                    _menuLightsManager.SetColorPreset(_redLights, true);
                    _resultsViewController.Init(results, transformedMap, map, false, highScore);
                    _resultsViewController.continueButtonPressedEvent += ResultsViewController_continueButtonPressedEvent;
                    _resultsViewController.restartButtonPressedEvent += ResultsViewController_restartButtonPressedEvent;
                }

                PresentViewController(_resultsViewController, immediately: true);
            }
        }

        private async Task InitiateAttemptWhenResolved(string username, string platformId)
        {
            // Disable the restart button until we know for sure another attempt can be made
            await UnityMainThreadTaskScheduler.Factory.StartNew(() =>
            {
                _songDetail.DisablePlayButton = true;
                _resultsViewController.GetField<Button>("_restartButton").gameObject.SetActive(false);
            });

            await Client.SendQualifierScore(Event.Guid, _currentMap, platformId, username, false, -1, (_) => Task.CompletedTask);

            // If the player fails or quits, a score won't be submitted, so we should do this here
            await Client.RequestAttempts(Event.Guid, _currentMap.Guid, async (packetWrapper) =>
            {
                var remainingAttempts = packetWrapper.Payload.Response.remaining_attempts.remaining_attempts;
                await UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    _songDetail.DisablePlayButton = remainingAttempts <= 0;
                    _resultsViewController.GetField<Button>("_restartButton").gameObject.SetActive(remainingAttempts > 0);
                    _bottomText.SetRemainingAttempts(remainingAttempts);
                });
            });
        }

        private Task SubmitScoreWhenResolved(string username, string platformId, LevelCompletionResults results)
        {
            Task.Run(async () =>
            {
                await Client.SendQualifierScore(Event.Guid, _currentMap, platformId, username, results.fullCombo, results.modifiedScore, async (packetWrapper) =>
                {
                    var scores = packetWrapper.Payload.Response.leaderboard_scores.Scores.Take(10).ToArray();
                    await UnityMainThreadTaskScheduler.Factory.StartNew(() => SetCustomLeaderboardScores(scores, platformId));
                });

                await Client.RequestAttempts(Event.Guid, _currentMap.Guid, async (packetWrapper) =>
                {
                    var remainingAttempts = packetWrapper.Payload.Response.remaining_attempts.remaining_attempts;
                    await UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                    {
                        _songDetail.DisablePlayButton = remainingAttempts <= 0;
                        _resultsViewController.GetField<Button>("_restartButton").gameObject.SetActive(remainingAttempts > 0);
                        _bottomText.SetRemainingAttempts(remainingAttempts);
                    });
                });
            });
            return Task.CompletedTask;
        }

        private async Task RequestLeaderboardAndAttemptsWhenResolved(string username, string platformId)
        {
            await Client.RequestLeaderboard(Event.Guid, _currentMap.Guid, async (packetWrapper) =>
            {
                var scores = packetWrapper.Payload.Response.leaderboard_scores.Scores.Take(10).ToArray();
                await UnityMainThreadTaskScheduler.Factory.StartNew(() => SetCustomLeaderboardScores(scores, platformId));
            });

            await Client.RequestAttempts(Event.Guid, _currentMap.Guid, async (packetWrapper) =>
            {
                var remainingAttempts = packetWrapper.Payload.Response.remaining_attempts.remaining_attempts;
                await UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    _songDetail.DisablePlayButton = remainingAttempts <= 0;
                    _resultsViewController.GetField<Button>("_restartButton").gameObject.SetActive(remainingAttempts > 0);
                    _bottomText.SetRemainingAttempts(remainingAttempts);
                });
            });
        }

        public void SetCustomLeaderboardScores(LeaderboardScore[] scores, string platformId)
        {
            var place = 1;
            var indexOfme = -1;
            _customLeaderboard.SetScores(scores.Select(x =>
            {
                if (x.PlatformId == platformId) indexOfme = place - 1;
                return new LeaderboardTableView.ScoreData(x.Score, x.Username, place++, x.FullCombo);
            }).ToList(), indexOfme);
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            if (topViewController is ResultsViewController)
            {
                _menuLightsManager.SetColorPreset(_defaultLights, false);
                _resultsViewController.GetField<Button>("_restartButton").gameObject.SetActive(true);
                ReenableDisallowedModifierToggles(_gameplayModifiersPanelController);
                DismissViewController(_resultsViewController);
            }
            else if (topViewController is SongDetail)
            {
                SetLeftScreenViewController(null, ViewController.AnimationType.Out);
                SetRightScreenViewController(null, ViewController.AnimationType.Out);
                SetBottomScreenViewController(null, ViewController.AnimationType.Out);
                DismissViewController(_songDetail);
            }
            else DidFinishEvent?.Invoke();
        }
    }
}
