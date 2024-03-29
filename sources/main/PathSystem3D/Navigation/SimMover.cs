using System;
using System.Collections.Generic;
using System.Drawing;
#if (COGBOT_LIBOMV || USE_STHREADS)
using ThreadPoolUtil;
using ThreadPoolUtil;
using ThreadStart = System.Threading.ThreadStart;
using AutoResetEvent = System.Threading.AutoResetEvent;
using ManualResetEvent = System.Threading.ManualResetEvent;
using TimerCallback = System.Threading.TimerCallback;
using Timer = System.Threading.Timer;
using Interlocked = System.Threading.Interlocked;
#else
using System.Threading;
#endif
using OpenMetaverse;
using PathSystem3D.Mesher;

namespace PathSystem3D.Navigation
{
    public interface SimMover : PathSystem3D.Navigation.SimPosition, SimDistCalc
    {
        bool TurnToward(Vector3d targetPosition);
        void StopMoving(bool fullStop);
        void StopMoving();
        bool SimpleMoveTo(Vector3d end, double maxDistance, float maxSeconds, bool stop);
        void Debug(string format, params object[] args);
        //List<SimObject> GetNearByObjects(double maxDistance, bool rootOnly);
        /*
         
         Inherited from SimPosition:
         
                    bool IsPassable { get; set; }
                    string DistanceVectorString(SimPosition RootObject);
                    Vector3 GetSimPosition();
                    float GetSizeDistance();
                    bool IsRegionAttached();
                    Quaternion GetSimRotation();
                    Vector3d GlobalPosition();
                    SimPathStore GetPathStore();
         
         */
        bool OpenNearbyClosedPassages();
        void ThreadJump();
        void IndicateRoute(IEnumerable<Vector3d> list, Color color4);
    }

    public interface SimDistCalc
    {        
        double Distance(SimPosition position);
    }

    public enum SimMoverState : byte
    {
        PAUSED = 0,
        MOVING = 1,
        BLOCKED = 2,
        COMPLETE = 3,
        TRYAGAIN = 4,
        THINKING = 5
    }


    public abstract class SimAbstractMover
    {
        public SimMoverState _STATE = SimMoverState.PAUSED;
        public SimMover Mover;
        //public Vector3d FinalLocation;
        public double FinalDistance;
        static public double CloseDistance = 1f;// SimPathStore.LargeScale-SimPathStore.StepSize;
        static public double TurnAvoid = 0f;
        public bool UseTurnAvoid = false;  // this toggles each time
        public SimPosition FinalPosition;
        static public bool UseSkippingToggle = true;
        static public bool UseSkippingIntially = false;
        public bool UseSkipping = UseSkippingIntially;
        static public bool UseReverse = false;
        public abstract SimMoverState Goto();
        public int completedAt = 0;
        public static bool UseStarJumpBreakaway = true;
        public static bool UseStarBreakaway = !UseStarJumpBreakaway;
        public static bool UseOpenNearbyClosedPassages = true;

        // just return v3.Z if unsure
        public virtual double GetZFor(Vector3d v3)
        {
            return SimPathStore.GlobalToLocal(v3).Z;
        }

        public SimAbstractMover(SimMover mover, SimPosition finalGoal, double finalDistance)
        {
            Mover = mover;
            SimPathStore.DebugDelegate = mover.Debug;
            FinalDistance = finalDistance;
            //var vFinalLocation = finalGoal.UsePosition.GlobalPosition;
           // FinalLocation = vFinalLocation;
            FinalPosition = finalGoal;
            var ps = mover.PathStore;
            if (ps != null)
            {
                ps.LastSimMover = mover;
            } else
            {
                Debug("No pathSTore!?!");
            }

        }

        public event Action<SimMoverState> OnMoverStateChange;
        public SimMoverState STATE
        {
            get { return _STATE; }
            set
            {
                if (value != _STATE)
                {
                    if (OnMoverStateChange != null)
                        OnMoverStateChange(_STATE);
                    _STATE = value;
                }
            }
        }

        public SimPathStore PathStore
        {
            get { return Mover.PathStore; }
        }


        public static SimAbstractMover CreateSimPathMover(SimMover mover, SimPosition finalGoal, double finalDistance)
        {
            return new SimCollisionPlaneMover(mover, finalGoal, finalDistance);
        }

        public Vector3 GetSimPosition()
        {
            return Mover.SimPosition;
        }

