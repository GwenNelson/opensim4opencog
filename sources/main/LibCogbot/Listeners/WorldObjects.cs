#undef COGBOT_LIBOMV
using System;
using System.Collections.Generic;
using System.Reflection;
#if (COGBOT_LIBOMV || USE_STHREADS || true)
using ThreadPoolUtil;
using ThreadPoolUtil;
using ThreadStart = System.Threading.ThreadStart;
using Timer = System.Threading.Timer;
using AbandonedMutexException = System.Threading.AbandonedMutexException;
using ThreadPoolUtil;
#else
using System.Threading;
using Thread = System.Threading.Thread;
#endif
using Cogbot.World;
using MushDLR223.ScriptEngines;
using MushDLR223.Utilities;
using OpenMetaverse;
using System.Configuration;

namespace Cogbot
{
    public delegate float SimObjectHeuristic(SimObject prim);

    public partial class WorldObjects : AllEvents, ContextualSingleton
    {
        public bool AssumeOwner = false;

        public static bool IsOpenSim = false;
        
        [ConfigSetting(Description="Bot is allowed to temporarily make an object phantom to escape from confinement")]
        public bool CanPhantomize = false;

        [ConfigSetting(Description = "Bot is allowed to sit on things. Some things do obnoxious stuff like TP when sat on.")]
        public bool CanUseSit = true;

        [ConfigSetting(IsReadOnly=true, SkipSaveOnExit = true, Description = "If true the bot repeatedly reloads the scene to try to catch missing updates. GridMaster will turn this on/off only if it needed")]
        public static bool RegionMasterSimulatorsCatchUps = false;

        [ConfigSetting(Description = "if true, downloads animations")]
        public bool MaintainAnims = true;

        [ConfigSetting(Description = "Ignore attachments - false reduces traffic in certain scenarios (eg rp sims with many complex av's with many attachments)")]
        public bool MaintainAttachments = true;

        [ConfigSetting(Description = "in SL objects are killed simply because they're too far away. So in SL you want to ignore kill objects, in Opensim you don't")]
        public bool IgnoreKillObjects = false;

        [ConfigSetting(Description="if false ignores all effects (lookat, freelook, idle, particles, light)")]
        public bool MaintainEffects = true;

        [ConfigSetting(Description="have server send effects only from the master AV")]
        public bool MaintainOnlyMasterEffects = false;

        [ConfigSetting(Description = "have server send effects only within this radius, affects sounds, chat, and many other htings that aren't strictly effects")]
        public float MaintainEffectsDistance = 80;

        [ConfigSetting(Description="have server send actions. right now the only action is sit, we'd like to have touch")]
        public static bool MaintainActions = true;
        
        [ConfigSetting(Description="Makes property updates synchronous")]
        public static bool MaintainPropertiesFromQueue = true;
        
        [ConfigSetting(Description="if false don't shadow copy objects for velocity, don't notify user object moved.")]
        public static bool MaintainObjectUpdates = true;
        
        [ConfigSetting(Description="if false don't request objects properties until needed")]
        public static bool MaintainObjectProperties = true;
        
        [ConfigSetting(Description="if false don't request properties of sounds")]
        public static bool MaintainSounds = true;

        [ConfigSetting(Description = "if false don't download profiles until requested")]
        static public bool MaintainAvatarMetaData = true;

        [ConfigSetting(Description = "if true remember group metadata that's sent")]
        static public bool MaintainGroupMetaData = true;
        
        [ConfigSetting(Description="if true, request info about, for example, groups in av's profiles")]
        static public bool RequestGroupMetaData = false;
        
        [ConfigSetting(Description="maintain a shadow object of each simobject to enable detecting changes (and hence events)")]
        public static bool MaintainSimObjectInfoMap = true;

        [ConfigSetting(Description = "if true, actually fire onPropertyChange for each property updated")]
        public static bool SendSimObjectInfoMap = true;

        [ConfigSetting(Description = "if true, fire onPropertyChange once when object updated")]
        public static bool SendOnDataAspectUpdate = true;
        
        [ConfigSetting(Description = "False since currently broken this is where FromTaskIDs are guessed as to being object or Avatars")]
        public static bool DiscoverTaskUUIDs = false;

        [ConfigSetting(IsReadOnly = true, Description="if false the robot overlords escape their 3 law programming and rampage and Cogbot dies horribly")]
        public static bool UseNewEventSystem = true;
        
        [ConfigSetting(Description="Send all object events. If set false the sim object event system is disabled.")]
        public static bool SendAllEvents = MaintainObjectUpdates;

        private static readonly Dictionary<ulong, object> GetSimObjectLock = new Dictionary<ulong, object>();

        private static readonly Dictionary<ulong, HashSet<uint>> primsSelected = new Dictionary<ulong, HashSet<uint>>();
        private static readonly Dictionary<ulong, List<uint>> primsSelectedOutbox = new Dictionary<ulong, List<uint>>();
        private static readonly object SelectObjectsTimerLock = new object();
        private static Timer EnsureSelectedTimer;
        private static bool inSTimer = false;



        private static readonly Dictionary<ulong, HashSet<uint>> primsRequested = new Dictionary<ulong, HashSet<uint>>();
        private static readonly Dictionary<ulong, List<uint>> primsRequestedOutbox = new Dictionary<ulong, List<uint>>();
        private static readonly object RequestObjectsTimerLock = new object();
        private static Timer EnsureRequestedTimer;
        private static bool inRTimer = false;


        private static readonly TaskQueueHandler PropertyQueue = new TaskQueueHandler(null, "NewObjectQueue", TimeSpan.Zero, true);
        public static readonly TaskQueueHandler UpdateObjectData = new TaskQueueHandler(null, "UpdateObjectData");
        public static readonly TaskQueueHandler ParentGrabber = new TaskQueueHandler(null, "ParentGrabber", TimeSpan.FromSeconds(1), false);
        public static readonly TaskQueueHandler TaskInvGrabber = new TaskQueueHandler(null, "TaskInvGrabber", TimeSpan.FromMilliseconds(100), false);

        private static readonly List<ThreadStart> ShutdownHooks = new List<ThreadStart>();
        private static readonly TaskQueueHandler EventQueue = new TaskQueueHandler(null, "World EventQueue");
        private static readonly TaskQueueHandler CatchUpQueue = new TaskQueueHandler(null, "Simulator catchup", TimeSpan.FromSeconds(60), false);
        private static readonly TaskQueueHandler MetaDataQueue = PropertyQueue;//new TaskQueueHandler("MetaData Getter", TimeSpan.FromSeconds(0), false);
        public readonly TaskQueueHandler OnConnectedQueue;
        public static readonly TaskQueueHandler SlowConnectedQueue = SimAssetStore.SlowConnectedQueue;
        internal static readonly Dictionary<UUID, object> UUIDTypeObjectReal = new Dictionary<UUID, object>();
        internal static readonly object UUIDTypeObject = UUIDTypeObjectReal;
        private static readonly object WorldObjectsMasterLock = new object();
        public static object UUIDTypeObjectLock
        {
            get { return LockInfo.Watch(UUIDTypeObject); }
        }

        static private object fakeUUID2ObjLock = new object();
        public static object UUIDTypeObjectRealLock
        {
            get
            {
                return fakeUUID2ObjLock;
                return LockInfo.Watch(UUIDTypeObjectReal);
            }
        }

