﻿/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
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

using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

using OpenMetaverse;
using log4net;

namespace OpenSim.Region.CoreModules
{
    public class ActivityDetector : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;

        public void Initialise(Nini.Config.IConfigSource source)
        {
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            m_scene = scene;
            m_scene.EventManager.OnMakeRootAgent += OnMakeRootAgent;
            m_scene.EventManager.OnNewClient += OnNewClient;
            m_scene.EventManager.OnClosingClient += OnClosingClient;
            m_scene.EventManager.OnAvatarEnteringNewParcel += OnEnteringNewParcel;
        }

        public void RemoveRegion(Scene scene)
        {
            m_scene.RequestModuleInterface<IPresenceService>().LogoutRegionAgents(scene.RegionInfo.RegionID);
            scene.EventManager.OnMakeRootAgent -= OnMakeRootAgent;
            scene.EventManager.OnNewClient -= OnNewClient;
            scene.EventManager.OnClosingClient -= OnClosingClient;
            scene.EventManager.OnAvatarEnteringNewParcel -= OnEnteringNewParcel;
        }

        public void RegionLoaded(Scene scene)
        {
            m_scene.RequestModuleInterface<IPresenceService>().LogoutRegionAgents(scene.RegionInfo.RegionID);
        }

        public string Name
        {
            get { return "ActivityDetector"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void OnMakeRootAgent(ScenePresence sp)
        {
            //m_log.DebugFormat("[ACTIVITY DETECTOR]: Detected root presence {0} in {1}", sp.UUID, sp.Scene.RegionInfo.RegionName);
            m_scene.RequestModuleInterface<IPresenceService>().ReportAgent(sp.ControllingClient.SessionId, sp.Scene.RegionInfo.RegionID);
            m_scene.RequestModuleInterface<IGridUserService>().SetLastPosition(sp.UUID.ToString(), UUID.Zero, sp.Scene.RegionInfo.RegionID, sp.AbsolutePosition, sp.Lookat);
        }

        public void OnNewClient(IClientAPI client)
        {
            client.OnConnectionClosed += OnConnectionClose;
        }

        private void OnClosingClient(IClientAPI client)
        {
            client.OnConnectionClosed -= OnConnectionClose;
        }

        public void OnConnectionClose(IClientAPI client)
        {
            if (client.IsLoggingOut)
            {
                IScenePresence sp = null;
                Vector3 position = new Vector3(128, 128, 0);
                Vector3 lookat = new Vector3(0, 1, 0);

                if (client.Scene.TryGetScenePresence(client.AgentId, out sp))
                {
                    if (sp.IsChildAgent)
                        return;

                    position = ((ScenePresence)sp).AbsolutePosition;
                    lookat = ((ScenePresence)sp).Lookat;
                }
                m_log.InfoFormat("[ActivityDetector]: Detected client logout {0} in {1}", client.AgentId, client.Scene.RegionInfo.RegionName);
                m_scene.RequestModuleInterface<IPresenceService>().LogoutAgent(client.SessionId);
                m_scene.RequestModuleInterface<IGridUserService>().LoggedOut(client.AgentId.ToString(), client.SessionId, client.Scene.RegionInfo.RegionID, position, lookat);
            }
        }

        public void OnEnteringNewParcel(ScenePresence sp, int localLandID, UUID regionID)
        {
            // Asynchronously update the position stored in the session table for this agent
            Util.FireAndForget(delegate(object o)
            {
                m_scene.RequestModuleInterface<IGridUserService>().SetLastPosition(sp.UUID.ToString(), sp.ControllingClient.SessionId, sp.Scene.RegionInfo.RegionID, sp.AbsolutePosition, sp.Lookat);
            });
        }
    }
}