        public SimMoverState FollowPathTo(IList<Vector3d> v3s, Vector3d finalTarget, double finalDistance)
        {
            completedAt = 0;
            STATE = SimMoverState.MOVING;
            Vector3d vstart = v3s[0];
            // Vector3 vv3 = SimPathStore.GlobalToLocal(vstart);

            v3s = GetSimplifedRoute(vstart, v3s);

            Mover.IndicateRoute(v3s, Color.Red);

            int maxReverse = 2;
            Debug("FollowPath: {0} -> {1} for {2}", v3s.Count, DistanceVectorString(finalTarget), finalDistance);
            int CanSkip = UseSkipping ? 0 : 2; //never right now
            int Skipped = 0;
            if (UseSkippingToggle) UseSkipping = !UseSkipping;
            int v3sLength = v3s.Count;
            int at = 0;
            int MoveToFailed = 0;
            while (at < v3sLength)
            {
                Vector3d v3 = v3s[at];
                // try to get there first w/in StepSize 0.2f 
                v3.Z = GetSimPosition().Z;
                if (!MoveTo(v3, PathStore.StepSize, 2))
                {
                    // didn't make it but are we close enough for govt work?
                    if (Mover.Distance(FinalPosition) < finalDistance)
                        return SimMoverState.COMPLETE;
                    // See what point in the list we are closest to
                    int nbest = ClosestAt(v3s);
                    if (nbest > at)
                    {
                        Debug("Fast-forward {0} -> {1} ", at, nbest);
                        at = nbest + 1;
                        continue;
                    }
                    // Try again with distance of 0.6f
                    if (!MoveTo(v3, PathStore.StepSize * 3, 2))
                    {
                        MoveToFailed++;
                        // still did not make it
                        OpenNearbyClosedPassages();
                        if (Skipped++ <= CanSkip)
                        {
                            // move around
                            MoveToPassableArround(GetSimPosition());
                            Skipped++;
                            // move onto the next point
                            at++;
                            continue;
                        }
                        {
                         if (UseStarJumpBreakaway)  Mover.ThreadJump();
                        }
                        Vector3 l3 = SimPathStore.GlobalToLocal(v3);
                        BlockTowardsVector(l3);
                        Debug("!MoveTo: {0} ", DistanceVectorString(v3));
                        MoveToPassableArround(l3);
                        return SimMoverState.TRYAGAIN;
                    }
                }
                else
                {
                    Skipped = 0;
                }
                if (DistanceNoZ(GetWorldPosition(), finalTarget) < finalDistance) return SimMoverState.COMPLETE;
                int best = ClosestAt(v3s);
                if (best > at)
                {
                    Debug("Fast forward {0} -> {1} ", at, best);
                }
                else if (best < at)
                {
                    if (!UseReverse || maxReverse-- < 0)
                    {
                        // not using reverse
                        //  Debug("Wont Reverse {0} -> {1} ", at, best);
                        best = at;
                    }
                    else
                    {
                        Debug("Reverse {0} -> {1} ", at, best);
                    }
                    if (MoveToFailed == 0)
                    {
                        best = at;
                    }
                    // for now never use (leave default false)
                    // UseReverse = !UseReverse;
                }
                completedAt = best;
                at = best + 1;

            }
                        
            double fd = DistanceNoZ(GetWorldPosition(), finalTarget);
            STATE = fd <= finalDistance ? SimMoverState.COMPLETE : SimMoverState.BLOCKED;
            Debug("{0}: {1:0.00}m", STATE, fd);
            return STATE;
        }

        public virtual IList<Vector3d> GetSimplifedRoute(Vector3d vstart, IList<Vector3d> v3s)
        {
            return SimPathStore.GetSimplifedRoute(vstart, v3s, 45, 5f);
        }

        public int ClosestAt(IList<Vector3d> v3s)
        {
            Vector3d newPos = GetWorldPosition();
            int v3sLength = v3s.Count - 1;
            double bestDist = DistanceNoZ(newPos, v3s[v3sLength]);
            int best = v3sLength;
            while (v3sLength-- > completedAt)
            {
                double lenNat = DistanceNoZ(newPos, v3s[v3sLength]);
                if (lenNat < bestDist)
                {
                    best = v3sLength;
                    bestDist = lenNat;
                }
            }
            if (completedAt < best)
            {
                completedAt = best;
            }
            return best;
        }

        public static double DistanceNoZ(Vector3d a, Vector3d b)
        {
            return SimPathStore.DistanceNoZ(a, b);
        }

        public string DistanceVectorString(Vector3d loc3d)
        {
            Vector3 loc = SimPathStore.GlobalToLocal(loc3d);
            SimPathStore R = SimPathStore.GetPathStore(loc3d);
            return String.Format("{0:0.00}m ", DistanceNoZ(GetWorldPosition(), loc3d))
                   + String.Format("{0}/{1:0.00}/{2:0.00}/{3:0.00}", R.RegionName, loc.X, loc.Y, loc.Z);
        }

        public string DistanceVectorString(SimPosition loc3d)
        {
            return DistanceVectorString(loc3d.GlobalPosition);
        }

        public string DistanceVectorString(Vector3 loc)
        {
            return String.Format("{0:0.00}m ", Vector3.Distance(GetSimPosition(), loc))
                   + String.Format("{0}/{1:0.00}/{2:0.00}/{3:0.00}", PathStore.RegionName, loc.X, loc.Y, loc.Z);
        }

