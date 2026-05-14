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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MechJebLib.FuelFlowSimulation;
using MuMech;
using static MuMech.MechJebModuleSmartASS;

namespace JSI
{
    /// <summary>
    /// JSIMechJeb provides an interface with the MechJeb plugin using
    /// direct API access (hard dependency on MechJeb).
    /// </summary>
    internal class JSIMechJeb : IJSIModule
    {
        static private readonly bool mjFound;

        private double deltaV, deltaVStage;

        private double lastUpdate = 0.0;
        private double landingLat, landingLon, landingAlt, landingErr = -1.0, landingTime = -1.0;

        static JSIMechJeb()
        {
            // With a hard dependency we still check the loaded assemblies so the module
            // behaves reasonably if MechJeb assembly is missing at runtime.
            try
            {
                var loadedMechJebAssy = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.name == "MechJeb2");
                mjFound = (loadedMechJebAssy != null);

                if (mjFound)
                {
                    IJSIModule.RegisterModule(typeof(JSIMechJeb));
                }
            }
            catch (Exception e)
            {
                JUtil.LogErrorMessage(null, "Exception initializing JSIMechJeb: {0}", e);
                mjFound = false;
            }
        }

        static public bool IsInstalled => mjFound;

        public JSIMechJeb(Vessel myVessel) : base(myVessel)
        {
            JUtil.LogInfo(this, "A supported version of MechJeb is {0}", (mjFound) ? "present" : "not available");
        }

        #region Internal Methods (direct API)
        /// <summary>
        /// Returns the master MechJeb instance for the vessel using the MuMech API.
        /// Returns a strongly-typed `MechJebCore`.
        /// </summary>
        static private MechJebCore GetMasterMechJeb(Vessel vessel)
        {
            if (!mjFound || vessel == null)
            {
                return null;
            }

            try
            {
                // Direct call into MuMech.VesselExtensions
                return VesselExtensions.GetMasterMechJeb(vessel) as MechJebCore;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Fetch a MechJeb ComputerModule directly from the MechJeb core.
        /// </summary>
        static private ComputerModule GetComputerModule(MechJebCore masterMechJeb, string computerModule)
        {
            if (masterMechJeb == null)
            {
                return null;
            }

            try
            {
                return masterMechJeb.GetComputerModule(computerModule);
            }
            catch
            {
                return null;
            }
        }

        static private T GetComputerModule<T>(MechJebCore mjCore) where T : ComputerModule
        {
            if (mjCore == null)
            {
                return null;
            }

            return mjCore.GetComputerModule<T>();
        }

        /// <summary>
        /// Returns whether the supplied MechJeb ComputerModule is enabled
        /// </summary>
        static private bool ModuleEnabled(ComputerModule module)
        {
            if (module == null)
            {
                return false;
            }

            try
            {
                return module.Enabled;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Return the latest landing simulation results, or null if there aren't any.
        /// </summary>
        static private ReentrySimulation.Result GetLandingResults(MechJebCore masterMechJeb)
        {
            try
            {
                var predictor = GetComputerModule<MechJebModuleLandingPredictions>(masterMechJeb);
                if (predictor != null && ModuleEnabled(predictor))
                {
                    return predictor.GetResult();
                }
            }
            catch { }

            return null;
        }

        private void EnactTargetAction(Vessel vessel, Target action)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return;
            }

            try
            {
                var activeSmartass = GetComputerModule<MechJebModuleSmartASS>(activeJeb);
                if (activeSmartass != null)
                {
                    // SmartASS.target is an int enum in MJ
                    activeSmartass.target = action;
                    activeSmartass.Engage(true);
                }
            }
            catch (Exception e)
            {
                JUtil.LogErrorMessage(null, "EnactTargetAction exception: {0}", e);
            }
        }

        private bool ReturnTargetState(Target action)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }

            try
            {
                var activeSmartass = GetComputerModule<MechJebModuleSmartASS>(activeJeb);
                if (activeSmartass != null)
                {
                    return (activeSmartass.target == action);
                }
            }
            catch { }

            return false;
        }
        #endregion