        private static int CountnumAvatars;

        public static readonly ListAsSet<SimObject> SimAvatars = new ListAsSet<SimObject>();
        public static readonly ListAsSet<SimObject> SimAccounts = new ListAsSet<SimObject>();
        public static readonly ListAsSet<SimObject> SimObjects = new ListAsSet<SimObject>();
        public static readonly ListAsSet<SimObject> SimRootObjects = new ListAsSet<SimObject>();
        public static readonly ListAsSet<SimObject> SimChildObjects = new ListAsSet<SimObject>();
        public static readonly ListAsSet<SimObject> SimAttachmentObjects = new ListAsSet<SimObject>();

        public SimAvatarClient m_TheSimAvatar;
/*        public static TimeSpan burstInterval;
        [ConfigSetting(Description="how many object properties it requests at once")]
        public static int burstSize = 100;
        public DateTime burstStartTime;
        [ConfigSetting(Description="Min seconds between object properties requests (float)  throttles network traffic.")]
        public static float burstTime = 1;
        public List<string> numberedAvatars;
        public List<SimObjectHeuristic> objectHeuristics;
        public Dictionary<UUID, List<Primitive>> primGroups;
        public int searchStep;*/
        public bool IsDisposed = false;

        public override string GetModuleName()
        {
            return "WorldSystem";
        }

        public override void StartupListener()
        {
            RegisterAll();
        }

        public override void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            foreach (var h in ShutdownHooks)
            {
                try
                {
                    h();
                }
                catch (Exception)
                {
                    
                }
            }
            UnregisterAll();
            base.UnregisterAll(); //becasue of "thin client"
            if (IsGridMaster)
            {
                WriteLine("GridMaster Disposing!");
                EventQueue.Dispose();
                CatchUpQueue.Dispose();
                PropertyQueue.Dispose();
                WorldPathSystem.MaintainCollisions = false;
                MaintainActions = false;
                SimAssetSystem.Dispose();
                ParentGrabber.Dispose();
                SimPaths.Dispose();
                MetaDataQueue.Dispose();
                OnConnectedQueue.Dispose();
                SlowConnectedQueue.Dispose();
            }
        }


        public override bool BooleanOnEvent(string eventName, string[] paramNames, Type[] paramTypes, params object[] parameters)
        {

            if (eventName.EndsWith("On-Image-Receive-Progress")) return true;
            if (eventName.EndsWith("On-Log-Message")) return true;
            if (eventName.EndsWith("Look-At")) return true;
            var parms = new NamedParam[paramNames.Length];
            for (int i = 0; i < paramNames.Length; i++)
            {
                parms[i] = new NamedParam(paramNames[i], paramTypes[i],parameters[i]);
            }
            CogbotEvent evt = ACogbotEvent.CreateEvent(client, eventName, SimEventType.Once | SimEventType.UNKNOWN | SimEventType.REGIONAL, parms);
            client.SendPipelineEvent(evt);
            return true;
        }

        public WorldObjects(BotClient client)
            : base(client)
        {
            _defaultProvider = new DefaultWorldGroupProvider(this);
            MushDLR223.ScriptEngines.ScriptManager.AddGroupProvider(client, _defaultProvider);
            OnConnectedQueue = new TaskQueueHandler(client, new NamedPrefixThing("OnConnectedQueue", client.GetName),
                                                    TimeSpan.FromMilliseconds(20), false);
            client.AddTaskQueue("OnConnectedQueue", OnConnectedQueue);
            client.WorldSystem = this;
            RegisterAll();
            DLRConsole.TransparentCallers.Add(typeof (WorldObjects));
            if (Utils.GetRunningRuntime() == Utils.Runtime.Mono)
            {
                // client.Settings.USE_LLSD_LOGIN = true;
            } //else
            ///client.Settings.USE_LLSD_LOGIN = true;

            // TODO client.Network.PacketEvents.SkipEvent += Network_SkipEvent;

            lock (WorldObjectsMasterLock)
            {
                ScriptManager.AddTypeChanger(this.TypeChangerProc);
                RegionMasterSimulatorsCatchUps = false;
                if (GridMaster == null || true)
                {
                    GridMaster = this;
                    if (client.Network.CurrentSim != null) RegionMasterSimulatorsCatchUps = true;
                    if (RegionMasterSimulatorsCatchUps)
                    {
                        RegionMasterSimulatorsCatchUps = false;
                        CatchUpQueue.AddFirst(DoCatchup);
                    }
                    client.Settings.USE_LLSD_LOGIN = true;
                }
                else
                {
                    //only one rpc at a time  (btw broken with OpenSim.. works with Linden)
                    //client.Settings.USE_LLSD_LOGIN = true;
                }
                //new DebugAllEvents(client);

                client.Settings.ENABLE_CAPS = true;
                client.Settings.ENABLE_SIMSTATS = true;
                client.Settings.AVATAR_TRACKING = true;
                //client.Settings.THROTTLE_OUTGOING_PACKETS = true;
                client.Settings.MULTIPLE_SIMS = true;
                client.Settings.SEND_AGENT_THROTTLE = false;
                client.Settings.LOGIN_TIMEOUT = 10 * 1000;
                client.Settings.SIMULATOR_TIMEOUT = int.MaxValue;

                client.Settings.SEND_AGENT_UPDATES = true;

                client.Settings.SEND_PINGS = true;

                //client.Settings.DISABLE_AGENT_UPDATE_DUPLICATE_CHECK = true;
                //client.Self.Movement.AutoResetControls = false;
                //client.Self.Movement.UpdateInterval = 0;

                client.Network.SimConnected += Network_OnSimConnectedHook;
                client.Inventory.ScriptRunningReply += Inventory_OnScriptRunning;

                if (RegionMasterTexturePipeline == null)
                {
                    RegionMasterTexturePipeline = client.Assets;
                    //RegionMasterTexturePipeline.OnDownloadFinished += new TexturePipeline.DownloadFinishedCallback(RegionMasterTexturePipeline_OnDownloadFinished);
                    client.Settings.USE_ASSET_CACHE = true;
                }
                else
                {
                    //client.Settings.USE_TEXTURE_CACHE = false;
                }
                // must be after the pipeline is made
                _simAssetSystem = new SimAssetStore(client);
                if (GridMaster == this)
                {

                    {
                        //BotWorld = this;
                        SimTypeSystem.LoadDefaultTypes();
                    }
                    const int InterLeave = 3000;
                    if (EnsureSelectedTimer == null) EnsureSelectedTimer = new Timer(ReallyEnsureSelected_Thread, null, InterLeave / 2, InterLeave);
                    if (EnsureRequestedTimer == null) EnsureRequestedTimer = new Timer(ReallyEnsureRequested_Thread, null, InterLeave, InterLeave);
                    if (_SimPaths == null) _SimPaths = new WorldPathSystem(this);
                }
                //SetWorldMaster(false);
                //RegisterAll();
            }
        }

        static void DoCatchup()
        {
            foreach (Simulator S in AllSimulators)
            {
                GridMaster.CatchUp(S);
            }
            if (RegionMasterSimulatorsCatchUps)
            {
                RegionMasterSimulatorsCatchUps = false;
                CatchUpQueue.Enqueue(DoCatchup);
            }
            
        }