        public bool MoveTo(Vector3d finalTarget)
        {
            return MoveTo(finalTarget, PathStore.StepSize * 2, 4);
        }


        public bool MoveTo(Vector3d finalTarget, double maxDistance, int maxSeconds)
        {
            finalTarget.Z = GetZFor(finalTarget);
            return Mover.SimpleMoveTo(finalTarget, maxDistance, maxSeconds, false);
        }

        delegate Vector3 MoveToPassable(Vector3 start);
        static int PathBreakAwayNumber = 0;
        readonly static List<MoveToPassable> PathBreakAways = new List<MoveToPassable>();

        private DateTime lastBreakAweayMoving = DateTime.Now;
        public Vector3 MoveToPassableArround(Vector3 start)
        {
            if (lastBreakAweayMoving.AddSeconds(3) < DateTime.Now)
            {
                return start;
            }
            lastBreakAweayMoving = DateTime.Now;
            OpenNearbyClosedPassages();
            if (!UseTurnAvoid)
            {
                UseTurnAvoid = !UseTurnAvoid;
                return start;
            }
            UseTurnAvoid = !UseTurnAvoid;

            lock (PathBreakAways)
            {
                if (PathBreakAways.Count == 0)
                {
                    if (UseStarBreakaway) PathBreakAways.Add(StarBreakaway);
                    if (UseStarJumpBreakaway) PathBreakAways.Add(StarJumpBreakaway);
                }
                if (PathBreakAways.Count == 0)
                {
                    return start;
                }
            }
            PathBreakAwayNumber++;
            if (PathBreakAwayNumber >= PathBreakAways.Count)
            {
                PathBreakAwayNumber = 0;
            }
            MoveToPassable pathBreakAway = null;
            lock(PathBreakAways)
            {
                pathBreakAway = PathBreakAways[PathBreakAwayNumber];
            }
            return pathBreakAway(start);
        }

        private Vector3 StarBreakaway(Vector3 start)
        {
            double A45 = 45f / SimPathStore.RAD2DEG;
            Debug("StarBreakaway avoidance");
            for (double angle = A45; angle < SimPathStore.PI2; angle += A45)
            {
                Vector3 next = ZAngleVector(angle + TurnAvoid) * 2 + start;
                {
                    Vector3d v3d = PathStore.LocalToGlobal(next);
                    if (MoveTo(v3d, 1, 2))
                    {
                        TurnAvoid += angle;  // update for next use
                        if (TurnAvoid > SimPathStore.PI2)
                            TurnAvoid -= SimPathStore.PI2;
                        return next;
                    }
                }
            }
            return start;
        }

        private Vector3 StarJumpBreakaway(Vector3 start)
        {
            double A45 = 45f / SimPathStore.RAD2DEG;
            Debug("StarJumpBreakaway avoidance");
            for (double angle = A45; angle < SimPathStore.PI2; angle += A45)
            {
                Vector3 next = ZAngleVector(angle + TurnAvoid) * 2 + start;
                {
                    Vector3d v3d = PathStore.LocalToGlobal(next);
                    Mover.ThreadJump();
                    v3d.Z = Mover.SimPosition.Z;
                    if (Mover.SimpleMoveTo(v3d, 1, 1, false))
                    {
                        TurnAvoid += angle;  // update for next use
                        if (TurnAvoid > SimPathStore.PI2)
                            TurnAvoid -= SimPathStore.PI2;
                        return start;
                    }
                }
            }
            return start;
        }

        public SimWaypoint GetWaypoint()
        {
            return PathStore.CreateClosestRegionWaypoint(GetSimPosition(), 2f);
        }

        public override string ToString()
        {

            string s = GetType().Name + "::" + Mover + " -> " + DistanceVectorString(FinalPosition) + " to " + FinalPosition;
            return s;
            //SimWaypoint point = Routes[0].StartNode;
            //int c = Routes.Count;
            //SimWaypoint end = Routes[c - 1].EndNode;
            //foreach (SimRoute A in Routes)
            //{
            //    SimWaypoint next = A.StartNode;
            //    if (next != point)
            //    {
            //        s += " -> " + next.ToString();
            //        point = next;
            //    }
            //    next = A.EndNode;
            //    if (next != point)
            //    {
            //        s += " -> " + next.ToString();
            //        point = next;
            //    }
            //}
            //return s;
        }

        public void Debug(string p, params object[] args)
        {
            string s = String.Format(String.Format("[MOVER] {0} for {1}", p, this.ToString()), args);
            //CollisionPlane.Debug(Mover+ "> " +s);            
            Mover.Debug(s);
        }

        public Vector3d GetWorldPosition()
        {
            return Mover.GlobalPosition;
        }

