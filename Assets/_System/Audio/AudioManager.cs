using _System.ECS.Components.Audio;
using FMOD.Studio;
using FMODUnity;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;
using static FMOD.Studio.PLAYBACK_STATE;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace _System.Audio
{
    public class AudioManager : MonoBehaviour
    {
        [SerializeField]
        private CharacterShopUIController characterShopUIController;

        [SerializeField]
        private AmuletShopUIController amuletShopUIController;

        [SerializeField]
        private EventReference gemCollectedSound;

        [SerializeField]
        private EventReference lobbyMusicNoDrums;

        [SerializeField]
        private EventReference lobbyMusicWithDrums;

        [SerializeField]
        private EventReference runMusic;

        [SerializeField]
        private EventReference bossMusic;

        [SerializeField]
        private EventReference whooshPlanet;

        [SerializeField]
        private EventReference whooshShop;

        [SerializeField]
        private EventReference playerDamagesSfx;

        [SerializeField]
        private EventReference enemiesDamagesSfx;

        [SerializeField]
        private EventReference levelUp;

        [SerializeField]
        private EventReference uiSfxSelect;

        [SerializeField]
        private EventReference uiSfxConfirm;

        [SerializeField]
        private EventReference uiSfxBack;

        private EntityManager _entityManager;
        private EntityQuery _soundQuery;

        private EventInstance lobbyMusicNoDrumsInstance;
        private EventInstance lobbyMusicWithDrumsInstance;
        private EventInstance runMusicInstance;
        private EventInstance bossMusicInstance;

        [Header("Music fade")]
        [SerializeField]
        private float musicFadeDuration = 1.5f;

        private float _pitchTarget;
        private float _pitchCurrent;
        private float _filterTarget;
        private float _filterCurrent;

        private float _runVolumeTarget;
        private float _runVolumeCurrent;
        private float _bossVolumeTarget;
        private float _bossVolumeCurrent;

        private bool _bossMusicStarted;

        // [SerializeField]
        // private GameManager _gameManager;

        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged += ProcessMusic;

            characterShopUIController.OnSelected += ProcessUiSelectSfx;
            characterShopUIController.OnConfirmed += ProcessUiConfirmSfx;
            characterShopUIController.OnBack += ProcessUiBackSfx;
            amuletShopUIController.OnSelected += ProcessUiSelectSfx;
            amuletShopUIController.OnConfirmed += ProcessUiConfirmSfx;
            amuletShopUIController.OnBack += ProcessUiBackSfx;
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStateChanged -= ProcessMusic;

            characterShopUIController.OnSelected -= ProcessUiSelectSfx;
            characterShopUIController.OnConfirmed -= ProcessUiConfirmSfx;
            characterShopUIController.OnBack -= ProcessUiBackSfx;
            amuletShopUIController.OnSelected -= ProcessUiSelectSfx;
            amuletShopUIController.OnConfirmed -= ProcessUiConfirmSfx;
            amuletShopUIController.OnBack -= ProcessUiBackSfx;
        }

        private void Start()
        {
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            _soundQuery = _entityManager.CreateEntityQuery(typeof(SoundPlayerTag));

            lobbyMusicNoDrumsInstance = RuntimeManager.CreateInstance(lobbyMusicNoDrums);
            lobbyMusicWithDrumsInstance = RuntimeManager.CreateInstance(lobbyMusicWithDrums);
            runMusicInstance = RuntimeManager.CreateInstance(runMusic);
            bossMusicInstance = RuntimeManager.CreateInstance(bossMusic);

            if (GameManager.Instance != null)
                ProcessMusic(GameManager.Instance.GetGameState());
        }

        private void Update()
        {
            ProcessEnemiesDamagesSfx();
            ProcessPlayerDamagesSfx();
            ProcessGemsCollection();
            ProcessBossMusic();
            UpdateMusicFade();
        }

        public void ProcessMusic(EGameState newState)
        {
            Debug.Log($"ProcessMusic: {newState}");

            switch (newState)
            {
                case EGameState.Lobby:
                    StopRunAndBossMusic();
                    SetLobbyParams(pitch: 0f, filter: 0f, snap: EnsureLobbyMusicPlaying());
                    break;

                case EGameState.CharacterSelection:
                case EGameState.AmuletShop:
                case EGameState.MetaProgression:
                    RuntimeManager.PlayOneShot(whooshShop);
                    StopRunAndBossMusic();
                    SetLobbyParams(pitch: 0f, filter: 1f, snap: EnsureLobbyMusicPlaying());
                    break;

                case EGameState.PlanetSelection:
                    RuntimeManager.PlayOneShot(whooshPlanet);
                    StopRunAndBossMusic();
                    SetLobbyParams(pitch: 1f, filter: 0f, snap: EnsureLobbyMusicPlaying());
                    break;

                case EGameState.Running:
                    StopLobbyMusic();
                    SetLobbyParams(pitch: 0f, filter: 0f, snap: false); // glide params back to neutral
                    EnterRunMusic();
                    break;

                case EGameState.Paused:
                case EGameState.UpgradeSelection:
                    RuntimeManager.PlayOneShot(levelUp);
                    break;

                case EGameState.GameOver:
                case EGameState.MainMenu:
                default:
                    StopLobbyMusic();
                    StopRunAndBossMusic();
                    SetLobbyParams(pitch: 0f, filter: 0f, snap: false); // neutral
                    break;
            }
        }

        private bool EnsureLobbyMusicPlaying()
        {
            lobbyMusicNoDrumsInstance.getPlaybackState(out var state);
            if (state == STOPPED || state == STOPPING)
            {
                lobbyMusicNoDrumsInstance.start();
                lobbyMusicWithDrumsInstance.start();
                return true;
            }

            return false;
        }

        private void SetLobbyParams(float pitch, float filter, bool snap = false)
        {
            _pitchTarget = pitch;
            _filterTarget = filter;

            if (!snap)
                return;

            _pitchCurrent = pitch;
            _filterCurrent = filter;
            ApplyGlobalParams();
        }

        private void UpdateMusicFade()
        {
            float step = musicFadeDuration <= 0f ? 1f : Time.unscaledDeltaTime / musicFadeDuration;

            if (StepToward(ref _pitchCurrent, _pitchTarget, step))
                RuntimeManager.StudioSystem.setParameterByName("Pitch", _pitchCurrent);

            if (StepToward(ref _filterCurrent, _filterTarget, step))
                RuntimeManager.StudioSystem.setParameterByName("Filter", _filterCurrent);

            if (StepToward(ref _runVolumeCurrent, _runVolumeTarget, step))
                runMusicInstance.setVolume(_runVolumeCurrent);

            if (StepToward(ref _bossVolumeCurrent, _bossVolumeTarget, step))
                bossMusicInstance.setVolume(_bossVolumeCurrent);

            StopIfFadedOut(runMusicInstance, _runVolumeCurrent, _runVolumeTarget);
            StopIfFadedOut(bossMusicInstance, _bossVolumeCurrent, _bossVolumeTarget);
        }

        private static bool StepToward(ref float current, float target, float step)
        {
            if (Mathf.Approximately(current, target))
                return false;

            current = Mathf.MoveTowards(current, target, step);
            return true;
        }

        private static void StopIfFadedOut(EventInstance instance, float current, float target)
        {
            if (target > 0f || current > 0f)
                return;

            instance.getPlaybackState(out var state);
            if (state != STOPPED && state != STOPPING)
                instance.stop(STOP_MODE.ALLOWFADEOUT);
        }

        private void ApplyGlobalParams()
        {
            RuntimeManager.StudioSystem.setParameterByName("Pitch", _pitchCurrent);
            RuntimeManager.StudioSystem.setParameterByName("Filter", _filterCurrent);
        }

        private void StopLobbyMusic()
        {
            lobbyMusicNoDrumsInstance.stop(STOP_MODE.ALLOWFADEOUT);
            lobbyMusicWithDrumsInstance.stop(STOP_MODE.ALLOWFADEOUT);
        }

        // run enter same crossfade sur fmod ?
        private void EnterRunMusic()
        {
            _bossMusicStarted = false;
            ResetBossSpawnedFlag();

            runMusicInstance.getPlaybackState(out var state);
            if (state == STOPPED || state == STOPPING)
            {
                runMusicInstance.start();
                _runVolumeCurrent = 0f;
                runMusicInstance.setVolume(0f);
            }

            _runVolumeTarget = 1f;
            _bossVolumeTarget = 0f;
        }

        // crossfade a passer sur fmod ?
        private void ProcessBossMusic()
        {
            if (_bossMusicStarted)
                return;

            if (
                GameManager.Instance == null
                || GameManager.Instance.GetGameState() != EGameState.Running
            )
                return;

            if (
                !_soundQuery.TryGetSingleton<SoundPlayerTag>(out var tag)
                || !tag.HaveBossSpawnedSound
            )
                return;

            _bossMusicStarted = true;

            bossMusicInstance.getPlaybackState(out var state);
            if (state == STOPPED || state == STOPPING)
            {
                bossMusicInstance.start();
                _bossVolumeCurrent = 0f;
                bossMusicInstance.setVolume(0f);
            }

            _bossVolumeTarget = 1f;
            _runVolumeTarget = 0f;
        }

        private void StopRunAndBossMusic()
        {
            _runVolumeTarget = 0f;
            _bossVolumeTarget = 0f;
            _bossMusicStarted = false;
        }

        private void ResetBossSpawnedFlag()
        {
            if (
                _soundQuery.TryGetSingleton<SoundPlayerTag>(out var tag) && tag.HaveBossSpawnedSound
            )
            {
                tag.HaveBossSpawnedSound = false;
                _soundQuery.SetSingleton(tag);
            }
        }

        private void ProcessGemsCollection()
        {
            //caca ?
            if (!_soundQuery.TryGetSingleton<SoundPlayerTag>(out var soundPlayerTag))
                return;

            for (var i = 0; i < soundPlayerTag.GemsCollectedSound; i++)
                RuntimeManager.PlayOneShot(gemCollectedSound);

            if (soundPlayerTag.GemsCollectedSound > 0)
            {
                soundPlayerTag.GemsCollectedSound = 0;
                _soundQuery.SetSingleton(soundPlayerTag);
            }
        }

        private void ProcessEnemiesDamagesSfx()
        {
            //caca ?
            if (!_soundQuery.TryGetSingleton<SoundPlayerTag>(out var soundPlayerTag))
                return;

            for (var i = 0; i < soundPlayerTag.EnemiesTookDamageSound; i++)
                RuntimeManager.PlayOneShot(enemiesDamagesSfx);

            if (soundPlayerTag.EnemiesTookDamageSound > 0)
            {
                soundPlayerTag.EnemiesTookDamageSound = 0;
                _soundQuery.SetSingleton(soundPlayerTag);
            }
        }

        private void ProcessPlayerDamagesSfx()
        {
            //caca ?
            if (!_soundQuery.TryGetSingleton<SoundPlayerTag>(out var soundPlayerTag))
                return;

            for (var i = 0; i < soundPlayerTag.PlayerTookDamageSound; i++)
                RuntimeManager.PlayOneShot(playerDamagesSfx);

            if (soundPlayerTag.PlayerTookDamageSound > 0)
            {
                soundPlayerTag.PlayerTookDamageSound = 0;
                _soundQuery.SetSingleton(soundPlayerTag);
            }
        }

        private void ProcessUiSelectSfx()
        {
            RuntimeManager.PlayOneShot(uiSfxSelect);
        }

        private void ProcessUiConfirmSfx()
        {
            RuntimeManager.PlayOneShot(uiSfxConfirm);
        }

        private void ProcessUiBackSfx()
        {
            RuntimeManager.PlayOneShot(uiSfxBack);
        }
    }
}
