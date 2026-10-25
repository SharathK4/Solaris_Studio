using UnityEngine;
using UnityEngine.InputSystem;

namespace Solaris
{
    public enum ScreenId
    {
        Title,
        Welcome,
        SongSelect,
        ChordTutorial,
        ControlsTutorial,
        Instruments,
        Stage,
    }

    /// <summary>
    /// Base for the seven full-screen experiences. A screen builds its own UI under <see cref="Root"/>,
    /// animates in on <see cref="Enter"/>, and is torn down by the router after it fades out.
    /// </summary>
    public abstract class AppScreen : MonoBehaviour
    {
        protected App App { get; private set; }
        public RectTransform Root { get; private set; }
        public CanvasGroup Group { get; private set; }
        public abstract ScreenId Id { get; }

        /// <summary>Grain strength for this screen (0 = none, ~0.14 = heavy film).</summary>
        public virtual float Grain => 0.07f;

        /// <summary>Additive light grain for dark screens (where the multiply grain can't show).</summary>
        public virtual float GrainLift => 0f;

        /// <summary>Which vision features this screen needs running.</summary>
        public virtual VisionNeeds Vision => VisionNeeds.None;

        protected bool Leaving { get; private set; }
        protected float Age { get; private set; }

        internal void Init(App app)
        {
            App = app;
            Root = (RectTransform)transform;
            Group = GetComponent<CanvasGroup>();
            Build();
        }

        protected abstract void Build();

        /// <summary>Called once the screen is visible; stagger reveal animations here.</summary>
        public virtual void Enter() { }

        public virtual void Exit() => Leaving = true;

        protected virtual void Tick(float dt) { }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            Age += dt;
            Tick(dt);
        }

        protected static bool Pressed(Key key) =>
            !AppArgs.Automation && Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;

        protected static bool Clicked() =>
            !AppArgs.Automation && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        protected static bool ConfirmPressed() =>
            Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space);

        protected void Go(ScreenId next)
        {
            if (Leaving) return;
            App.Router.Go(next);
        }
    }

    [System.Flags]
    public enum VisionNeeds
    {
        None = 0,
        Camera = 1,
        Hands = 2 | Camera,
        Face = 4 | Camera,
        Segmentation = 8 | Camera,
        All = Hands | Face | Segmentation,
    }
}