        /// <summary>
        /// Blocks points -45 to +45 degrees in front of Bot (assumes the bot is heading toward V3)
        /// </summary>
        /// <param name="v3"></param>
        public void BlockTowardsVector(Vector3 l3)
        {
            OpenNearbyClosedPassages();
            Mover.IndicateRoute(new List<Vector3d>() {GetWorldPosition(),PathStore.LocalToGlobal(l3)}, Color.Olive);
            PathStore.SetBlockedTemp(GetSimPosition(), l3, 45, SimPathStore.BLOCKED);
        }

        public virtual bool OpenNearbyClosedPassages()
        {
            return UseOpenNearbyClosedPassages && Mover.OpenNearbyClosedPassages();
        }

        public static Vector3 ZAngleVector(double ZAngle)
        {
            while (ZAngle < 0)
            {
                ZAngle += SimPathStore.PI2;
            }
            while (ZAngle > SimPathStore.PI2)
            {
                ZAngle -= SimPathStore.PI2;
            }
            return new Vector3((float)Math.Sin(ZAngle), (float)Math.Cos(ZAngle), 0);
        }

        //public abstract bool FollowPathTo(Vector3d globalEnd, double distance);
    }

    public class SimCollisionPlaneMover : SimAbstractMover
    {

        public override IList<Vector3d> GetSimplifedRoute(Vector3d vstart, IList<Vector3d> v3s)
        {
            if (UsedBigZ > 0 || PreXp)
                return SimPathStore.GetSimplifedRoute(vstart, v3s, 20, 0.5f);
            return  SimPathStore.GetSimplifedRoute(vstart, v3s, 45, 2f);
        }

        public override double GetZFor(Vector3d v3)
        {
           // return v3.Z;
            Vector3 local = SimPathStore.GlobalToLocal(v3);
            return MoverPlaneZ.GetHeight(local);
        }

        public override bool OpenNearbyClosedPassages()
        {
            Vector3 v3 = GetSimPosition();
            byte b = PathStore.GetNodeQuality(v3, MoverPlaneZ);
            if (b < 200) b += 100;
            if (b > 254) b = 254;
            CollisionPlane MoverPlane = MoverPlaneZ;
            PathStore.SetNodeQuality(v3, b, MoverPlane);
            bool changed = base.OpenNearbyClosedPassages();
            if (changed)
            {
                MoverPlane.HeightMapNeedsUpdate = true;
                MoverPlane.MatrixNeedsUpdate = true;
            }
            return changed;
        }

        public SimCollisionPlaneMover(SimMover mover, SimPosition finalGoal, double finalDistance) :
            base(mover, finalGoal, finalDistance)
        {
            CollisionPlane MoverPlane = MoverPlaneZ;
            float startZ = (float)SimPathStore.CalcStartZ(mover.SimPosition.Z, finalGoal.SimPosition.Z);
            double diff = MoverPlane.MinZ - startZ;

            MoverPlane.MinZ = startZ;
            MoverPlane.MaxZ = startZ + 3;
            if (diff > .5 || diff < -0.5f)
            {
                MoverPlane.HeightMapNeedsUpdate = true;
            }
        }


        public static Dictionary<SimMover, CollisionPlane> MoverPlanes = new Dictionary<SimMover, CollisionPlane>();

        public CollisionPlane MoverPlaneZ
        {
            get
            {
                CollisionPlane _MoverPlane;
                lock (MoverPlanes)
                {
                    if (!MoverPlanes.ContainsKey(Mover))
                    {
                        _MoverPlane = MoverPlanes[Mover] = PathStore.GetCollisionPlane(Mover.SimPosition.Z);
                        _MoverPlane.HeightMapNeedsUpdate = true;
                        _MoverPlane.Users++;
                    }
                    _MoverPlane = MoverPlanes[Mover];
                    if (_MoverPlane.PathStore != Mover.PathStore)
                    {
                        if (_MoverPlane!=null)
                        {
                            _MoverPlane.Users--;
                        }
                        _MoverPlane = MoverPlanes[Mover] = PathStore.GetCollisionPlane(Mover.SimPosition.Z);
                        _MoverPlane.HeightMapNeedsUpdate = true;
                        _MoverPlane.Users++;
                    }
                }
                return _MoverPlane;
            }
        }

        public float BumpConstraint = CollisionIndex.MaxBumpInOpenPath;
        public static float MaxBigZ = 9f;

        public override SimMoverState Goto()
        {
            if (Goto(FinalPosition, FinalDistance)) return SimMoverState.COMPLETE;
            return STATE;
            BumpConstraint -= 0.05f; // tighten constraints locally
            if (BumpConstraint < 0.15) BumpConstraint = 0.9f; // recycle
            CollisionPlane MoverPlane = MoverPlaneZ;
            MoverPlane.ChangeConstraints(GetSimPosition(), BumpConstraint);
            if (Goto(FinalPosition, FinalDistance))
            {
                // use new tighter constraint semipermanently
                MoverPlane.GlobalBumpConstraint = BumpConstraint;
                return SimMoverState.COMPLETE;
            }
            return STATE;
        }

