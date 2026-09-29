using UnityEngine;
using UntitledGame.Environment;

namespace UntitledGame.Core
{
    /// <summary>Loads the save, restores time of day, autosaves, and sets frame pacing.</summary>
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private DayNightCycle dayNight;
        [SerializeField] private float autosaveSeconds = 45f;

        private float _nextSave;

        private void Awake()
        {
            SaveSystem.Load();
            // A cozy game doesn't need 144 fps; leaving CPU/GPU headroom keeps Mei's AI snappy.
            double hz = Screen.currentResolution.refreshRateRatio.value;
            QualitySettings.vSyncCount = hz > 100 ? 2 : 1;
            Application.targetFrameRate = 60;
        }

        private void Start()
        {
            if (dayNight == null) dayNight = DayNightCycle.Instance;
            if (dayNight != null)
            {
                var data = SaveSystem.Data;
                dayNight.TimeOfDay = data.timeOfDay;
                dayNight.Day = Mathf.Max(1, data.day);
                dayNight.RealSecondsPerHour = SaveSystem.Settings.realSecondsPerHour;
            }
            _nextSave = Time.time + autosaveSeconds;
        }

        private void Update()
        {
            if (Time.time < _nextSave) return;
            _nextSave = Time.time + autosaveSeconds;
            Persist();
        }

        private void Persist()
        {
            if (dayNight != null)
            {
                SaveSystem.Data.timeOfDay = dayNight.TimeOfDay;
                SaveSystem.Data.day = dayNight.Day;
            }
            SaveSystem.Save();
        }

        private void OnApplicationQuit() => Persist();
    }
}