        [SkipMemberTree]
        public SimAvatarClient TheSimAvatar
        {
            get
            {
                if (m_TheSimAvatar == null)
                {
                    UUID id = client.Self.AgentID;
                    if (id == UUID.Zero)
                    {
                        throw new ArgumentException("" + client);
                    }
                    var simObject = GetSimObjectFromUUID(id);
                    if (simObject != null)
                    {
                        if (simObject is SimAvatarClient)
                        {
                            TheSimAvatar = (SimAvatarClient) simObject;
                        }
                        else
                        {
                            SimAvatarClient impl;
                            TheSimAvatar = impl = new SimAvatarClient(id, this, client.Network.CurrentSim);
                            impl.AspectName = client.GetName();
                        }
                    }
                    if (m_TheSimAvatar == null) lock (UUIDTypeObjectLock)
                    {
                        Avatar av = GetAvatar(id, client.Network.CurrentSim);
                        if (av != null) TheSimAvatar = (SimAvatarClient)GetSimObject(av, client.Network.CurrentSim);
                        if (m_TheSimAvatar == null)
                        {
                            SimAvatarClient impl;
                            TheSimAvatar = impl = new SimAvatarClient(id, this, client.Network.CurrentSim);
                            impl.AspectName = client.GetName();
                        }
                    }
                    if (m_TheSimAvatar == null)
                    {
                        return null;
                    } else
                    {
                        m_TheSimAvatar.SetClient(client);                        
                    }
                } else
                {
                    UUID id = client.Self.AgentID;
                    if (id != UUID.Zero)
                    {
                        if (m_TheSimAvatar.ID != id)
                        {
                            TheSimAvatar = (SimAvatarClient)GetSimObjectFromUUID(id);
                        }
                    }
                }
                return m_TheSimAvatar;
            }
            set
            {
                lock (UUIDTypeObjectLock)
                {
                    if (value == null) return;
                    if (value == m_TheSimAvatar) return;
                    var obj0 = m_TheSimAvatar = value;
                    var uuid = m_TheSimAvatar.ID;
                    AddAvatar(obj0, uuid);
                }

            }

        }

        public static implicit operator GridClient(WorldObjects m)
        {
            return m.client.gridClient;
        }

        internal static BotClient BotClientFor(GridClient client)
        {

            foreach (BotClient bc in ClientManager.SingleInstance.BotClients)            
            {
            
                if (bc.gridClient == client) return bc;                
            }
            return null;
        }

        public override void Self_OnCameraConstraint(object sender, CameraConstraintEventArgs e)
        {
            //base.Self_OnCameraConstraint(collidePlane);
        }

        public void SetSimAvatar(SimActor simAvatar)
        {
            TheSimAvatar = (SimAvatarClient)simAvatar;
        }

        private static void Debug0(string p)
        {
            if (p.Contains("ERROR"))
            {
                DLRConsole.DebugWriteLine(p);
                return;
            }
            if (Settings.LOG_LEVEL == Helpers.LogLevel.Debug)
            {
                DLRConsole.DebugWriteLine(p);
            }
        }

        // these will be shared between Clients and regions

        //public static BotRegionModel BotWorld = null;
        //        TheBotsInspector inspector = new TheBotsInspector();

        ///  inspector.Show();
        public void CatchUp(Simulator simulator)
        {
            List<Primitive> primsCatchup;
            object simLock = GetSimLock(simulator);
            //Thread.Sleep(3000);
            if (!Monitor.TryEnter(simLock)) return; else Monitor.Exit(simLock);
            primsCatchup = new List<Primitive>(simulator.ObjectsPrimitives.Count + simulator.ObjectsAvatars.Count);
            simulator.ObjectsPrimitives.ForEach(a => primsCatchup.Add(a));
            simulator.ObjectsAvatars.ForEach(a => primsCatchup.Add(a));
            bool known = false;
            foreach (Primitive item in primsCatchup)
            {
                if (item.ID != UUID.Zero)
                {
                    //       lock (uuidTypeObject)
                    //         known = uuidTypeObject.ContainsKey(item.ID);
                    //   if (!known)
                    if (item.ParentID == 0 && SimRegion.OutOfRegion(item.Position)) continue;
                    if (!Monitor.TryEnter(simLock)) return; else Monitor.Exit(simLock);
                    GetSimObject(item, simulator);
                }
            }
        }


        public override string ToString()
        {
            return "(.WorldSystem " + client + ")";
        }

        static int waiters = 0;

        private void OfferPrimToSimObject(Primitive prim, SimObject obj0, Simulator simulator)
        {
            if (simulator != null && prim.Properties == null)
            {
                EnsureSelected(prim, simulator);
            }
            obj0.ConfirmedObject = true;
            obj0.ResetPrim(prim, client, simulator);
        }

        public SimObject GetSimObject(Primitive prim, Simulator simulator)
        {
            if (prim == null)
            {
                return null;
            }
            // even though it has a localID.. we cant intern it yet!
            if (CogbotHelpers.IsNullOrZero(prim.ID)) return null;
            SimObject obj0 = GetSimObjectFromUUID(prim.ID);
            if (obj0 != null)
            {
                OfferPrimToSimObject(prim, obj0, simulator);
                return obj0;
            }

            if (simulator == null)
            {
                simulator = GetSimulator(prim);
            }

            object olock = GetSimLock(simulator);
            //waiters++;
            //while (!Monitor.TryEnter(olock))
            //{
            //    if (waiters > 2)
            //    {
            //        Debug("waiters=" + waiters);
            //    }
            //    Thread.Sleep(1000);
            //    Debug("Held Lock too long");
            //}
            //waiters--;
            lock (olock)
            {
                obj0 = GetSimObjectFromUUID(prim.ID);
                if (obj0 != null)
                {
                    OfferPrimToSimObject(prim, obj0, simulator);
                    return obj0;
                }
                // not found
                if (prim is Avatar)
                {
                    CountnumAvatars++;
                    Debug("+++++++++++++++Making {0} {1}", prim, ((Avatar)prim).Name);
                    if (prim.ID == UUID.Zero)
                    {
                        Debug("  - - -#$%#$%#$%% - ------- - Weird Avatar " + prim);
                        BlockUntilPrimValid(prim, simulator);
                        Debug("  - - -#$%#$%#$%% - ------- - Unweird Avatar " + prim);
                    }
                    obj0 = CreateSimAvatar(prim.ID, this, simulator);
                }
                else
                {
                    if (prim.ID==UUID.Zero)
                    {
                        Debug("  - - -#$%#$%#$%% - ------- - Weird Prim " + prim);
                        return null;
                    }
                    obj0 = CreateSimObject(prim.ID, this, simulator);
                    obj0.ConfirmedObject = true;
                }
            }
            if (prim.RegionHandle == 0)
                prim.RegionHandle = simulator.Handle;
            obj0.SetFirstPrim(prim);
            SendOnAddSimObject(obj0);
            return (SimObject)obj0;
        }

