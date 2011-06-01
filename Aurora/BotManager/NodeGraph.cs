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
using System.Linq;
using System.Text;
using OpenMetaverse;
using OpenSim.Framework;

namespace Aurora.BotManager
{
    public class NodeGraph
    {
        private List<Vector3> m_listOfPositions = new List<Vector3> ();
        private List<TravelMode> m_listOfStates = new List<TravelMode> ();
        private object m_lock = new object ();
        private DateTime m_lastChangedPosition = DateTime.MinValue;

        public NodeGraph ()
        {
        }

        #region Add

        public void Add (Vector3 position, TravelMode state)
        {
            lock (m_lock)
            {
                m_listOfPositions.Add (position);
                m_listOfStates.Add (state);
            }
        }

        public void AddRange (IEnumerable<Vector3> positions, IEnumerable<TravelMode> states)
        {
            lock (m_lock)
            {
                m_listOfPositions.AddRange (positions);
                m_listOfStates.AddRange (states);
            }
        }

        #endregion

        #region Clear

        public void Clear ()
        {
            lock (m_lock)
            {
                m_listOfPositions.Clear ();
                m_listOfStates.Clear ();
            }
        }

        #endregion

        public bool GetNextPosition (Vector3 currentPos, float closeToRange, int secondsBeforeForcedTeleport, out Vector3 position, out TravelMode state, out bool needsToTeleportToPosition)
        {
            bool found = false;
            lock (m_lock)
            {
            findNewTarget:
                position = Vector3.Zero;
                state = TravelMode.None;
                needsToTeleportToPosition = false;
                if (m_listOfPositions.Count > 0)
                {
                    position = m_listOfPositions[0];
                    state = m_listOfStates[0];
                    if (m_lastChangedPosition == DateTime.MinValue)
                        m_lastChangedPosition = DateTime.Now;
                    if (position.ApproxEquals (currentPos, closeToRange))
                    {
                        //Its close to a position, go look for the next pos
                        m_listOfPositions.RemoveAt (0);
                        m_listOfStates.RemoveAt (0);
                        m_lastChangedPosition = DateTime.MinValue;
                        goto findNewTarget;
                    }
                    else
                    {
                        if ((DateTime.Now - m_lastChangedPosition).Seconds > secondsBeforeForcedTeleport)
                            needsToTeleportToPosition = true;
                    }
                    return true;
                }
            }
            return found;
        }

        public void CopyFrom (NodeGraph graph)
        {
            m_listOfPositions = graph.m_listOfPositions;
            m_listOfStates = graph.m_listOfStates;
        }
    }
}