        #region Updater methods (direct)
        /// <summary>
        /// Update the landing prediction stats
        /// </summary>
        private void UpdateLandingStats(MechJebCore activeJeb)
        {
            if (Planetarium.GetUniversalTime() - lastUpdate < 0.5)
            {
                // Don't update more than twice a second.
                return;
            }

            if (activeJeb == null)
            {
                return;
            }

            try
            {
                var result = GetLandingResults(activeJeb);
                if (result != null)
                {
                    if (result.Outcome == ReentrySimulation.Outcome.LANDED)
                    {
                        var endPosition = result.EndPosition;
                        // AbsoluteVector exposes latitude / longitude directly
                        landingLat = endPosition.Latitude;
                        landingLon = endPosition.Longitude;

                        // Small fudge factor - we define 0,0 as "invalid"
                        if (landingLat == 0.0)
                        {
                            landingLat = 0.0001;
                        }
                        if (landingLon == 0.0)
                        {
                            landingLon = 0.0001;
                        }

                        landingAlt = FinePrint.Utilities.CelestialUtilities.TerrainAltitude(vessel.mainBody, landingLat, landingLon);

                        // Target -> get lat/lon using the MechJeb API
                        var target = activeJeb.Target;
                        if (target != null)
                        {
                            // targetLatitude/targetLongitude may be EditableAngle/AbsoluteVector/etc.
                            // Dynamic cast will invoke implicit conversions if available.
                            double targetLat = target.targetLatitude;
                            double targetLon = target.targetLongitude;
                            double targetAlt = FinePrint.Utilities.CelestialUtilities.TerrainAltitude(vessel.mainBody, targetLat, targetLon);

                            landingErr = Vector3d.Distance(vessel.mainBody.GetRelSurfacePosition(landingLat, landingLon, landingAlt),
                                                        vessel.mainBody.GetRelSurfacePosition(targetLat, targetLon, targetAlt));
                        }

                        landingTime = result.EndUT;

                        lastUpdate = Planetarium.GetUniversalTime();
                    }
                }
            }
            catch (Exception e)
            {
                JUtil.LogErrorMessage(this, "Exception trap in UpdateLandingStats(): {0}", e);
            }
        }