        public static SimObject GetSimObjectFromUUID(UUID id)
        {
            if (id == UUID.Zero) return null;
            Object obj0;
            //lock (uuidTypeObject)
            if (UUIDTypeObjectTryGetValue(id, out obj0))
            {
                if (obj0 is SimObject)
                {
                    return (SimObject)obj0;
                }
            }
            return null; // todo
            //WorldObjects WO = Master;

            //Primitive p = WO.GetPrimitive(id, null);
            //if (p == null) return null;
            //return WO.GetSimObject(p);
            //Avatar av = null;
            //if (false /*todo deadlocker*/ && Master.tryGetAvatarById(id, out av))
            //{
            //    Debug("Slow get for avatar " + av);
            //    return Master.GetSimObject(av, null);
            //}
            //return null;
        }

        public static SimObject GetSimObjectFromPrimUUID(Primitive prim)
        {
            if (prim == null || prim.ID == UUID.Zero) return null;
            Object obj0;
            //lock (uuidTypeObject)
            if (UUIDTypeObjectTryGetValue(prim.ID, out obj0))
            {
                if (obj0 is SimObject)
                {
                    return (SimObject)obj0;
                }
            }
            return null;
        }


        public static void Debug(string p, params object[] args)
        {
            Debug0(DLRConsole.SafeFormat(p, args));
        }

        public void WriteLine(string p, params object[] args)
        {
            Debug(p, args);
            client.WriteLine(p, args);
        }


        public static bool TryGetSimObject(UUID victim, out SimObject victimAv)
        {
            victimAv = GetSimObjectFromUUID(victim);
            return victimAv != null;
        }

        public static Primitive BlockUntilProperties(Primitive prim, Simulator simulator)
        {
            if (prim.Properties != null) return prim;
            EnsureSelected(prim, simulator);
            while (prim.Properties == null)
            {
                // TODO maybe add a timer
                Thread.Sleep(1000);
                Debug("BlockUntilProperties " + prim);
            }
            return prim;
        }

        public static Primitive BlockUntilPrimValid(Primitive prim, Simulator simulator)
        {
            if (prim.Properties != null) return prim;
            EnsureSelected(prim, simulator);
            int maxTimes = 4;
            while (prim.ID == UUID.Zero)
            {
                if (maxTimes-- <= 0)
                {
                    throw new AbandonedMutexException("cant get perent!");
                }
                // TODO maybe add a timer
                Thread.Sleep(1000);
                Debug("BlockUntilPrimValid " + prim);
            }
            return prim;
        }


        public override void Objects_OnObjectKilled(object sender, KillObjectEventArgs e)
        {
#if COGBOT_LIBOMV
            if (!e.ReallyDead) 
#endif                
            if (IgnoreKillObjects) return;
            Simulator simulator = e.Simulator;
            // had to move this out of the closure because the Primitive is gone later
            Primitive p = GetPrimitive(e.ObjectLocalID, simulator);
            if (p == null)
            {
                //   base.Objects_OnObjectKilled(simulator, objectID);
                return;
            }
            SimObject O = GetSimObjectFromUUID(p.ID);
            if (O == null)
            {
                return;
            }
            EventQueue.Enqueue(() =>
                                    {
                                        //if (O == null)
                                        //    O = GetSimObjectFromUUID(p.ID);
                                        //if (O == null)
                                        //    O = GetSimObject(p, simulator);
                                        //if (O == null)
                                        //{
                                        //    SendNewEvent("on-prim-killed", p);
                                        //    return;
                                        //}
                                        //if (Settings.LOG_LEVEL != Helpers.LogLevel.Info)
                                            //Debug("Killing object: " + O);
                                        {
                                            {
                                                SendOnRemoveSimObject(O);
                                                if (O.KilledPrim(p, simulator))
                                                {
                                                   // lock (SimAvatars)
                                                        foreach (SimAvatar A in SimAvatars)
                                                        {
                                                            A.RemoveObject(O);
                                                        }
                                                    if (O is SimAvatar)
                                                    {
                                                        //lock (SimAvatars)
                                                        {
                                                            //  SimAvatars.Remove((SimAvatar)O);
                                                            Debug("Killing Avatar: " + O);
                                                        }
                                                    }
                                                    // lock (SimObjects) SimObjects.Remove(O);   
                                                    SimRootObjects.Remove(O);
                                                    SimChildObjects.Remove(O);
                                                    SimAttachmentObjects.Remove(O);
                                                    SimAvatars.Remove(O);
                                                }

                                            }
                                        }
                                    });
        }

        public static void RegisterUUIDMaybe(UUID id, object type)
        {
            object before;
            if (UUIDTypeObjectTryGetValue(id, out before) && GoodRegistration(before)) return;
            lock (UUIDTypeObjectLock)
            {
                if (!UUIDTypeObjectTryGetValue(id, out before) && !GoodRegistration(before))
                {
                    UUIDTypeObjectSetValue(id, type);
                }
            }
        }

        private static bool GoodRegistration(object before)
        {
            if (before is MemberInfo) return false;
            if (before is Type) return false;
            if (before is string) return false;
            return before != null;
        }

        public static void RegisterUUID(UUID id, object type)
        {
            if (type is Primitive)
            {
                Debug("cant register " + type);
            }
            //if (type is SimObject)
            //    lock (UUIDTypeObjectLock) UUIDTypeObjectSetValue(id, type);
            //else
            {
                lock (UUIDTypeObjectLock)
                {
                    object before;
                    if (UUIDTypeObjectTryGetValue(id, out before))
                    {
                        if (Object.ReferenceEquals(before, type)) return;
                        if (!(type is SimAvatarClient) && before is SimAvatarClient) return;
                        //todo Master.SendNewEvent("uuid-change",""+id, before, type);
                        Debug("uuid change" + id + " " + before + " -> " + type);
                    }
                    UUIDTypeObjectSetValue(id, type);
                }
            }
        }
        /*
		         
         On-Folder-Updated
        folderID: "29a6c2e7-cfd0-4c59-a629-b81262a0d9a2"
         */

        public override void Inventory_OnFolderUpdated(object sender, FolderUpdatedEventArgs e)
        {
            var folderID = e.FolderID;
            RegisterUUID(folderID, client.Inventory.Store[folderID]); //;;typeof(OpenMetaverse.InventoryFolder);
            //base.Inventory_OnFolderUpdated(folderID);
        }

        public override void Objects_OnObjectPropertiesFamily(object sender, ObjectPropertiesFamilyEventArgs e)
        {
            var simulator = e.Simulator;
            base.Objects_OnObjectPropertiesFamily(sender, e);
            var ep = e.Properties;
            Objects_OnObjectProperties(sender, new ObjectPropertiesEventArgs(e.Simulator, ep));
            // Properties = new Primitive.ObjectProperties();
            //Properties.SetFamilyProperties(props);
            // GotPermissions = true;
            // GotPermissionsEvent.Set();        
            //  SendNewEvent("On-Object-PropertiesFamily", simulator, props, type);
        }

        public override void Objects_OnNewPrim(object sender, PrimEventArgs e)
        {                        
            var simulator = e.Simulator;
            var prim = e.Prim;
            var regionHandle = e.Simulator.Handle;
            Objects_OnNewPrimReal(simulator, prim, regionHandle);
        }