        // this belongs on C-Plane or PathStore
        static private int UsedBigZ = 0;
        private float Orig = 0;
        static private float Works = 0;
        private bool PreXp;

        private bool Goto(SimPosition globalEnd, double distance)
        {
            bool OnlyStart = true;
            bool MadeIt = true;

            int maxTryAgains = 0;
            IList<Vector3d> route = null;
            CollisionPlane MoverPlane = MoverPlaneZ;
            bool faked;
            bool changePlanes = false;
            while (OnlyStart && MadeIt)
            {
                float InitZ = Mover.SimPosition.Z;
                Vector3d v3d = GetWorldPosition();
                if (Mover.Distance(FinalPosition) < distance) return true;
                float G = MoverPlane.GlobalBumpConstraint;
                if (G < 1f) Orig = G;
                SimMoverState prev = STATE;
                STATE = SimMoverState.THINKING;
                route = SimPathStore.GetPath(MoverPlane, v3d, globalEnd.UsePosition.GlobalPosition, distance, out OnlyStart, out faked);
                // todo need to look at OnlyStart?
                PreXp = route.Count < 3 && G < MaxBigZ;
                while (route.Count < 3 && G < MaxBigZ && faked)
                {
                    if (G < 0.5)
                    {
                        G += 0.1f;
                    }
                    else
                    {
                        G += 0.5f;
                        if (Works > G) G = Works;
                    }

                    MoverPlane.GlobalBumpConstraint = G;
                    route = SimPathStore.GetPath(MoverPlane, v3d, globalEnd.UsePosition.GlobalPosition, distance, out OnlyStart, out faked);
                }
                if (PreXp)
                {
                    Works = G;
                    UsedBigZ++;
                    Debug("BigG used {0} instead of {1} !!", G, Orig);
                    OnlyStart = true;
                }
                else
                {  // not PreXP
                    if (UsedBigZ > 0)
                    {
                        if (Works > 0.7f) Works -= 0.5f;  // todo not as liberal the next time?
                        UsedBigZ = 0;
                        Debug("BigG RESTORE {0} instead of {1} !!", Orig, MoverPlane.GlobalBumpConstraint);
                        MoverPlane.GlobalBumpConstraint = Orig;
                    }
                }
                STATE = prev;
                STATE = FollowPathTo(route, globalEnd.UsePosition.GlobalPosition, distance);
                Vector3 newPos = GetSimPosition();
                           switch (STATE)
                {
                    case SimMoverState.TRYAGAIN:
                        {
                            if (maxTryAgains-- > 0)
                            {
                                OnlyStart = true;
                                continue;
                            }
                            MadeIt = false;
                            continue;
                        }
                    case SimMoverState.COMPLETE:
                        {
                            MadeIt = true;
                            continue;
                        }
                    case SimMoverState.BLOCKED:
                        {
                            float globalEndUsePositionGlobalPositionZ = (float)globalEnd.UsePosition.GlobalPosition.Z;
                            if (faked)
                            {
                                if (Math.Abs(Mover.SimPosition.Z - InitZ) > 1)
                                {
                                    MoverPlane = MoverPlanes[Mover] = PathStore.GetCollisionPlane(Mover.SimPosition.Z);
                                } else
                                {
                                    MoverPlane = MoverPlanes[Mover] = PathStore.GetCollisionPlane(globalEndUsePositionGlobalPositionZ);
                                }
                            }
                            OnlyStart = true;
                            MadeIt = false;
                            continue;
                        }
                    case SimMoverState.PAUSED:
                    case SimMoverState.MOVING:
                    case SimMoverState.THINKING:
                    default:
                        {
                            //MadeIt = true;
                            OnlyStart = true;
                            continue;
                        }
                }

            
            }
            if (!MadeIt && route!=null)
            {
                SimMoverState prev = STATE;
                STATE = SimMoverState.THINKING;
                CollisionPlane CP = MoverPlane;
                if (CP.HeightMapNeedsUpdate) CP.MatrixNeedsUpdate = true;
                if (!CP.MatrixNeedsUpdate || !CP.HeightMapNeedsUpdate)
                {
                    if (false)
                    {
                        CP.MatrixNeedsUpdate = true;
                        CP.HeightMapNeedsUpdate = true;
                        Debug("Faking matrix needs update?");
                    }
                }
                else
                {
                    Debug("Matrix really needed update");
                }
                Vector3d v3d = GetWorldPosition();
                double fd = DistanceNoZ(v3d, globalEnd.GlobalPosition);
                if (fd < distance)
                {
                    STATE = SimMoverState.COMPLETE;
                    return true;
                }
                CP.EnsureUpdated();
                int index = ClosetPointOnRoute(route, v3d);
                if (index > 0)
                {
                    var blockMe = new List<Vector3d>();
                    if (index + 2 < route.Count)
                    {
                        Vector3d firstBad = route[index + 1];
                        var np = v3d + ((firstBad - v3d)/2);
                        blockMe.Add(np);
                        blockMe.Add(firstBad);
                        DepricateRoute(blockMe, fd);
                        STATE = prev;
                        return MadeIt;
                    }
                }

                if (fd > distance && fd > 2)
                {
                    if (false) DepricateRoute(route, fd);
                }
                Debug("Too far away " + fd + " wanted " + distance);
                STATE = prev;
            }
            return MadeIt;
        }

