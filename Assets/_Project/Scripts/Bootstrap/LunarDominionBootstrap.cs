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
    /// its Canvas Scaler handles 1080x1920 portrait scaling directly.</summary>
    public class LunarDominionBootstrap : MonoBehaviour
    {
        private GameController _controller;
        private TopBarView _topBar;
        private BaseView _baseView;
        private BuildingPanelView _buildingPanel;
        private ArmyView _armyView;
        private ExpeditionsView _expeditionsView;
        private BottomNavView _bottomNav;
        private ResearchView _researchView;
        private DevHUDView _devHud;

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
            _baseView.Build(_controller, canvas);
            _baseView.OnBuildingSelected += OnBuildingSelected;

            _buildingPanel = new GameObject("BuildingPanelView").AddComponent<BuildingPanelView>();
            _buildingPanel.Build(canvas, _controller);

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
        }

        private void OnBuildingSelected(Core.BuildingDefinition def)
        {
            _researchView.Hide();
            _baseView.Highlight(def.Id, _controller.State.GetLevel(def.Id) == 0);
            _buildingPanel.Show(def);
        }

        private void OnTabSelected(NavTab tab)
        {
            _buildingPanel.Hide();
            _armyView.Hide();
            _expeditionsView.Hide();
            _researchView.Hide();
            switch (tab)
            {
                case NavTab.Base:
                    _baseView.SetVisible(true);
                    break;
                case NavTab.Army:
                    _baseView.SetVisible(false);
                    _armyView.Show();
                    break;
                case NavTab.Expeditions:
                    _baseView.SetVisible(false);
                    _expeditionsView.Show();
                    break;
                case NavTab.Research:
                    _baseView.SetVisible(false);
                    _researchView.Show();
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
