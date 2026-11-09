using LunarDominion.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LunarDominion.Bootstrap
{
    /// <summary>Single entry point for a blank scene. Add this component to an
    /// empty GameObject, press Play, and the whole Phase 2–4 interface is
    /// built at runtime — no prefabs or scene assets required.
    /// Unity UI (uGUI) was chosen over UI Toolkit: it is the proven choice
    /// for in-game mobile HUDs with dynamic, per-frame resource text, and
    /// its Canvas Scaler handles 1920x1080 landscape scaling directly.</summary>
    public class LunarDominionBootstrap : MonoBehaviour
    {
        private GameController _controller;
        private TopBarView _topBar;
        private BaseView _baseView;
        private ExteriorView _exteriorView;
        private MapInfoView _mapInfoView;
        private BuildingPanelView _buildingPanel;
        private ArmyView _armyView;
        private ExpeditionsView _expeditionsView;
        private BottomNavView _bottomNav;
        private ResearchView _researchView;
        private DevHUDView _devHud;
        private SplashView _splashView;

        private void Awake()
        {
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                // StandaloneInputModule uses the legacy Input API, so it
                // also breaks under Input System Package only. Use the
                // Input System UI module when that system is enabled.
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }

            var canvas = UIHelpers.CreateCanvas("MainCanvas");

            var controllerGo = new GameObject("GameController");
            _controller = controllerGo.AddComponent<GameController>();

            _topBar = new GameObject("TopBarView").AddComponent<TopBarView>();
            _topBar.Build(canvas, _controller);

            _baseView = new GameObject("BaseView").AddComponent<BaseView>();
            _baseView.Build(_controller);
            _baseView.OnBuildingSelected += OnBuildingSelected;

            // Exterior map view (Phase 1): the big terrain + camera
            // switch. Built after the base view so its terrain sits
            // under the base ground; starts hidden (Base is default).
            _exteriorView = new GameObject("ExteriorView").AddComponent<ExteriorView>();
            _exteriorView.Build();
            _exteriorView.OnMapPointSelected += OnMapPointSelected;
            _exteriorView.OnEmptyMapTapped += OnEmptyMapTapped;

            _mapInfoView = new GameObject("MapInfoView").AddComponent<MapInfoView>();
            _mapInfoView.Build(canvas);

            _buildingPanel = new GameObject("BuildingPanelView").AddComponent<BuildingPanelView>();
            _buildingPanel.Build(canvas, _controller);
            _baseView.OnEmptyBaseTapped += OnEmptyBaseTapped;

            _armyView = new GameObject("ArmyView").AddComponent<ArmyView>();
            _armyView.Build(canvas, _controller);

            _expeditionsView = new GameObject("ExpeditionsView").AddComponent<ExpeditionsView>();
            _expeditionsView.Build(canvas, _controller);

            _researchView = new GameObject("ResearchView").AddComponent<ResearchView>();
            _researchView.Build(canvas, _controller);

            _bottomNav = new GameObject("BottomNavView").AddComponent<BottomNavView>();
            _bottomNav.Build(canvas);
            _bottomNav.OnTabSelected += OnTabSelected;

            _devHud = new GameObject("DevHUDView").AddComponent<DevHUDView>();
            _devHud.Build(canvas, _controller);

            _controller.OnStateChanged += RefreshAll;
            RefreshAll();

            // In-game splash: artwork holds ~2s over the finished base,
            // then fades out. Tap anywhere to skip. Built last so it
            // covers everything, on its own canvas above MainCanvas.
            _splashView = new GameObject("SplashView").AddComponent<SplashView>();
            _splashView.Build();
        }

        private void OnBuildingSelected(Core.BuildingDefinition def)
        {
            _researchView.Hide();
            _baseView.Highlight(def.Id, _controller.State.GetLevel(def.Id) == 0);
            _buildingPanel.Show(def);
        }

        // Tap on empty base ground (not a building) hides any building
        // panel that is showing. Hide() is a no-op when already hidden.
        private void OnEmptyBaseTapped()
        {
            _buildingPanel.Hide();
        }

        // Tap on a map node or camp shows its info panel.
        private void OnMapPointSelected(string title, string info)
        {
            _mapInfoView.Show(title, info);
        }

        // Tap on empty map ground hides the info panel.
        private void OnEmptyMapTapped()
        {
            _mapInfoView.Hide();
        }

        private void OnTabSelected(NavTab tab)
        {
            _buildingPanel.Hide();
            _mapInfoView.Hide();
            _armyView.Hide();
            _expeditionsView.Hide();
            _researchView.Hide();
            switch (tab)
            {
                case NavTab.Base:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(true);
                    break;
                case NavTab.Army:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(false);
                    _armyView.Show();
                    break;
                case NavTab.Expeditions:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(false);
                    _expeditionsView.Show();
                    break;
                case NavTab.Research:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(false);
                    _researchView.Show();
                    break;
                case NavTab.Map:
                    // The detailed buildings are HIDDEN in the Map view
                    // — at 5x distance they are tiny, and the simplified
                    // BaseMarker (shown by ExteriorView) represents the
                    // base instead. The wall/roads/props stay visible
                    // (tiny from far, occluded by the marker's bold
                    // wall); the ground, mountains and planet stay.
                    _baseView.SetVisible(false);
                    _exteriorView.SetVisible(true);
                    break;
            }
        }

        private void RefreshAll()
        {
            _topBar?.Refresh();
            _buildingPanel?.Refresh();
            _armyView?.Refresh();
            _expeditionsView?.Refresh();
            _researchView?.Refresh();
        }
    }
}