        static public int ClosetPointOnRoute(IList<Vector3d> route, Vector3d closeTo)
        {
            int thisPoint = 0;
            int bestPoint = -1;
            double bestPointDist = 9999999;
            foreach (Vector3d vector3D in route)
            {
                double thisPointDist = DistanceNoZ(vector3D, closeTo);
                if (thisPointDist < bestPointDist)
                {
                    bestPoint = thisPoint;
                    bestPointDist = thisPointDist;
                }
                else
                {
                    if (thisPointDist > bestPointDist && bestPoint != -1)
                    {
                        return bestPoint;
                    }
                }
                thisPoint++;
            }
            return bestPoint;
        }

        /// <summary>
        /// Make sure this the non-simplfied route
        /// </summary>
        /// <param name="route"></param>
        private void DepricateRoute(IEnumerable<Vector3d> route, double finalDist)
        {
            List<Vector3d> reallyDepricate = new List<Vector3d>();
            List<ThreadStart> listUndo = new List<ThreadStart>();
            const int time = 120;
            Vector3d first = default(Vector3d);
            foreach (Vector3d list in route)
            {
                if (first == default(Vector3d))
                {
                    first = list;
                    continue;
                }
                if (Vector3d.Distance(first, list) > finalDist) break;
                reallyDepricate.Add(list);
            }
            Mover.IndicateRoute(reallyDepricate, Color.Orchid);
            foreach (Vector3d list in reallyDepricate)
            {
                PathStore.BlockPointTemp(SimPathStore.GlobalToLocal(list), listUndo, SimPathStore.BLOCKED);
            }

            if (listUndo.Count == 0) return;
            string tmp = string.Format("Blocking {0} points for {1} seconds", listUndo.Count, time);
            Thread thr = new Thread(() =>
                                        {
                                            Debug(tmp);
                                            Thread.Sleep(time*1000);
                                            Debug("Un-{0}", tmp);
                                            foreach (ThreadStart undo in listUndo)
                                            {
                                                undo();
                                            }
                                        })
                             {
                                 Name = tmp
                             };
            thr.Start();
        }
  
    }


    public class SimReRouteMover : SimAbstractMover
    {
        public SimReRouteMover(SimMover mover, SimPosition finalGoal, double finalDistance)
            : base(mover, finalGoal, finalDistance)
        {

        }

        public IList<SimRoute> GetRouteList(SimWaypoint to, out bool IsFake)
        {
            SimWaypoint from = GetWaypoint();

            //IList<SimRoute> route = PathStore.GetRoute(from, to, out IsFake);
            //if (false)
            //{
            //    ////pathByNodes
            //    //if (GraphFormer.DEBUGGER != null)
            //    //{
            //    //    new Thread(new ThreadStart(delegate()
            //    //    {
            //    //        //GraphFormer.DEBUGGER.Invalidate();
            //    //        //      GraphFormer.DEBUGGER.SetTryPathNow(from, to, pathByNodes);
            //    //    })).Start();
            //    //}
            //}
            // TODO return PathStore.GetRoute(from, to, out IsFake);
            throw new Exception("The method or operation is not implemented.");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="IsFake"></param>
        /// <returns></returns>s
        public bool TryGotoTarget(SimPosition pos, out bool IsFake)
        {
            IsFake = false;
            SimMoverState state = SimMoverState.TRYAGAIN;
            while (state == SimMoverState.TRYAGAIN)
            {
                SimWaypoint target = (SimWaypoint)pos;
                IList<SimRoute> routes = (IList<SimRoute>)GetRouteList(target, out IsFake);
                if (routes == null) return false;
                SimRouteMover ApproachPlan = new SimRouteMover(Mover, routes, pos.GlobalPosition, pos.GetSizeDistance());
                state = ApproachPlan.Goto();
                if (state == SimMoverState.COMPLETE) return true;
            }
            return false;

            //== SimMoverState.COMPLETE;
        }

        public bool GotoSimRoute(SimPosition pos)
        {
            bool IsFake;
            for (int i = 0; i < 19; i++)
            {
                Debug("PLAN GotoTarget: " + pos);
                // StopMoving();
                if (TryGotoTarget(pos, out IsFake))
                {
                    Mover.StopMoving();
                    Mover.TurnToward(pos.GlobalPosition);
                    Debug("SUCCESS GotoTarget: " + pos);
                    return true;
                }

                //TurnToward(pos);
                double posDist = Mover.Distance(pos);
                if (posDist <= pos.GetSizeDistance() + 0.5)
                {
                    Debug("OK GotoTarget: " + pos);
                    return true;
                }
                TurnAvoid += 115;
                while (TurnAvoid > 360)
                {
                    TurnAvoid -= 360;
                }
                //Vector3d newPost = GetLeftPos(TurnAvoid, 4f);

                //StopMoving();
                //Debug("MOVELEFT GotoTarget: " + pos);
                //MoveTo(newPost, 2f, 4);
                if (IsFake) break;
            }
            Debug("FAILED GotoTarget: " + pos);
            return false;
        }

        public override SimMoverState Goto()
        {
            GotoSimRoute(FinalPosition);
            return STATE;
        }
    }

