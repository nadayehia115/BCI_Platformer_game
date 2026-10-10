using UnityEngine;
using UnityEngine.UI;

// Audio controls: speaker (mute) button, music and effects volume sliders. State lives in AudioManager.
public class SettingsMenu : MonoBehaviour
{
    [SerializeField] Slider musicSlider;
    [SerializeField] Slider sfxSlider;
    [SerializeField] Button muteButton;
    [SerializeField] Image speakerIcon;
    [SerializeField] Sprite speakerOn;
    [SerializeField] Sprite speakerOff;

    AudioManager audioManager;
    bool refreshing;

    void Start()
    {
        audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            Debug.LogWarning("SettingsMenu: no AudioManager in the scene.");
            enabled = false;
            return;
        }

        musicSlider.onValueChanged.AddListener(OnMusicChanged);
        sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        muteButton.onClick.AddListener(audioManager.ToggleMute);
        audioManager.Changed += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        if (audioManager != null) audioManager.Changed -= Refresh;
    }

    void OnMusicChanged(float v)
    {
        if (refreshing) return;
        if (audioManager.Muted) audioManager.SetMuted(false);   // touching a slider unmutes
        audioManager.SetMusicVolume(v);
    }

    void OnSfxChanged(float v)
    {
        if (refreshing) return;
        if (audioManager.Muted) audioManager.SetMuted(false);
        audioManager.SetSfxVolume(v);
        audioManager.PlayHover();                                // audible preview
    }

    void Refresh()
    {
        refreshing = true;
        musicSlider.SetValueWithoutNotify(audioManager.MusicVolume);   // sliders keep the stored levels while muted
        sfxSlider.SetValueWithoutNotify(audioManager.SfxVolume);
        refreshing = false;
        speakerIcon.sprite = audioManager.Muted ? speakerOff : speakerOn;
    }
}
