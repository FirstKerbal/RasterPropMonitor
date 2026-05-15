/*****************************************************************************
 * RasterPropMonitor
 * =================
 * Plugin for Kerbal Space Program
 *
 *  by Mihara (Eugene Medvedev), MOARdV, and other contributors
 * 
 * RasterPropMonitor is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, revision
 * date 29 June 2007, or (at your option) any later version.
 * 
 * RasterPropMonitor is distributed in the hope that it will be useful, but
 * WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
 * or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
 * for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with RasterPropMonitor.  If not, see <http://www.gnu.org/licenses/>.
 ****************************************************************************/

using Expansions.Missions.Editor;
using KSPCommunityLib.Logging;
using MuMech;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace JSI
{
    /// <summary>
    /// MechJebRPMMenu provides a comprehensive text menu interface to MechJeb 2.15+
    /// matching full feature parity with MechJeb's IMGUI interface.
    /// </summary>
    public class MechJebRPM : InternalModule
    {
        #region Configuration Fields
        [KSPField]
        public string pageTitle = "%.%.%.%.%.%.%.%.%.%.%.%.%.%.%.%.% %.%.%.%.%.%.%.%.%.%.%.%.%.%.%.%.%%.%.%.%.%.%.%.%.%.%.MechJeb%.%.%.%.%.%.%.%.%.%.%.%.%.%.%.%.% %.%.%.%.%.%.%.%.%.%.%.%.%.%.%.%.%";

        [KSPField]
        public int buttonUp = 0;

        [KSPField]
        public int buttonDown = 1;

        [KSPField]
        public int buttonEnter = 2;

        [KSPField]
        public int buttonEsc = 3;

        [KSPField]
        public int buttonHome = 4;

        [KSPField]
        public int buttonRight = 5;

        [KSPField]
        public int buttonLeft = 6;
        #endregion

        #region Instance State
        private TextMenu topMenu;
        private TextMenu currentMenu;
        private bool pageActiveState = false;
        private MechJebCore mjCore = null;
        private MechJebModuleSmartASS mjSmartASS = null;
        private MechJebModuleDockingAutopilot mjDockingAutoPilot = null;
        private MechJebModuleRendezvousAutopilot mjRendezvousAutopilot = null;
        private MechJebModuleTranslatron mjTranslatron = null;
        private MechJebModuleSpaceplaneAutopilot mjSpacePlaneAutopilot = null;
        private MechJebModuleLandingPredictions mjLandingPredictions = null;
        private MechJebModuleLandingGuidance mjLandingGuidance = null;
        private MechJebModuleWarpController mjWarpController = null;
        private MechJebModuleStageStats mjStageStats = null;
        private MechJebModuleAirplaneGuidance mjAirplaneGuidance = null;
        private Vessel activeVessel = null;

        private TextMenu smartassOrbitalMenu;
        private TextMenu smartassSurfaceMenu;
        private TextMenu smartassTargetMenu;

        // LEGACY: Hohmann state - no longer used, wrapper uses MechJeb's OperationGeneric directly
        // Keeping for reference only - these fields are not used after wrapper conversion
        // private object genericTransferOperation;
        // private bool genericCapture = true;
        // private bool genericPlanCapture = true;
        // private bool genericRendezvous = true;
        // private bool genericCoplanar = false;
        // private double genericLagTime = 0.0;

        // LEGACY: advancedTransferOperation not used - wrapper uses MechJeb's static array
        // Display cache variables still needed for UI refresh
        private bool advancedTransferSelectLowestDV = true;  // UI state for radio button display
        private double advancedTransferDeltaV = 0.0;         // Cached for display
        private double advancedTransferDepartureUT = 0.0;    // Cached for display
        private double advancedTransferDuration = 0.0;       // Cached for display

        // Stage stats update timing
        private double lastStageStatsUpdateUT = 0.0;
        
        // Menu stacks for navigation
        private Stack<TextMenu> menuStack = new Stack<TextMenu>();
        
        #endregion

        #region Initialization
        public void Start()
        {
            MechJebProxy.Initialize();

            BuildMenus();
        }

        private void BuildMenus()
        {
            topMenu = new TextMenu();
            topMenu.labelColor = JUtil.ColorToColorTag(Color.white);
            topMenu.selectedColor = JUtil.ColorToColorTag(Color.green);
            topMenu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            // Add all main menu entries
            topMenu.AddMenuItem("Attitude Control (SmartASS)", () => PushMenu(BuildSmartASSMenu()));
            topMenu.AddMenuItem("Ascent Guidance", () => PushMenu(BuildAscentMenu()), IsAscentAvailable);
            topMenu.AddMenuItem("Landing Guidance", () => PushMenu(BuildLandingMenu()),
                () => vessel != null && !vessel.LandedOrSplashed);
            topMenu.AddMenuItem("Maneuver Planner", () => PushMenu(BuildManeuverPlannerMenu()));
            topMenu.AddMenuItem("Node Editor", () => PushMenu(BuildNodeEditorMenu()),
                () => vessel != null && vessel.patchedConicSolver != null && 
                        vessel.patchedConicSolver.maneuverNodes.Count > 0);
            topMenu.AddMenuItem("Execute Node", () => ExecuteNode(),
                () => vessel != null && vessel.patchedConicSolver != null && 
                        vessel.patchedConicSolver.maneuverNodes.Count > 0);
            topMenu.AddMenuItem("Rendezvous", () => PushMenu(BuildRendezvousMenu()),
                () => FlightGlobals.fetch.VesselTarget != null);
            topMenu.AddMenuItem("Docking Guidance", () => PushMenu(BuildDockingMenu()),
                () => FlightGlobals.fetch.VesselTarget != null);
            topMenu.AddMenuItem("Translatron", () => PushMenu(BuildTranslatronMenu()));
            topMenu.AddMenuItem("Rover Autopilot", () => PushMenu(BuildRoverMenu()),
                () => vessel != null && vessel.Landed);
            topMenu.AddMenuItem("Aircraft Autopilot", () => PushMenu(BuildAircraftMenu()),
                () => vessel != null && vessel.atmDensity > 0);
            topMenu.AddMenuItem("Spaceplane Guidance", () => PushMenu(BuildSpaceplaneMenu()),
                () => vessel != null && vessel.atmDensity > 0);
            topMenu.AddMenuItem("Utilities", () => PushMenu(BuildUtilitiesMenu()));
            topMenu.AddMenuItem("Info Display", () => PushMenu(BuildInfoMenu()));
            topMenu.AddMenuItem("Settings", () => PushMenu(BuildSettingsMenu()));

            currentMenu = topMenu;
        }

        #endregion

        #region SmartASS Menu
        private TextMenu BuildSmartASSMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddMenuItem("[MODE: ORBITAL]", () => PushMenu(BuildSmartASSOrbitalMenu()));
            menu.AddMenuItem("[MODE: SURFACE]", () => PushMenu(BuildSmartASSSurfaceMenu()));
            menu.AddMenuItem("[MODE: TARGET]", () => PushMenu(BuildSmartASSTargetMenu()),
                () => FlightGlobals.fetch.VesselTarget != null);
            menu.AddMenuItem("[MODE: ADVANCED]", () => PushMenu(BuildSmartASSAdvancedMenu()));
            menu.AddMenuItem("[MODE: AUTO]", () => SetSmartASSAuto());
            menu.AddMenuItem("------", null);
            menu.AddMenuItem("OFF", () => SetSmartASSTarget(MechJebModuleSmartASS.Target.OFF));
            menu.AddMenuItem("KILL ROTATION", () => SetSmartASSTarget(MechJebModuleSmartASS.Target.KILLROT));
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildSmartASSOrbitalMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            smartassOrbitalMenu = menu;

            menu.Add(new TextMenu.Item("PROGRADE", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.PROGRADE));
            menu.Add(new TextMenu.Item("RETROGRADE", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.RETROGRADE));
            menu.Add(new TextMenu.Item("NORMAL+", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.NORMAL_PLUS));
            menu.Add(new TextMenu.Item("NORMAL-", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.NORMAL_MINUS));
            menu.Add(new TextMenu.Item("RADIAL+", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.RADIAL_PLUS));
            menu.Add(new TextMenu.Item("RADIAL-", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.RADIAL_MINUS));
            menu.AddMenuItem("NODE", () => SetSmartASSTarget(MechJebModuleSmartASS.Target.NODE),
                () => vessel != null && vessel.patchedConicSolver != null && 
                        vessel.patchedConicSolver.maneuverNodes.Count > 0);
            menu.AddMenuItem("------", null);
            menu.AddToggleItem("Force Roll", mjSmartASS, MechJebProxy.f_SmartASS_ForceRol);
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildSmartASSSurfaceMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            smartassSurfaceMenu = menu;

            menu.Add(new TextMenu.Item("SURFACE PROGRADE", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.SURFACE_PROGRADE));
            menu.Add(new TextMenu.Item("SURFACE RETROGRADE", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.SURFACE_RETROGRADE));
            menu.Add(new TextMenu.Item("HORIZONTAL+", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.HORIZONTAL_PLUS));
            menu.Add(new TextMenu.Item("HORIZONTAL-", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.HORIZONTAL_MINUS));
            menu.Add(new TextMenu.Item("VERTICAL+", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.VERTICAL_PLUS));
            menu.Add(new TextMenu.Item("SURFACE", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.SURFACE));
            menu.AddMenuItem("------", null);
            menu.AddMJItem("Heading",
                mjSmartASS.srfHdg,
                1.0, v => v.ToString("F1") + "°", null, true, 0, true, 360);
            menu.AddMJItem("Pitch",
                mjSmartASS.srfPit,
                1.0, v => v.ToString("F1") + "°", null, true, -90, true, 90);
            menu.AddMJItem("Roll",
                mjSmartASS.srfRol,
                1.0, v => v.ToString("F1") + "°", null, true, 0, true, 360);
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildSmartASSTargetMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            smartassTargetMenu = menu;

            menu.Add(new TextMenu.Item("TARGET+", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.TARGET_PLUS));
            menu.Add(new TextMenu.Item("TARGET-", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.TARGET_MINUS));
            menu.Add(new TextMenu.Item("RELATIVE VEL+", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.RELATIVE_PLUS));
            menu.Add(new TextMenu.Item("RELATIVE VEL-", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.RELATIVE_MINUS));
            menu.Add(new TextMenu.Item("PARALLEL+", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.PARALLEL_PLUS));
            menu.Add(new TextMenu.Item("PARALLEL-", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.PARALLEL_MINUS));
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildSmartASSAdvancedMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.Add(new TextMenu.Item("Set ADVANCED Mode", SetSmartASSTarget, (int)MechJebModuleSmartASS.Target.ADVANCED));
            menu.AddMenuItem(() => "Reference: [" + mjSmartASS.advReference.ToString() + "]", () => CycleSmartASSAdvancedReference(1));
            menu.AddMenuItem(() => "Direction: [" + mjSmartASS.advDirection.ToString() + "]", () => CycleSmartASSAdvancedDirection(1));
            menu.AddToggleItem("Force Roll", mjSmartASS, MechJebProxy.f_SmartASS_ForceRol);
            menu.AddMJItem("Roll Angle",
                mjSmartASS.rol,
                1.0, v => v.ToString("F1") + "°", null, true, 0, true, 360);
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private void SetSmartASSAuto()
        {
            SetSmartASSTarget(MechJebModuleSmartASS.Target.AUTO);
        }

        private void SetSmartASSTarget(int target, TextMenu.Item item)
        {
            SetSmartASSTarget((MechJebModuleSmartASS.Target)target);
        }

        private void SetSmartASSTarget(MechJebModuleSmartASS.Target target)
        {
            if (mjSmartASS == null) return;
            mjSmartASS.target = target;
            mjSmartASS.Engage();
        }

        private void CycleSmartASSAdvancedReference(int direction)
        {
            if (mjSmartASS == null) return;
            var values = (AttitudeReference[])Enum.GetValues(typeof(AttitudeReference));
            int idx = Array.IndexOf(values, mjSmartASS.advReference);
            if (idx < 0) idx = 0;
            int next = (idx + direction + values.Length) % values.Length;
            mjSmartASS.advReference = values[next];
            mjSmartASS.Engage();
        }

        private void CycleSmartASSAdvancedDirection(int direction)
        {
            if (mjSmartASS == null) return;
            var values = (Vector6.Direction[])Enum.GetValues(typeof(Vector6.Direction));
            int idx = Array.IndexOf(values, mjSmartASS.advDirection);
            if (idx < 0) idx = 0;
            int next = (idx + direction + values.Length) % values.Length;
            mjSmartASS.advDirection = values[next];
            mjSmartASS.Engage();
        }
        #endregion

        #region Ascent Menu
        private TextMenu BuildAscentMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddToggleItem("ENGAGE Ascent Autopilot",
                () => mjCore.AscentSettings?.AscentAutopilot?.Enabled == true,
                (val) => MechJebProxy.SetAscentAutopilotEngaged(mjCore, val));

            menu.AddMenuItem("------", null);

            // Orbit parameters
            menu.AddMJItem("Target Altitude",
                mjCore.AscentSettings.DesiredOrbitAltitude,
                1000.0, v => (v / 1000.0).ToString("F1") + " km", null, true, 1000.0, false, 0);
            menu.AddMJItem("Target Inclination", mjCore.AscentSettings.DesiredInclination,
                0.5, v => v.ToString("F2") + "°", null, true, 0, true, 180);
            menu.AddMenuItem("Set to Current Inclination", () =>
            {
                if (mjCore.AscentSettings == null || vessel == null) return;
                mjCore.AscentSettings.DesiredInclination.Val = vessel.orbit.inclination;
            });

            menu.AddMenuItem("------", null);

            // Sub-menus
            menu.AddMenuItem("Path Editor", () => PushMenu(BuildAscentPathMenu()));
            menu.AddMenuItem("Staging & Thrust", () => PushMenu(BuildAscentStagingMenu()));
            menu.AddMenuItem("Launch Parameters", () => PushMenu(BuildAscentLaunchMenu()));
            menu.AddMenuItem("Guidance & Safety", () => PushMenu(BuildAscentGuidanceMenu()));

            menu.AddMenuItem("------", null);

            menu.AddToggleItem("Auto-Warp",
                () => mjCore.Node.Autowarp,
                (val) => mjCore.Node.Autowarp = val);

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildAscentPathMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddToggleItem("Automatic Altitude Turn", mjCore.AscentSettings, MechJebProxy.f_Ascent_AutoPath);

            menu.AddMJItem("Turn Start Alt", mjCore.AscentSettings.TurnStartAltitude,
                1000.0, v => (v / 1000.0).ToString("F1") + " km",
                () => !mjCore.AscentSettings.AutoPath);

            menu.AddMJItem("Turn Start Vel", mjCore.AscentSettings.TurnStartVelocity,
                10.0, v => v.ToString("F0") + " m/s",
                () => !mjCore.AscentSettings.AutoPath);

            menu.AddMJItem("Turn End Alt", mjCore.AscentSettings.TurnEndAltitude,
                1000.0, v => (v / 1000.0).ToString("F1") + " km",
                () => !mjCore.AscentSettings.AutoPath);

            menu.AddMJItem("Final Flight Path Angle", mjCore.AscentSettings.TurnEndAngle,
                0.5, v => v.ToString("F1") + "°");

            menu.AddMJItem("Turn Shape", mjCore.AscentSettings.TurnShapeExponent,
                0.01, v => (v * 100.0).ToString("F0") + "%");

            menu.AddNumericItem("Auto Turn %", 
                () => mjCore.AscentSettings.AutoTurnPerc * 100.0,
                (val) => mjCore.AscentSettings.AutoTurnPerc = (float)(val / 100.0),
                0.5, v => v.ToString("F1") + "%",
                () => mjCore.AscentSettings.AutoPath, true, 0.5, true, 105.0);

            menu.AddMJItem("Auto Turn Spd", mjCore.AscentSettings.AutoTurnSpdFactor,
                0.5, v => v.ToString("F1"),
                () => mjCore.AscentSettings.AutoPath, true, 4.0, true, 80.0);

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildAscentStagingMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddToggleItem("Autostage",
                () => mjCore.AscentSettings.Autostage,
                (val) => mjCore.AscentSettings.Autostage = val);
            menu.AddMJItem("Stop at Stage", mjCore.Staging.AutostageLimit,
                1.0, v => v.ToString("F0"), null, true, 0, false, 0);

            menu.AddMenuItem("------", null);

            menu.AddToggleItem("Limit to Prevent Overheats", mjCore.Thrust, MechJebProxy.f_Thrust_LimitToPreventOverheats);
            menu.AddToggleItem("Limit by Max Q", mjCore.Thrust, MechJebProxy.f_Thrust_LimitDynamicPressure);
            menu.AddMJItem("Max Q", mjCore.Thrust.MaxDynamicPressure,
                1000.0, v => v.ToString("F0") + " Pa", null, true, 0, false, 0);
            menu.AddToggleItem("Limit Acceleration", mjCore.Thrust, MechJebProxy.f_Thrust_LimitAcceleration);
            menu.AddMJItem("Max Acceleration", mjCore.Thrust.MaxAcceleration,
                0.1, v => v.ToString("F1") + " m/s²", null, true, 0, false, 0);
            menu.AddToggleItem("Limit Throttle", mjCore.Thrust, MechJebProxy.f_Thrust_LimitThrottle);
            menu.AddMJItem("Max Throttle", mjCore.Thrust.MaxThrottle,
                1.0, v => v.ToString("F0") + "%", null, true, 0, true, 100);

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildAscentLaunchMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddMJItem("Desired LAN", mjCore.AscentSettings.DesiredLan,
                0.5, v => v.ToString("F2") + "°", null, true, 0, true, 360);
            menu.AddMJItem("Launch Phase Angle", mjCore.AscentSettings.LaunchPhaseAngle,
                0.5, v => v.ToString("F2") + "°", null, true, -360, true, 360);
            menu.AddMJItem("Launch LAN Difference", mjCore.AscentSettings.LaunchLANDifference,
                0.5, v => v.ToString("F2") + "°", null, true, -360, true, 360);

            menu.AddMenuItem("------", null);

            menu.AddMJItem("Warp Countdown", mjCore.AscentSettings.WarpCountDown,
                1.0, v => v.ToString("F0") + " s", null, true, 0, false, 0);
            menu.AddToggleItem("Skip Circularization",
                () => mjCore.AscentSettings.SkipCircularization,
                (val) => mjCore.AscentSettings.SkipCircularization = val);

            menu.AddMenuItem("[BACK]", () => PopMenu());
            return menu;
        }

        private TextMenu BuildAscentGuidanceMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddToggleItem("Force Roll", mjCore.AscentSettings, MechJebProxy.f_Ascent_ForceRoll);
            menu.AddMJItem("Vertical Roll", mjCore.AscentSettings.VerticalRoll,
                1.0, v => v.ToString("F1") + "°", null, true, -180, true, 180);
            menu.AddMJItem("Turn Roll", mjCore.AscentSettings.TurnRoll,
                1.0, v => v.ToString("F1") + "°", null, true, -180, true, 180);
            menu.AddMJItem("Roll Altitude", mjCore.AscentSettings.RollAltitude,
                1.0, v => v.ToString("F1") + " km", null, true, 0, false, 0);

            menu.AddMenuItem("------", null);

            menu.AddToggleItem("Limit AoA", mjCore.AscentSettings, MechJebProxy.f_Ascent_LimitAoA);
            menu.AddMJItem("Max AoA", mjCore.AscentSettings.MaxAoA,
                0.5, v => v.ToString("F1") + "°", null, true, 0, true, 45);
            menu.AddMJItem("AoA Fadeout Pressure", mjCore.AscentSettings.AOALimitFadeoutPressure,
                100.0, v => v.ToString("F0") + " Pa", null, true, 0, false, 0);

            menu.AddMenuItem("------", null);

            menu.AddToggleItem("Corrective Steering", mjCore.AscentSettings, MechJebProxy.f_Ascent_CorrectiveSteering);
            menu.AddMJItem("Corrective Gain", mjCore.AscentSettings.CorrectiveSteeringGain,
                0.1, v => v.ToString("F2"));

            menu.AddMenuItem("[BACK]", () => PopMenu());
            return menu;
        }
        #endregion

        #region Landing Menu
        private TextMenu BuildLandingMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            // Actions
            menu.AddMenuItem("Land at Target", () => mjCore.Landing.LandAtPositionTarget(mjLandingGuidance),
                () => mjCore.Target.PositionTargetExists);
            menu.AddMenuItem("Land Somewhere", () => mjCore.Landing.LandUntargeted(mjLandingGuidance));
            menu.AddMenuItem("STOP", () => mjCore.Landing.StopLanding(),
                () => mjCore.Landing != null && mjCore.Landing.Enabled);

            menu.AddMenuItem("------", null);

            // Targeting
            menu.AddMenuItem("Pick Target on Map", () => mjCore.Target.PickPositionTargetOnMap());
            menu.AddNumericItem("Target Latitude",
                () => mjCore.Target.targetLatitude,
                (val) => mjCore.Target.SetPositionTarget(vessel?.mainBody, val, mjCore.Target.targetLongitude),
                0.1, v => v.ToString("F3") + "°", null, true, -90, true, 90);
            menu.AddNumericItem("Target Longitude",
                () => mjCore.Target.targetLongitude,
                (val) => mjCore.Target.SetPositionTarget(vessel?.mainBody, mjCore.Target.targetLatitude, val),
                0.1, v => v.ToString("F3") + "°", null, true, -180, true, 180);

            menu.AddMenuItem("------", null);

            // Settings
            menu.AddMJItem("Touchdown Speed", mjCore.Landing.TouchdownSpeed,
                0.5, v => v.ToString("F1") + " m/s", null, true, 0, false, 0);
            menu.AddToggleItem("Deploy Gear", mjCore.Landing, MechJebProxy.f_Landing_DeployGears);
            menu.AddToggleItem("Deploy Chutes", mjCore.Landing, MechJebProxy.f_Landing_DeployChutes);
            menu.AddMJItem("Limit Gear Stage", mjCore.Landing.LimitGearsStage,
                1.0, v => v.ToString("F0"), null, true, 0, false, 0);
            menu.AddMJItem("Limit Chute Stage", mjCore.Landing.LimitChutesStage,
                1.0, v => v.ToString("F0"), null, true, 0, false, 0);
            menu.AddToggleItem("Use RCS", mjCore.Landing, MechJebProxy.f_Landing_UseRCS);

            menu.AddMenuItem("------", null);

            // Predictions sub-menu
            menu.AddMenuItem("Predictions Info", () => PushMenu(BuildLandingPredictionsMenu()));

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildLandingPredictionsMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddToggleItem("Show Trajectory", mjLandingPredictions, MechJebProxy.f_Landing_ShowTrajectory);

            menu.AddMenuItem("------", null);

            // These are info items, will be updated dynamically
            menu.AddMenuItem("Predicted Landing:", null);
            menu.AddMenuItem(() => "  Lat: " + GetLandingPredLatitude());
            menu.AddMenuItem(() => "  Lon: " + GetLandingPredLongitude());
            menu.AddMenuItem(() => "  Time: " + GetLandingPredTime());
            menu.AddMenuItem(() => "  Max Gees: " + GetLandingPredGees());

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        #endregion

        #region Maneuver Planner Menu
        // Menu matching IMGUI Maneuver Planner exactly
        private TextMenu BuildManeuverPlannerMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            // Match exact IMGUI dropdown order (alphabetical)
            menu.AddMenuItem("advanced transfer to another planet", () => PushMenu(BuildAdvancedTransferMenu()),
                () => FlightGlobals.fetch.VesselTarget is CelestialBody);
            menu.AddMenuItem("change apoapsis", () => PushMenu(BuildOperationMenu(MechJebProxy.OpChangeApoapsis, PopulateChangeApoapsisMenu)));
            menu.AddMenuItem("change both Pe and Ap", () => PushMenu(BuildOperationMenu(MechJebProxy.OpEllipticize, PopulateEllipticizeMenu)));
            menu.AddMenuItem("change eccentricity", () => PushMenu(BuildOperationMenu(MechJebProxy.OpEccentricity, PopulateChangeEccentricityMenu)));
            menu.AddMenuItem("change inclination", () => PushMenu(BuildOperationMenu(MechJebProxy.OpChangeInclination, PopulateChangeInclinationMenu)));
            menu.AddMenuItem("change longitude of ascending node", () => PushMenu(BuildOperationMenu(MechJebProxy.OpChangeLAN, PopulateChangeLANMenu)));
            menu.AddMenuItem("change periapsis", () => PushMenu(BuildOperationMenu(MechJebProxy.OpChangePeriapsis, PopulateChangePeriapsisMenu)));
            menu.AddMenuItem("change semi-major axis", () => PushMenu(BuildOperationMenu(MechJebProxy.OpChangeSemiMajorAxis, PopulateChangeSMAMenu)));
            menu.AddMenuItem("change surface longitude of apsis", () => PushMenu(BuildOperationMenu(MechJebProxy.OpLongitude, PopulateChangeSurfaceLongitudeMenu)));
            menu.AddMenuItem("circularize", () => PushMenu(BuildOperationMenu(MechJebProxy.OpCircularize)));
            menu.AddMenuItem("fine tune closest approach to target", () => PushMenu(BuildOperationMenu(MechJebProxy.OpCourseCorrection, PopulateCourseCorrectMenu)),
                () => FlightGlobals.fetch.VesselTarget != null);
            menu.AddMenuItem("intercept target at chosen time", () => PushMenu(BuildOperationMenu(MechJebProxy.OpLambert, PopulateLambertMenu)),
                () => FlightGlobals.fetch.VesselTarget != null);
            menu.AddMenuItem("match planes with target", () => PushMenu(BuildOperationMenu(MechJebProxy.OpMatchPlane)),
                () => FlightGlobals.fetch.VesselTarget != null);
            menu.AddMenuItem("match velocities with target", () => PushMenu(BuildOperationMenu(MechJebProxy.OpMatchVelocity)),
                () => FlightGlobals.fetch.VesselTarget != null);
            menu.AddMenuItem("resonant orbit", () => PushMenu(BuildOperationMenu(MechJebProxy.OpResonantOrbit, PopulateResonantOrbitMenu)));
            menu.AddMenuItem("return from a moon", () => PushMenu(BuildOperationMenu(MechJebProxy.OpMoonReturn, PopulatedMoonReturnMenu)),
                () => vessel != null && vessel.mainBody != null && vessel.mainBody.referenceBody != null && 
                        vessel.mainBody.referenceBody != Planetarium.fetch.Sun);
            menu.AddMenuItem("transfer to another planet", () => PushMenu(BuildOperationMenu(MechJebProxy.OpInterplanetaryTransfer, PopulateInterplanetaryTransferMenu)),
                () => FlightGlobals.fetch.VesselTarget is CelestialBody);
            menu.AddMenuItem("two impulse (Hohmann) transfer to target", () => PushMenu(BuildOperationMenu(MechJebProxy.OpGeneric, PopulateHohmannMenu)),
                () => FlightGlobals.fetch.VesselTarget != null);

            menu.AddMenuItem("------", null);
            menu.AddMenuItem("Remove ALL nodes", () => RemoveAllNodes());
            menu.AddMenuItem("------", null);
            
            // Node execution controls (matching IMGUI bottom controls)
            menu.AddToggleItem("Auto-warp",
                () => mjCore.Node.Autowarp,
                (val) => mjCore.Node.Autowarp = val);
            menu.AddMJItem("Lead time",
                mjCore.Node.LeadTime,
                1.0, v => v.ToString("F0") + " s", null, true, 0, false, 0);

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu CreateOperationMenu(Operation op)
        {
            return new TextMenu
            {
                labelColor = JUtil.ColorToColorTag(Color.white),
                selectedColor = JUtil.ColorToColorTag(Color.green),
                disabledColor = JUtil.ColorToColorTag(Color.gray),
                menuTitle = op.GetName(),
            };
        }

        private TextMenu BuildOperationMenu(Operation op, Action<TextMenu, Operation> populateOperationMenuFunc = null)
        {
            var menu = CreateOperationMenu(op);

            // Eventually we probably want to just use reflection to populate the menu items to set the parameters for the operation
            // I assume that's how MJ is doing it internally anyway
            if (populateOperationMenuFunc != null)
            {
                populateOperationMenuFunc(menu, op);
            }

            var timeSelector = op.GetTimeSelector();
            if (timeSelector != null)
            {
                AddTimeSelectorMenuItems(menu, op);
            }
            else
            {
                menu.AddMenuItem("[Create Node]", () => MechJebProxy.ExecuteOperation(op, mjCore, vessel));
            }

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private void AddTimeSelectorMenuItems(TextMenu menu, Operation op)
        {
            var timeSelector = op.GetTimeSelector();

            menu.AddMenuItem("Schedule the burn:", null);

            for (int i = 0; i < timeSelector._timeRefNames.Length; ++i)
            {
                var timeReference = timeSelector._allowedTimeRef[i];
                var timeRefName = "  " + timeSelector._timeRefNames[i];
                int timeRefIndex = i;
                switch (timeReference)
                {
                    case TimeReference.X_FROM_NOW:
                        menu.AddMenuItem(timeRefName, () => PushMenu(BuildTimeSelectorLeadTimeMenu(op, timeRefIndex)));
                        break;
                    case TimeReference.ALTITUDE:
                        menu.AddMenuItem(timeRefName, () => PushMenu(BuildTimeSelectorAltitudeMenu(op, timeRefIndex)));
                        break;
                    default:
                        menu.AddMenuItem(timeRefName, () => ExecuteOperation(op, timeRefIndex));
                        break;
                }
            }
        }

        /// <summary>
        /// Builds a submenu for setting altitude timing and executing an operation
        /// </summary>
        private TextMenu BuildTimeSelectorAltitudeMenu(Operation op, int timeRefIndex)
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);
            menu.menuTitle = op.GetName();

            var ts = op.GetTimeSelector();

            menu.AddMJItem("At Altitude", ts.CircularizeAltitude,
                1000.0, v => (v / 1000.0).ToString("F1") + " km", null, true, 1.0, false, 0);
            
            // Find ALTITUDE index for this operation's TimeSelector
            menu.AddMenuItem("[Create Node]", () => {
                ts._currentTimeRef = timeRefIndex;
                MechJebProxy.ExecuteOperation(op, mjCore, vessel);
                PopMenu();
            });
            
            menu.AddMenuItem("[BACK]", () => PopMenu());
            return menu;
        }

        /// <summary>
        /// Builds a submenu for setting lead time and executing an operation
        /// </summary>
        private TextMenu BuildTimeSelectorLeadTimeMenu(Operation op, int timeRefIndex)
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);
            menu.menuTitle = op.GetName();

            var ts = op.GetTimeSelector();

            menu.AddMJItem("Seconds from now", ts.LeadTime,
                10.0, v => v.ToString("F0") + " s", null, true, 0, false, 0);
            
            // Find X_FROM_NOW index for this operation's TimeSelector
            menu.AddMenuItem("[Create Node]", () => {
                ts._currentTimeRef = timeRefIndex;
                MechJebProxy.ExecuteOperation(op, mjCore, vessel);
                PopMenu();
            });
            
            menu.AddMenuItem("[BACK]", () => PopMenu());
            return menu;
        }

        void ExecuteOperation(Operation op, int timeRefIndex)
        {
            var timeSelector = op.GetTimeSelector();
            timeSelector._currentTimeRef = timeRefIndex;
            MechJebProxy.ExecuteOperation(op, mjCore, vessel);
        }

        private void PopulateChangeApoapsisMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationApoapsis;
            menu.AddMJItem("New Apoapsis", op.NewApA,
                1000.0, v => (v / 1000.0).ToString("F1") + " km", null, true, 1.0, false, 0);
        }

        private void PopulateChangePeriapsisMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationPeriapsis;
            menu.AddMJItem("New Periapsis", op.NewPeA,
                1000.0, v => (v / 1000.0).ToString("F1") + " km", null, true, 1.0, false, 0);
        }

        private void PopulateChangeSMAMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationSemiMajor;
            menu.AddMJItem("New Semi-Major Axis", MechJebProxy.OpChangeSemiMajorAxis.NewSma,
                1000.0, v => (v / 1000.0).ToString("F1") + " km", null, true, 1.0, false, 0);
        }

        private void PopulateChangeInclinationMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationInclination;
            menu.AddMJItem("New Inclination", op.NewInc,
                0.5, v => v.ToString("F1") + "°", null, true, -180, true, 180);
        }

        private void PopulateChangeLANMenu(TextMenu menu, Operation baseOp)
        {
            menu.AddMJItem("New LAN", mjCore.Target.targetLongitude.Degrees,
                0.5, v => v.ToString("F1") + "°", null, true, 0, true, 360);
        }

        private void PopulateHohmannMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationGeneric;

            // "no insertion burn (impact/flyby)" checkbox - inverted Capture bool
            menu.AddToggleItem("no insertion burn (impact/flyby)",
                () => !op.Capture,
                (val) => op.Capture = !val);

            // "Plan insertion burn" checkbox
            menu.AddToggleItem("Plan insertion burn", op, MechJebProxy.f_Generic_PlanCapture);

            // "coplanar maneuver" checkbox
            menu.AddToggleItem("coplanar maneuver", op, MechJebProxy.f_Generic_Coplanar);

            // Rendezvous vs Transfer radio buttons - use isSelected for green highlighting
            menu.AddMenuItem("Rendezvous", () => op.Rendezvous = true, () => op.Rendezvous);
            menu.AddMenuItem("Transfer", () => op.Rendezvous = false, () => !op.Rendezvous);

            // Rendezvous time offset (LagTime in seconds)
            menu.AddMJItem("rendezvous time offset", op.LagTime,
                1.0, v => v.ToString("F0") + " sec", null, false, 0, false, 0);
        }

        private void PopulateEllipticizeMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationEllipticize;
            menu.AddMJItem("New periapsis", op.NewPeA,
                1000.0, v => (v / 1000.0).ToString("F1") + " km", null, true, 1.0, false, 0);
            menu.AddMJItem("New apoapsis", op.NewApA,
                1000.0, v => (v / 1000.0).ToString("F1") + " km", null, true, 1.0, false, 0);
        }

        private void PopulateChangeEccentricityMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationEccentricity;
            menu.AddMJItem("New eccentricity", op.NewEcc,
                0.01, v => v.ToString("F3"), null, true, 0, true, 0.99);
        }

        private void PopulateChangeSurfaceLongitudeMenu(TextMenu menu, Operation baseOp)
        {
            menu.AddMJItem("Target longitude", mjCore.Target.targetLongitude.Degrees,
                1.0, v => v.ToString("F1") + "°", null, true, -180, true, 180);
        }

        private void PopulateCourseCorrectMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationCourseCorrection;

            // OperationCourseCorrection: CourseCorrectFinalPeA or InterceptDistance parameters (no time selector)
            // Check if target is celestial body or vessel
            ITargetable target = FlightGlobals.fetch.VesselTarget;
            bool isCelestialTarget = target is CelestialBody;
            
            if (isCelestialTarget)
            {
                menu.AddMJItem("Target periapsis", op.CourseCorrectFinalPeA,
                    1000.0, v => (v / 1000.0).ToString("F1") + " km", null, true, 0, false, 0);
            }
            else
            {
                menu.AddMJItem("Distance at closest approach", op.InterceptDistance,
                    10.0, v => v.ToString("F0") + " m", null, true, 0, false, 0);
            }
        }

        private void PopulateLambertMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationLambert;
            menu.AddMJItem("Intercept after", op.InterceptInterval,
                60.0, v => FormatTime(v), null, true, 60, false, 0);
        }

        private void PopulateResonantOrbitMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationResonantOrbit;
            menu.AddMJItem("Resonance numerator", op.ResonanceNumerator,
                1.0, v => ((int)v).ToString(), null, true, 1, false, 0);
            menu.AddMJItem("Resonance denominator", op.ResonanceDenominator,
                1.0, v => ((int)v).ToString(), null, true, 1, false, 0);
        }

        private void PopulatedMoonReturnMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationMoonReturn;
            menu.AddMJItem("Return altitude", MechJebProxy.OpMoonReturn.MoonReturnAltitude,
                10.0, v => (v / 1000.0).ToString("F0") + " km", null, true, 10, false, 0);
        }

        private void PopulateInterplanetaryTransferMenu(TextMenu menu, Operation baseOp)
        {
            var op = baseOp as OperationInterplanetaryTransfer;
            menu.AddToggleItem("Wait for optimal phase angle", op, MechJebProxy.f_InterplanetaryTransfer_WaitForPhaseAngle);
        }

        private TextMenu BuildAdvancedTransferMenu()
        {
            var op = MechJebProxy.OpAdvancedTransfer;
            var menu = CreateOperationMenu(op);

            // Mode selection header
            menu.AddMenuItem("--- Porkchop selection ---", null);

            // Status display - shows computation progress/ready status
            menu.AddMenuItem(() => GetAdvancedTransferStatusText());
            menu.AddMenuItem(() => {
                if (MechJebProxy.GetAdvancedTransferSelection(op, out double dep, out double dur, out double dv) && dv > 0)
                    return "ΔV: " + dv.ToString("F1") + " m/s";
                return "ΔV: ---";
            });

            // Include capture burn checkbox - wraps operation field
            menu.AddToggleItem("Include capture burn", op, MechJebProxy.f_AdvancedTransfer_IncludeCaptureBurn);

            // Periapsis input - wraps periapsisHeight field (in km)
            menu.AddMJItem("Periapsis", op.periapsisHeight,
                10.0, v => v.ToString("F0") + " km", null, true, 10.0, false, 0);

            // Selection mode - Lowest ΔV vs ASAP - use isSelected for green highlighting
            menu.AddMenuItem("Lowest ΔV", SelectAdvancedTransferLowestDV, null, () => advancedTransferSelectLowestDV);
            menu.AddMenuItem("ASAP", SelectAdvancedTransferASAP, null, () => !advancedTransferSelectLowestDV);

            menu.AddMenuItem(() => "Departure in " + GetAdvancedTransferDepartureText());
            menu.AddMenuItem(() => "Transit duration " + GetAdvancedTransferTransitText());

            menu.AddMenuItem("------", null);
            menu.AddMenuItem("[Start/Refresh Compute]", () => StartAdvancedTransferCompute());
            menu.AddMenuItem("[Create node]", () => CreateAdvancedTransferNode());
            menu.AddMenuItem("[Create and execute]", () => CreateAndExecuteAdvancedTransfer());
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private string GetAdvancedTransferStatusText()
        {
            var op = MechJebProxy.OpAdvancedTransfer;
            if (op == null) return "Not available";
            int progress;
            bool finished = MechJebProxy.IsAdvancedTransferFinished(op, out progress);
            if (finished)
            {
                // Update cached selection info for display
                MechJebProxy.GetAdvancedTransferSelection(op, 
                    out advancedTransferDepartureUT, out advancedTransferDuration, out advancedTransferDeltaV);
                return "Ready";
            }
            return "Computing: " + progress + "%";
        }

        private string GetAdvancedTransferDepartureText()
        {
            if (advancedTransferDepartureUT <= 0) return "---";
            double dt = advancedTransferDepartureUT - Planetarium.GetUniversalTime();
            if (dt < 0) return "any time now";
            return FormatTime(dt);
        }

        private string GetAdvancedTransferTransitText()
        {
            if (advancedTransferDuration <= 0) return "---";
            return FormatTime(advancedTransferDuration);
        }

        private void CreateAndExecuteAdvancedTransfer()
        {
            CreateAdvancedTransferNode();
            if (mjCore != null)
            {
                mjCore.Node.ExecuteOneNode(null);
            }
        }

        private void StartAdvancedTransferCompute()
        {
            if (mjCore == null || vessel == null) return;
            var targetController = mjCore.Target;
            if (targetController == null || FlightGlobals.fetch.VesselTarget == null) return;
            if (!(FlightGlobals.fetch.VesselTarget is CelestialBody)) return;

            OperationAdvancedTransfer op = MechJebProxy.OpAdvancedTransfer;
            if (op == null) return;

            MechJebProxy.StartAdvancedTransferCompute(
                op,
                vessel.orbit,
                Planetarium.GetUniversalTime(),
                targetController);
        }

        private void SelectAdvancedTransferLowestDV()
        {
            advancedTransferSelectLowestDV = true;
            var op = MechJebProxy.OpAdvancedTransfer;
            if (op == null) return;
            MechJebProxy.SelectAdvancedTransferLowestDV(op);
        }

        private void SelectAdvancedTransferASAP()
        {
            advancedTransferSelectLowestDV = false;
            var op = MechJebProxy.OpAdvancedTransfer;
            if (op == null) return;
            MechJebProxy.SelectAdvancedTransferASAP(op);
        }

        private void CreateAdvancedTransferNode()
        {
            if (vessel == null || mjCore == null) return;
            var targetController = mjCore.Target;
            if (targetController == null) return;

            var op = MechJebProxy.OpAdvancedTransfer;
            if (op == null) return;

            // Check if computation is finished
            int progress;
            if (!MechJebProxy.IsAdvancedTransferFinished(op, out progress))
            {
                // Not ready yet - need to compute first
                return;
            }

            MechJebProxy.CreateNodesFromOperation(op, vessel.orbit, Planetarium.GetUniversalTime(), targetController, vessel);
        }

        private void RemoveAllNodes()
        {
            if (vessel == null || vessel.patchedConicSolver == null) return;
            while (vessel.patchedConicSolver.maneuverNodes.Count > 0)
            {
                vessel.patchedConicSolver.maneuverNodes[0].RemoveSelf();
            }
        }
        #endregion

        #region Node Editor Menu
        private TextMenu BuildNodeEditorMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddMenuItem("-- ADJUST NODE --", null);
            menu.AddMenuItem("Prograde +1 m/s", () => AdjustNode(Vector3d.forward, 1));
            menu.AddMenuItem("Prograde -1 m/s", () => AdjustNode(Vector3d.forward, -1));
            menu.AddMenuItem("Prograde +10 m/s", () => AdjustNode(Vector3d.forward, 10));
            menu.AddMenuItem("Prograde -10 m/s", () => AdjustNode(Vector3d.forward, -10));
            menu.AddMenuItem("------", null);
            menu.AddMenuItem("Normal +1 m/s", () => AdjustNode(Vector3d.up, 1));
            menu.AddMenuItem("Normal -1 m/s", () => AdjustNode(Vector3d.up, -1));
            menu.AddMenuItem("Normal +10 m/s", () => AdjustNode(Vector3d.up, 10));
            menu.AddMenuItem("Normal -10 m/s", () => AdjustNode(Vector3d.up, -10));
            menu.AddMenuItem("------", null);
            menu.AddMenuItem("Radial +1 m/s", () => AdjustNode(Vector3d.right, 1));
            menu.AddMenuItem("Radial -1 m/s", () => AdjustNode(Vector3d.right, -1));
            menu.AddMenuItem("Radial +10 m/s", () => AdjustNode(Vector3d.right, 10));
            menu.AddMenuItem("Radial -10 m/s", () => AdjustNode(Vector3d.right, -10));
            menu.AddMenuItem("------", null);
            menu.AddMenuItem("Delete Node", () => DeleteCurrentNode());
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private void AdjustNode(Vector3d direction, double amount)
        {
            if (vessel == null || vessel.patchedConicSolver == null) return;
            if (vessel.patchedConicSolver.maneuverNodes.Count == 0) return;

            ManeuverNode node = vessel.patchedConicSolver.maneuverNodes[0];
            Vector3d dv = node.DeltaV;
            dv += direction * amount;
            node.DeltaV = dv;
            node.solver.UpdateFlightPlan();
        }

        private void DeleteCurrentNode()
        {
            if (vessel == null || vessel.patchedConicSolver == null) return;
            if (vessel.patchedConicSolver.maneuverNodes.Count == 0) return;

            vessel.patchedConicSolver.maneuverNodes[0].RemoveSelf();
        }
        #endregion

        #region Execute Node
        private void ExecuteNode()
        {
            if (mjCore == null) return;
            if (mjCore.Node != null && mjCore.Node.Enabled)
            {
                mjCore.Node.Abort();
            }
            else
            {
                mjCore.Node.ExecuteOneNode(null);
            }
        }
        #endregion

        #region Rendezvous Menu
        private TextMenu BuildRendezvousMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddToggleItem("ENGAGE Rendezvous Autopilot",
                () => mjRendezvousAutopilot?.Enabled == true,
                (val) => MechJebProxy.SetRendezvousAutopilotEngaged(mjCore, val));

            menu.AddMenuItem("------", null);

            menu.AddMJItem("Desired Distance",
                mjRendezvousAutopilot.desiredDistance,
                10.0, v => v.ToString("F0") + " m", null, true, 0, false, 0);

            menu.AddMJItem("Max Phasing Orbits",
                mjRendezvousAutopilot.maxPhasingOrbits,
                1.0, v => v.ToString("F0"), null, true, 0, false, 0);
            menu.AddMJItem("Max Closing Speed",
                mjRendezvousAutopilot.maxClosingSpeed,
                1.0, v => v.ToString("F1") + " m/s", null, true, 0, false, 0);

            menu.AddMenuItem("------", null);

            // Info display
            menu.AddMenuItem("-- RENDEZVOUS INFO --", null);
            menu.AddMenuItem(() => "Status: " + mjRendezvousAutopilot?.status ?? "");

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }
        #endregion

        #region Docking Menu
        private TextMenu BuildDockingMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddToggleItem("ENGAGE Docking Autopilot",
                () => mjDockingAutoPilot?.Enabled == true,
                (val) => mjDockingAutoPilot.Enabled = val);

            menu.AddMenuItem("------", null);

            menu.AddMJItem("Speed Limit",
                mjDockingAutoPilot.speedLimit,
                0.1, v => v.ToString("F1") + " m/s", null, true, 0, false, 0);

            menu.AddToggleItem("Force Roll", mjDockingAutoPilot, MechJebProxy.f_Docking_ForceRoll);
            menu.AddMJItem("Roll",
                mjDockingAutoPilot.rol,
                1.0, v => v.ToString("F1") + "°", null, true, -180, true, 180);

            menu.AddToggleItem("Override Safe Distance", mjDockingAutoPilot, MechJebProxy.f_Docking_OverrideSafeDistance);
            menu.AddMJItem("Safe Distance",
                mjDockingAutoPilot.overridenSafeDistance,
                0.1, v => v.ToString("F1") + " m", () => mjDockingAutoPilot.overrideSafeDistance, true, 0, false, 0);

            menu.AddToggleItem("Override Target Size", mjDockingAutoPilot, MechJebProxy.f_Docking_OverrideTargetSize);
            menu.AddMJItem("Target Size",
                mjDockingAutoPilot.overridenTargetSize,
                0.1, v => v.ToString("F1") + " m", () => mjDockingAutoPilot.overrideTargetSize, true, 0, false, 0);

            menu.AddToggleItem("Draw Bounding Box",
                mjDockingAutoPilot, MechJebProxy.f_Docking_DrawBoundingBox);

            menu.AddMenuItem("------", null);

            // Status
            menu.AddMenuItem("Status:", null);
            menu.AddMenuItem(() => "  " + mjDockingAutoPilot?.status ?? "");
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }
        #endregion

        #region Translatron Menu
        private TextMenu BuildTranslatronMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddMenuItem("-- MODE --", null);
            menu.AddMenuItem("OFF", () => mjTranslatron.SetMode(MechJebModuleThrustController.TMode.OFF));
            menu.AddMenuItem("Keep Orbital Vel", () => mjTranslatron.SetMode(MechJebModuleThrustController.TMode.KEEP_ORBITAL));
            menu.AddMenuItem("Keep Surface Vel", () => mjTranslatron.SetMode(MechJebModuleThrustController.TMode.KEEP_SURFACE));
            menu.AddMenuItem("Keep Vertical Vel", () => mjTranslatron.SetMode(MechJebModuleThrustController.TMode.KEEP_VERTICAL));
            menu.AddMenuItem("Direct", () => mjTranslatron.SetMode(MechJebModuleThrustController.TMode.DIRECT));

            menu.AddMenuItem("------", null);

            menu.AddMJItem("Target Speed", mjTranslatron.trans_spd,
                0.1, v => v.ToString("F1") + " m/s", null, true, 0, false, 0);

            menu.AddToggleItem("Kill Horizontal", mjCore.Thrust, MechJebProxy.f_Thrust_TransKillH);

            menu.AddMenuItem("------", null);

            menu.AddMenuItem("!! PANIC !!", () => mjTranslatron?.PanicSwitch());

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }
        #endregion

        #region Rover Menu
        private TextMenu BuildRoverMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddMenuItem("Drive to Target", () => DriveToTarget(),
                () => mjCore.Target.PositionTargetExists);
            menu.AddMenuItem("STOP", () => StopRover());

            menu.AddMenuItem("------", null);

            menu.AddToggleItem("Control Heading", mjCore.Rover, MechJebProxy.f_Rover_ControlHeading);
            menu.AddMJItem("Heading", mjCore.Rover.heading,
                1.0, v => v.ToString("F1") + "°", null, true, 0, true, 360);

            menu.AddToggleItem("Control Speed", mjCore.Rover, MechJebProxy.f_Rover_ControlSpeed);
            menu.AddMJItem("Speed", mjCore.Rover.speed,
                0.5, v => v.ToString("F1") + " m/s", null, true, 0, false, 0);

            menu.AddMenuItem("------", null);

            menu.AddToggleItem("Stability Control", mjCore.Rover, MechJebProxy.f_Rover_StabilityControl);
            menu.AddToggleItem("Brake on Eject", mjCore.Rover, MechJebProxy.f_Rover_BrakeOnEject);
            menu.AddToggleItem("Brake on Energy Depletion", mjCore.Rover, MechJebProxy.f_Rover_BrakeOnEnergyDepletion);
            menu.AddToggleItem("Warp to Daylight", mjCore.Rover, MechJebProxy.f_Rover_WarpToDaylight);

            menu.AddMenuItem("------", null);

            menu.AddMenuItem("Waypoints", () => PushMenu(BuildRoverWaypointsMenu()));

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildRoverWaypointsMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);

            menu.AddMenuItem("Add Waypoint", () => AddRoverWaypoint());
            menu.AddMenuItem("Clear All Waypoints", () => ClearRoverWaypoints());
            // Waypoint list would go here

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private void DriveToTarget()
        {
            if (mjCore == null) return;
            { mjCore.Rover.ControlHeading = true; mjCore.Rover.ControlSpeed = true; };
        }

        private void StopRover()
        {
            if (mjCore == null) return;
            { mjCore.Rover.ControlHeading = false; mjCore.Rover.ControlSpeed = false; };
        }

        private void AddRoverWaypoint()
        {
            if (mjCore == null || vessel == null) return;
            mjCore.Rover.Waypoints.Add(new MechJebWaypoint(vessel.latitude, vessel.longitude));
        }

        private void ClearRoverWaypoints()
        {
            if (mjCore == null) return;
            mjCore.Rover.Waypoints.Clear();
        }
        #endregion

        #region Aircraft Menu
        private bool IsAircraftAutopilotEngaged()
        {
            return mjCore?.Airplane?.Users != null &&
                   mjAirplaneGuidance != null &&
                   mjCore.Airplane.Users.Contains(mjAirplaneGuidance);
        }

        private void SetAircraftAutopilotEngaged(bool engaged)
        {
            if (mjCore?.Airplane?.Users == null || mjAirplaneGuidance == null) return;

            if (engaged)
            {
                SyncAirplaneGuidanceTargetsToAutopilot();
                if (!mjCore.Airplane.Users.Contains(mjAirplaneGuidance))
                    mjCore.Airplane.Users.Add(mjAirplaneGuidance);
            }
            else
            {
                if (mjCore.Airplane.Users.Contains(mjAirplaneGuidance))
                    mjCore.Airplane.Users.Remove(mjAirplaneGuidance);
            }
        }

        private void SyncAirplaneGuidanceTargetsToAutopilot()
        {
            if (mjCore?.Airplane == null || mjAirplaneGuidance == null) return;

            if (mjAirplaneGuidance.AltitudeTargettmp != null)
                mjCore.Airplane.AltitudeTarget = mjAirplaneGuidance.AltitudeTargettmp.Val;
            if (mjAirplaneGuidance.VertSpeedTargettmp != null)
                mjCore.Airplane.VertSpeedTarget = mjAirplaneGuidance.VertSpeedTargettmp.Val;
            if (mjAirplaneGuidance.HeadingTargettmp != null)
                mjCore.Airplane.HeadingTarget = mjAirplaneGuidance.HeadingTargettmp.Val;
            if (mjAirplaneGuidance.RollTargettmp != null)
                mjCore.Airplane.RollTarget = mjAirplaneGuidance.RollTargettmp.Val;
            if (mjAirplaneGuidance.SpeedTargettmp != null)
                mjCore.Airplane.SpeedTarget = mjAirplaneGuidance.SpeedTargettmp.Val;
        }

        private double GetAircraftAltitudeTarget()
        {
            return mjAirplaneGuidance?.AltitudeTargettmp?.Val ?? mjCore?.Airplane?.AltitudeTarget ?? 0.0;
        }

        private void SetAircraftAltitudeTarget(double value)
        {
            if (mjAirplaneGuidance?.AltitudeTargettmp != null)
                mjAirplaneGuidance.AltitudeTargettmp.Val = value;
            if (mjCore?.Airplane != null)
                mjCore.Airplane.AltitudeTarget = value;
        }

        private double GetAircraftVertSpeedTarget()
        {
            return mjAirplaneGuidance?.VertSpeedTargettmp?.Val ?? mjCore?.Airplane?.VertSpeedTarget ?? 0.0;
        }

        private void SetAircraftVertSpeedTarget(double value)
        {
            if (mjAirplaneGuidance?.VertSpeedTargettmp != null)
                mjAirplaneGuidance.VertSpeedTargettmp.Val = value;
            if (mjCore?.Airplane != null)
                mjCore.Airplane.VertSpeedTarget = value;
        }

        private double GetAircraftHeadingTarget()
        {
            return mjAirplaneGuidance?.HeadingTargettmp?.Val ?? mjCore?.Airplane?.HeadingTarget ?? 0.0;
        }

        private void SetAircraftHeadingTarget(double value)
        {
            if (mjAirplaneGuidance?.HeadingTargettmp != null)
                mjAirplaneGuidance.HeadingTargettmp.Val = value;
            if (mjCore?.Airplane != null)
                mjCore.Airplane.HeadingTarget = value;
        }

        private double GetAircraftRollTarget()
        {
            return mjAirplaneGuidance?.RollTargettmp?.Val ?? mjCore?.Airplane?.RollTarget ?? 0.0;
        }

        private void SetAircraftRollTarget(double value)
        {
            if (mjAirplaneGuidance?.RollTargettmp != null)
                mjAirplaneGuidance.RollTargettmp.Val = value;
            if (mjCore?.Airplane != null)
                mjCore.Airplane.RollTarget = value;
        }

        private double GetAircraftSpeedTarget()
        {
            return mjAirplaneGuidance?.SpeedTargettmp?.Val ?? mjCore?.Airplane?.SpeedTarget ?? 0.0;
        }

        private void SetAircraftSpeedTarget(double value)
        {
            if (mjAirplaneGuidance?.SpeedTargettmp != null)
                mjAirplaneGuidance.SpeedTargettmp.Val = value;
            if (mjCore?.Airplane != null)
                mjCore.Airplane.SpeedTarget = value;
        }

        private TextMenu BuildAircraftMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddMenuItem(
                () => IsAircraftAutopilotEngaged() ? "Disengage Autopilot" : "Engage Autopilot",
                () => SetAircraftAutopilotEngaged(!IsAircraftAutopilotEngaged()));
            menu.AddMenuItem("------", null);

            menu.AddMenuItem("-- ALTITUDE --", null);
            menu.AddToggleItem("Altitude Hold",
                () => mjCore.Airplane.AltitudeHoldEnabled,
                (val) =>
                {
                    mjCore.Airplane.AltitudeHoldEnabled = val;
                    if (val)
                    {
                        SyncAirplaneGuidanceTargetsToAutopilot();
                        mjCore.Airplane.EnableAltitudeHold();
                    }
                    else
                    {
                        mjCore.Airplane.DisableAltitudeHold();
                    }
                });
            menu.AddNumericItem("Target Altitude", GetAircraftAltitudeTarget, SetAircraftAltitudeTarget,
                50.0, v => v.ToString("F0") + " m", null, true, 0, false, 0);
            menu.AddToggleItem("Vertical Speed Hold",
                () => mjCore.Airplane.VertSpeedHoldEnabled,
                (val) =>
                {
                    mjCore.Airplane.VertSpeedHoldEnabled = val;
                    if (val)
                    {
                        SyncAirplaneGuidanceTargetsToAutopilot();
                        mjCore.Airplane.EnableVertSpeedHold();
                    }
                    else
                    {
                        mjCore.Airplane.DisableVertSpeedHold();
                    }
                });
            menu.AddNumericItem("Target Vert Speed", GetAircraftVertSpeedTarget, SetAircraftVertSpeedTarget,
                1.0, v => v.ToString("F1") + " m/s", null, false, 0, false, 0);

            menu.AddMenuItem("------", null);

            menu.AddMenuItem("-- HEADING --", null);
            menu.AddToggleItem("Heading Hold",
                () => mjCore.Airplane.HeadingHoldEnabled,
                (val) =>
                {
                    mjCore.Airplane.HeadingHoldEnabled = val;
                    if (val)
                    {
                        SyncAirplaneGuidanceTargetsToAutopilot();
                        mjCore.Airplane.EnableHeadingHold();
                    }
                    else
                    {
                        mjCore.Airplane.DisableHeadingHold();
                    }
                });
            menu.AddNumericItem("Target Heading", GetAircraftHeadingTarget, SetAircraftHeadingTarget,
                1.0, v => v.ToString("F1") + "°", null, true, 0, true, 360);
            menu.AddToggleItem("Roll Hold",
                () => mjCore.Airplane.RollHoldEnabled,
                (val) => { if (val) { SyncAirplaneGuidanceTargetsToAutopilot(); mjCore.Airplane.RollHoldEnabled = true; } else mjCore.Airplane.RollHoldEnabled = false; });
            menu.AddNumericItem("Target Roll", GetAircraftRollTarget, SetAircraftRollTarget,
                1.0, v => v.ToString("F1") + "°", null, true, -180, true, 180);

            menu.AddMenuItem("------", null);

            menu.AddMenuItem("-- SPEED --", null);
            menu.AddToggleItem("Speed Hold",
                () => mjCore.Airplane.SpeedHoldEnabled,
                (val) =>
                {
                    mjCore.Airplane.SpeedHoldEnabled = val;
                    if (val)
                    {
                        SyncAirplaneGuidanceTargetsToAutopilot();
                        mjCore.Airplane.EnableSpeedHold();
                    }
                    else
                    {
                        mjCore.Airplane.DisableSpeedHold();
                    }
                });
            menu.AddNumericItem("Target Speed", GetAircraftSpeedTarget, SetAircraftSpeedTarget,
                1.0, v => v.ToString("F1") + " m/s", null, true, 0, false, 0);

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }
        #endregion

        #region Spaceplane Menu
        private TextMenu BuildSpaceplaneMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddMenuItem("Autoland", () => mjSpacePlaneAutopilot.Autoland(null));
            menu.AddMenuItem("Hold Heading & Altitude", () => { var ap = mjSpacePlaneAutopilot?.Autopilot; if (ap != null) { ap.HeadingHoldEnabled = true; ap.AltitudeHoldEnabled = true; } });
            menu.AddMenuItem("Autopilot OFF", mjSpacePlaneAutopilot.AutopilotOff);

            menu.AddMenuItem("------", null);

            menu.AddMJItem("Glideslope", mjSpacePlaneAutopilot.glideslope,
                0.1, v => v.ToString("F1") + "°", null, true, 0, true, 30);
            menu.AddMJItem("Approach Speed", mjSpacePlaneAutopilot.approachSpeed,
                1.0, v => v.ToString("F1") + " m/s", null, true, 0, false, 0);
            menu.AddMJItem("Touchdown Speed", mjSpacePlaneAutopilot.touchdownSpeed,
                1.0, v => v.ToString("F1") + " m/s", null, true, 0, false, 0);

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        #endregion

        #region Utilities Menu
        private TextMenu BuildUtilitiesMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.disabledColor = JUtil.ColorToColorTag(Color.gray);

            menu.AddMenuItem("Stage Once", () => mjCore.Staging.AutostageOnce(null));
            menu.AddMenuItem("Autostage Options", () => PushMenu(BuildAutostageOptionsMenu()));

            menu.AddMenuItem("------", null);

            // Delta-V info
            menu.AddMenuItem("-- DELTA-V INFO --", null);
            menu.AddMenuItem(() => "Stage dV (Vac): " + GetStageDeltaVText(mjCore, true));
            menu.AddMenuItem(() => "Total dV (Vac): " + FormatDeltaV(MechJebProxy.GetTotalVacuumDeltaV(mjCore)));
            menu.AddMenuItem(() => "Stage dV (Atm): " + GetStageDeltaVText(mjCore, false));
            menu.AddMenuItem(() => "Total dV (Atm): " + FormatDeltaV(MechJebProxy.GetTotalAtmoDeltaV(mjCore)));

            menu.AddMenuItem("------", null);

            menu.AddMenuItem("Warp Helper", () => PushMenu(BuildWarpHelperMenu()));

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildAutostageOptionsMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);

            menu.AddMJItem("Pre-Delay", mjCore.Staging.AutostagePreDelay,
                0.1, v => v.ToString("F1") + " s", null, true, 0, false, 0);
            menu.AddMJItem("Post-Delay", mjCore.Staging.AutostagePostDelay,
                0.1, v => v.ToString("F1") + " s", null, true, 0, false, 0);
            menu.AddMJItem("Clamp Thrust %", mjCore.Staging.ClampAutoStageThrustPct,
                1.0, v => v.ToString("F0") + "%", null, true, 0, true, 100);

            menu.AddMenuItem("------", null);

            menu.AddMJItem("Fairing Max Flux", mjCore.Staging.FairingMaxAerothermalFlux,
                1000.0, v => v.ToString("F0"), null, true, 0, false, 0);
            menu.AddMJItem("Fairing Max Q", mjCore.Staging.FairingMaxDynamicPressure,
                1000.0, v => v.ToString("F0") + " Pa", null, true, 0, false, 0);
            menu.AddMJItem("Fairing Min Alt", mjCore.Staging.FairingMinAltitude,
                1000.0, v => (v / 1000.0).ToString("F1") + " km", null, true, 0, false, 0);

            menu.AddMenuItem("------", null);

            menu.AddMJItem("Hot Staging Lead", mjCore.Staging.HotStagingLeadTime,
                0.1, v => v.ToString("F1") + " s", null, true, 0, false, 0);
            menu.AddToggleItem("Drop Solids", mjCore.Staging, MechJebProxy.f_Staging_DropSolids);
            menu.AddMJItem("Drop Solids Lead", mjCore.Staging.DropSolidsLeadTime,
                0.1, v => v.ToString("F1") + " s", () => mjCore.Staging.DropSolids, true, 0, false, 0);

            menu.AddMenuItem("[BACK]", () => PopMenu());
            return menu;
        }

        private TextMenu BuildWarpHelperMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);

            menu.AddMenuItem("Warp to Apoapsis", () => WarpToApoapsis());
            menu.AddMenuItem("Warp to Periapsis", () => WarpToPeriapsis());
            menu.AddMenuItem("Warp to Node", () => WarpToNode(),
                () => vessel != null && vessel.patchedConicSolver != null &&
                        vessel.patchedConicSolver.maneuverNodes.Count > 0);
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        #endregion

        #region Info Display Menu
        private TextMenu BuildInfoMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);

            menu.AddMenuItem("Orbit Info", () => PushMenu(BuildOrbitInfoMenu()));
            menu.AddMenuItem("Surface Info", () => PushMenu(BuildSurfaceInfoMenu()));
            menu.AddMenuItem("Target Info", () => PushMenu(BuildTargetInfoMenu()),
                () => FlightGlobals.fetch.VesselTarget != null);
            menu.AddMenuItem("Vessel Info", () => PushMenu(BuildVesselInfoMenu()));
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildOrbitInfoMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);

            // These items will have their labels updated dynamically
            menu.AddMenuItem(() => "Apoapsis: " + FormatDistance(vessel != null ? vessel.orbit.ApA : 0));
            menu.AddMenuItem(() => "Periapsis: " + FormatDistance(vessel != null ? vessel.orbit.PeA : 0));
            menu.AddMenuItem(() => "Eccentricity: " + (vessel != null ? vessel.orbit.eccentricity.ToString("F4") : "---"));
            menu.AddMenuItem(() => "Inclination: " + FormatAngle(vessel != null ? vessel.orbit.inclination : 0));
            menu.AddMenuItem(() => "LAN: " + FormatAngle(vessel != null ? vessel.orbit.LAN : 0));
            menu.AddMenuItem(() => "Arg. of PE: " + FormatAngle(vessel != null ? vessel.orbit.argumentOfPeriapsis : 0));
            menu.AddMenuItem(() => "Period: " + FormatTime(vessel != null ? vessel.orbit.period : 0));
            menu.AddMenuItem(() => "Time to AP: " + FormatTime(GetTimeToApoapsis()));
            menu.AddMenuItem(() => "Time to PE: " + FormatTime(GetTimeToPeriapsis()));
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildSurfaceInfoMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.AddMenuItem(() => "Altitude (ASL): " + FormatDistance(vessel != null ? vessel.altitude : 0));
            menu.AddMenuItem(() => "Altitude (AGL): " + FormatDistance(vessel != null ? vessel.radarAltitude : 0));
            menu.AddMenuItem(() => "Latitude: " + FormatAngle(vessel != null ? vessel.latitude : 0));
            menu.AddMenuItem(() => "Longitude: " + FormatAngle(vessel != null ? vessel.longitude : 0));
            menu.AddMenuItem(() => "Surface Speed: " + FormatSpeed(vessel != null ? vessel.srfSpeed : 0));
            menu.AddMenuItem(() => "Vertical Speed: " + FormatSpeed(vessel != null ? vessel.verticalSpeed : 0));
            menu.AddMenuItem(() => "Horizontal Speed: " + FormatSpeed(vessel != null ? vessel.horizontalSrfSpeed : 0));
            menu.AddMenuItem(() => "Heading: " + FormatAngle(GetSurfaceHeading()));
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildTargetInfoMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.AddMenuItem(() => "Distance: " + GetTargetDistanceText());
            menu.AddMenuItem(() => "Relative Velocity: " + GetTargetRelVelText());
            menu.AddMenuItem(() => "Closest Approach: " + GetTargetClosestApproachText());
            menu.AddMenuItem(() => "Time to Closest: " + GetTargetTimeToClosestText());
            menu.AddMenuItem(() => "Rel Inclination: " + GetTargetRelInclinationText());
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }

        private TextMenu BuildVesselInfoMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);
            menu.AddMenuItem(() => "Mass: " + GetVesselMassText());
            menu.AddMenuItem(() => "TWR: " + GetVesselTwrText());
            menu.AddMenuItem(() => "Max Thrust: " + GetVesselMaxThrustText());
            menu.AddMenuItem(() => "Current Thrust: " + GetVesselCurrentThrustText());
            menu.AddMenuItem(() => "Total dV (Vac): " + FormatDeltaV(MechJebProxy.GetTotalVacuumDeltaV(mjCore)));
            menu.AddMenuItem(() => "Total dV (Atm): " + FormatDeltaV(MechJebProxy.GetTotalAtmoDeltaV(mjCore)));
            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }
        #endregion

        #region Settings Menu
        private TextMenu BuildSettingsMenu()
        {
            var menu = new TextMenu();
            menu.labelColor = JUtil.ColorToColorTag(Color.white);
            menu.selectedColor = JUtil.ColorToColorTag(Color.green);

            menu.AddMenuItem("-- THRUST LIMITS --", null);
            menu.AddToggleItem("Prevent Overheats", mjCore.Thrust, MechJebProxy.f_Thrust_LimitToPreventOverheats);
            menu.AddToggleItem("Limit by Max Q", mjCore.Thrust, MechJebProxy.f_Thrust_LimitDynamicPressure);
            menu.AddToggleItem("Limit to Terminal Velocity", mjCore.Thrust, MechJebProxy.f_Thrust_LimitToTerminalVelocity);
            menu.AddToggleItem("Limit Acceleration", mjCore.Thrust, MechJebProxy.f_Thrust_LimitAcceleration);
            menu.AddToggleItem("Limit Throttle", mjCore.Thrust, MechJebProxy.f_Thrust_LimitThrottle);

            menu.AddMenuItem("------", null);

            menu.AddToggleItem("Prevent Flameout", mjCore.Thrust, MechJebProxy.f_Thrust_LimitToPreventFlameout);
            menu.AddMJItem("Flameout Safety", mjCore.Thrust.FlameoutSafetyPct,
                1.0, v => v.ToString("F0") + "%", null, true, 0, true, 100);
            menu.AddToggleItem("Smooth Throttle", mjCore.Thrust, MechJebProxy.f_Thrust_SmoothThrottle);
            menu.AddToggleItem("Manage Intakes", mjCore.Thrust, MechJebProxy.f_Thrust_ManageIntakes);
            menu.AddToggleItem("Differential Throttle", mjCore.Thrust, MechJebProxy.f_Thrust_DifferentialThrottle);

            menu.AddMenuItem("------", null);

            menu.AddMenuItem("-- NODE EXECUTION --", null);
            menu.AddToggleItem("Auto-Warp", mjCore.Node, MechJebProxy.f_Node_Autowarp);

            menu.AddMenuItem("[BACK]", () => PopMenu());

            return menu;
        }
        #endregion

        #region Menu Navigation
        private void PushMenu(TextMenu newMenu)
        {
            if (newMenu != null)
            {
                menuStack.Push(currentMenu);
                currentMenu = newMenu;
            }
        }

        private void PopMenu()
        {
            if (menuStack.Count > 0)
            {
                currentMenu = menuStack.Pop();
            }
        }

        private void GoHome()
        {
            menuStack.Clear();
            currentMenu = topMenu;
        }

        private bool IsAscentAvailable()
        {
            if (vessel == null) return false;
            if (vessel.LandedOrSplashed) return true;

            if (vessel.situation == Vessel.Situations.ORBITING)
            {
                double atmosphere = vessel.mainBody != null ? vessel.mainBody.atmosphereDepth : 0;
                if (atmosphere <= 0) atmosphere = 0;
                return !(vessel.orbit.PeA > atmosphere && vessel.orbit.ApA > atmosphere);
            }

            return true;
        }
        #endregion

        #region Update Loop
        public void Update()
        {
            // Update MechJeb core reference if vessel changed
            if (vessel != activeVessel || mjCore == null)
            {
                mjSmartASS = null;
                mjDockingAutoPilot = null;
                mjRendezvousAutopilot = null;
                mjTranslatron = null;
                mjSpacePlaneAutopilot = null;
                mjLandingPredictions = null;
                mjLandingGuidance = null;
                mjWarpController = null;
                mjStageStats = null;
                mjAirplaneGuidance = null;

                activeVessel = vessel;
                mjCore = vessel.GetMasterMechJeb();
                if (mjCore != null)
                {
                    mjSmartASS = mjCore.GetComputerModule<MechJebModuleSmartASS>();
                    mjDockingAutoPilot = mjCore.GetComputerModule<MechJebModuleDockingAutopilot>();
                    mjRendezvousAutopilot = mjCore.GetComputerModule<MechJebModuleRendezvousAutopilot>();
                    mjTranslatron = mjCore.GetComputerModule<MechJebModuleTranslatron>();
                    mjSpacePlaneAutopilot = mjCore.GetComputerModule<MechJebModuleSpaceplaneAutopilot>();
                    mjLandingPredictions = mjCore.GetComputerModule<MechJebModuleLandingPredictions>();
                    mjLandingGuidance = mjCore.GetComputerModule<MechJebModuleLandingGuidance>();
                    mjWarpController = mjCore.GetComputerModule<MechJebModuleWarpController>();
                    mjStageStats = mjCore.GetComputerModule<MechJebModuleStageStats>();
                    mjAirplaneGuidance = mjCore.GetComputerModule<MechJebModuleAirplaneGuidance>();
                }
            }

            if (mjCore == null)
            {
                return;
            }

            if (!pageActiveState) return;

            // Update tracked items for the current menu only
            UpdateTrackedItems();

            double ut = Planetarium.GetUniversalTime();
            if (ut - lastStageStatsUpdateUT > 1.0)
            {
                mjStageStats?.RequestUpdate();
                lastStageStatsUpdateUT = ut;
            }
        }

        private void UpdateTrackedItems()
        {
            if (mjCore == null || currentMenu == null) return;

            if (currentMenu is TextMenu menu)
            {
                menu.UpdateTrackedItems();
            }

            UpdateSmartASSSelections();
        }

        private void UpdateSmartASSSelections()
        {
            if (mjSmartASS == null) return;

            int currentTarget = (int)mjSmartASS.target;

            UpdateMenuSelectionById(smartassOrbitalMenu, currentTarget);
            UpdateMenuSelectionById(smartassSurfaceMenu, currentTarget);
            UpdateMenuSelectionById(smartassTargetMenu, currentTarget);
        }

        private void UpdateMenuSelectionById(TextMenu menu, int targetId)
        {
            if (menu == null) return;

            for (int i = 0; i < menu.Count; i++)
            {
                bool match = (menu[i].id == targetId);
                menu[i].isSelected = match;
            }
        }
        #endregion

        #region Warp Helpers
        private void WarpToApoapsis()
        {
            if (vessel == null) return;
            double ut = vessel.orbit.NextApoapsisTime(Planetarium.GetUniversalTime());
            mjWarpController?.WarpToUT(ut);
        }

        private void WarpToPeriapsis()
        {
            if (vessel == null) return;
            double ut = vessel.orbit.NextPeriapsisTime(Planetarium.GetUniversalTime());
            mjWarpController?.WarpToUT(ut);
        }

        private void WarpToNode()
        {
            if (vessel == null || vessel.patchedConicSolver == null) return;
            if (vessel.patchedConicSolver.maneuverNodes.Count == 0) return;
            double ut = vessel.patchedConicSolver.maneuverNodes[0].UT;
            mjWarpController?.WarpToUT(ut);
        }

        private void WarpToSOI()
        {
            if (vessel == null) return;
            if (vessel.orbit.patchEndTransition == Orbit.PatchTransitionType.FINAL) return;
            double ut = vessel.orbit.EndUT;
            mjWarpController?.WarpToUT(ut);
        }
        #endregion

        #region Landing Prediction Helpers
        private string GetLandingPredLatitude()
        {
            var result = mjLandingPredictions?.Result;
            if (result == null) return "---";
            return result.EndPosition.Latitude.ToString("F3") + "°";
        }

        private string GetLandingPredLongitude()
        {
            var result = mjLandingPredictions?.Result;
            if (result == null) return "---";
            return result.EndPosition.Longitude.ToString("F3") + "°";
        }

        private string GetLandingPredTime()
        {
            var result = mjLandingPredictions?.Result;
            if (result == null) return "---";
            double dt = result.EndUT - Planetarium.GetUniversalTime();
            return FormatTime(dt);
        }

        private string GetLandingPredGees()
        {
            var result = mjLandingPredictions?.Result;
            if (result == null) return "---";
            double gees = (result?.MaxDragGees ?? 0);
            return gees.ToString("F2");
        }
        #endregion

        #region Formatting Helpers
        private static string FormatDistance(double meters)
        {
            if (double.IsNaN(meters)) return "---";
            if (Math.Abs(meters) >= 1000.0) return (meters / 1000.0).ToString("F1") + " km";
            return meters.ToString("F1") + " m";
        }

        private static string FormatSpeed(double mps)
        {
            if (double.IsNaN(mps)) return "---";
            return mps.ToString("F1") + " m/s";
        }

        private static string FormatDeltaV(double mps)
        {
            if (double.IsNaN(mps)) return "---";
            return mps.ToString("F0") + " m/s";
        }

        private static string FormatAngle(double deg)
        {
            if (double.IsNaN(deg)) return "---";
            return deg.ToString("F2") + "°";
        }

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds)) return "---";
            if (seconds < 0) seconds = 0;
            return KSPUtil.PrintTimeCompact(seconds, false);
        }

        private double GetTimeToApoapsis()
        {
            if (vessel == null) return 0;
            double ut = vessel.orbit.NextApoapsisTime(Planetarium.GetUniversalTime());
            return ut - Planetarium.GetUniversalTime();
        }

        private double GetTimeToPeriapsis()
        {
            if (vessel == null) return 0;
            double ut = vessel.orbit.NextPeriapsisTime(Planetarium.GetUniversalTime());
            return ut - Planetarium.GetUniversalTime();
        }

        private string GetStageDeltaVText(MechJebCore core, bool vacuum)
        {
            var stats = vacuum ? core.StageStats.VacStats : core.StageStats.AtmoStats;
            if (stats == null || stats.Count == 0) return "---";
            double dv = stats[0].DeltaV;
            return FormatDeltaV(dv);
        }

        private string GetTargetDistanceText()
        {
            if (vessel == null || FlightGlobals.fetch.VesselTarget == null) return "---";
            ITargetable target = FlightGlobals.fetch.VesselTarget;
            Vector3d tgtPos = target.GetTransform().position;
            return FormatDistance(Vector3d.Distance(vessel.GetWorldPos3D(), tgtPos));
        }

        private string GetTargetRelVelText()
        {
            if (vessel == null || FlightGlobals.fetch.VesselTarget == null) return "---";
            Orbit targetOrbit = FlightGlobals.fetch.VesselTarget.GetOrbit();
            if (targetOrbit == null) return "---";
            double ut = Planetarium.GetUniversalTime();
            Vector3d v1 = vessel.orbit.SwappedOrbitalVelocityAtUT(ut);
            Vector3d v2 = targetOrbit.SwappedOrbitalVelocityAtUT(ut);
            return FormatSpeed((v1 - v2).magnitude);
        }

        private string GetTargetClosestApproachText()
        {
            if (vessel == null || FlightGlobals.fetch.VesselTarget == null) return "---";
            Orbit targetOrbit = FlightGlobals.fetch.VesselTarget.GetOrbit();
            if (targetOrbit == null) return "---";
            double dist = vessel.orbit.NextClosestApproachDistance(targetOrbit, Planetarium.GetUniversalTime());
            return FormatDistance(dist);
        }

        private string GetTargetTimeToClosestText()
        {
            if (vessel == null || FlightGlobals.fetch.VesselTarget == null) return "---";
            Orbit targetOrbit = FlightGlobals.fetch.VesselTarget.GetOrbit();
            if (targetOrbit == null) return "---";
            double ut = vessel.orbit.NextClosestApproachTime(targetOrbit, Planetarium.GetUniversalTime());
            return FormatTime(ut - Planetarium.GetUniversalTime());
        }

        private string GetTargetRelInclinationText()
        {
            if (vessel == null || FlightGlobals.fetch.VesselTarget == null) return "---";
            Orbit targetOrbit = FlightGlobals.fetch.VesselTarget.GetOrbit();
            if (targetOrbit == null) return "---";
            double rel = Vector3d.Angle(vessel.orbit.GetOrbitNormal(), targetOrbit.GetOrbitNormal());
            return rel.ToString("F2") + "°";
        }

        private string GetVesselMassText()
        {
            if (vessel == null) return "---";
            return vessel.GetTotalMass().ToString("F2") + " t";
        }

        private string GetVesselTwrText()
        {
            if (vessel == null) return "---";
            double g = vessel.mainBody != null ? vessel.mainBody.GeeASL * 9.80665 : 9.80665;
            double thrust = GetMaxThrust();
            double mass = vessel.GetTotalMass();
            if (mass <= 0) return "---";
            return (thrust / (mass * g)).ToString("F2");
        }

        private string GetVesselMaxThrustText()
        {
            if (vessel == null) return "---";
            return GetMaxThrust().ToString("F0") + " kN";
        }

        private string GetVesselCurrentThrustText()
        {
            if (vessel == null) return "---";
            return GetCurrentThrust().ToString("F0") + " kN";
        }

        private double GetSurfaceHeading()
        {
            if (vessel == null || vessel.ReferenceTransform == null) return 0;
            return vessel.ReferenceTransform.rotation.eulerAngles.y;
        }

        private double GetMaxThrust()
        {
            if (vessel == null) return 0;
            double max = 0;
            var engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            for (int i = 0; i < engines.Count; i++)
            {
                ModuleEngines engine = engines[i];
                if (engine == null) continue;
                double limiter = engine.thrustPercentage / 100.0;
                max += engine.maxThrust * limiter;
            }
            return max;
        }

        private double GetCurrentThrust()
        {
            if (vessel == null) return 0;
            double current = 0;
            var engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            for (int i = 0; i < engines.Count; i++)
            {
                ModuleEngines engine = engines[i];
                if (engine == null) continue;
                current += engine.finalThrust;
            }
            return current;
        }
        #endregion

        #region Button Handlers
        public void PageActive(bool active, int pageNumber)
        {
            pageActiveState = active;
        }

        // Alias for compatibility with configs that use ClickProcessor
        public void ClickProcessor(int buttonID)
        {
            ButtonProcessor(buttonID);
        }

        public void ButtonProcessor(int buttonID)
        {
            if (!pageActiveState || currentMenu == null) return;

            if (buttonID == buttonUp)
            {
                currentMenu.PreviousItem();
            }
            else if (buttonID == buttonDown)
            {
                currentMenu.NextItem();
            }
            else if (buttonID == buttonEnter)
            {
                currentMenu.SelectItem();
                UpdateTrackedItems();
            }
            else if (buttonID == buttonEsc)
            {
                PopMenu();
            }
            else if (buttonID == buttonHome)
            {
                GoHome();
            }
            else if (buttonID == buttonRight)
            {
                // For value items, increase
                IncrementCurrentValue(1);
            }
            else if (buttonID == buttonLeft)
            {
                // For value items, decrease
                IncrementCurrentValue(-1);
            }
        }

        private void IncrementCurrentValue(int direction)
        {
			if (mjCore == null || currentMenu == null) return;
            currentMenu.IncrementCurrentValue(direction);
		}

        #endregion

        #region Render

        StringBuilder stringBuilder = new StringBuilder();
        public string ShowMenu(int screenWidth, int screenHeight)
        {
            if (!MechJebProxy.IsAvailable)
            {
                return "MechJeb not available\n\n" + (MechJebProxy.InitializationError ?? "Unknown error");
            }

            if (mjCore == null)
            {
                return "No MechJeb core found on this vessel";
            }

            UpdateTrackedItems();

            stringBuilder.Clear();
            stringBuilder.AppendLine(pageTitle);
            currentMenu.ShowMenu(stringBuilder, screenWidth, screenHeight - 1);
            return stringBuilder.ToString();
        }
        #endregion
    }

    internal static class TextMenuExtensions
    {
        public static void AddMJItem(this TextMenu menu, string label,
            EditableDouble editableDouble,
            double step, Func<double, string> format,
            Func<bool> enabledCheck = null,
            bool hasMin = false, double min = 0,
            bool hasMax = false, double max = 0)
        {
            if (editableDouble == null)
            {
                Log.Error($"editableDouble is null trying to add numeric item {label}");
                return;
            }
            menu.AddNumericItem(label, () => editableDouble.Val, val => editableDouble.Val = val, step, format, enabledCheck, hasMin, min, hasMax, max);
        }

        public static void AddMJItem(this TextMenu menu, string label,
            EditableDoubleMult editableDoubleMult,
            double step, Func<double, string> format,
            Func<bool> enabledCheck = null,
            bool hasMin = false, double min = 0,
            bool hasMax = false, double max = 0)
        {
            if (editableDoubleMult == null)
            {
                Log.Error($"editableDoubleMult is null trying to add numeric item {label}");
                return;
            }
            menu.AddNumericItem(label, () => editableDoubleMult.Val, val => editableDoubleMult.Val = val, step, format, enabledCheck, hasMin, min, hasMax, max);
        }

        public static void AddMJItem(this TextMenu menu, string label,
            EditableInt editableInt,
            double step, Func<double, string> format,
            Func<bool> enabledCheck = null,
            bool hasMin = false, double min = 0,
            bool hasMax = false, double max = 0)
        {
            if (editableInt == null)
            {
                Log.Error($"editableInt is null trying to add numeric item {label}");
                return;
            }
            menu.AddNumericItem(label, () => editableInt.Val, val => editableInt.Val = (int)val, step, format, enabledCheck, hasMin, min, hasMax, max);
        }
    }
}