    public class SimRouteMover : SimAbstractMover
    {

        IList<SimRoute> Routes;
        int CurrentRouteIndex = 0;
        SimRoute OuterRoute = null;


        public SimRouteMover(SimMover mover, IList<SimRoute> routes, Vector3d finalGoal, double finalDistance)
            : base(mover, mover.PathStore.CreateClosestWaypoint(finalGoal, finalDistance), finalDistance)
        {
            Routes = routes;
            OuterRoute = new SimRouteMulti(routes);
        }

        public override SimMoverState Goto()
        {
            STATE = SimMoverState.MOVING;
            int CanSkip = 0;
            int Skipped = 0;
            int tried = 0;
            SimRoute prev = null;

            for (int cI = CurrentRouteIndex; cI < Routes.Count; cI++)
            {
                CurrentRouteIndex = cI;
                if (cI > 0)
                {
                    prev = Routes[cI - 1];
                }

                SimRoute route = Routes[cI];
                if (route.IsBlocked)
                {
                    STATE = SimMoverState.BLOCKED;
                    continue;
                }

                tried++;
                // TRY
                STATE = FollowRoute(route);

                double distance = Mover.Distance(FinalPosition);
                if (STATE == SimMoverState.BLOCKED)
                {
                    Mover.StopMoving();
                    //  SetBlocked(route);
                    if (distance < FinalDistance)
                    {
                        return SimMoverState.COMPLETE;
                    }
                    //CreateSurroundWaypoints();
                    route.ReWeight(1.1f);
                    route.BumpyCount++;
                    if (CanSkip > 0)
                    {
                        CanSkip--;
                        Skipped++;
                        continue;
                    }
                    if (route.BumpyCount > 0 && Skipped == 0)
                    {
                        SetBlocked(route);
                        //  if (prev!=null) if (FollowRoute(prev.Reverse) == SimMoverState.COMPLETE)
                        // {
                        //   return SimMoverState.TRYAGAIN;
                        // }
                    }
                    return STATE;
                }
                if (STATE == SimMoverState.PAUSED)
                {
                    if (distance < FinalDistance)
                    {
                        return SimMoverState.COMPLETE;
                    }
                    return SimMoverState.TRYAGAIN;
                }
                if (STATE == SimMoverState.COMPLETE)
                {
                    // if made it here then the prev was very good
                    if (prev != null)
                        prev.ReWeight(0.8f);
                }

                if (distance < FinalDistance)
                {
                    return SimMoverState.COMPLETE;
                }
            }
            if (STATE != SimMoverState.COMPLETE)
            {

                if (tried == 0)
                {
                    return SimMoverState.TRYAGAIN;
                }
                return STATE;
            }
            OuterRoute.ReWeight(0.7f); // Reward
            //TODO Mover.PathStore.AddArc(OuterRoute);
            STATE = SimMoverState.COMPLETE;
            return STATE;
        }

        public void SetBlocked(SimRoute StuckAt)
        {
            BlockTowardsVector(StuckAt._EndNode.SimPosition);

            Vector3d pos = Mover.GlobalPosition;
            if (StuckAt.BlockedPoint(GetGlobal(pos)))
            {
                StuckAt.ReWeight(1.1f);
                Debug("BLOCKED: " + StuckAt);
            }
            else
            {
                SimRoute StuckAt2 = OuterRoute.WhichRoute(GetGlobal(pos));
                if (StuckAt2 == null)
                {
                    StuckAt.ReWeight(1.3f);
                    //OuterRoute.ReWeight(1.2f);
                    Debug("INACESSABLE: " + StuckAt);
                    StuckAt.Passable = false;
                }
                else
                {
                    StuckAt2.ReWeight(1.1f);
                    Debug("ROUTE BLOCKED: " + StuckAt2);
                    StuckAt2.BlockedPoint(GetGlobal(pos));
                    StuckAt2.Passable = false;
                    StuckAt2.Reverse.Passable = false;
                }
            }

            // SimPathStore.Instance.RemoveArc(StuckAt);
            ///SimPathStore.Instance.RemoveArc(StuckAt.Reverse);

            STATE = SimMoverState.BLOCKED;
        }

        public Vector3d GetGlobal(Vector3d pos)
        {
            return pos;// PathStore.LocalToGlobal(pos);
        }


