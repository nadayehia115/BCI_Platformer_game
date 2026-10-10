using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Central audio: music + UI sound effects, volumes and mute state saved in PlayerPrefs.
// Muting never touches the stored volumes, so unmuting restores the previous levels.
public class AudioManager : MonoBehaviour
{
    const string MusicKey = "audio.music";
    const string SfxKey = "audio.sfx";
    const string MutedKey = "audio.muted";

    public static AudioManager Instance { get; private set; }

    [Header("Sources")]
    [SerializeField] AudioSource musicSource;
    [SerializeField] AudioSource sfxSource;

    [Header("Clips")]
    [SerializeField] AudioClip menuMusic;
    public AudioClip hoverClip;
    public AudioClip clickClip;
    public AudioClip backClip;

    [Header("Behaviour")]
    [Tooltip("Music stops when a scene other than this one loads (leave blank to keep playing everywhere).")]
    [SerializeField] string menuSceneName = "MainMenu";

    public event Action Changed;

    public float MusicVolume { get; private set; }
    public float SfxVolume { get; private set; }
    public bool Muted { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        MusicVolume = PlayerPrefs.GetFloat(MusicKey, 0.6f);
        SfxVolume = PlayerPrefs.GetFloat(SfxKey, 0.8f);
        Muted = PlayerPrefs.GetInt(MutedKey, 0) == 1;

        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    void Start()
    {
        if (Instance != this) return;
        Apply();
        if (menuMusic != null && musicSource != null)
        {
            musicSource.clip = menuMusic;
            musicSource.loop = true;
            musicSource.Play();
        }
    }

    void OnDestroy()
    {
        if (Instance == this) SceneManager.activeSceneChanged -= OnSceneChanged;
    }

    void OnSceneChanged(Scene from, Scene to)
    {
        if (musicSource == null || string.IsNullOrEmpty(menuSceneName)) return;
        if (to.name != menuSceneName) musicSource.Stop();
        else if (!musicSource.isPlaying && menuMusic != null) musicSource.Play();
    }

    public void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MusicKey, MusicVolume);
        Apply();
    }

    public void SetSfxVolume(float value)
    {
        SfxVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(SfxKey, SfxVolume);
        Apply();
    }

    public void SetMuted(bool muted)
    {
        Muted = muted;
        PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
        Apply();
    }

    public void ToggleMute() => SetMuted(!Muted);

    void Apply()
    {
        if (musicSource != null) musicSource.volume = Muted ? 0f : MusicVolume;
        if (sfxSource != null) sfxSource.volume = Muted ? 0f : SfxVolume;
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public void PlaySfx(AudioClip clip)
    {
        if (clip != null && sfxSource != null && !Muted) sfxSource.PlayOneShot(clip);
    }

    public void PlayHover() => PlaySfx(hoverClip);
    public void PlayClick() => PlaySfx(clickClip);
    public void PlayBack() => PlaySfx(backClip);
}