        /// <summary>
        /// Updates dV stats (dV and dVStage) using MechJeb StageStats directly
        /// </summary>
        private void UpdateDeltaVStats(MechJebCore activeJeb)
        {
            try
            {
                var stagestats = GetComputerModule<MechJebModuleStageStats>(activeJeb);
                if (stagestats == null)
                {
                    deltaV = deltaVStage = 0.0;
                    return;
                }

                // Request MJ to refresh its stats for this vessel
                stagestats.RequestUpdate();

                int atmStatsLength = 0, vacStatsLength = 0;

                var atmStatsO = stagestats.AtmoStats;
                var vacStatsO = stagestats.VacStats;

                if (atmStatsO != null)
                {
                    try { atmStatsLength = atmStatsO.Count; } catch { atmStatsLength = 0; }
                }

                if (vacStatsO != null)
                {
                    try { vacStatsLength = vacStatsO.Count; } catch { vacStatsLength = 0; }
                }

                deltaV = deltaVStage = 0.0;

                if (atmStatsLength > 0 && atmStatsLength == vacStatsLength)
                {
                    double atmospheresLocal = vessel.staticPressurekPa * PhysicsGlobals.KpaToAtmospheres;

                    for (int i = 0; i < atmStatsLength; ++i)
                    {
                        double atm = stagestats.AtmoStats[i].DeltaV;
                        double vac = stagestats.VacStats[i].DeltaV;
                        double stagedV = UtilMath.LerpUnclamped(vac, atm, atmospheresLocal);

                        deltaV += stagedV;

                        if (i == (atmStatsLength - 1))
                        {
                            deltaVStage = stagedV;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                JUtil.LogErrorMessage(this, "Exception trap in UpdateDeltaVStats(): {0}", e);
            }
        }
        #endregion

        #region External interface (unchanged signatures, direct API)
        public bool GetMechJebAvailable()
        {
            try
            {
                return (GetMasterMechJeb(vessel) != null);
            }
            catch { return false; }
        }

        public void SetSmartassMode(Target t)
        {
            EnactTargetAction(vessel, t);
        }

        public MechJebModuleSmartASS.Target GetSmartassMode()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var activeSmartass = GetComputerModule<MechJebModuleSmartASS>(activeJeb);
                if (activeSmartass != null)
                {
                    return activeSmartass.target;
                }
            }

            return Target.OFF;
        }

        public double GetLandingError()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                UpdateLandingStats(activeJeb);
                return landingErr;
            }
            else
            {
                return -1.0;
            }
        }

        public double GetLandingLatitude()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                UpdateLandingStats(activeJeb);
                return landingLat;
            }
            else
            {
                return 0.0;
            }
        }

        public double GetLandingLongitude()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                UpdateLandingStats(activeJeb);
                return landingLon;
            }
            else
            {
                return 0.0;
            }
        }

        public double GetLandingTime()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                UpdateLandingStats(activeJeb);
                return Math.Max(0.0, landingTime - Planetarium.GetUniversalTime());
            }
            else
            {
                return 0.0;
            }
        }

        public double GetLandingAltitude()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                UpdateLandingStats(activeJeb);
                return landingAlt;
            }
            else
            {
                return 0.0;
            }
        }

        public double GetDeltaV()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                UpdateDeltaVStats(activeJeb);
                return deltaV;
            }
            else
            {
                return double.NaN;
            }
        }

        public double GetStageDeltaV()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                UpdateDeltaVStats(activeJeb);
                return deltaVStage;
            }
            else
            {
                return double.NaN;
            }
        }

        public double GetLaunchAltitude()
        {
            double alt = 0.0;
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ascent = GetComputerModule<MechJebModuleAscentSettings>(activeJeb);
                if (ascent != null)
                {
                    try
                    {
                        var desiredAlt = ascent.DesiredOrbitAltitude;
                        // desiredAlt may be EditableDoubleMult - dynamic cast handles conversion
                        alt = desiredAlt.Val;
                    }
                    catch { }
                }
            }
            return alt;
        }

        public void SetLaunchAltitude(double altitude)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ascent = GetComputerModule<MechJebModuleAscentSettings>(activeJeb);
                if (ascent != null)
                {
                    try
                    {
                        var desiredAlt = ascent.DesiredOrbitAltitude;
                        desiredAlt.Val = altitude;
                    }
                    catch { }
                }
            }
        }

        public double GetLaunchInclination()
        {
            double angle = 0.0;
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ascent = GetComputerModule<MechJebModuleAscentSettings>(activeJeb);
                if (ascent != null)
                {
                    try
                    {
                        var inclination = ascent.DesiredInclination;
                        angle = inclination.Val;
                    }
                    catch { }
                }
            }
            return angle;
        }

        public void SetLaunchInclination(double inclination)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ascent = GetComputerModule<MechJebModuleAscentSettings>(activeJeb);
                if (ascent != null)
                {
                    try
                    {
                        ascent.DesiredInclination.Val = inclination;
                    }
                    catch { }
                }
            }
        }

        public double GetForceRollAngle()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var activeSmartass = GetComputerModule<MechJebModuleSmartASS>(activeJeb);
                if (activeSmartass != null)
                {
                    try
                    {
                        return activeSmartass.rol.Val;
                    }
                    catch { }
                }
            }
            return 0.0;
        }

        public double GetTerminalVelocity()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                try
                {
                    var vesselState = activeJeb.VesselState;
                    if (vesselState != null)
                    {
                        double value = vesselState.TerminalVelocity();
                        return (double.IsNaN(value)) ? double.PositiveInfinity : value;
                    }
                }
                catch { }
            }
            return double.NaN;
        }

        public bool PositionTargetExists()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                try
                {
                    var target = activeJeb.Target;
                    if (target != null)
                    {
                        return target.PositionTargetExists;
                    }
                }
                catch { }
            }
            return false;
        }

        public bool AutopilotEnabled()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }

            try
            {
                var attitude = activeJeb.Attitude;
                if (ModuleEnabled(attitude))
                {
                    var activeSmartass = GetComputerModule<MechJebModuleSmartASS>(activeJeb);
                    var users = attitude.Users;
                    if (users != null && activeSmartass != null)
                    {
                        return users.Contains(activeSmartass);
                    }
                }
            }
            catch { }

            return false;
        }

        private bool ForceRollState(double roll)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }

            try
            {
                var activeSmartass = GetComputerModule<MechJebModuleSmartASS>(activeJeb);
                if (activeSmartass != null)
                {
                    bool force = (bool)activeSmartass.forceRol;
                    double rol = activeSmartass.rol.Val;
                    return force && (Math.Abs(roll - rol) < 0.5);
                }
            }
            catch { }

            return false;
        }

        public bool GetModuleExists(string moduleName)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            var module = GetComputerModule(activeJeb, moduleName);
            return (module != null);
        }

        public void ForceRoll(bool state, double roll)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var activeSmartass = GetComputerModule<MechJebModuleSmartASS>(activeJeb);
                if (activeSmartass != null)
                {
                    try
                    {
                        if (state)
                        {
                            activeSmartass.rol.Val = roll;
                        }
                        activeSmartass.forceRol = state;
                        activeSmartass.Engage(true);
                    }
                    catch { }
                }
            }
        }

        public void CircularizeAtAltitude(double altitude)
        {
            if (GetMechJebAvailable() && altitude >= vessel.orbit.PeA && altitude <= vessel.orbit.ApA)
            {
                double UT = vessel.orbit.GetNextTimeOfRadius(Planetarium.GetUniversalTime(), vessel.orbit.referenceBody.Radius + altitude);

                Vector3d dV = OrbitalManeuverCalculator.DeltaVToCircularize(vessel.orbit, UT);

                JUtil.RemoveAllNodes(vessel.patchedConicSolver);

                VesselExtensions.PlaceManeuverNode(vessel, vessel.orbit, dV, UT);
            }
        }

        public void ChangeApoapsis(double altitude)
        {
            if (GetMechJebAvailable() && altitude >= vessel.orbit.PeA)
            {
                double UT = vessel.orbit.GetNextPeriapsisTime(Planetarium.GetUniversalTime());

                Vector3d dV = OrbitalManeuverCalculator.DeltaVToChangeApoapsis(vessel.orbit, UT, vessel.orbit.referenceBody.Radius + altitude);

                JUtil.RemoveAllNodes(vessel.patchedConicSolver);

                VesselExtensions.PlaceManeuverNode(vessel, vessel.orbit, dV, UT);
            }
        }

        public void ChangePeriapsis(double altitude)
        {
            if (GetMechJebAvailable() && altitude <= vessel.orbit.ApA)
            {
                double UT = vessel.orbit.GetNextApoapsisTime(Planetarium.GetUniversalTime());

                Vector3d dV = OrbitalManeuverCalculator.DeltaVToChangePeriapsis(vessel.orbit, UT, vessel.orbit.referenceBody.Radius + altitude);

                JUtil.RemoveAllNodes(vessel.patchedConicSolver);

                VesselExtensions.PlaceManeuverNode(vessel, vessel.orbit, dV, UT);
            }
        }

        public void CircularizeAt(double UT)
        {
            Vector3d dV = OrbitalManeuverCalculator.DeltaVToCircularize(vessel.orbit, UT);

            JUtil.RemoveAllNodes(vessel.patchedConicSolver);

            VesselExtensions.PlaceManeuverNode(vessel, vessel.orbit, dV, UT);
        }

        public double SpaceplaneHoldAltitude()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ap = GetComputerModule<MechJebModuleAirplaneAutopilot>(activeJeb);
                if (ap != null)
                {
                    try
                    {
                        return ap.AltitudeTarget;
                    }
                    catch { }
                }
            }
            return 0.0;
        }

        public void SetSpaceplaneHoldAltitude(double altitude)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ap = GetComputerModule<MechJebModuleAirplaneAutopilot>(activeJeb);
                if (ap != null)
                {
                    try
                    {
                        ap.AltitudeTarget = altitude;
                    }
                    catch { }
                }
            }
        }

        public bool SpaceplaneAltitudeProximity()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ap = GetComputerModule<MechJebModuleAirplaneAutopilot>(activeJeb);
                if (ap != null)
                {
                    try
                    {
                        double goalAltitude = ap.AltitudeTarget;
                        double currentAltitude = vessel.altitude;
                        return (Math.Abs(currentAltitude - goalAltitude) <= 500.0);
                    }
                    catch { }
                }
            }
            return false;
        }

        public double SpaceplaneHoldHeading()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ap = GetComputerModule<MechJebModuleAirplaneAutopilot>(activeJeb);
                if (ap != null)
                {
                    try
                    {
                        return ap.HeadingTarget;
                    }
                    catch { }
                }
            }
            return 0.0;
        }

        public void SetSpaceplaneHoldHeading(double heading)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ap = GetComputerModule<MechJebModuleAirplaneAutopilot>(activeJeb);
                if (ap != null)
                {
                    try
                    {
                        ap.HeadingTarget = heading;
                    }
                    catch { }
                }
            }
        }

        public double SpaceplaneGlideslope()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ap = GetComputerModule<MechJebModuleSpaceplaneAutopilot>(activeJeb);
                if (ap != null)
                {
                    try
                    {
                        return ap.glideslope.Val;
                    }
                    catch { }
                }
            }
            return 0.0;
        }

        public void SetSpaceplaneGlideslope(double angle)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ap = GetComputerModule<MechJebModuleSpaceplaneAutopilot>(activeJeb);
                if (ap != null)
                {
                    try
                    {
                        ap.glideslope.Val = angle;
                    }
                    catch { }
                }
            }
        }
        #endregion

        #region MechJebRPMButtons (direct API implementations)
        public void ButtonNodeExecute(bool state)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var node = activeJeb.Node;
                var mp = GetComputerModule<MechJebModuleManeuverPlanner>(activeJeb);
                if (node != null && mp != null)
                {
                    if (state)
                    {
                        if (!ModuleEnabled(node))
                        {
                            node.ExecuteOneNode(mp);
                        }
                    }
                    else
                    {
                        node.Abort();
                    }
                }
            }
        }

        public bool ButtonNodeExecuteState()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb != null)
            {
                var ap = activeJeb.Node;
                return ModuleEnabled(ap);
            }
            else
            {
                return false;
            }
        }

        public void ButtonAscentGuidance(bool state)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            var ap = activeJeb?.Ascent;
            if (ap == null)
            {
                return;
            }

            MechJebProxy.SetAscentAutopilotEngaged(activeJeb, state);
        }

        public bool ButtonAscentGuidanceState()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            var ap = activeJeb?.Ascent;
            return ap != null && ap.Enabled;
        }

        public void ButtonDockingGuidance(bool state)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return;
            }

            var autopilot = GetComputerModule<MechJebModuleDockingAutopilot>(activeJeb);
            var autopilotController = GetComputerModule<MechJebModuleDockingGuidance>(activeJeb);

            if (autopilot != null && autopilotController != null)
            {
                var users = autopilot.Users;
                if (users != null)
                {
                    if (ModuleEnabled(autopilot))
                    {
                        users.Remove(autopilotController);
                    }
                    else if (FlightGlobals.fetch.VesselTarget is ModuleDockingNode)
                    {
                        users.Add(autopilotController);
                    }
                }
            }
        }

        public bool ButtonDockingGuidanceState()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }
            var ap = GetComputerModule<MechJebModuleDockingAutopilot>(activeJeb);
            return ModuleEnabled(ap);
        }

        public void ButtonPlotHohmannTransfer(bool state)
        {
            if (!ButtonPlotHohmannTransferState())
            {
                return;
            }

            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return;
            }

            try
            {
                var target = activeJeb.Target;
                Orbit targetOrbit = (Orbit)target.TargetOrbit;
                Orbit o = vessel.orbit;
                Vector3d dV;
                double nodeUT = 0.0;

                if (o.referenceBody == targetOrbit.referenceBody)
                {
                    Vector3d dV2;
                    double nodeUT2;

                    // TODO: should we use the 2nd one sometimes?
                    (dV, nodeUT, dV2, nodeUT2) = OrbitalManeuverCalculator.DeltaVAndTimeForHohmannTransfer(o, targetOrbit, Planetarium.GetUniversalTime());
                }
                else
                {
                    dV = OrbitalManeuverCalculator.DeltaVAndTimeForInterplanetaryTransferEjection(o, Planetarium.GetUniversalTime(), targetOrbit, true, out nodeUT);
                }

                JUtil.RemoveAllNodes(vessel.patchedConicSolver);

                VesselExtensions.PlaceManeuverNode(vessel, o, dV, nodeUT);
            }
            catch (Exception e)
            {
                JUtil.LogErrorMessage(this, "ButtonPlotHohmannTransfer exception: {0}", e);
            }
        }

        public bool ButtonPlotHohmannTransferState()
        {
            if (!mjFound)
            {
                return false;
            }
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }

            var target = activeJeb.Target;
            if (target == null)
            {
                return false;
            }

            try
            {
                if (!target.NormalTargetExists)
                {
                    return false;
                }

                Orbit o = vessel.orbit;
                if (o.eccentricity > 0.2)
                {
                    return false;
                }

                Orbit targetOrbit = (Orbit)target.TargetOrbit;
                if (o.referenceBody == targetOrbit.referenceBody)
                {
                    if (targetOrbit.eccentricity >= 1.0)
                    {
                        return false;
                    }

                    if (o.RelativeInclination_DEG(targetOrbit) > 30.0 && o.RelativeInclination_DEG(targetOrbit) < 150.0)
                    {
                        return false;
                    }
                }
                else
                {
                    if (o.referenceBody.referenceBody == null)
                    {
                        return false;
                    }
                    if (o.referenceBody.referenceBody != targetOrbit.referenceBody)
                    {
                        return false;
                    }
                    if (o.referenceBody.orbit.RelativeInclination_DEG(targetOrbit) > 30.0)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void ButtonLandingGuidance(bool state)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return;
            }

            var autopilot = GetComputerModule<MechJebModuleLandingAutopilot>(activeJeb);
            if (autopilot == null)
            {
                return;
            }

            try
            {
                bool enabled = ModuleEnabled(autopilot);
                if (state != enabled)
                {
                    var landingGuidanceAP = GetComputerModule<MechJebModuleLandingGuidance>(activeJeb);
                    if (state)
                    {
                        var target = activeJeb.Target;
                        if (landingGuidanceAP != null)
                        {
                            if (target != null && target.PositionTargetExists)
                            {
                                autopilot.LandAtPositionTarget(landingGuidanceAP);
                            }
                            else
                            {
                                autopilot.LandUntargeted(landingGuidanceAP);
                            }
                        }
                    }
                    else
                    {
                        autopilot.StopLanding();
                    }
                }
            }
            catch { }
        }

        public bool ButtonLandingGuidanceState()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }
            var ap = GetComputerModule<MechJebModuleLandingAutopilot>(activeJeb);
            return ModuleEnabled(ap);
        }

        public void ButtonForceRoll(bool state)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return;
            }

            var activeSmartass = GetComputerModule<MechJebModuleSmartASS>(activeJeb);
            if (activeSmartass != null)
            {
                activeSmartass.forceRol = state;
                activeSmartass.Engage(true);
            }
        }

        public bool ButtonForceRollState()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }

            var activeSmartass = GetComputerModule<MechJebModuleSmartASS>(activeJeb);
            if (activeSmartass != null)
            {
                try
                {
                    return (bool)activeSmartass.forceRol;
                }
                catch { }
            }

            return false;
        }

        public void ButtonForceRoll0(bool state) { ForceRoll(state, 0.0); }
        public bool ButtonForceRoll0State() { return ForceRollState(0.0); }

        public void ButtonForceRoll90(bool state) { ForceRoll(state, 90.0); }
        public bool ButtonForceRoll90State() { return ForceRollState(90.0); }

        public void ButtonForceRoll180(bool state) { ForceRoll(state, 180.0); }
        public bool ButtonForceRoll180State() { return ForceRollState(180.0); }

        public void ButtonForceRoll270(bool state) { ForceRoll(state, -90.0); }
        public bool ButtonForceRoll270State() { return ForceRollState(-90.0); }

        public void ButtonEnableLandingPrediction(bool state)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return;
            }

            var predictor = GetComputerModule<MechJebModuleLandingPredictions>(activeJeb);
            var landingGuidanceAP = GetComputerModule<MechJebModuleLandingGuidance>(activeJeb);

            if (predictor != null && landingGuidanceAP != null)
            {
                var users = predictor.Users;
                if (users != null)
                {
                    if (state)
                    {
                        users.Add(landingGuidanceAP);
                    }
                    else
                    {
                        users.Remove(landingGuidanceAP);
                    }
                }
            }
        }

        public bool ButtonEnableLandingPredictionState()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }

            var ap = GetComputerModule<MechJebModuleLandingPredictions>(activeJeb);
            return ModuleEnabled(ap);
        }

        public void ButtonRendezvousAutopilot(bool state)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return;
            }

            var autopilot = GetComputerModule<MechJebModuleRendezvousAutopilot>(activeJeb);
            var autopilotController = GetComputerModule<MechJebModuleRendezvousAutopilotWindow>(activeJeb);

            if (autopilot != null && autopilotController != null)
            {
                var users = autopilot.Users;
                if (users != null)
                {
                    if (state)
                    {
                        users.Add(autopilotController);
                    }
                    else
                    {
                        users.Remove(autopilotController);
                    }
                }
            }
        }

        public bool ButtonRendezvousAutopilotState()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }
            var ap = GetComputerModule<MechJebModuleRendezvousAutopilot>(activeJeb);
            return ModuleEnabled(ap);
        }

        public void ButtonSpaceplaneHoldHeading(bool state)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return;
            }

            var ap = GetComputerModule<MechJebModuleAirplaneAutopilot>(activeJeb);
            if (ap != null)
            {
                if (state)
                {
                    ap.EnableHeadingHold();
                    ap.EnableAltitudeHold();
                }
                else
                {
                    ap.DisableHeadingHold();
                    ap.DisableAltitudeHold();
                }
            }
        }

        public bool ButtonSpaceplaneHoldHeadingState()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null) return false;
            var ap = GetComputerModule<MechJebModuleAirplaneAutopilot>(activeJeb);
            if (ap != null)
            {
                try
                {
                    return (ap.HeadingHoldEnabled);
                }
                catch { }
            }
            return false;
        }

        public void ButtonSpaceplaneAutoland(bool state)
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return;
            }

            var ap = GetComputerModule<MechJebModuleSpaceplaneAutopilot>(activeJeb);
            var controller = GetComputerModule<MechJebModuleSpaceplaneGuidance>(activeJeb);
            if (ap != null && controller != null)
            {
                if (state)
                {
                    ap.Autoland(controller);
                }
                else
                {
                    ap.AutopilotOff();
                }
            }
        }

        public bool ButtonSpaceplaneAutolandState()
        {
            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null) return false;
            var ap = GetComputerModule<MechJebModuleSpaceplaneAutopilot>(activeJeb);
            if (ap != null)
            {
                return ap.Users.Count > 0;
            }
            return false;
        }

        public void MatchVelocities(bool state)
        {
            if (!MatchVelocitiesState())
            {
                return;
            }

                try
                {
                    MechJebCore activeJeb = GetMasterMechJeb(vessel);
                    if (activeJeb == null)
                    {
                        return;
                    }

                    var target = activeJeb.Target;
                if (target == null) return;

                Orbit targetOrbit = (Orbit)target.TargetOrbit;
                Orbit o = vessel.orbit;
                Vector3d dV;
                double nodeUT;
                JUtil.GetClosestApproach(o, targetOrbit, out nodeUT);

                dV = OrbitalManeuverCalculator.DeltaVToMatchVelocities(o, nodeUT, targetOrbit);

                JUtil.RemoveAllNodes(vessel.patchedConicSolver);

                VesselExtensions.PlaceManeuverNode(vessel, o, dV, nodeUT);
            }
            catch (Exception e)
            {
                JUtil.LogErrorMessage(this, "MatchVelocities tripped an exception: {0}", e);
            }
        }

        public bool MatchVelocitiesState()
        {
            if (!mjFound)
            {
                return false;
            }

            MechJebCore activeJeb = GetMasterMechJeb(vessel);
            if (activeJeb == null)
            {
                return false;
            }

            var target = activeJeb.Target;
            if (target == null)
            {
                return false;
            }

            try
            {
                return target.NormalTargetExists;
            }
            catch
            {
                return false;
            }
        }

        // Off button
        public void ButtonOff(bool state)
        {
            EnactTargetAction(vessel, Target.OFF);
        }

        public bool ButtonOffState()
        {
            return ReturnTargetState(Target.OFF);
        }

        public void ButtonNode(bool state)
        {
            if (vessel.patchedConicSolver != null)
            {
                if (state && vessel.patchedConicSolver.maneuverNodes.Count > 0)
                {
                    EnactTargetAction(vessel, Target.NODE);
                }
                else if (!state)
                {
                    EnactTargetAction(vessel, Target.OFF);
                }
            }
        }

        public bool ButtonNodeState()
        {
            return ReturnTargetState(Target.NODE);
        }

        public void ButtonKillRot(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.KILLROT : Target.OFF);
        }

        public bool ButtonKillRotState()
        {
            return ReturnTargetState(Target.KILLROT);
        }

        public void ButtonPrograde(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.PROGRADE : Target.OFF);
        }
        public bool ButtonProgradeState()
        {
            return ReturnTargetState(Target.PROGRADE);
        }

        public void ButtonRetrograde(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.RETROGRADE : Target.OFF);
        }
        public bool ButtonRetrogradeState()
        {
            return ReturnTargetState(Target.RETROGRADE);
        }

        public void ButtonNormalPlus(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.NORMAL_PLUS : Target.OFF);
        }
        public bool ButtonNormalPlusState()
        {
            return ReturnTargetState(Target.NORMAL_PLUS);
        }

        public void ButtonNormalMinus(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.NORMAL_MINUS : Target.OFF);
        }
        public bool ButtonNormalMinusState()
        {
            return ReturnTargetState(Target.NORMAL_MINUS);
        }

        public void ButtonRadialPlus(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.RADIAL_PLUS : Target.OFF);
        }
        public bool ButtonRadialPlusState()
        {
            return ReturnTargetState(Target.RADIAL_PLUS);
        }

        public void ButtonRadialMinus(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.RADIAL_MINUS : Target.OFF);
        }
        public bool ButtonRadialMinusState()
        {
            return ReturnTargetState(Target.RADIAL_MINUS);
        }

        public void ButtonSurfacePrograde(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.SURFACE_PROGRADE : Target.OFF);
        }
        public bool ButtonSurfaceProgradeState()
        {
            return ReturnTargetState(Target.SURFACE_PROGRADE);
        }

        public void ButtonSurfaceRetrograde(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.SURFACE_RETROGRADE : Target.OFF);
        }
        public bool ButtonSurfaceRetrogradeState()
        {
            return ReturnTargetState(Target.SURFACE_RETROGRADE);
        }

        public void ButtonHorizontalPlus(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.HORIZONTAL_PLUS : Target.OFF);
        }
        public bool ButtonHorizontalPlusState()
        {
            return ReturnTargetState(Target.HORIZONTAL_PLUS);
        }

        public void ButtonHorizontalMinus(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.HORIZONTAL_MINUS : Target.OFF);
        }
        public bool ButtonHorizontalMinusState()
        {
            return ReturnTargetState(Target.HORIZONTAL_MINUS);
        }

        public void ButtonVerticalPlus(bool state)
        {
            EnactTargetAction(vessel, (state) ? Target.VERTICAL_PLUS : Target.OFF);
        }
        public bool ButtonVerticalPlusState()
        {
            return ReturnTargetState(Target.VERTICAL_PLUS);
        }

        public void ButtonTargetPlus(bool state)
        {
            if (!state)
            {
                EnactTargetAction(vessel, Target.OFF);
            }
            else if (FlightGlobals.fetch.VesselTarget != null)
            {
                EnactTargetAction(vessel, Target.TARGET_PLUS);
            }
        }
        public bool ButtonTargetPlusState()
        {
            return ReturnTargetState(Target.TARGET_PLUS);
        }

        public void ButtonTargetMinus(bool state)
        {
            if (!state)
            {
                EnactTargetAction(vessel, Target.OFF);
            }
            else if (FlightGlobals.fetch.VesselTarget != null)
            {
                EnactTargetAction(vessel, Target.TARGET_MINUS);
            }
        }
        public bool ButtonTargetMinusState()
        {
            return ReturnTargetState(Target.TARGET_MINUS);
        }

        public void ButtonRvelPlus(bool state)
        {
            if (!state)
            {
                EnactTargetAction(vessel, Target.OFF);
            }
            else if (FlightGlobals.fetch.VesselTarget != null)
            {
                EnactTargetAction(vessel, Target.RELATIVE_PLUS);
            }
        }
        public bool ButtonRvelPlusState()
        {
            return ReturnTargetState(Target.RELATIVE_PLUS);
        }

        public void ButtonRvelMinus(bool state)
        {
            if (!state)
            {
                EnactTargetAction(vessel, Target.OFF);
            }
            else if (FlightGlobals.fetch.VesselTarget != null)
            {
                EnactTargetAction(vessel, Target.RELATIVE_MINUS);
            }
        }
        public bool ButtonRvelMinusState()
        {
            return ReturnTargetState(Target.RELATIVE_MINUS);
        }

        public void ButtonParPlus(bool state)
        {
            if (!state)
            {
                EnactTargetAction(vessel, Target.OFF);
            }
            else if (FlightGlobals.fetch.VesselTarget != null)
            {
                EnactTargetAction(vessel, Target.PARALLEL_PLUS);
            }
        }
        public bool ButtonParPlusState()
        {
            return ReturnTargetState(Target.PARALLEL_PLUS);
        }

        public void ButtonParMinus(bool state)
        {
            if (!state)
            {
                EnactTargetAction(vessel, Target.OFF);
            }
            else if (FlightGlobals.fetch.VesselTarget != null)
            {
                EnactTargetAction(vessel, Target.PARALLEL_MINUS);
            }
        }
        public bool ButtonParMinusState()
        {
            return ReturnTargetState(Target.PARALLEL_MINUS);
        }

        #endregion
    }
}
