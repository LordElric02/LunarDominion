using System.Collections.Generic;
using LunarDominion.Audio;
using LunarDominion.Core;
using LunarDominion.Expeditions;
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
        private ArmyView _armyView;
        private BossLairView _lairView;
        private MusicDirector _music;
        private MapExcursionService _excursions;
        private MapPointData _selectedPoint;
        private BuildingPanelView _buildingPanel;
        private HeroesView _heroesView;
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
            _exteriorView.OnBackToBaseClicked += () => OnTabSelected(NavTab.Base);
            // Map search (Phase 3, 2026-11-12): the FIND bar lives
            // on the shared canvas; the view shows/hides it with
            // the Map tab.
            _exteriorView.BuildSearchUi(canvas);

            _mapInfoView = new GameObject("MapInfoView").AddComponent<MapInfoView>();
            _mapInfoView.Build(canvas);

            // Army tab (2026-11-12, standing armies): build and
            // edit the standing armies. The excursion service is
            // created further down, so the out-check is a lazy
            // lambda read at refresh time.
            _armyView = new GameObject("ArmyView").AddComponent<ArmyView>();
            _armyView.Build(canvas, _controller);
            _armyView.IsArmyOut = army => _excursions != null && _excursions.IsArmyOut(army);

            // Boss lair interior (2026-11-10): a full-screen view inside
            // the War Robot's lair, separate from the exterior map.
            // Tapping the lair on the map opens this view (the map march
            // is not used for bosses); the fight happens here, with the
            // player's heroes. Hidden until a lair is tapped.
            _lairView = new GameObject("BossLairView").AddComponent<BossLairView>();
            _lairView.Build(canvas, _controller);
            _lairView.OnFightComplete += OnLairFightComplete;
            _lairView.OnCloseClicked += OnLairCloseClicked;

            // Map excursions (2026-11-09): the rules for sending the
            // army from the map. Built after the controller, exterior
            // view, and info panel exist.
            _excursions = new MapExcursionService(_controller.State, _controller.Economy, _controller.Expeditions,
                _controller.Balance, _controller.UnitDefinitions, _controller.EncounterDefinitions,
                _exteriorView, () => _controller.SaveGame(), () => _controller.NotifyChanged());
            _excursions.OnExcursionResult += OnExcursionResult;
            _excursions.OnExcursionStateChanged += OnExcursionStateChanged;
            _mapInfoView.OnArmySendClicked += OnArmySendClicked;

            _buildingPanel = new GameObject("BuildingPanelView").AddComponent<BuildingPanelView>();
            _buildingPanel.Build(canvas, _controller);
            _baseView.OnEmptyBaseTapped += OnEmptyBaseTapped;

            _heroesView = new GameObject("HeroesView").AddComponent<HeroesView>();
            _heroesView.Build(canvas, _controller);

            _researchView = new GameObject("ResearchView").AddComponent<ResearchView>();
            _researchView.Build(canvas, _controller);

            _bottomNav = new GameObject("BottomNavView").AddComponent<BottomNavView>();
            _bottomNav.Build(canvas);
            _bottomNav.OnTabSelected += OnTabSelected;

            _devHud = new GameObject("DevHUDView").AddComponent<DevHUDView>();
            _devHud.Build(canvas, _controller);

            // Background music (2026-11-13): one director, three loops.
            // Starts on the Base theme under the splash; tab switches
            // and the lair hooks below crossfade it from there.
            _music = new GameObject("MusicDirector").AddComponent<MusicDirector>();
            _music.PlayBase();
            // Sound effects (C5): created once here; the boss
            // fight reaches it through SfxDirector.Instance.
            new GameObject("SfxDirector").AddComponent<SfxDirector>();

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
        // Excursion button when the point can take the army. A BossLair
        // tap opens the lair interior view instead (2026-11-10): the
        // boss fight happens inside the lair, with heroes, not a map
        // march — so the info panel is not shown for the lair.
        private void OnMapPointSelected(MapPointData point)
        {
            _selectedPoint = point;
            if (point != null && point.Kind == MapPointKind.BossLair)
            {
                OpenLairView();
                return;
            }
            ShowMapPoint(point);
        }

        // Open the lair interior view (full-screen, inside the lair).
        // The map hides underneath; closing returns to the Map. Clear
        // the selected point so nothing refreshes the map info panel
        // over the lair view while it is open (the lair shows its own
        // result; the map refreshes on close). The boss never vanishes
        // now (2026-11-10 ladder), so the lair always opens.
        private void OpenLairView()
        {
            _mapInfoView.Hide();
            _selectedPoint = null;
            _exteriorView.SetVisible(false);
            _lairView.Show();
            _music?.PlayLair();
        }

        // A boss fight finished in the lair view. The boss never
        // vanishes now (2026-11-10 ladder) — the lair view already
        // unlocked the next level on a victory, and the player taps
        // Back to Map to return. Just refresh the map.
        private void OnLairFightComplete(BattleResult result)
        {
            RefreshMapNodeStates();
        }

        // Back to Map tapped in the lair view: hide the lair, re-show
        // the exterior map (which restores the map camera).
        private void OnLairCloseClicked()
        {
            _lairView.Hide();
            _mapInfoView.Hide();
            _selectedPoint = null;
            _exteriorView.SetVisible(true);
            RefreshMapNodeStates();
            _music?.PlayExterior();
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
            bool sendable = false;
            var armyCanSend = new bool[GameState.MaxArmies];
            if (point.Kind != MapPointKind.Camp && point.Kind != MapPointKind.BossLair && _excursions.IsNodeOccupied(point.Index))
            {
                if (_excursions.OccupationInfo(point.Index, out int army, out int troops, out var remaining))
                {
                    info = $"{point.Info}\nBeing farmed by Army {army} — {troops} troop{(troops == 1 ? "" : "s")} — {MapExcursionService.FormatRemaining(remaining)} left.";
                }
                else
                {
                    info = $"{point.Info}\nBeing farmed.";
                }
            }
            else if (point.Kind != MapPointKind.Camp && point.Kind != MapPointKind.BossLair && _excursions.IsNodeDepleted(point.Index, out var remaining))
            {
                info = $"{point.Info}\nDepleted — regenerates in {MapExcursionService.FormatRemaining(remaining)}.";
            }
            else if (_excursions.CanSendToPoint(point, out var targetReason))
            {
                // Standing armies (2026-11-12): one send button
                // per army; each army's own blocker (locked, out,
                // empty, all injured) is listed under the info.
                sendable = true;
                for (int army = 0; army < GameState.MaxArmies; army++)
                {
                    armyCanSend[army] = _excursions.CanSendArmy(point, army, out var armyReason);
                    if (!armyCanSend[army] && armyReason != "Ready to send.")
                    {
                        info = $"{info}\n{armyReason}";
                    }
                }
            }
            else
            {
                info = $"{point.Info}\n{targetReason}";
            }
            // A camp above the player's Nexus is sendable (the fight
            // proceeds and likely fails) — show the too-strong warning
            // under the info instead of the old tier-lock message.
            string warning = _excursions.WarningFor(point);
            if (!string.IsNullOrEmpty(warning))
            {
                info = $"{info}\n{warning}";
            }
            _mapInfoView.Show(point.Title, info);
            if (sendable)
            {
                for (int army = 0; army < GameState.MaxArmies; army++)
                {
                    _mapInfoView.SetArmyButton(army, $"Army {army + 1}", true, armyCanSend[army]);
                }
            }
        }

        // Tap on empty map ground hides the info panel.
        private void OnEmptyMapTapped()
        {
            _selectedPoint = null;
            _mapInfoView.Hide();
        }

        // An army button tapped in the info panel (standing
        // armies, 2026-11-12): send that standing army as built
        // in the Army tab — no squad picking at send time.
        private void OnArmySendClicked(int army)
        {
            if (_selectedPoint == null) return;
            var point = _selectedPoint;
            // The point's info panel goes away as the army heads
            // out (Patrick 2026-11-12) — hidden BEFORE the start
            // call so a failure result can still pop over it.
            _mapInfoView.Hide();
            _selectedPoint = null;
            _excursions.StartExcursion(point, army);
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
        // reading; tapping the point again refreshes it. And never pop
        // the map info panel over the lair view while it is open: a boss
        // fight finishing triggers this, and the lair shows its own
        // result — the map refreshes when the lair closes.
        private void OnExcursionStateChanged()
        {
            if (_lairView != null && _lairView.IsVisible) return;
            if (_mapInfoView.ShowingResult) return;
            // Refresh ONLY a panel that is actually on screen
            // (Patrick 2026-11-12): this used to resurrect the
            // panel after it had been dismissed — on send, and
            // again when the army got home — popping up a camp
            // the player never tapped.
            if (!_mapInfoView.IsShowing) return;
            if (_selectedPoint != null) ShowMapPoint(_selectedPoint);
        }

        // Node visuals, three states (2026-11-09): occupied nodes are
        // amber and visible; depleted nodes vanish until they regenerate;
        // available nodes are normal and visible. Also restores the
        // marker at an occupied node (the army farming there). Called on
        // Map open, on excursion results, and on state changes while the
        // Map is visible (so a node regenerating or an occupation ending
        // is noticed). Also re-applies camp positions/visibility
        // (2026-11-09): the view's SetVisible re-shows every point, so
        // a defeated camp must be re-hidden and a respawned camp
        // re-placed at its saved spot.
        private void RefreshMapNodeStates()
        {
            if (_exteriorView == null || _excursions == null) return;
            _excursions.SyncCamps();
            _excursions.SyncBoss();
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
            _armyView.Hide();
            _lairView?.Hide();
            _heroesView.Hide();
            _researchView.Hide();
            switch (tab)
            {
                case NavTab.Base:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(true);
                    _baseView.SetMountainsVisible(true);
                    break;
                case NavTab.Heroes:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(false);
                    _baseView.SetMountainsVisible(true);
                    _heroesView.Show();
                    break;
                case NavTab.Army:
                    _exteriorView.SetVisible(false);
                    _baseView.SetVisible(false);
                    _baseView.SetMountainsVisible(true);
                    _armyView.Show();
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
            // Music follows the area: the Map gets the Exterior theme;
            // every management tab keeps the Base theme. (Entering the
            // lair overrides this via OpenLairView.)
            if (tab == NavTab.Map) _music?.PlayExterior();
            else _music?.PlayBase();
        }

        private void RefreshAll()
        {
            _topBar?.Refresh();
            _buildingPanel?.Refresh();
            _heroesView?.Refresh();
            _armyView?.Refresh();
            _researchView?.Refresh();
            // An occupation may have ended (its timer is real-time, and
            // it can end while the app was closed — the harvest is
            // granted on the next refresh). A node may also have
            // regenerated while the Map is open. A defeated camp may
            // also have respawned (its timer is real-time too).
            _excursions?.CheckOccupationDue();
            _excursions?.CheckCampRespawns();
            _excursions?.CheckBossRespawn();
            if (_exteriorView != null && _exteriorView.IsVisible) RefreshMapNodeStates();
        }
    }
}