        public SimMoverState FollowRoute(SimRoute route)
        {
            Vector3d vectStart = route.StartNode.GlobalPosition;
            Vector3d vectMover = Mover.GlobalPosition;
            double currentDistFromStart = DistanceNoZ(vectMover, vectStart);
            if (currentDistFromStart > CloseDistance)
            {
                Debug("FollowRoute: TRYING for Start " + vectMover + " -> " + vectStart);
                if (!MoveTo(vectStart))
                {
                    Debug("FollowRoute: FAILED Start " + vectMover + " -> " + vectStart);
                    return SimMoverState.TRYAGAIN;
                }
            }
            Vector3d endVect = route.EndNode.GlobalPosition;

            bool MadeIt = MoveTo(endVect);

            if (!MadeIt)
            {
                Debug("FollowRoute: BLOCKED ROUTE " + vectMover + "-> " + endVect);
                //route.Passable = false;
                return SimMoverState.BLOCKED;
            }

            Vector3d endVectMover = Mover.GlobalPosition;
            double currentDistFromfinish = DistanceNoZ(endVectMover, endVect);
            if (currentDistFromfinish > CloseDistance)
            {
                Debug("FollowRoute: CANNOT FINISH " + endVectMover + " -> " + endVect);
                return SimMoverState.PAUSED;
            }
            Debug("FollowRoute: SUCCEED " + vectStart + " -> " + endVectMover);
            return SimMoverState.COMPLETE;
        }
    }

    //}

    //public class SimVectorMover : SimAbstractMover
    //{

    //    public SimVectorMover(SimMover mover, Vector3d finalGoal, double finalDistance)
    //        : base(mover, finalGoal, finalDistance)           
    //    {
    //    }

    //    public bool FollowPathTo(Vector3d vector3, double finalDistance)
    //    {

    //        int OneCount = 0;
    //        Mover.TurnToward(vector3);
    //        if (DistanceNoZ(GlobalPosition(), vector3) < finalDistance) return true;
    //        for (int trial = 0; trial < 25; trial++)
    //        {
    //            Mover.StopMoving();
    //            Application.DoEvents();
    //            Vector3d start = GetUsePosition();

    //            if (!PathStore.IsPassable(start))
    //            {
    //                start = MoveToPassableArround(start);
    //            }
    //            Vector3d end = vector3;


    //            List<Vector3d> v3s = (List<Vector3d>)Mover.PathStore.GetV3Route(start, end);
    //            if (v3s.Count > 1)
    //            {
    //                if (DistanceNoZ(v3s[0], start) > DistanceNoZ(v3s[v3s.Count - 1], start))
    //                    v3s.Reverse();
    //            }
    //            else
    //            {
    //                MoveToPassableArround(GlobalPosition());
    //                //  GetUsePosition();
    //                if (OneCount > 3) return false;
    //                OneCount++;
    //            }

    //            Debug("Path {1}: {0} ", v3s.Count, trial);
    //            if (FollowPath(v3s, vector3, finalDistance)) return true;
    //            if (DistanceNoZ(GlobalPosition(), vector3) < finalDistance) return true;

    //        }
    //        return false;
    //    }


    //    public bool FollowPath(List<Vector3d> v3sIn, Vector3d finalTarget, double finalDistance)
    //    {
    //        IList<Vector3d> v3s = PathStore.GetSimplifedRoute(GlobalPosition(), v3sIn, 10, 8f);
    //        Debug("FollowPath: {0} -> {1}", v3sIn.Count, v3s.Count);
    //        int CanSkip = 2;
    //        int Skipped = 0;
    //        foreach (Vector3d v3 in v3s)
    //        {
    //            STATE = SimMoverState.MOVING;
    //            //  if (DistanceNoZ(v3, GlobalPosition()) < dist) continue;
    //            if (!MoveTo(v3))
    //            {
    //                STATE = SimMoverState.TRYAGAIN;
    //                if (DistanceNoZ(GlobalPosition(), finalTarget) < finalDistance) return true;
    //                if (!Mover.MoveTo(v3,PathStore.LargeScale,4))
    //                {
    //                    if (Skipped++ <= CanSkip)
    //                    {
    //                        MoveToPassableArround(GlobalPosition());
    //                        Skipped++;
    //                        continue;
    //                    }
    //                    BlockTowardsVector(v3);
    //                    STATE = SimMoverState.BLOCKED;
    //                    return false;
    //                }
    //            }
    //            else
    //            {
    //                STATE = SimMoverState.MOVING;
    //                Skipped = 0;
    //            }

    //        }
    //        STATE = SimMoverState.COMPLETE;
    //        return true;
    //    }



    //    public override SimMoverState Goto()
    //    {
    //        if (FollowPathTo(FinalLocation, FinalDistance))
    //        {
    //            return SimMoverState.COMPLETE;
    //        }
    //        return SimMoverState.TRYAGAIN;
    //    }
    //}
}
