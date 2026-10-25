using System;
using System.Collections.Generic;
using UnityEngine;

namespace Solaris
{
    /// <summary>Owns the current screen and cross-fades between screens.</summary>
    public sealed class ScreenRouter
    {
        static readonly Dictionary<ScreenId, Type> k_Types = new Dictionary<ScreenId, Type>
        {
            { ScreenId.Title, typeof(Screens.TitleScreen) },
            { ScreenId.Welcome, typeof(Screens.WelcomeScreen) },
            { ScreenId.SongSelect, typeof(Screens.SongSelectScreen) },
            { ScreenId.ChordTutorial, typeof(Screens.ChordTutorialScreen) },
            { ScreenId.ControlsTutorial, typeof(Screens.ControlsTutorialScreen) },
            { ScreenId.Instruments, typeof(Screens.InstrumentScreen) },
            { ScreenId.Stage, typeof(Screens.StageScreen) },
        };

        readonly App m_App;
        readonly RectTransform m_Host;
        public AppScreen Current { get; private set; }
        public event Action<AppScreen> Changed;

        public ScreenRouter(App app, RectTransform host)
        {
            m_App = app;
            m_Host = host;
        }

        public void Go(ScreenId id, float crossfade = 1.0f)
        {
            var go = new GameObject(id.ToString(), typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(m_Host, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var next = (AppScreen)go.AddComponent(k_Types[id]);
            next.Init(m_App);
            m_App.ApplyScreenNeeds(next);

            var previous = Current;
            Current = next;
            next.Group.alpha = 0f;

            if (previous != null)
            {
                previous.Exit();
                previous.Group.interactable = false;
                previous.Group.blocksRaycasts = false;
                Anim.Fade(previous.Group, 0f, crossfade * 0.85f, 0f, Ease.InOutSine,
                    () => { if (previous) UnityEngine.Object.Destroy(previous.gameObject); });
            }

            Anim.Fade(next.Group, 1f, crossfade, previous != null ? crossfade * 0.35f : 0f, Ease.InOutSine);
            next.Enter();
            Changed?.Invoke(next);
            Debug.Log($"[Solaris] Screen -> {id}");
        }

        public static bool TryParse(string value, out ScreenId id)
        {
            id = ScreenId.Title;
            if (string.IsNullOrEmpty(value)) return false;
            if (int.TryParse(value, out int n) && n >= 1 && n <= 7)
            {
                id = (ScreenId)(n - 1);
                return true;
            }
            return Enum.TryParse(value, true, out id);
        }
    }
}