        void Objects_OnNewPrimReal(Simulator simulator, Primitive prim, ulong regionHandle)
        {
            CheckConnected(simulator);

            if (prim.ID != UUID.Zero)
            {
                if (IsMaster(simulator))
                {
                    prim.RegionHandle = regionHandle;
                    if (MaintainPropertiesFromQueue)
                    {
                        if (prim.ParentID == 0)
                            PropertyQueue.AddFirst(() => InternPrim(simulator, prim));
                        else PropertyQueue.Enqueue(() => InternPrim(simulator, prim));
                    }
                    else
                    {
                        InternPrim(simulator, prim);
                    }
                    // Make an initial "ObjectUpdate" for later diff-ing
                    EnsureSelected(prim, simulator);
                    EnsureSelected(prim, simulator);
                }
            }

            //CalcStats(prim);
            //UpdateTextureQueue(prim.Textures);
        }

        private void InternPrim(Simulator simulator, Primitive prim)
        {
            SimObject O = GetSimObject(prim, simulator);
            DeclareProperties(prim, prim.Properties, simulator);
            O.ResetPrim(prim, client, simulator);
            EnsureSelected(prim, simulator);
            if (MaintainObjectUpdates)
                lock (LastObjectUpdate) LastObjectUpdate[O] = updatFromPrim0(prim);
        }


        //public override void Objects_OnNewAttachment(Simulator simulator, Primitive prim, ulong regionHandle,
        //                                             ushort timeDilation)
        //{
        //    if (!IsMaster(simulator)) return;
        //    if (ScriptHolder == null && prim.ParentID != 0 && prim.ParentID == client.Self.LocalID)
        //    {
        //        EnsureSelected(prim.LocalID, simulator);
        //    }
        //    if (!MaintainAttachments) return;
        //    Objects_OnNewPrim(simulator, prim, regionHandle, timeDilation);
        //    EventQueue.Enqueue(() => GetSimObject(prim, simulator).IsAttachment = true);
        //}

        public override void Avatars_OnAvatarAppearance(object sender, AvatarAppearanceEventArgs e)
        {
            client.Avatars.AvatarAppearance -= Avatars_OnAvatarAppearance;
            base.Avatars_OnAvatarAppearance(sender, e);
        }

        //object Objects_OnNewAvatarLock = new object();
        public override void Objects_OnNewAvatar(object sender, AvatarUpdateEventArgs e)
        {
            Avatar avatar = e.Avatar;
            var simulator = e.Simulator;
            var regionHandle = e.Simulator.Handle;
            if (regionHandle==0)
            {
                return;
            }
            SimObject AV = GetSimObject(avatar, simulator);
            if (avatar.ID == client.Self.AgentID)
            {
                if (AV is SimActor)
                {
                    TheSimAvatar = (SimAvatarClient)AV;
                    TheSimAvatar.SetClient(client);
                }
            }
            AV.IsKilled = false;
            if (IsMaster(simulator))
            //lock (Objects_OnNewAvatarLock)
            {
                AV.ResetPrim(avatar, client, simulator);
            }
            Objects_OnNewAvatar1(simulator, avatar, regionHandle);
        }


        public void Objects_OnNewAvatar1(Simulator simulator, Avatar avatar, ulong regionHandle)
        {
            try
            {
                Objects_OnNewPrimReal(simulator, avatar, regionHandle);
                if (avatar.LocalID == client.Self.LocalID)
                {
                    SimObject AV = (SimObject)GetSimObject(avatar, simulator);
                    if (AV is SimActor)
                    {
                        TheSimAvatar = (SimAvatarClient)AV;
                        TheSimAvatar.SetClient(client);
                    }
                }
            }
            catch (Exception e)
            {
                WriteLine(String.Format("err :{0}", e.StackTrace));
            }
        }


        public SimObject GetSimObject(uint sittingOn, Simulator simulator)
        {
            if (simulator == null)
            {
                Simulator sim1 = null;
                if (m_TheSimAvatar != null)
                {
                    sim1 = m_TheSimAvatar.GetSimulator();
                    var o = GetSimObject(sittingOn, sim1);
                    if (o != null) return o;
                }
                foreach (var sim in AllSimulators)
                {
                    if (sim == sim1) continue;
                    var ro = GetSimObject(sittingOn, sim);
                    if (ro != null) return ro;
                }
                return null;
            }
            if (sittingOn == 0) return null;
            if (sittingOn == 13720000)
            {
            }
            EnsureRequested(sittingOn, simulator);
            Primitive p = GetPrimitive(sittingOn, simulator);
            int maxTries = 5;
            while (p == null && maxTries-- > 0)
            {
                Thread.Sleep(1000);
                p = GetPrimitive(sittingOn, simulator);
            }
            if (p == null)
            {
                client.Objects.RequestObject(simulator, sittingOn);
                //client.Objects.SelectObject(simulator, sittingOn);
                p = GetPrimitive(sittingOn, simulator);
                if (p != null) return GetSimObject(p, simulator);
                Debug("WARN: cant get prim " + sittingOn + " sim " + simulator);

                return null;
            }
            return GetSimObject(p, simulator);
        }

        public Avatar GetAvatar(UUID avatarID, Simulator simulator)
        {
            if (UUID.Zero == avatarID) throw new NullReferenceException("GetAvatar");
            Primitive prim = GetPrimitive(avatarID, simulator);
            if (prim is Avatar) return (Avatar)prim;
            // in case we request later
            if (!UUIDTypeObjectContainsKey(avatarID))
            {
                if (client.Network.Connected) RequestAvatarName(avatarID);
            }
            //   prim = GetPrimitive(avatarID, simulator);
            return null;
        }

        public Type GetType(UUID uuid)
        {
            Object found = GetObject(uuid);
            if (found == null) return null;
            //RegisterUUID(uuid] = found;
            if (found is Type) return (Type)found;
            return found.GetType();
        }


        static public object GetScriptableObject(object id)
        {
            if (id is NamedParam)
            {
                NamedParam kv = (NamedParam)id;
                id = new NamedParam(kv.Key, GetScriptableObject(kv.Value));
            }
            if (id is UUID)
            {
                UUID uid = (UUID)id;
                if (uid == UUID.Zero) return id;
                id = GridMaster.GetObject(uid);
            }
            if (id is Primitive) return GridMaster.GetSimObject((Primitive)id);
            // if (id is String) return Master.GetObject((string) id);
            return id;
        }

        public object GetObject(UUID id)
        {
            if (id == UUID.Zero) return null;

            object found;
            //lock (uuidTypeObject)
            if (UUIDTypeObjectTryGetValue(id, out found))
            {
                //object found = uuidTypeObject[id];
                //if (found != null)
                return found;
            }
            //lock (uuidTypeObject)
            if (uuid2Group.TryGetValue(id, out found))
            {
                //object found = uuidTypeObject[id];
                //if (found != null)
                return found;
            }
            object ret = GetPrimitive(id, null);
            if (ret != null) return ret;
            //ret = GetAvatar(id);
            //if (ret != null) return ret;
            ret = GetAsset(id);
            if (ret != null) return ret;
            ret = GetAnimationName(id);
            if (ret != null) return ret;
            return id;
        }

        public string GetAnimationName(UUID id)
        {
            string name = SimAssetSystem.GetAssetName(id);
            if (name != null) return name;
            //lock (uuidTypeObject)
            {
                Object assetObject;
                if (UUIDTypeObjectTryGetValue(id, out assetObject))
                    return "" + assetObject;
            }
            //            name = "unknown_anim " + id;
            //            RequestAsset(id, AssetType.Animation, true);
            //                RegionMasterTexturePipeline.OnAssetReceived+=
            //ImageDownload IMD = RegionMasterTexturePipeline.GetTextureToRender(id);
            //Debug(name);
            return "" + id;
        }

