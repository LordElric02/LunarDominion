using System.Collections.Generic;
using LunarDominion.Expeditions;
using LunarDominion.UI;
using LunarDominion.Units;
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
        private SquadPickerView _squadPicker;
        private MapExcursionService _excursions;
        private MapPointData _selectedPoint;
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

            // Squad picker (2026-11-09): choosing which units to send.
            // Built with the map views; hidden until Send is tapped.
            _squadPicker = new GameObject("SquadPickerView").AddComponent<SquadPickerView>();
            _squadPicker.Build(canvas, _controller);
            _squadPicker.OnConfirmClicked += OnSquadPickerConfirm;
            _squadPicker.OnCancelClicked += OnSquadPickerCancel;

            // Map excursions (2026-11-09): the rules for sending the
            // army from the map. Built after the controller, exterior
            // view, and info panel exist.
            _excursions = new MapExcursionService(_controller.State, _controller.Economy, _controller.Expeditions,
                _controller.Balance, _controller.UnitDefinitions, _controller.EncounterDefinitions,
                _exteriorView, () => _controller.SaveGame(), () => _controller.NotifyChanged());
            _excursions.OnExcursionResult += OnExcursionResult;
            _excursions.OnExcursionStateChanged += OnExcursionStateChanged;
            _mapInfoView.OnSendClicked += OnSendExcursionClicked;

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

        // Tap on a map node or camp shows its info panel, with the Send
        // Excursion button when the point can take the army.
        private void OnMapPointSelected(MapPointData point)
        {
            _selectedPoint = point;
            ShowMapPoint(point);
        }

        // Show the selected point's info + Send button state. An occupied
        // node shows who is farming it and the time left, with no button;
        // a depleted node shows its regen countdown and no button; a point
        // that cannot be sent (no army, no energy, or a march/occupation
        // already underway) shows the reason instead of the button.
        private void ShowMapPoint(MapPointData point)
        {
            if (point == null) return;
            string info = point.Info;
            bool showSend = false;
            if (point.Kind != MapPointKind.Camp && _excursions.IsNodeOccupied(point.Index))
            {
                if (_excursions.OccupationInfo(point.Index, out int troops, out var remaining))
                {
                    info = $"{point.Info}\nBeing farmed by {troops} troop{(troops == 1 ? "" : "s")} — {MapExcursionService.FormatRemaining(remaining)} left.";
                }
                else
                {
                    info = $"{point.Info}\nBeing farmed.";
                }
            }
            else if (point.Kind != MapPointKind.Camp && _excursions.IsNodeDepleted(point.Index, out var remaining))
            {
                info = $"{point.Info}\nDepleted — regenerates in {MapExcursionService.FormatRemaining(remaining)}.";
            }
            else if (_excursions.CanSend(point, out var reason))
            {
                showSend = true;
            }
            else if (reason != "Ready to send.")
            {
                info = $"{point.Info}\n{reason}";
            }
            // A camp above the player's Nexus is sendable (the fight
            // proceeds and likely fails) — show the too-strong warning
            // under the info instead of the old tier-lock message.
            string warning = _excursions.WarningFor(point);
            if (!string.IsNullOrEmpty(warning))
            {
                info = $"{info}\n{warning}";
            }
            _mapInfoView.Show(point.Title, info, showSend);
            _mapInfoView.SetSendEnabled(!_excursions.IsMarching);
        }

        // Tap on empty map ground hides the info panel.
        private void OnEmptyMapTapped()
        {
            _selectedPoint = null;
            _mapInfoView.Hide();
        }

        // Send Excursion tapped in the info panel: show the squad
        // picker (pre-filled with the whole army) instead of sending
        // immediately, so the player chooses which units go.
        private void OnSendExcursionClicked()
        {
            if (_selectedPoint == null) return;
            _squadPicker.Show(_selectedPoint.Title, _excursions.DefaultSquadCounts());
        }

        // Squad picker confirmed: convert the chosen counts to army
        // indices (first N of each type, in army order — same as the
        // Expeditions menu) and send that squad.
        private void OnSquadPickerConfirm(Dictionary<UnitId, int> counts)
        {
            _squadPicker.Hide();
            if (_selectedPoint == null) return;
            var squad = SquadIndicesForCounts(counts);
            if (squad.Count == 0) return;
            _excursions.StartExcursion(_selectedPoint, squad);
        }

        private void OnSquadPickerCancel()
        {
            _squadPicker.Hide();
        }

        private List<int> SquadIndicesForCounts(Dictionary<UnitId, int> counts)
        {
            var remaining = new Dictionary<UnitId, int>(counts);
            var indices = new List<int>();
            if (_controller.State.Army == null) return indices;
            for (int i = 0; i < _controller.State.Army.Count; i++)
            {
                var unit = _controller.State.Army[i];
                if (unit == null) continue;
                if (remaining.ContainsKey(unit.Id) && remaining[unit.Id] > 0)
                {
                    remaining[unit.Id]--;
                    indices.Add(i);
                }
            }
            return indices;
        }

        // An excursion resolved (collection / battle) or could not start:
        // show the outcome in the info panel — but only while the Map is
        // open. An occupation can complete while the player is on another
        // tab; the harvest still lands (top bar updates) and the node is
        // gone when they next open the Map, without the panel popping up
        // over the Base view.
        private void OnExcursionResult(string title, string body)
        {
            if (_exteriorView != null && _exteriorView.IsVisible)
            {
                _mapInfoView.ShowResult(title, body);
            }
            RefreshMapNodeStates();
        }

        // Marching started, finished, or cancelled: refresh the panel's
        // Send button — but never overwrite a result the player is
        // reading; tapping the point again refreshes it.
        private void OnExcursionStateChanged()
        {
            if (_mapInfoView.ShowingResult) return;
            if (_selectedPoint != null) ShowMapPoint(_selectedPoint);
        }

        // Node visuals, three states (2026-11-09): occupied nodes are
        // amber and visible; depleted nodes vanish until they regenerate;
        // available nodes are normal and visible. Also restores the
        // marker at an occupied node (the army farming there). Called on
        // Map open, on excursion results, and on state changes while the
        // Map is visible (so a node regenerating or an occupation ending
        // is noticed).
        private void RefreshMapNodeStates()
        {
            if (_exteriorView == null || _excursions == null) return;
            for (int i = 0; i < 9; i++)
            {
                if (_excursions.IsNodeOccupied(i))
                {
                    _exteriorView.SetNodeVisible(i, true);
                    _exteriorView.SetNodeOccupied(i, true);
                }
                else if (_excursions.IsNodeDepleted(i))
                {
                    _exteriorView.SetNodeOccupied(i, false);
                    _exteriorView.SetNodeVisible(i, false);
                }
                else
                {
                    _exteriorView.SetNodeVisible(i, true);
                    _exteriorView.SetNodeOccupied(i, false);
                }
            }
            _excursions.RefreshMarkerVisual();
        }

        private void OnTabSelected(NavTab tab)
        {
            // Leaving the Map tab cancels an in-flight excursion before
            // any outcome is applied (energy is only spent at arrival).
            _excursions?.Cancel();
            _selectedPoint = null;
            _buildingPanel.Hide();
            _mapInfoView.Hide();
            _squadPicker.Hide();
            _armyView.Hide();
            _expeditionsView.Hide();
            _researchView.Hide();
            switch (tab)
            {
                case NavTab.Base:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(true);
                    _baseView.SetMountainsVisible(true);
                    break;
                case NavTab.Army:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(false);
                    _baseView.SetMountainsVisible(true);
                    _armyView.Show();
                    break;
                case NavTab.Expeditions:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(false);
                    _baseView.SetMountainsVisible(true);
                    _expeditionsView.Show();
                    break;
                case NavTab.Research:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(false);
                    _baseView.SetMountainsVisible(true);
                    _researchView.Show();
                    break;
                case NavTab.Map:
                    // The detailed buildings are HIDDEN in the Map view
                    // — at 5x distance they are tiny, and the sci-fi
                    // BaseCastle (shown by ExteriorView) represents the
                    // base instead. The horizon mountains are hidden
                    // too (Patrick 2026-11-09: they crowd the big map);
                    // the ground and planet stay.
                    _baseView.SetVisible(false);
                    _baseView.SetMountainsVisible(false);
                    _exteriorView.SetVisible(true);
                    RefreshMapNodeStates();
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
            // An occupation may have ended (its timer is real-time, and
            // it can end while the app was closed — the harvest is
            // granted on the next refresh). A node may also have
            // regenerated while the Map is open.
            _excursions?.CheckOccupationDue();
            if (_exteriorView != null && _exteriorView.IsVisible) RefreshMapNodeStates();
        }
    }
}
