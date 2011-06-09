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
using System.Reflection;
using System.Timers;
using Timer = System.Timers.Timer;
using System.Threading;
using System.Text;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Physics.Manager;
using OpenMetaverse;
using Aurora.Framework;
using log4net;

namespace Aurora.Modules
{
    public class PhysicsMonitor : ISharedRegionModule, IPhysicsMonitor
    {
        #region Private class

        protected class PhysicsStats
        {
            public float StatPhysicsTaintTime;
            public float StatPhysicsMoveTime;
            public float StatCollisionOptimizedTime;
            public float StatSendCollisionsTime;
            public float StatAvatarUpdatePosAndVelocity;
            public float StatPrimUpdatePosAndVelocity;
            public float StatUnlockedArea;
            public float StatContactLoopTime;
            public float StatFindContactsTime;
            public float StatCollisionAccountingTime;
        }

        #endregion

        #region Declares

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected DateTime m_lastUpdated = DateTime.Now;
        protected Dictionary<UUID, PhysicsStats> m_lastPhysicsStats = new Dictionary<UUID, PhysicsStats>();
        protected Dictionary<UUID, PhysicsStats> m_currentPhysicsStats = new Dictionary<UUID, PhysicsStats>();
        protected Timer m_physicsStatTimer;
        protected List<Scene> m_scenes = new List<Scene>();
        protected bool m_collectingStats = false;
        protected bool m_waitingForCollectionOfStats = true;

        #endregion

        #region ISharedRegionModule Members

