/*
 * Copyright (c) Contributors, http://aurora-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Nini.Config;

namespace Aurora.Modules
{
    /// <summary>
    /// This module helps keep the sim running when it begins to slow down, or if it freezes, restarts it
    /// </summary>
    public class SimProtection : INonSharedRegionModule
    {
        #region Declares

        private static readonly log4net.ILog m_log
            = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        //Normal Sim FPS
        private float BaseRateFramesPerSecond = 45;
        // When BaseRate / current FPS is less than this percent, begin shutting down services
        protected float PercentToBeginShutDownOfServices = 50;
        protected Scene m_scene;
        protected bool m_Enabled = false;
        protected float TimeAfterToReenablePhysics = 20;
        protected float TimeAfterToReenableScripts;
        protected DateTime DisabledPhysicsStartTime = DateTime.MinValue;
        protected DateTime DisabledScriptStartTime = DateTime.MinValue;
        protected bool AllowDisableScripts = true;
        protected bool AllowDisablePhysics = true;
        //Time before a sim sitting at 0FPS is restarted automatically
        protected float MinutesBeforeZeroFPSKills = 1;
        protected bool KillSimOnZeroFPS = true;
        protected DateTime SimZeroFPSStartTime = DateTime.MinValue;
        protected Timer TimerToCheckHeartbeat = null;
        protected float TimeBetweenChecks = 1;
        protected ISimFrameMonitor m_statsReporter = null;

        #endregion

        #region INonSharedRegionModule

        public void Initialise(IConfigSource source)
        {
            if (!source.Configs.Contains("Protection"))
                return;
            TimeAfterToReenableScripts = TimeAfterToReenablePhysics * 2;
            IConfig config = source.Configs["Protection"];
            m_Enabled = config.GetBoolean("Enabled", false);
            BaseRateFramesPerSecond = config.GetFloat("BaseRateFramesPerSecond", 45);
            PercentToBeginShutDownOfServices = config.GetFloat("PercentToBeginShutDownOfServices", 50);
            TimeAfterToReenablePhysics = config.GetFloat("TimeAfterToReenablePhysics", 20);
            AllowDisableScripts = config.GetBoolean("AllowDisableScripts", true);
            AllowDisablePhysics = config.GetBoolean("AllowDisablePhysics", true);
            KillSimOnZeroFPS = config.GetBoolean("RestartSimIfZeroFPS", true);
            MinutesBeforeZeroFPSKills = config.GetFloat("TimeBeforeZeroFPSKills", 1);
            TimeBetweenChecks = config.GetFloat("TimeBetweenChecks", 1);
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
        }

        public void PostInitialise()
        {
        }

        public string Name
        {
            get { return "SimProtection"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;
            TimerToCheckHeartbeat.Stop();
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;
            m_scene = scene;
            BaseRateFramesPerSecond = scene.BaseSimFPS;
            m_statsReporter =  (ISimFrameMonitor)m_scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_scene.RegionInfo.RegionID.ToString(), "SimFrameStats");
            if (m_statsReporter == null)
            {
                m_log.Warn("[SimProtection]: Cannot be used as SimStatsReporter does not exist.");
                return;
            }
            TimerToCheckHeartbeat = new Timer();
            TimerToCheckHeartbeat.Interval = TimeBetweenChecks * 1000 * 60;//minutes
            TimerToCheckHeartbeat.Elapsed += OnCheck;
            TimerToCheckHeartbeat.Start();
        }

        #endregion

        #region Protection

        private void OnCheck(object sender, ElapsedEventArgs e)
        {
            IEstateModule mod = m_scene.RequestModuleInterface<IEstateModule> ();
            if (AllowDisableScripts && m_statsReporter.LastReportedSimFPS < BaseRateFramesPerSecond * (PercentToBeginShutDownOfServices / 100) && m_statsReporter.LastReportedSimFPS != 0)
            {
                //Less than the percent to start shutting things down... Lets kill some stuff
                if (mod != null)
                    mod.SetSceneCoreDebug (false, m_scene.RegionInfo.RegionSettings.DisableCollisions, m_scene.RegionInfo.RegionSettings.DisablePhysics); //These are opposite of what you want the value to be... go figure
                DisabledScriptStartTime = DateTime.Now;
            }
            if (m_scene.RegionInfo.RegionSettings.DisableScripts &&
               AllowDisableScripts &&
               SimZeroFPSStartTime != DateTime.MinValue && //This makes sure we don't screw up the setting if the user disabled physics manually
               SimZeroFPSStartTime.AddSeconds (TimeAfterToReenableScripts) > DateTime.Now)
            {
                DisabledScriptStartTime = DateTime.MinValue;
                if (mod != null)
                    mod.SetSceneCoreDebug (true, m_scene.RegionInfo.RegionSettings.DisableCollisions, m_scene.RegionInfo.RegionSettings.DisablePhysics); //These are opposite of what you want the value to be... go figure
            }

            if (m_statsReporter.LastReportedSimFPS == 0 && KillSimOnZeroFPS)
            {
                if (SimZeroFPSStartTime == DateTime.MinValue)
                    SimZeroFPSStartTime = DateTime.Now;
                if (SimZeroFPSStartTime.AddMinutes(MinutesBeforeZeroFPSKills) > SimZeroFPSStartTime)
                    MainConsole.Instance.RunCommand("shutdown");
            }
            else if (SimZeroFPSStartTime != DateTime.MinValue)
                SimZeroFPSStartTime = DateTime.MinValue;
            
            float[] stats = m_scene.RequestModuleInterface<IMonitorModule>().GetRegionStats(m_scene.RegionInfo.RegionID.ToString());
            if (stats[2]/*PhysicsFPS*/ < BaseRateFramesPerSecond * (PercentToBeginShutDownOfServices / 100) &&
                stats[2] != 0 &&
                AllowDisablePhysics &&
                !m_scene.RegionInfo.RegionSettings.DisablePhysics) //Don't redisable physics again, physics will be frozen at the last FPS
            {
                DisabledPhysicsStartTime = DateTime.Now;
                if (mod != null)
                    mod.SetSceneCoreDebug(m_scene.RegionInfo.RegionSettings.DisableScripts, m_scene.RegionInfo.RegionSettings.DisableCollisions, false); //These are opposite of what you want the value to be... go figure
            }

            if (m_scene.RegionInfo.RegionSettings.DisablePhysics &&
                AllowDisablePhysics &&
                DisabledPhysicsStartTime != DateTime.MinValue && //This makes sure we don't screw up the setting if the user disabled physics manually
                DisabledPhysicsStartTime.AddSeconds(TimeAfterToReenablePhysics) > DateTime.Now)
            {
                DisabledPhysicsStartTime = DateTime.MinValue;
                if (mod != null)
                    mod.SetSceneCoreDebug(m_scene.RegionInfo.RegionSettings.DisableScripts, m_scene.RegionInfo.RegionSettings.DisableCollisions, true);//These are opposite of what you want the value to be... go figure
            }
        }

        #endregion
    }
}