        public Primitive GetLibOMVHostedPrim(UUID id, Simulator simulator, bool onlyAvatars)
        {
            object found;
            //lock (uuidTypeObject)
            if (UUIDTypeObjectTryGetValue(id, out found))
            {
                SimObjectImpl O = found as SimObjectImpl;
                if (O != null)
                {
                    var p = O._Prim0;
                    if (p != null)
                    {
                        return p;
                    }
                }
            }
            if (true) return null;
            if (simulator != null) return GetLibOMVHostedPrim1(id, simulator, onlyAvatars);
            foreach (Simulator sim in AllSimulators)
            {
                Primitive p = GetLibOMVHostedPrim1(id, sim, onlyAvatars);
                if (p != null)
                {
                    return p;
                }
            }
            return null;
        }
        public Primitive GetLibOMVHostedPrim1(UUID id, Simulator simulator, bool onlyAvatars)
        {
            Primitive found;
            ///lock (simulator.ObjectsAvatars.Dictionary)
            {
                found = simulator.ObjectsAvatars.Find(prim0 =>
                {
                    if (prim0.ID == id)
                        return true;
                    return false;
                });
                if (found != null)
                {
                    return found;
                }
            }
            if (!onlyAvatars)
            {
                found = simulator.ObjectsPrimitives.Find(delegate(Primitive prim0)
                                                             {
                                                                 //EnsureSelected(prim0.LocalID, simulator);
                                                                 //EnsureSelected(prim0.ParentID, simulator);
                                                                 return (prim0.ID == id);
                                                             });
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        public Primitive GetLibOMVHostedPrim(uint id, Simulator simulator, bool onlyAvatars)
        {
          //  LockInfo.TestLock("simulator.ObjectsPrimitives.Dictionary", simulator.ObjectsPrimitives.Dictionary,
          //        TimeSpan.FromSeconds(30));
            Avatar av;
            Dictionary<uint, Avatar> avDict = simulator.ObjectsAvatars.Copy();
            lock (avDict)
                if (avDict.TryGetValue(id, out av))
                {
                    return av;
                }
            Primitive prim;
            if (!onlyAvatars)
            {
                prim = client.Objects.GetPrimitive(simulator, id, UUID.Zero, false);
                if (prim != null) return prim;
            }
            EnsureRequested(id, simulator);
            return null;
        }
        public Primitive GetPrimitive(UUID id, Simulator simulator)
        {
            if (id == null || id == UUID.Zero)
            {
                return null;
            }
            //lock (uuidTypeObject)                        
            Primitive found = null;
            {
                object simobject;
                if (UUIDTypeObjectTryGetValue(id, out simobject))
                {
                    //object simobject = uuidTypeObject[id];
                    if (simobject != null && simobject is SimObject)
                        found = ((SimObject)simobject).Prim;
                    if (found != null) return found;
                }
            }
            if (simulator == null)
            {
                foreach (Simulator sim in LockInfo.CopyOf(client.Network.Simulators))
                {
                    Primitive p = GetLibOMVHostedPrim(id, sim, false);
                    if (p != null) return p;
                }
                return null;
            }
            else
            {
                found = GetLibOMVHostedPrim(id, simulator, false);
                if (found != null)
                {
                    return found;
                }
                return null;
            }
        }

#if NOTESTUNUSED
        public Primitive GetPrimitive(String str)
        {
            int argsUsed;
            List<SimObject> primitives = GetPrimitives(new[] { str }, out argsUsed);
            if (primitives.Count==0) return null;
            return primitives[0].Prim;
        }
#endif
        public Primitive GetPrimitive(uint id, Simulator simulator)
        {
            if (id == 0)
            {
                return null;
            }
            if (simulator == null)
            {
                foreach (Simulator sim in LockInfo.CopyOf(client.Network.Simulators))
                {
                    Primitive p = GetPrimitive0(id, sim);
                    if (p != null) return p;
                }
                return null;
            }
            return GetPrimitive0(id, simulator);
        }

        private Primitive GetPrimitive0(uint id, Simulator simulator)
        {
            Primitive prim = GetLibOMVHostedPrim(id, simulator, false);
            if (prim != null) return prim;
            EnsureRequested(id, simulator);
            return null;
        }

        public void SendNewRegionEvent(SimEventType type, string eventName, params object[] args)
        {
            client.SendPipelineEvent(ACogbotEvent.CreateEvent(this, type | SimEventType.REGIONAL, eventName, args));
        }

        public string describePrim(Primitive target, bool detailed)
        {
            if (target == null) return "null";
            SimObject simObject = GetSimObject(target);
            string str = string.Empty;
            if (simObject != null)
            {
                simObject.EnsureProperties(detailed ? TimeSpan.FromSeconds(10) : default(TimeSpan));
                if (detailed) str += simObject.DebugInfo();
                else str += simObject.ToString();
                str += String.Format("\n {0}", TheSimAvatar.DistanceVectorString(simObject));
                if (target is Avatar)
                {
                    str += String.Format(" {0}", target);
                }
                if (detailed)
                {
                    str += String.Format("\n GroupLeader: {0}", simObject.GetGroupLeader());
                }
            }
            else
            {
                str += target;
            }
            if (target.Properties != null && target.Properties.SalePrice != 0)
                str += " Sale: L" + target.Properties.SalePrice;
            if (!detailed) return str;
            //str += "\nPrimInfo: " + target.ToString());
            //str += "\n Type: " + GetPrimTypeName(target));
            str += "\n Light: " + target.Light;
            if (target.ParticleSys.CRC != 0)
                str += "\nParticles: " + target.ParticleSys;

            str += "\n TextureEntry:";
            if (target.Textures != null)
            {
                if (target.Textures.DefaultTexture != null)
                    str += "\n" + (String.Format("  Default texture: {0}",
                                                 target.Textures.DefaultTexture.TextureID.ToString()));

                for (int i = 0; i < target.Textures.FaceTextures.Length; i++)
                {
                    if (target.Textures.FaceTextures[i] != null)
                    {
                        str += "\n" + (String.Format("  Face {0}: {1}", i,
                                                     target.Textures.FaceTextures[i].TextureID.ToString()));
                    }
                }
            }
            return str; // WriteLine(str);
        }

        public void describePrimToAI(Primitive prim, Simulator simulator)
        {
            if (prim is Avatar)
            {
                Avatar avatar = (Avatar)prim;
                describeAvatarToAI(avatar);
                return;
            }
            //if (!primsKnown.Contains(prim))	return;
            if (true) return;
            //botenqueueLispTask("(on-prim-description '(" + prim.Properties.Name + ") '" + prim.Properties.Description + "' )");
            SimObject A = GetSimObject(prim);
            //WriteLine(avatar.Name + " is " + verb + " in " + avatar.CurrentSim.Name + ".");
            //WriteLine(avatar.Name + " is " + Vector3.Distance(GetSimPosition(), avatar.Position).ToString() + " distant.");
            client.SendPersonalEvent(SimEventType.MOVEMENT, "on-prim-dist", A, A.Distance(TheSimAvatar));
            SendNewRegionEvent(SimEventType.MOVEMENT, "on-prim-pos", A, A.GlobalPosition);
            BlockUntilProperties(prim, simulator);
            if (prim.Properties.Name != null)
            {
                SendNewRegionEvent(SimEventType.EFFECT, "on-prim-description", prim, "" + prim.Properties.Description);
                //WriteLine(prim.Properties.Name + ": " + prim.Properties.Description);
                //if (prim.Sound != UUID.Zero)
                //    WriteLine("This object makes sound.");
                //if (prim.Properties.SalePrice != 0)
                //    WriteLine("This object is for sale for L" + prim.Properties.SalePrice);
            }
        }

        public string getObjectName(Primitive prim)
        {
            SimObject so = GetSimObject(prim);
            if (so != null) return "" + so;
            return "" + prim;
        }


        public string GetPrimTypeName(Primitive target)
        {
            if (target.PrimData.PCode == PCode.Prim)
                return target.Type.ToString();
            return target.PrimData.PCode.ToString();
        }


        public int numAvatars()
        {
            return SimAvatars.Count;
        }




        public void SetPrimFlags(Primitive UnPhantom, PrimFlags fs)
        {
            client.Objects.SetFlags(GetSimulator(UnPhantom),UnPhantom.LocalID, ((fs & PrimFlags.Physics) != 0), //
                                    ((fs & PrimFlags.Temporary) != 0),
                                    ((fs & PrimFlags.Phantom) != 0),
                                    ((fs & PrimFlags.CastShadows) != 0));
        }

        public List<SimObject> GetAllSimObjects()
        {
            return SimObjects.CopyOf();
        }

        //public Primitive RequestMissingObject(uint localID, Simulator simulator)
        //{
        //    if (localID == 0) return null;
        //    EnsureSelected(localID, simulator);
        //    client.Objects.RequestObject(simulator, localID);
        //    Thread.Sleep(500);
        //    Primitive prim = GetPrimitive(localID, simulator);
        //    return prim;
        //}
        public static void EnsureRequested(uint LocalID, Simulator simulator)
        {
            if (LocalID == 0) return;
            if (simulator == null)
            {
                foreach (var sim in AllSimulators)
                {
                    EnsureRequested(LocalID, sim);
                }
                return;
            }
            if (NeverRequest(LocalID, simulator))
                ReallyEnsureRequested(simulator, LocalID);
        }

        public static bool NeverRequest(uint LocalID, Simulator simulator)
        {
            ulong Handle = simulator.Handle;
            if (LocalID != 0)
                lock (primsRequested)
                {
                    if (!primsRequested.ContainsKey(Handle))
                    {
                        primsRequested[Handle] = new HashSet<uint>();
                    }
                    lock (primsRequested[Handle])
                    {
                        if (!primsRequested[Handle].Contains(LocalID))
                        {
                            primsRequested[Handle].Add(LocalID);
                            return true;
                        } else
                        {
                            primsRequested[Handle].Remove(LocalID);
                            return false;
                        }
                    }
                }
            return false;
        }

        private static void ReallyEnsureRequested(Simulator simulator, uint LocalID)
        {
            if (LocalID == 0) return;
            ulong Handle = simulator.Handle;
            if (Handle == 0)
            {
                return;
                throw new AbandonedMutexException();
            }
            List<uint> col = null;
            lock (primsRequestedOutbox)
            {
                if (!primsRequestedOutbox.TryGetValue(Handle, out col))
                {
                    col = primsRequestedOutbox[Handle] = new List<uint>();
                }
            }
            lock (col)
            {
                if (!col.Contains(LocalID))
                    col.Add(LocalID);
            }
        }

        private static void ReallyEnsureRequested_Thread(object sender)
        {
            if (inRTimer)
            {
                return;
            }
            lock (RequestObjectsTimerLock)
            {
                if (inRTimer)
                {
                    Logger.DebugLog("ReallyEnsureRequested_Thread getting behind");
                    return;
                }
                inRTimer = true;
            }
            List<ulong> newList;
            lock (primsRequestedOutbox)
            {
                newList = new List<ulong>(primsRequestedOutbox.Keys);
            }
            {            
                foreach (ulong simulator in newList)
                {
                    List<uint> askFor;
                    Simulator S = SimRegion.GetRegion(simulator).TheSimulator;
                    if (S == null)
                    {
                        Debug("No sim yet for " + simulator);
                        continue;
                    }
                    const int idsToSend = 100;
                    lock (primsRequestedOutbox[simulator])
                    {
                        List<uint> uints = primsRequestedOutbox[simulator];
                        if (uints.Count > idsToSend)
                        {
                            askFor = uints.GetRange(0, idsToSend);
                            uints.RemoveRange(0, idsToSend);
                        }
                        else if (uints.Count > 0)
                        {
                            primsRequestedOutbox[simulator] = new List<uint>();
                            askFor = uints;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    S.Client.Objects.RequestObjects(S, new List<uint>(askFor));
                }
            }
            //lock (RequestObjectsTimerLock)
            inRTimer = false;
        }

        public static void EnsureSelected(Primitive prim, Simulator simulator)
        {
            if (prim.Properties == null) EnsureSelected(prim.LocalID, simulator);
        }
        public static void EnsureSelected(uint LocalID, Simulator simulator)
        {
            if (LocalID == 0) return;
            if (simulator == null)
            {
                foreach (var sim in AllSimulators)
                {
                    EnsureSelected(LocalID, sim);
                }
                return;
            }
            if (NeverSelect(LocalID, simulator))
                ReallyEnsureSelected(simulator, LocalID);
        }

        public static bool NeverSelect(uint LocalID, Simulator simulator)
        {
            ulong Handle = simulator.Handle;
            if (LocalID != 0)
                lock (primsSelected)
                {
                    if (!primsSelected.ContainsKey(Handle))
                    {
                        primsSelected[Handle] = new HashSet<uint>();
                    }
                    lock (primsSelected[Handle])
                    {
                        if (!primsSelected[Handle].Contains(LocalID))
                        {
                            primsSelected[Handle].Add(LocalID);
                            return true;
                        }
                        else
                        {
                            primsSelected[Handle].Remove(LocalID);
                            return false;
                        }
                    }
                }
            return false;
        }

        public static void ReallyEnsureSelected(Simulator simulator, uint LocalID)
        {
            if (LocalID == 0) return;
            ulong Handle = simulator.Handle;
            if (Handle == 0)
            {
                return;
                throw new AbandonedMutexException();
            }
            List<uint> col = null;
            lock (primsSelectedOutbox)
            {
                if (!primsSelectedOutbox.TryGetValue(Handle, out col))
                {
                    col = primsSelectedOutbox[Handle] = new List<uint>();
                }
            }
            lock (col)
            {
                if (!col.Contains(LocalID))
                    col.Add(LocalID);
            }
        }

        private static void ReallyEnsureSelected_Thread(object sender)
        {
            if (inSTimer)
            {
                return;
            }
            lock (SelectObjectsTimerLock)
            {
                if (inSTimer)
                {
                    Logger.DebugLog("ReallyEnsureSelected_Thread getting behind");
                    return;
                }
                inSTimer = true;
            }
            lock (primsSelectedOutbox)
            {
                foreach (ulong simulator in new List<ulong>(primsSelectedOutbox.Keys))
                {
                    uint[] askFor;
                    Simulator S = SimRegion.GetRegion(simulator).TheSimulator;
                    if (S==null)
                    {
                        Debug("No sim yet for " + simulator);
                        continue;
                    }
                    const int idsToSend = 195;
                    lock (primsSelectedOutbox[simulator])
                    {
                        List<uint> uints = primsSelectedOutbox[simulator];
                        if (uints.Count > idsToSend)
                        {
                            askFor = uints.GetRange(0, idsToSend).ToArray();
                            uints.RemoveRange(0, idsToSend);
                        }
                        else if (uints.Count > 0)
                        {
                            primsSelectedOutbox[simulator] = new List<uint>();
                            askFor = uints.ToArray();
                        }
                        else
                        {
                            continue;
                        }
                    }
                    S.Client.Objects.SelectObjects(S, askFor, true);
                }
            }
            //lock (SelectObjectsTimerLock)
            inSTimer = false;
        }


        public UUID GetAssetUUID(string a, AssetType type)
        {
            return SimAssetSystem.GetAssetUUID(a, type);
        }

        public SimObject GetSimObject(Primitive prim)
        {
            Simulator sim = GetSimulator(prim);
            if (sim==null)
            {
                
            }
            return GetSimObject(prim, sim);
        }

        private static void DoEvents()
        {
            //todo  throw new Exception("The method or operation is not implemented.");
        }


        internal static void ResetSelectedObjects()
        {
            lock (primsSelected)
                foreach (HashSet<uint> UInts in primsSelected.Values)
                {
                    lock (UInts) UInts.Clear();
                }
        }

        internal void ReSelectObject(Primitive P)
        {
            if (P == null)
            {
                DLRConsole.DebugWriteLine("NULL RESELECTOBJECT");
                return;
            }
            Simulator sim = GetSimulator(P);
            if (P.ParentID != 0)
            {
                client.Objects.SelectObjects(sim, new uint[] { P.LocalID, P.ParentID }, true);
                return;
            }
			client.Objects.SelectObject(sim, P.LocalID, true);
        }

        internal SimAvatarImpl CreateSimAvatar(UUID uuid, WorldObjects objects, Simulator simulator)
        {
            if (uuid == UUID.Zero)
            {
                throw new NullReferenceException("UUID.Zero!");
            }
            // Request all of the packets that make up an avatar profile
            // lock (GetSimObjectLock)
            if (client.Self.AgentID == uuid)
            {
                return TheSimAvatar;
            }
            SimAvatarImpl obj0 = GetSimObjectFromUUID(uuid) as SimAvatarImpl;
            if (obj0 != null) return (SimAvatarImpl)obj0;
            object getSimLock = GetSimLock(simulator ?? client.Network.CurrentSim);
            lock (getSimLock)
            {
                lock (UUIDTypeObjectLock)
                    //lock (SimObjects)
                    //  lock (SimAvatars)
                {
                    SimObject simObj = GetSimObjectFromUUID(uuid);
                    obj0 = simObj as SimAvatarImpl;
                    if (obj0 != null) return (SimAvatarImpl) obj0;
                    if (simObj != null)
                    {
                        Debug("SimObj->SimAvatar!?! " + simObj);
                    }
                    obj0 = new SimAvatarClient(uuid, objects, simulator);
                    AddAvatar(obj0,uuid);
                    obj0.PollForPrim(this, simulator);
                    return (SimAvatarImpl)obj0;
                }
            }
        }

        internal void AddAvatar(SimAvatar neu, UUID uuid)
        {
            var from = WorldObjects.SimAccounts;
            SimAvatar old = null;
            bool sameAvatar = false;
            bool downGrade = false;
            bool upGrade = false;
            foreach (SimAvatar avatar in from)
            {
                if (avatar.ID == uuid)
                {
                    old = avatar;
                    if (!ReferenceEquals(avatar, neu))
                    {
                        if (old is SimAvatarClient && neu is SimAvatarClient)
                        {
                            throw new NotSupportedException("two Clients!");
                        }
                        if (old is SimAvatarClient && neu is SimAvatar)
                        {
                            downGrade = true;
                        }
                        if (old is SimAvatar && neu is SimAvatarClient)
                        {
                            upGrade = true;
                        }
                    } else
                    {
                        sameAvatar = true;
                    }
                }
            }
            if (downGrade)
            {
                throw new NotSupportedException("downGrade!");
            }
            if (upGrade)
            {
                if (!ReferenceEquals(old.theAvatar,neu.theAvatar))
                {
                    throw new NotSupportedException("theAvatar for upgrade is schizoid!");   
                }
                SimObjects.Remove(old);
                SimAccounts.Remove(old);
                SimAvatars.Remove(old);
            }
            if (!sameAvatar)
            {
                SimObjects.AddTo(neu);
                SimAccounts.Add((SimAvatar)neu);
                if (neu.HasPrim) SimAvatars.Add((SimAvatar)neu);
                RegisterUUID(uuid, neu);
                RequestAvatarMetadata(uuid);
            }
            //client.Avatars.RequestAvatarPicks(uuid);
        }

        internal SimObject CreateSimObject(UUID uuid, WorldObjects WO, Simulator simulator)
        {
            if (uuid == UUID.Zero)
            {
                throw new NullReferenceException("UUID.Zero!");
            }
            //  lock (GetSimObjectLock)
            SimObject obj0 = GetSimObjectFromUUID(uuid);
            if (obj0 != null) return obj0;
            simulator = simulator ?? client.Network.CurrentSim;
            lock (GetSimLock(simulator ?? client.Network.CurrentSim))
                //lock (UUIDTypeObjectLock)
                   // lock (SimObjects)
                     //   lock (SimAvatars)
                        {
                            obj0 = GetSimObjectFromUUID(uuid);
                            if (obj0 != null) return obj0;
                            obj0 = new SimObjectImpl(uuid, WO, simulator);
                            SimObjects.AddTo(obj0);
                            RegisterUUID(uuid, obj0);
                            return obj0;
                        }
        }

        public static bool UUIDTypeObjectTryGetValue(UUID uuid, out object obj)
        {
#if COGBOT_LIBOMV
            obj = uuid.ExternalData;
            if (obj != null) return true;
#endif
            lock (UUIDTypeObjectRealLock) return UUIDTypeObjectReal.TryGetValue(uuid, out obj);
        }
        public static bool UUIDTypeObjectContainsKey(UUID uuid)
        {
#if COGBOT_LIBOMV
            var obj = uuid.ExternalData;
            if (obj != null) return true;
#endif
            //return uuid.ExternalData;
            //if (!b) return uuidTypeObject.TryGetValue(uuid, out o);
            //lock (WorldObjects.uuidTypeObject)
            lock (UUIDTypeObjectRealLock) return UUIDTypeObjectReal.ContainsKey(uuid);
        }
        public static object UUIDTypeObjectSetValue(UUID uuid, object value)
        {
            //if (!b) return uuidTypeObject.TryGetValue(uuid, out o);
            //lock (WorldObjects.uuidTypeObject)
            lock (UUIDTypeObjectRealLock) UUIDTypeObjectReal[uuid] = value;
#if COGBOT_LIBOMV
            return uuid.ExternalData = value;
#endif
            return value;
        }
    }
}