        public string Name
        {
            get { return "PhysicsMonitor"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Initialise(IConfigSource source)
        {
            if (m_physicsStatTimer == null)
            {
                m_physicsStatTimer = new Timer();
                m_physicsStatTimer.Interval = 10000;
                m_physicsStatTimer.Elapsed += PhysicsStatsHeartbeat;
            }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public void AddRegion (Scene scene)
        {
            m_scenes.Add (scene);
            scene.RegisterModuleInterface<IPhysicsMonitor> (this);
            if (MainConsole.Instance != null)
            {
                MainConsole.Instance.Commands.AddCommand (
                    "physics stats", "physics stats", "physics stats <region>", PhysicsStatsCommand);
                MainConsole.Instance.Commands.AddCommand (
                    "physics profiler", "physics profiler", "physics profiler <region>", PhysicsProfilerCommand);
                MainConsole.Instance.Commands.AddCommand (
                    "physics current stats", "physics current stats", "physics current stats <region> NOTE: these are not calculated and are in milliseconds per unknown time", CurrentPhysicsStatsCommand);
            }
        }

        public void RemoveRegion(Scene scene)
        {
            m_scenes.Remove(scene);
            scene.UnregisterModuleInterface<IPhysicsMonitor>(this);
        }

        public void RegionLoaded(Scene scene)
        {
        }

        #endregion

        protected virtual void PhysicsStatsCommand(string[] cmd)
        {
            List<Scene> scenesToRun = new List<Scene>();
            if (cmd.Length == 3)
            {
                foreach (Scene scene in m_scenes)
                {
                    if (scene.RegionInfo.RegionName == cmd[2])
                        scenesToRun.Add(scene);
                }
                if (scenesToRun.Count == 0)
                    scenesToRun = m_scenes;
            }
            else
                scenesToRun.AddRange(m_scenes);

            //Set all the bools to true
            m_collectingStats = true;
            m_waitingForCollectionOfStats = true;
            //Start the timer as well
            m_physicsStatTimer.Start();
            m_log.Info("Collecting Stats Now... Please wait...");
            while(m_waitingForCollectionOfStats)
            {
                Thread.Sleep(50);
            }
            m_collectingStats = false;

            foreach (Scene scene in scenesToRun)
            {
                PhysicsStats stats = null;
                while (stats == null)
                {
                    m_lastPhysicsStats.TryGetValue(scene.RegionInfo.RegionID, out stats);
                }
                DumpStatsToConsole(scene, stats);
            }
        }

        protected virtual void PhysicsProfilerCommand(string[] cmd)
        {
            List<Scene> scenesToRun = new List<Scene>();
            if (cmd.Length == 3)
            {
                foreach (Scene scene in m_scenes)
                {
                    if (scene.RegionInfo.RegionName == cmd[2])
                        scenesToRun.Add(scene);
                }
                if (scenesToRun.Count == 0)
                    scenesToRun = m_scenes;
            }
            else
                scenesToRun.AddRange(m_scenes);

            //Set all the bools to true
            m_collectingStats = true;
            m_waitingForCollectionOfStats = true;
            //Start the timer as well
            m_physicsStatTimer.Start();
            m_log.Info("Collecting Stats Now... Please wait...");
            while(m_waitingForCollectionOfStats)
            {
                Thread.Sleep(50);
            }
            m_collectingStats = false;

            Thread thread = new Thread(StartThread);
            thread.Start(scenesToRun);
        }

        private void StartThread(object scenes)
        {
            Culture.SetCurrentCulture ();
            try
            {
                List<Scene> scenesToRun = (List<Scene>)scenes;
                System.Windows.Forms.Application.Run(new PhysicsProfilerForm(scenesToRun));
            }
            catch(Exception ex)
            {
                m_log.Warn("There was an error opening the form: " + ex.ToString());
            }
        }

        protected virtual void CurrentPhysicsStatsCommand(string[] cmd)
        {
            List<Scene> scenesToRun = new List<Scene>();
            if (cmd.Length == 3)
            {
                foreach (Scene scene in m_scenes)
                {
                    if (scene.RegionInfo.RegionName == cmd[2])
                        scenesToRun.Add(scene);
                }
                if (scenesToRun.Count == 0)
                    scenesToRun = m_scenes;
            }
            else
                scenesToRun.AddRange(m_scenes);

            //Set all the bools to true
            m_collectingStats = true;
            m_waitingForCollectionOfStats = true;
            //Start the timer as well
            m_physicsStatTimer.Start();
            m_log.Info("Collecting Stats Now... Please wait...");
            while(m_waitingForCollectionOfStats)
            {
                Thread.Sleep(50);
            }
            m_collectingStats = false;

            foreach (Scene scene in scenesToRun)
            {
                PhysicsStats stats = null;
                while (stats == null)
                {
                    m_currentPhysicsStats.TryGetValue(scene.RegionInfo.RegionID, out stats);
                }
                DumpStatsToConsole(scene, stats);
            }
        }

        protected virtual void DumpStatsToConsole(Scene scene, PhysicsStats stats)
        {
            m_log.Info("------  Physics Stats for region " + scene.RegionInfo.RegionName + "  ------");
            m_log.Info ("   All stats are in milliseconds spent per second.");
            m_log.Info ("   These are in the order they are run in the PhysicsScene.");
            m_log.Info (" PhysicsTaintTime: " + stats.StatPhysicsTaintTime);
            m_log.Info (" PhysicsMoveTime: " + stats.StatPhysicsMoveTime);
            m_log.Info (" FindContactsTime: " + stats.StatFindContactsTime);
            m_log.Info (" ContactLoopTime: " + stats.StatContactLoopTime);
            m_log.Info (" CollisionAccountingTime: " + stats.StatCollisionAccountingTime);
            m_log.Info (" CollisionOptimizedTime: " + stats.StatCollisionOptimizedTime);
            m_log.Info (" SendCollisionsTime: " + stats.StatSendCollisionsTime);
            m_log.Info (" AvatarUpdatePosAndVelocity: " + stats.StatAvatarUpdatePosAndVelocity);
            m_log.Info (" PrimUpdatePosAndVelocity: " + stats.StatPrimUpdatePosAndVelocity);
            m_log.Info (" UnlockedArea: " + stats.StatUnlockedArea);
            m_log.Info ("");
        }

        protected virtual void PhysicsStatsHeartbeat(object sender, ElapsedEventArgs e)
        {
            if(!m_collectingStats)
                return;
            lock (m_currentPhysicsStats)
            {
                foreach (KeyValuePair<UUID, PhysicsStats> kvp in m_currentPhysicsStats)
                {
                    //Save the stats in the last one so we can keep them for the console commands
                    //Divide by 10 so we get per second
                    m_lastPhysicsStats[kvp.Key] = kvp.Value;
                    m_lastPhysicsStats[kvp.Key].StatAvatarUpdatePosAndVelocity /= 10;
                    m_lastPhysicsStats[kvp.Key].StatCollisionOptimizedTime /= 10;
                    m_lastPhysicsStats[kvp.Key].StatPhysicsMoveTime /= 10;
                    m_lastPhysicsStats[kvp.Key].StatPhysicsTaintTime /= 10;
                    m_lastPhysicsStats[kvp.Key].StatPrimUpdatePosAndVelocity /= 10;
                    m_lastPhysicsStats[kvp.Key].StatSendCollisionsTime /= 10;
                    m_lastPhysicsStats[kvp.Key].StatUnlockedArea /= 10;
                    m_lastPhysicsStats[kvp.Key].StatFindContactsTime /= 10;
                    m_lastPhysicsStats[kvp.Key].StatContactLoopTime /= 10;
                    m_lastPhysicsStats[kvp.Key].StatCollisionAccountingTime /= 10;
                    //Add the stats to the profiler
                    Profiler p = ProfilerManager.GetProfiler();
                    p.AddStat("StatAvatarUpdatePosAndVelocity " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatAvatarUpdatePosAndVelocity);
                    p.AddStat("StatCollisionOptimizedTime " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatCollisionOptimizedTime);
                    p.AddStat("StatPhysicsMoveTime " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatPhysicsMoveTime);
                    p.AddStat("StatPhysicsTaintTime " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatPhysicsTaintTime);
                    p.AddStat("StatPrimUpdatePosAndVelocity " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatPrimUpdatePosAndVelocity);
                    p.AddStat("StatSendCollisionsTime " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatSendCollisionsTime);
                    p.AddStat("StatUnlockedArea " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatUnlockedArea);
                    p.AddStat("StatFindContactsTime " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatFindContactsTime);
                    p.AddStat("StatContactLoopTime " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatContactLoopTime);
                    p.AddStat("StatCollisionAccountingTime " + kvp.Key,
                        m_lastPhysicsStats[kvp.Key].StatCollisionAccountingTime);
                }
                m_currentPhysicsStats.Clear();
            }
            m_lastUpdated = DateTime.Now;
            //If there are stats waiting, we just pulled them
            m_waitingForCollectionOfStats = false;
        }

        public virtual void AddPhysicsStats(UUID RegionID, PhysicsScene scene)
        {
            if(!m_collectingStats)
                return;
            lock (m_currentPhysicsStats)
            {
                PhysicsStats stats;
                if (!m_currentPhysicsStats.TryGetValue(RegionID, out stats))
                {
                    stats = new PhysicsStats();
                    stats.StatAvatarUpdatePosAndVelocity = scene.StatAvatarUpdatePosAndVelocity;
                    stats.StatCollisionOptimizedTime = scene.StatCollisionOptimizedTime;
                    stats.StatPhysicsMoveTime = scene.StatPhysicsMoveTime;
                    stats.StatPhysicsTaintTime = scene.StatPhysicsTaintTime;
                    stats.StatPrimUpdatePosAndVelocity = scene.StatPrimUpdatePosAndVelocity;
                    stats.StatSendCollisionsTime = scene.StatSendCollisionsTime;
                    stats.StatUnlockedArea = scene.StatUnlockedArea;
                    stats.StatFindContactsTime = scene.StatFindContactsTime;
                    stats.StatContactLoopTime = scene.StatContactLoopTime;
                    stats.StatCollisionAccountingTime = scene.StatCollisionAccountingTime;
                }
                else
                {
                    stats.StatAvatarUpdatePosAndVelocity += scene.StatAvatarUpdatePosAndVelocity;
                    stats.StatCollisionOptimizedTime += scene.StatCollisionOptimizedTime;
                    stats.StatPhysicsMoveTime += scene.StatPhysicsMoveTime;
                    stats.StatPhysicsTaintTime += scene.StatPhysicsTaintTime;
                    stats.StatPrimUpdatePosAndVelocity += scene.StatPrimUpdatePosAndVelocity;
                    stats.StatSendCollisionsTime += scene.StatSendCollisionsTime;
                    stats.StatUnlockedArea += scene.StatUnlockedArea;
                    stats.StatFindContactsTime += scene.StatFindContactsTime;
                    stats.StatContactLoopTime += scene.StatContactLoopTime;
                    stats.StatCollisionAccountingTime += scene.StatCollisionAccountingTime;
                }

                m_currentPhysicsStats[RegionID] = stats;

                PhysicsStats ProfilerStats = new PhysicsStats();
                ProfilerStats.StatAvatarUpdatePosAndVelocity = scene.StatAvatarUpdatePosAndVelocity;
                ProfilerStats.StatCollisionOptimizedTime = scene.StatCollisionOptimizedTime;
                ProfilerStats.StatPhysicsMoveTime = scene.StatPhysicsMoveTime;
                ProfilerStats.StatPhysicsTaintTime = scene.StatPhysicsTaintTime;
                ProfilerStats.StatPrimUpdatePosAndVelocity = scene.StatPrimUpdatePosAndVelocity;
                ProfilerStats.StatSendCollisionsTime = scene.StatSendCollisionsTime;
                ProfilerStats.StatUnlockedArea = scene.StatUnlockedArea;
                ProfilerStats.StatFindContactsTime = scene.StatFindContactsTime;
                ProfilerStats.StatContactLoopTime = scene.StatContactLoopTime;
                ProfilerStats.StatCollisionAccountingTime = scene.StatCollisionAccountingTime;

                //Add the stats to the profiler
                Profiler p = ProfilerManager.GetProfiler();
                p.AddStat("CurrentStatAvatarUpdatePosAndVelocity " + RegionID,
                    ProfilerStats.StatAvatarUpdatePosAndVelocity);
                p.AddStat("CurrentStatCollisionOptimizedTime " + RegionID,
                    ProfilerStats.StatCollisionOptimizedTime);
                p.AddStat("CurrentStatPhysicsMoveTime " + RegionID,
                    ProfilerStats.StatPhysicsMoveTime);
                p.AddStat("CurrentStatPhysicsTaintTime " + RegionID,
                    ProfilerStats.StatPhysicsTaintTime);
                p.AddStat("CurrentStatPrimUpdatePosAndVelocity " + RegionID,
                    ProfilerStats.StatPrimUpdatePosAndVelocity);
                p.AddStat("CurrentStatSendCollisionsTime " + RegionID,
                    ProfilerStats.StatSendCollisionsTime);
                p.AddStat("CurrentStatUnlockedArea " + RegionID,
                    ProfilerStats.StatUnlockedArea);
                p.AddStat("CurrentStatFindContactsTime " + RegionID,
                    ProfilerStats.StatFindContactsTime);
                p.AddStat("CurrentStatContactLoopTime " + RegionID,
                    ProfilerStats.StatContactLoopTime);
                p.AddStat("CurrentStatCollisionAccountingTime " + RegionID,
                    ProfilerStats.StatCollisionAccountingTime);
            }
        }
    }
}
