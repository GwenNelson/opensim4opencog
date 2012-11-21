using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using AIMLbot;
using AltAIMLbot;
using AltAIMLParser;
using MushDLR223.ScriptEngines;
using MushDLR223.Utilities;
using MushDLR223.Virtualization;
using RTParser;
using RTParser.AIMLTagHandlers;
using RTParser.Database;
using RTParser.Utils;
using RTParser.Variables;
using MasterRequest = AltAIMLParser.Request;
using DataUnifiable = System.String;
using DataUnifiableYYY = RTParser.Unifiable;

namespace AltAIMLbot
{
    public interface User : IUser, UserStaticModel, RTParser.UserConversationScope, RTParser.UserDuringProcessing
    {
        void DisposeObject();
        bool IsValid { get; set; }
        GraphMaster StartGraph { get; set; }
        object TemplatesLock { get; }
        GraphMaster HeardYouSayGraph { get; set; }
        Request LastRequest { get; set; }
        AltBot rbot { get; }
        string blackBoardThat { get; set; }
        Dictionary<string, double> Qstate { get; }

        void RaiseEvent(string p, AltBot robot);

        MasterRequest CreateRequest(Unifiable message, User targetUser);
        IEnumerable<string> getThats();
        IEnumerable<string> getTopics();
        IEnumerable<string> getPreStates();
        IEnumerable<string> getPostStates();
        void setUserID(string id);
        bool SetMeMyselfAndI(string fullname);
    }
}

namespace RTParser
{
    public interface IUser
    {
        UserStaticModel StaticModel { get; }
        UserConversationScope ConversationScope { get; }
        UserConversationScope DuringProcessing { get; }
        User Value { get; }
    }

    public interface UserStaticModel : IUser
    {
        AltBot bot { get; }
        string UserID { get; set; }
        string UserName { get; set; }

        GraphMaster GetResponseGraph(User target);
        void StampResponseGiven();
        bool CanGiveResponseNow();
        int MaxInputs { get; set; }
        bool IsRoleAcct { get; set; }
        OutputDelegate userTrace { set; }
        DateTime NameUsedOrGivenTime { get; set; }
        PrintOptions WriterOptions { get; }
        bool RespondToChat { get; set; }
        int MaxRespondToChatPerMinute { get; set; }

        /// <summary>
        /// If user says something to robot we.. do a user.CreateRequest("Hi robot",BotAsUser);
        /// </summary>
        /// <param name="input"></param>
        /// <param name="targetUser"></param>
        /// <returns></returns>
        MasterRequest CreateRequest(Unifiable input, Unifiable said, User targetUser);
        MasterRequest CreateRequest(Unifiable message, User target, Unifiable targetsaid, GraphMaster G, Request parentRequest);

        TaskQueueHandler GetTaskQueueHandler(string find);
        void AddTodoItem(CrossAppDomainDelegate todo);

        string UserDirectory { get; }
        void LoadDirectory(string userdir);
        void SaveDirectory(string userDirectory);
        void SyncDirectory(string userdir);
        void SaveOften();
        bool DoUserCommand(string args, OutputDelegate console);

        void WriteToUserTrace(string s, params object[] args);

        QuerySettings GetQuerySettings();
        QuerySettings GetQuerySettingsSRAI();
        ConversationLog GetConversationLog(string userName, bool createIfMissing);
        bool IsNamed(string lname);
    }

    /// <summary>
    /// Encapsulates information and history of a user who has interacted with the bot
    /// </summary>
    public class MasterUser : StaticAIMLUtils, IUser, IDisposable, ISettingsDictionary, UserConversationScope,
                                     UserDuringProcessing,
                                     User
    {
        /// <summary>
        /// List of possible non-determinstic "states". When present will select the one with 
        /// the highest score
        /// </summary>
        public Dictionary<string, double> Qstate
        {
            get { return _Qstate; }
        }
        public Dictionary<string, double> _Qstate = new Dictionary<string, double>();

        /// <summary>
        /// the value of the "that" on the blackboard predicate
        /// </summary>
        public string blackBoardThat { get; set; }
        public void RaiseEvent(string name, AltBot robot)
        {
            try
            {
                var R = CreateRequest("ONUSER" + name + " " + UserID, ResponderJustSaid, rbot.BotAsUser);
                R.Graph = robot.DefaultEventGraph;
                R.AddGraph(robot.DefaultEventGraph);
                R.AddGraph(StartGraph);
                robot.ChatWithRequest(R);
            }
            catch (Exception e)
            {
                writeDebugLine("ONUSER" + name + " " + e);
            }
        }

        public Request LastRequest
        {
            get
            {
                if (_lastRequest == null) return CreateRequest("PING", ResponderJustSaid, rbot.BotAsUser);
                return _lastRequest;
            }
            set
            {
                _lastRequest = value;
            }
        }
        public static bool ThatIsStoredBetweenUsers = true;
        public readonly object QueryLock = new object();
        public bool IsValid { get; set; }
        #region Attributes

        public readonly Dictionary<string, TaskQueueHandler> TaskQueueHandlers =
            new Dictionary<string, TaskQueueHandler>();

        public TaskQueueHandler GetTaskQueueHandler(string find)
        {
            find = (find ?? "").Trim().ToLower();
            if (find == "") find = "conversation";
            if (find == "conversation") find = UserID;

            lock (TaskQueueHandlers)
            {
                TaskQueueHandler tqh;
                if (!TaskQueueHandlers.TryGetValue(find, out tqh))
                {
                    tqh = new TaskQueueHandler(bot.ObjectRequester, UserName + " tq " + find);
                    TaskQueueHandlers[find] = tqh;
                    tqh.Start();
                    return tqh;
                }
                return tqh;
            }
        }

        public ConversationLog GetConversationLog(string find, bool createIfMissing)
        {
            find = (find ?? "").Trim().ToLower();
            if (find == "") find = UserName;
            User userFound = rbot.FindOrCreateUser(find);
            ConversationLog log = ConversationLog.GetConversationLog(this, userFound, createIfMissing);
            return log;
        }

        public ListAsSet<TemplateInfo> VisitedTemplates { get; set; }
        public ListAsSet<TemplateInfo> ProofTemplates { get; set; }
        public ListAsSet<TemplateInfo> UsedChildTemplates { get; set; }
        public ListAsSet<TemplateInfo> DisabledTemplates { get; set; }
        public ICollection<GraphMaster> DisallowedGraphs { get; set; }
        public ListAsSet<GraphQuery> AllQueries { get; set; }

        public DateTime LastResponseGivenTime { get; set; }
        public bool RespondToChat { get; set; }
        public int MaxRespondToChatPerMinute { get; set; }
        public DateTime NameUsedOrGivenTime { get; set; }

        public static int DefaultMaxResultsSaved = 15;
        public static bool NeverSaveUsers;
        public int MaxResultsSaved = DefaultMaxResultsSaved;
        public bool IsRoleAcct { get; set; }

        private object ChatWithThisUser(string cmd, Request request)
        {
            Request req = request.CreateSubRequest(cmd, null);
            req.Responder = this;
            req.IsToplevelRequest = request.IsToplevelRequest;
            return rbot.LightWeigthBotDirective(cmd, req);
        }

        /// <summary>
        /// The local instance of the GUID that identifies this user to the bot
        /// </summary>
        private Unifiable id;

        /// <summary>
        /// The bot this user is using
        /// </summary>
        public AltBot bot { get; set; }
        public AltBot rbot
        {
            get { return bot; }
        }
        public TaskQueueHandler OnTaskAtATimeHandler
        {
            get { return GetTaskQueueHandler("HeardSelfSsy"); }
        }

        public static Thread SaveTimer;

        public void Enqueue(string queuename, TaskType taskType, string taskName, ThreadStart evt)
        {
            TaskQueueHandler queueHandler = GetTaskQueueHandler(queuename);
            queueHandler.CreateTask(taskType, taskName, evt, true);
            // why was this line was here!? (the above should have taken care of it) OnTaskAtATimeHandler.Enqueue(evt);
        }

        private readonly object SaveLock = new object();
        public static void SaveAllOften(AltBot robot)
        {
            foreach (User node in robot.SetOfUsers)
            {
                node.SaveOften();
            }
        }
        public void SaveOften()
        {
            if (IsRoleAcct) return;
            if (NeverSave) return;
            lock (SaveLock)
            {
                if (!needsSave)
                {
                    WriteToUserTrace("Skipping save");
                    return;
                }
                needsSave = false;
            }
            try
            {
                if (IsRoleAcct) return;
                SaveDirectory(_saveToDirectory);
            }
            catch (Exception exception)
            {
                WriteToUserTrace("ERROR saving " + exception);
            }
            lock (SaveLock)
            {
                needsSave = true;
            }
        }


        public GraphMaster HeardYouSayGraph
        {
            get { return FindGraphLocally("heardyousaygraph") ?? rbot.DefaultStartGraph; }
            set
            {
                if (!Predicates.containsLocalCalled("heardyousaygraph"))
                    Predicates.addSetting("heardyousaygraph", value.ScriptingName);
                else Predicates.updateSetting("heardyousaygraph", value.ScriptingName);
                GraphMaster lg = HeardYouSayGraph;
                if (lg != value)
                {
                    bot.writeToLog("ERROR CANT FIND " + value.ScriptingName + " from " + lg);
                }
            }
        }

        private GraphMaster FindGraphLocally(string varname)
        {
            //Predicates.IsTraced = false;
            var v = Predicates.grabSetting(varname);
            if (Unifiable.IsMissing(v)) return null;
            if (Unifiable.IsNullOrEmpty(v)) return null;
            GraphMaster _Graph = rbot.GetGraph(v, null);
            if (_Graph != null)
            {
                return _Graph;
            }
            bot.writeToLog("ERROR CANT FIND " + varname);
            return _Graph;
        }

        public GraphMaster HeardSelfSayGraph
        {
            get { return FindGraphLocally("heardselfsay") ?? rbot.DefaultHeardSelfSayGraph; }
            set
            {
                if (!Predicates.containsLocalCalled("heardselfsay"))
                    Predicates.addSetting("heardselfsay", value.ScriptingName);
                else Predicates.updateSetting("heardselfsay", value.ScriptingName);
                GraphMaster lg = HeardSelfSayGraph;
                if (lg != value)
                {
                    bot.writeToLog("ERROR CANT FIND " + value.ScriptingName + " from " + lg);
                }
            }
        }

        /// <summary>
        /// The grahmaster this user is using
        /// // this stil is not "listener"
        /// </summary>
        public GraphMaster StartGraph
        {
            get
            {
                GraphMaster v = FindGraphLocally("startgraph") ?? rbot.DefaultStartGraph;
                if (v.Size == 0) v = rbot.DefaultStartGraph;
                return v;
            }
            set
            {
                if (!Predicates.containsLocalCalled("startgraph"))
                    Predicates.addSetting("startgraph", value.ScriptingName);
                else Predicates.updateSetting("startgraph", value.ScriptingName);
                GraphMaster lg = StartGraph;
                if (lg != value)
                {
                    bot.writeToLog("ERROR CANT FIND " + value.ScriptingName + " from " + lg);
                }
            }
        }

        public object TemplatesLock
        {
            get { return VisitedTemplates; }
        }

        public string StartGraphName
        {
            get
            {
                return StartGraph.ScriptingName;
            }
            set
            {
                StartGraph = rbot.GetGraph(value, StartGraph);
            }
        }

        public int MaxInputs
        {
            get
            {
                if (Predicates.containsSettingCalled("maxinputs"))
                {
                    var mi = Predicates.grabSetting("maxinputs");
                    int miv;
                    if (int.TryParse(mi.ToString(), out miv))
                    {
                        return miv;
                    }
                }
                return 1;
            }
            set { Predicates.addSetting("maxinputs", "" + value); }
        }


        /// <summary>
        /// The GUID that identifies this user to the bot
        /// </summary>
        public string UserID
        {
            get
            {
                string uid = this.id;
                if (string.IsNullOrEmpty(uid))
                {
                    uid = Predicates.grabSetting("id");
                }
                return uid;
            }
            set
            {
                if (value == null) return;
                string puser = UserID;
                if (puser != null && puser.ToLower() == value.ToLower())
                {
                    return;
                }
                if (!LegalNameCheck(value)) return;
                this.id = value;
                if (Predicates == null) return;
                Predicates.addSetting("id", value);
            }
        }

        private bool LegalNameCheck(string value)
        {
            if (value.ToLower() == "friend")
            {
                WriteToUserTrace("Friend: " + value);
                return false;
            }
            return true;
        }

        public string UserName
        {
            get { return GetValueORElse(this.Predicates, "name", () => UserID.Replace("_", " ")); }

            set
            {
                if (value == null) return;
                string puser = UserName;
                if (puser != null && puser.ToLower() == value.ToLower())
                {
                    return;
                }
                if (!LegalNameCheck(value))
                {
                    return;
                }
                if (false && rbot.IsLastKnownUser(value))
                {
                    return;
                }
                SetMeMyselfAndI(value);
            }
        }

        public bool SetMeMyselfAndI(string value)
        {
            string saved = value.Replace("_", " ").Trim(" ,".ToCharArray());
            if (saved.Length == 0) return false;
            if (Predicates == null) return false;
            //var prev = Predicates.IsIdentityReadOnly;
            try
            {
                string[] split = value.Split(new[] { " ", "-", "_" }, StringSplitOptions.RemoveEmptyEntries);
                //Predicates.IsIdentityReadOnly = false;
                Predicates["id"] = UserID;
                if (IsIncomplete(value))
                {
                    throw new NullReferenceException("SetMeMyselfAndI: " + value);
                    return false;
                }
                Predicates["name"] = value;
                Predicates["me"] = value;
                Predicates["myself"] = value;
                Predicates["i"] = value;
                Predicates["my"] = NatLangDb.MakePossesive(value);
                if (IsIncomplete(split[0]))
                {
                    throw new NullReferenceException("SetMeMyselfAndI: " + split[0]);
                    return false;
                }
                Predicates["firstname"] = split[0];
                if (split.Length > 1)
                {
                    Predicates["fullname"] = value;
                    if (split.Length == 2) Predicates["lastname"] = split[1];
                    if (split.Length == 3)
                    {
                        Predicates["middlename"] = split[1];
                        Predicates["lastname"] = split[2];
                    }
                }
                return true;
            }
            finally
            {
              //  Predicates.IsIdentityReadOnly = prev;
            }
        }

        private string GetValueORElse(ISettingsDictionary dictionary, string settingname, Func<String> func)
        {
            if (dictionary != null && dictionary.containsLocalCalled(settingname))
            {
                string value = dictionary.grabSetting(settingname);
                if (!IsNullOrEmpty(value)) return value.Trim();
            }
            return func();
        }

        /// <summary>
        /// A collection of all the result objects returned to the user in this session
        /// (in reverse order of time)
        /// </summary>
        private readonly List<Result> Results = new List<Result>();

        /// <summary>
        /// A collection of all the Interaction objects returned to the user in this session
        /// (in reverse order of time)
        /// </summary>
        private readonly List<InteractionResult> Interactions = new List<InteractionResult>();


        private readonly List<Unifiable> _topics = new List<Unifiable>();

        public IList<Unifiable> Topics
        {
            get
            {
                if (_topics.Count == 0) return new List<Unifiable> { Topic };
                return _topics;
            }
        }

        /// <summary>
        /// the value of the "topic" predicate
        /// </summary>
        public Unifiable Topic
        {
            get { return TopicSetting; }
        }

        public Unifiable TopicSetting
        {
            get
            {
                if (!this.Predicates.containsSettingCalled("topic"))
                {
                    return rbot.NOTOPIC;
                }
                var t = this.Predicates.grabSetting("topic");
                if (IsNullOrEmpty(t))
                {
                    return rbot.NOTOPIC;
                }
                return t;
            }
            set { Predicates.addSetting("topic", value); }
        }

        /// <summary>
        /// the predicates associated with this particular user
        /// </summary>
        public SettingsDictionary Predicates { get; private set; }

        //public SettingsDictionary Predicates0;

        /// <summary>
        /// The most recent result to be returned by the bot
        /// </summary>
        public Result LastResult
        {
            get { return GetResult(0, true); }
        }

        public PrintOptions PrintOptions { get; set; }

        public IEnumerable<Unifiable> BotOutputs
        {
            get
            {
                var raws = new List<Unifiable>();
                int added = 0;
                string lastOutput = "";
                if (this.SailentResultCount > 0)
                {
                    lock (Results)
                        foreach (var result in Results)
                        {
                            if (result.Responder == this) continue;
                            string thisOutput = result.RawOutput;
                            if (thisOutput == "*") continue;
                            if (thisOutput == lastOutput) continue;
                            lastOutput = thisOutput;
                            raws.Add(result.RawOutput);
                            added++;
                            if (added > 2) break;
                        }
                }
                if (raws.Count == 0) raws.Add("HELLO"); //since nothing is known yet!
                if (raws.Count == 0) raws.Add(Unifiable.STAR);
                return raws;
            }
        }

        public void ShowConversationLog(OutputDelegate console, int maxHistory)
        {
            console("--------------------------------------------------------------------");
            for (int i = 0; i < maxHistory; i++)
            {
                ShowConversationLogResult(i, console);
            }
            console("--------------------------------------------------------------------");
        }
        public void ShowConversationLogResult(int stepsBack, OutputDelegate console)
        {
            int count = Results.Count - stepsBack - 1;
            if (count < 0) return;
            Result result = Results[count];
            console("Requester:  " + result.Requester.UserName + ", \"" + result.ChatInput.RawText + "\"");
            console("Responder:  " + result.Responder.UserName + ", \"" + result.ChatOutput.RawText + "\"");
        }

        #endregion

        #region Methods

        public void InsertProvider(ParentProvider pp)
        {
            Predicates.InsertProvider(pp);
        }

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="UserID">The GUID of the user</param>
        /// <param name="bot">the bot the user is connected to</param>
        internal MasterUser(string userID,string fullname, AltBot bot, SettingsDictionary dict)
        {
            this.bot = bot;
            IsValid = true;
            userTrace = WriteToUserTrace;
            MaxRespondToChatPerMinute = 10;
            RespondToChat = true;
            LastResponseGivenTime = DateTime.Now;
            NameUsedOrGivenTime = DateTime.Now;
            VisitedTemplates = new ListAsSet<TemplateInfo>();
            UsedChildTemplates = new ListAsSet<TemplateInfo>();
            ProofTemplates = new ListAsSet<TemplateInfo>();
            DisabledTemplates = new ListAsSet<TemplateInfo>();
            DisallowedGraphs = new HashSet<GraphMaster>();
            qsbase = new QuerySettingsImpl(rbot.GetQuerySettings());
            PrintOptions = new PrintOptions("PO_" + userID);
            if (userID.Length > 0)
            {
                bool isUser = dict == null;
                WriterOptions = new PrintOptions("PW_" + userID);
                this.id = userID;
                qsbase.IsTraced = IsTraced = rbot.IsTraced;
                // we dont inherit the BotAsUser we inherit the bot's setings
                // ApplySettings(bot.BotAsUser, this);
                string parserToCamelCase = AltBot.ToMtCase(fullname);
                string predMtName = parserToCamelCase + "Predicates";
                this.Predicates = dict ?? bot.MakeSettingsDictionary(predMtName);
                this.Predicates.IsTraced = qsbase.IsTraced;
                //this.Predicates.AddPrefix("user.", () => this);      
                if (isUser)
                {
                    this.bot.DefaultPredicates.Clone(this.Predicates);

                    //this.Predicates.AddGetSetProperty("topic", new CollectionProperty(_topics, () => bot.NOTOPIC));
                    this.Predicates.addSetting("topic", rbot.NOTOPIC);
                    this.Predicates.InsertFallback(() => bot.AllUserPreds);
                }
                UserID = userID;                
                UserName = fullname;
                blackBoardThat = "";
                WithoutTrace(Predicates,() => SetMeMyselfAndI(fullname));
                //this.Predicates.addSetting("topic", "NOTOPIC");
                if (false && SaveTimer == null)
                {
                    SaveTimer = new Thread(() =>
                    {
                        Thread.Sleep(new TimeSpan(0, 5, 0));
                        SaveAllOften(bot);
                    })
                    {
                        Name = "SaveUsersTimer"
                    };
                    SaveTimer.Start();
                }
                needsSave = true;
                StampResponseGiven();
                rbot.AddExcuteHandler(userID, ChatWithThisUser);
                AllQueries = new ListAsSet<GraphQuery>();
            }
            else
            {
                throw new Exception("The UserID cannot be empty");
            }
        }

        /// <summary>
        /// If the user is being traced/debugged
        /// </summary>
        public bool IsTraced
        {
            get { return qsbase.IsTraced; }
            set { qsbase.IsTraced = value; }
        }

        private readonly QuerySettings qsbase;

        public QuerySettings GetQuerySettings()
        {
            //if (CurrentRequest != null)
            //{
            //   return CurrentRequest;
            //}
            return qsbase;
        }

        public QuerySettings GetQuerySettingsSRAI()
        {
            return QuerySettings.SRAIDefaults;
        }

        public override string ToString()
        {
            return UserID;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            if (SaveTimer != null)
            {
                try
                {
                    SaveTimer.Abort();
                }
                catch (Exception)
                {
                }
                SaveTimer = null;
            }
            lock (ShutdownHooks)
            {
                foreach (var threadStart in ShutdownHooks)
                {
                    try
                    {
                        threadStart.Invoke();
                    }
                    catch (Exception exception)
                    {
                        writeDebugLine("ERROR: shutdownhook " + threadStart + " " + exception);
                    }
                    ShutdownHooks.Clear();
                }
            }
            SaveOften();
        }

        /// <summary>
        /// Returns the Unifiable to use for the next that part of a subsequent path
        /// </summary>
        /// <returns>the Unifiable to use for that</returns>
        public Unifiable LastSaidByReponder(UserConversationScope responder)
        {
            if (this.SailentResultCount > 0)
            {
                Result v = GetResult(0, true, responder);
                return v.RawOutput;
            }
            else
            {
                return Unifiable.STAR;
            }
        }

        /// <summary>
        /// Returns the first sentence of the last output from the bot
        /// </summary>
        /// <returns>the first sentence of the last output from the bot</returns>
        public Unifiable getThat(User responder)
        {
            return this.getThat(0, 0, responder);
        }

        /// <summary>
        /// Returns the first sentence of the output "n" steps ago from the bot
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <returns>the first sentence of the output "n" steps ago from the bot</returns>
        public Unifiable getThat(int n, User responder)
        {
            return this.getThat(n, 0, responder);
        }

        /// <summary>
        /// Returns the sentence numbered by "sentence" of the output "n" steps ago from the bot
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <param name="sentence">the sentence number to get</param>
        /// <returns>the sentence numbered by "sentence" of the output "n" steps ago from the bot</returns>
        public Unifiable getThat(int n, int sentence, User responder)
        {
            if ((n >= 0) & (n < this.SailentResultCount))
            {
                Result historicResult = GetResult(n, true, responder);
                if (historicResult == null) return "";
                if ((sentence >= 0) & (sentence < historicResult.OutputSentenceCount))
                {
                    return historicResult.GetOutputSentence(sentence);
                }
            }
            return Unifiable.Empty;
        }

        /// <summary>
        /// Returns the first sentence of the last output from the bot
        /// </summary>
        /// <returns>the first sentence of the last output from the bot</returns>
        public Unifiable getInputSentence(User responder)
        {
            return this.getInputSentence(0, 0, responder);
        }

        /// <summary>
        /// Returns the first sentence from the output from the bot "n" steps ago
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <returns>the first sentence from the output from the bot "n" steps ago</returns>
        public Unifiable getInputSentence(int n, User responder)
        {
            return this.getInputSentence(n, 0, responder);
        }


        /// <summary>
        /// Returns the identified sentence number from the input from the bot "n" steps ago
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <param name="sentence">the sentence number to return</param>
        /// <returns>the identified sentence number from the input from the bot "n" steps ago</returns>
        public Unifiable getInputSentence(int n, int sentence, User responder)
        {
            if (n == 0)
            {
                return CurrentRequest.ChatInput.GetSentence(sentence, true);
            }
            n = n - 1;
            if ((n >= 0) & (n < this.SailentResultCount))
            {
                Result historicInput = GetResult(n, false, responder);
                if (historicInput != null && (sentence >= 0) & (sentence < historicInput.InputSentences.Count))
                {
                    return historicInput.InputSentences[sentence];
                }
            }
            return Unifiable.Empty;
        }

        /// <summary>
        /// Returns the identified sentence number from the input from the bot "n" steps ago
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <param name="sentence">the sentence number to return</param>
        /// <returns>the identified sentence number from the input from the bot "n" steps ago</returns>
        public Unifiable getRequestSentence(int n, int sentence, User responder)
        {
            if (n == 0)
            {
                return CurrentRequest.ChatInput.GetSentence(sentence, true);
            }
            n = n - 1;
            if ((n >= 0) & (n < this.SailentResultCount))
            {
                Result historicInput = GetResult(n, false, responder);
                if ((sentence >= 0) & (sentence < historicInput.InputSentences.Count))
                {
                    return historicInput.InputSentences[sentence];
                }
            }
            return Unifiable.Empty;
        }

        /// <summary>
        /// Returns the first sentence of the last output from the bot
        /// </summary>
        /// <returns>the first sentence of the last output from the bot</returns>
        public Unifiable getResponseSentence(User responder)
        {
            return this.getResponseSentence(0, 0, responder);
        }

        /// <summary>
        /// Returns the first sentence from the output from the bot "n" steps ago
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <returns>the first sentence from the output from the bot "n" steps ago</returns>
        public Unifiable getResponseSentence(int n, User responder)
        {
            return this.getResponseSentence(n, 0, responder);
        }

        /// <summary>
        /// Returns the identified sentence number from the output from the bot "n" steps ago
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <param name="sentence">the sentence number to return</param>
        /// <returns>the identified sentence number from the output from the bot "n" steps ago</returns>
        public Unifiable getResponseSentence(int n, int sentence, User responder)
        {
            if ((n >= 0) & (n < this.SailentResultCount))
            {
                Result historicResult = GetResult(n, false, responder);
                if ((sentence >= 0) & (sentence < historicResult.InputSentences.Count))
                {
                    return historicResult.InputSentences[sentence];
                }
                return historicResult.ChatOutput.GetSentence(sentence, true);
            }
            return Unifiable.Empty;
        }

        public bool SuspendAddResultToUser { get; set; }

        public int SailentResultCount
        {
            get
            {
                lock (Results)
                {
                    return Results.Count;
                }
            }
        }

        /// <summary>
        /// Adds the latest result from the bot to the Results collection
        /// </summary>
        /// <param name="latestResult">the latest result from the bot</param>
        public void addResult(Result latestResult)
        {
            if (SuspendAddResultToUser)
            {
                Request r = latestResult.request;
                if (r != null)
                {
                    if (r.IsTraced)
                        AltBot.writeDebugLine("AIMLTRACE: SuspendAddResultToUser, " + latestResult);
                }
                return;
            }
            lock (Results)
            {
                if (Results.Contains(latestResult))
                {
                    //writeDebugLine("DEBUG9 Trying to resave results ! " + latestResult);
                    return;
                }
                this.Results.Insert(0, latestResult);
                latestResult.FreeRequest();
                int rc = this.SailentResultCount;
                if (rc > MaxResultsSaved)
                {
                    this.Results.RemoveRange(MaxResultsSaved, rc - MaxResultsSaved);
                }
            }
            addResultTemplates(latestResult);
        }

        #endregion

        public Unifiable That
        {
            get
            {
                Unifiable something;
                var lastResponder = this.LastResponder;
                if (lastResponder != null)
                {
                    var tt = lastResponder.JustSaid;
                    if (IsSomething(tt, out something)) return something;
                }
                if (Predicates != null)
                {
                    var tt = Predicates.grabSetting("that");
                    if (IsSomething(tt, out something)) return something;
                }
                Result r = GetResult(0, true) ?? GetResult(0, false, lastResponder);
                if (r != null && IsSomething(r.NormalizedOutput, out something))
                {

                    return something;
                }
                if (lastResponder != null && IsSomething(lastResponder.JustSaid, out something)) return something;
                if (ThatIsStoredBetweenUsers)
                {
                    return Unifiable.EnglishNothing;
                }
                var fr = CurrentRequest;
                while
                    (fr != null)
                {
                    var frithat = fr.ithat;
                    if (IsSomething(frithat, out something)) return something;
                    fr = fr.ParentRequest;
                }
                if (IsSomething(getLastBotOutputForThat(), out something)) return something;
                return Unifiable.EnglishNothing;
            }
            set
            {
                if (IsNullOrEmpty(value)) throw new NullReferenceException("set_That: " + this);
                if (!IsEnglish(value))
                {
                    return;
                }
                Result r = GetResult(0, true);
                if (r != null)
                {
                    r.SetOutput = (value);
                }
                Predicates["that"] = value;
                if (CurrentRequest != null)
                {
                    CurrentRequest.ithat = value;
                }
                var LastResponder = this.LastResponder;
                if (LastResponder != null)
                {
                    LastResponder.JustSaid = value;
                }
            }
        }

        private Unifiable getLastBotOutputForThat()
        {
            Unifiable something;
            Result r = GetResult(0, true) ?? GetResult(0, false, LastResponder);
            if (r != null && IsSomething(r.NormalizedOutput, out something)) return something;
            if (LastResponder != null && IsSomething(LastResponder.JustSaid, out something)) return something;
            return Unifiable.EnglishNothing;
        }

        public Unifiable _JustSaid;

        public Unifiable JustSaid
        {
            get
            {
                //if (true) return That;
                Unifiable something;
                if (IsSomething(_JustSaid, out something)) return something;
                if (LastResponder != null)
                {
                    var vv = Predicates.grabSetting("lastsaid,thatstar");
                    if (IsSomething(vv, out something)) return something;
                    var llr = LastResponder.LastResponder;
                    if (llr != null && llr != this)
                    {
                        vv = LastResponder.Value.Predicates.grabSetting("lastsaid");
                        if (IsSomething(vv, out something)) return something;
                    }
                    // infinate loop here -> return LastReponder.ResponderJustSaid;
                }
                return Unifiable.EnglishNothing;
            }
            set
            {
                if (IsNullOrEmpty(value))
                {
                    rbot.RaiseError(new InvalidOperationException("set_JustSaid: " + this));
                    return;
                }
                if (!IsValue(value))
                {
                    rbot.RaiseError(new InvalidOperationException("set_JustSaid: TAG: " + value + " for " + this));
                    return;
                }
                if (!IsEnglish(value))
                {
                    return;
                }

                // the (_JustSaid != value) holds back the infinate looping                
                if (_JustSaid == null || SymTrim(_JustSaid).ToUpper() != SymTrim(value).ToUpper())
                {
                    Unifiable something;
                    if (IsSomething(value, out something))
                    {
                        value = something;
                        _JustSaid = value;
                        Predicates.addSetting("lastsaid", value);
                        Predicates.addSetting("thatstar", value);
                        if (LastResponder != null)
                        {
                            LastResponder.ResponderJustSaid = value;
                        }
                    }
                }
            }
        }

        private bool IsEnglish(Unifiable value)
        {
            if (IsNullOrEmpty(value))
                return false;
            if (IsMissing(value))
                return false;
            if (IsIncomplete(value))
                return false;
            if (!IsValue(value))
                return false;
            Unifiable temp;
            if (!IsSomething(value, out temp)) return false;
            string svalue = value.AsString();
            if (svalue.Contains("="))
                return false;
            if (svalue.Contains("*"))
                return false;
            return true;
        }

        public Unifiable ResponderJustSaid
        {
            get
            {
                return That;
                {
                    var vv = Predicates.grabSetting("that");
                    if (Unifiable.IsMulti(vv))
                    {
                        AltBot.writeDebugLine("WARNING ONLY USING ONE Result: " + Unifiable.DescribeUnifiable(vv));
                        vv = ((Unifiable)vv).Possibles[0];
                    }
                    Unifiable output;
                    if (IsSomething(vv, out output))
                    {
                        return output;
                    }
                    if (LastResponder != null) return LastResponder.JustSaid;
                    return That;
                }
            }
            set
            {
                if (IsNullOrEmpty(value))
                {
                    rbot.RaiseError(new InvalidOperationException("set_ResponderJustSaid: " + this));
                    return;
                }
                if (!IsValue(value))
                {
                    rbot.RaiseError(
                        new InvalidOperationException("set_ResponderJustSaid: !IsValue: " + value + " for " + this));
                    return;
                }
                That = value;
                return;
                if (LastResponder != null)
                {
                    LastResponder.JustSaid = value;
                }
                Predicates["that"] = value;
                //ithat = value;
            }
        }

        public bool IsNamed(string lname)
        {
            return UserName == lname || UserID == lname;
        }

        private UserConversationScope LastReponderFromDictionary()
        {
            foreach (var name in NamesStrings("you,lastusername,lastuserid"))
            {
                string name1 = name;
                string lname =
                    WithoutTrace(Predicates,
                                 () =>
                                 {
                                     string realName;
                                     return SettingsDictionaryReal.grabSettingDefaultDict(Predicates, name1,
                                                                                      out realName);
                                 });
                if (!string.IsNullOrEmpty(lname))
                {
                    if (LastResult != null)
                    {
                        UserConversationScope user0 = LastResultOtherParticipant();
                        if (user0.IsNamed(lname))
                        {
                            return user0;
                        }
                    }
                    User user = rbot.FindUser0(lname);
                    if (user != null && user != this)
                    {
                        return user;
                    }
                }
            }
            return null;
        }
        private UserConversationScope _LastResponderCahced = null;
        public UserConversationScope LastResponder
        {
            get
            {
                if (_LastResponderCahced != null)
                {

                    if (_LastResponderCahced.Value.IsValid) return _LastResponderCahced;
                    _LastResponderCahced = null;
                }
                _LastResponderCahced = LastReponderFromDictionary();
                if (_LastResponderCahced != null && _LastResponderCahced.Value.IsValid) return _LastResponderCahced;
                return LastResultOtherParticipant();
            }
            set
            {
                if (value == null || value == this) return;
                _LastResponderCahced = value;
                UserStaticModel sm = value.StaticModel;
                Predicates["lastuserid"] = sm.UserID;
                string userName = sm.UserName;
                Predicates["lastusername"] = userName;
                Predicates["you"] = userName;
                Predicates["yourself"] = userName;
                Predicates["your"] = NatLangDb.MakePossesive(userName);
            }
        }

        private UserConversationScope LastResultOtherParticipant()
        {
            if (LastResult != null)
            {
                UserConversationScope lastResultResponder = LastResult.Responder;
                if (lastResultResponder != this && lastResultResponder != null) return lastResultResponder;
                lastResultResponder = LastResult.Requester;
                if (lastResultResponder != this && lastResultResponder != null) return lastResultResponder;
            }
            return null;
        }

        public bool DoUserCommand(string input, OutputDelegate console)
        {
            if (input == null) return false;
            input = input.Trim();
            if (input.StartsWith("@"))
            {
                input = input.TrimStart(new[] { ' ', '@' });
                switch (input)
                {
                    case "save":
                        {
                            SaveOften();
                        }
                        return true;
                    default:
                        break;
                }
            }
            string output;
            string cmd;
            if (!SplitOff(input, " ", out cmd, out output))
            {
                cmd = "";
                output = input;
            }
            {
                if (input == "proof")
                {
                    var cis = VisitedTemplates;
                    console("-----------------------------------------------------------------");
                    console("LS: count=" + cis.Count + " local=" + StartGraph);
                    GraphMaster.PrintToWriter(cis, PrintOptions.SAVE_TO_FILE, new OutputDelegateWriter(console), null,
                                              TimeSpan.Zero);
                    console("-----------------------------------------------------------------");
                    return true;
                }
            }
            if (input == "log")
            {
                ShowConversationLog(console, 1000);
                return true;
            }
            //if (input == "") return false;
            if (input == "")
            {
                console(Predicates.ToDebugString());
                AltBot.WriteUserInfo(console, "", this);
                return true;
            }
            return ((SettingsDictionaryReal)Predicates).DoSettingsCommand(input, console);
        }

        public OutputDelegate userTrace { get; set; }

        public void WriteToUserTrace(string s, params object[] objects)
        {
            try
            {
                s = SafeFormat("{0} {1}", UserName ?? UserID, SafeFormat(s, objects));
                if (s.ToUpper().Contains("ERROR"))
                {
                    bot.writeToLog(s, objects);
                }
                if (userTrace != null && userTrace != WriteToUserTrace)
                {
                    userTrace(s);
                    return;
                }
                rbot.writeToUserLog(s);
            }
            catch (Exception exception)
            {
                rbot.writeToLog(exception);
            }
        }

        public void addResultTemplates(Result result)
        {
            lock (TemplatesLock)
            {
                lock (result.ResultTemplates)
                    VisitedTemplates.AddRange(result.ResultTemplates);

                var proof = result.ProofTemplate();
                if (proof == null) return;
                if (result.request.ParentRequest != null)
                {
                    UsedChildTemplates.Add(proof);
                }
                else
                {
                    ProofTemplates.Add(proof);
                }
            }
        }

        public void addRequestTemplates(Request request)
        {
            lock (TemplatesLock)
            {
                lock (request.UsedResults)
                {
                    foreach (var list in request.UsedResults)
                    {
                        addResultTemplates(list);
                    }
                }
            }
        }

        public Result GetResult(int i, bool mustBeSalient)
        {
            return GetResult(i, mustBeSalient, null);
        }

        public Result GetResult(int i, bool mustBeSalient, UserConversationScope responder)
        {
            bool mustBeResponder = responder != null;
            if (i == -1) return CurrentRequest.CurrentResult;
            lock (Results)
            {
                if (i >= Results.Count)
                {
                    return null;
                }
                {
                    foreach (var r in Results)
                    {
                        if (r.Responder == this) continue;
                        if (mustBeResponder) if (r.Responder != responder) continue;
                        if (mustBeSalient && !r.IsSalient) continue;
                        if (i == 0) return r;
                        i--;
                    }
                    return null;
                }
            }
        }

        public Request CurrentRequest { get; set; }

        public Result GetResult(int i)
        {
            return GetResult(i, false);
        }

        public void setOutputSentence(int n, int sent, string data)
        {
            if (n >= this.Results.Count)
            {
                return;
                //this.Results[n] = new   Result(this, this.bot, new Request("", this, this.bot),this);
            }
            Result historicResult = (Result)this.Results[n];
            historicResult.SetOutputSentence(sent, data);

        }

        public void SetOutputSentences(string args, User responder)
        {
            args = ForOutputTemplate(args);
            Result result = GetResult(0, false, responder);
            if (result != null)
                result.SetOutput = args;
            else
            {
                bot.writeToLog("no last result in SetOutputSentences " + args + " for " + this);
            }
        }

        public bool CanUseTemplate(TemplateInfo info, Result request)
        {
            lock (TemplatesLock)
            {
                if (DisabledTemplates.Contains(info)) return false;
            }
#if !(EXTREME_DEBUGGING)
            return true;
#endif
            lock (TemplatesLock)
            {
                if (VisitedTemplates.Contains(info))
                {
                    if (info.CategoryInfo != null && info.CategoryInfo.Pattern != null &&
                        info.CategoryInfo.Pattern.FullPath != null)
                    {
                        if (info.CategoryInfo.Pattern.FullPath.AsString() == "*")
                        {
                            return true;
                        }
                    }
                    // return false;
                }
            }
            return true;
        }


        public string UserDirectory
        {
            get { return _saveToDirectory; }
            set
            {
                _saveToDirectory = value;
                Predicates.addSetting("userdir", value);
            }
        }

        private string _saveToDirectory;
        private bool needsSave;
        private bool insideSave;
        private bool noLoad = true;
        public bool NeverSave = NeverSaveUsers;
        private bool NeedAiml = false;
        private readonly List<CrossAppDomainDelegate> ShutdownHooks = new List<CrossAppDomainDelegate>();
        private readonly List<CrossAppDomainDelegate> OnNeedAIML = new List<CrossAppDomainDelegate>();
        private Request _lastRequest;
        //public int depth { get; set; }


        public void SyncDirectory(string userdir)
        {
            if (_saveToDirectory == null) _saveToDirectory = userdir;
            noLoad = false;
            lock (SaveLock)
            {
                if (insideSave) return;
            }
            LoadDirectory(userdir);
            if (userdir != null) Predicates.addSetting("userdir", userdir);
            if (IsRoleAcct) return;
            SaveDirectory(userdir);
        }

        public void SaveDirectory(string userdir)
        {
            _saveToDirectory = userdir;
            needsSave = true;
            lock (SaveLock)
            {
                if (insideSave) return;
                insideSave = true;
            }
            try
            {
                if (NeverSave) return;
                SaveDirectory0(userdir);
            }
            finally
            {
                lock (SaveLock)
                {
                    insideSave = false;
                }
            }
        }

        private void SaveDirectory0(string userdir)
        {
            if (userdir == null && _saveToDirectory == null)
            {
                WriteToUserTrace("no saveto dir specified");
                return;
            }
            if (!HostSystem.CreateDirectory(userdir))
            {
                WriteToUserTrace("Cannot SaveDirectory {0}", userdir);
                return;
            }
            // or WriteLine but this is spammy 
            OutputDelegate logger = DEVNULL;
            logger("DEBUG9 Saving User Directory {0}", userdir);
            ((SettingsDictionaryReal)Predicates).SaveTo(userdir, "user.predicates", "UserPredicates.xml");
            GraphMaster gm = rbot.GetGraph(UserID, StartGraph);
            gm.WriteToFile(UserID, HostSystem.Combine(userdir, UserID) + ".saved", PrintOptions.SAVE_TO_FILE, logger);
        }

        public void LoadDirectory(string userdir)
        {
            lock (SaveLock)
            {
                if (insideSave) return;
                insideSave = true;
            }
            try
            {
                LoadDirectory0(userdir);
            }
            finally
            {
                lock (SaveLock)
                {
                    insideSave = false;
                }
            }
        }

        private void LoadDirectory0(string userdir)
        {
            if (HostSystem.DirExists(userdir))
            {
                if (noLoad) return;
                LoadUserSettings(userdir);
                LoadUserAiml(userdir);
                WriteToUserTrace("LoadDirectory " + userdir + " -DEBUG9");
            }
            else
            {
                WriteToUserTrace("Not LoadDirectory " + userdir);
            }
        }

        public void LoadUserSettings(string userdir)
        {
            if (HostSystem.DirExists(userdir))
            {
                if (noLoad) return;
                foreach (string s in HostSystem.GetFiles(userdir, "*.xml"))
                {
                    LoadUserSettings(s);
                }
                return;
            }
            if (HostSystem.FileExists(userdir))
            {
                if (!userdir.EndsWith(".xml"))
                {
                    return;
                }
                if (userdir.EndsWith("Predicates.xml"))
                {
                    SettingsDictionaryReal.loadSettingsNow(Predicates, null, userdir, new SettingsPolicy(true, true), null);
                }
                return;
            }
        }

        public void LoadUserAiml(string userdir)
        {
            if (HostSystem.DirExists(userdir))
            {
                AddTodoItem(() => LoadUserAiml0(userdir));
                return;
            }
            // process previous todo list
            DoPendingTodoList();
            LoadUserAiml0(userdir);
        }

        private void LoadUserAiml0(string userdir)
        {
            string[] hostSystemGetFiles = HostSystem.GetFiles(userdir, "*.aiml");
            if (hostSystemGetFiles == null || hostSystemGetFiles.Length <= 0) return;
            var request = new Request("@echo load user aiml ", this, Unifiable.EnglishNothing, rbot.BotAsUser, bot,
                                            null, StartGraph);
            request.TimesOutAt = DateTime.Now + new TimeSpan(0, 15, 0);
            request.Graph = StartGraph;
            request.LoadingFrom = userdir;
            var options = request.LoadOptions; //LoaderOptions.GetDefault(request);
            var gs = bot.GlobalSettings;
            try
            {
                if (rbot.BotAsUser == this)
                {
                    bot.GlobalSettings = SettingsDictionaryReal.ToSettingsDictionary(Predicates);
                }
                request.Loader.loadAIMLURI(userdir, options.Value);
            }
            finally
            {
                bot.GlobalSettings = gs;
            }
        }

        public void AddTodoItem(CrossAppDomainDelegate action)
        {
            lock (OnNeedAIML)
            {
                OnNeedAIML.Add(action);
            }
            if (NeedAiml)
            {
                DoPendingTodoList();
            }
        }

        private void DoPendingTodoList()
        {
            lock (OnNeedAIML)
            {
                //    if (!needAiml) return;
                NeedAiml = true;
            }
            DoPendingUntilComplete(OnNeedAIML);
        }

        internal static void DoPendingUntilComplete(ICollection<CrossAppDomainDelegate> todoList)
        {
            if (todoList == null) return;
            while (true)
            {
                var todo = new List<CrossAppDomainDelegate>();
                lock (todoList)
                {
                    if (todoList.Count == 0) return;
                    todo.AddRange(todoList);
                    todoList.Clear();
                }
                foreach (var list in todo)
                {
                    try
                    {
                        list();
                    }
                    catch (Exception e)
                    {
                        writeDebugLine("DoPendingUntilComplete: " + list + " - " + e);
                    }
                }
            }
        }

        #region Implementation of ISettingsDictionary

        /// <summary>
        /// Adds a bespoke setting to the Settings class (accessed via the grabSettings(string name)
        /// method.
        /// </summary>
        /// <param name="name">The name of the new setting</param>
        /// <param name="value">The value associated with this setting</param>
        public bool addSetting(string name, object value)
        {
            return Predicates.addSetting(name, value);
        }

        /// <summary>
        /// Removes the named setting from this class
        /// </summary>
        /// <param name="name">The name of the setting to remove</param>
        public bool removeSetting(string name)
        {
            return Predicates.removeSetting(name);
        }

        /// <summary>
        /// Updates the named setting with a new value whilst retaining the position in the
        /// dictionary
        /// </summary>
        /// <param name="name">the name of the setting</param>
        /// <param name="value">the new value</param>
        public bool updateSetting(string name, object value)
        {
            return Predicates.updateSetting(name, value);
        }

        /// <summary>
        /// Returns the value of a setting given the name of the setting
        /// </summary>
        /// <param name="name">the name of the setting whose value we're interested in</param>
        /// <returns>the value of the setting</returns>
        public DataUnifiable grabSetting(string name)
        {
            return Predicates.grabSetting(name);
        }

        /// <summary>
        /// Checks to see if a setting of a particular name exists
        /// </summary>
        /// <param name="name">The setting name to check</param>
        /// <returns>Existential truth value</returns>
        public bool containsLocalCalled(string name)
        {
            return Predicates.containsLocalCalled(name);
        }

        public bool containsSettingCalled(string name)
        {
            return Predicates.containsSettingCalled(name);
        }

        public string NameSpace
        {
            get { return UserID; }
        }

        public PrintOptions WriterOptions { get; set; }

        public actMSM botActionMSM
        {
            get { return rbot.pMSM; }
        }

        public IEnumerable<string> SettingNames(ICollectionRequester requester, int depth)
        {
            //get 
            {
                return Predicates.SettingNames(requester, depth);
            }
        }

        #endregion

        public GraphMaster GetResponseGraph(User target)
        {
            GraphMaster G = StartGraph;
            if (G != null) return G;
            G = HeardSelfSayGraph;
            if (G != null) return G;
            return this.HeardSelfSayGraph;// GetResponseGraph(this);
        }

        public MasterRequest CreateRequest(Unifiable message, User target)
        {
            target = target ?? LastResponder.Value;
            Unifiable targetJustSaid = ResponderJustSaid;
            if (target != null && !Unifiable.IsNull(targetJustSaid)) targetJustSaid = target.JustSaid;
            var mr = CreateRequest(message, target, targetJustSaid, GetResponseGraph(target), null);
            return mr;
        }

        public MasterRequest CreateRequest(Unifiable message, Unifiable said, User target)
        {
            var mr = CreateRequest(message, target, said, GetResponseGraph(target), null);
            return mr;
        }

        public MasterRequest CreateRequest(Unifiable message, User target, Unifiable said, GraphMaster G, Request parentRequest)
        {
            if (G == null) G = GetResponseGraph(target);
            bool asIsToplevelRequest = true;
            //depth = 0;
            MasterRequest request;

            if (target == null)
            {
                if (LastResponder != null)
                {
                    target = LastResponder.Value;

                }
                else
                {
                    target = this;
                }
            }
            Unifiable targetJustSaid = said ?? ResponderJustSaid;
            if (target != null && !Unifiable.IsNull(said)) targetJustSaid = target.JustSaid;
            if (parentRequest == null)
            {
                request = new MasterRequest(message, this, targetJustSaid, target, bot, parentRequest, G);
            }
            else
            {
                request = parentRequest.CreateSubRequest(message, this, targetJustSaid, target, bot, parentRequest, G);
            }
            if (parentRequest != null)
            {
                asIsToplevelRequest = false;
                request.OriginalSalientRequest = parentRequest.OriginalSalientRequest;
                request.Graph = G ?? parentRequest.Graph;
                request.ParentRequest = parentRequest;
                request.IsTraced = false;
            }
            else
            {
                asIsToplevelRequest = true;
                request.OriginalSalientRequest = request;
            }
            DoPendingTodoList();

            if (asIsToplevelRequest)
            {
                request.depth = 0;
                request.IsToplevelRequest = true;
                if (CurrentRequest != null) CurrentRequest = request;
                request.Responder = target;
                request.TargetBot = this.bot;
                request.writeToLog = writeDebugLine;
                //request.TimesOutAt = DateTime.Now + TimeSpan.FromSeconds(10);
                request.ResetValues(true);
                Result res = request.FindOrCreateCurrentResult();
                request.ExitQueue.Add("LastResponder = target;", () => { LastResponder = target; });

                var roles = this.Predicates.grabSetting("roles");
                if (!IsMissing(roles) && !IsNullOrEmpty(roles))
                {
                    foreach (var role in SingleNameValue.AsCollection(roles))
                    {
                        request.AddGraph(rbot.GetUserGraph( bot.ToValueString(role)));
                    }
                }
            }
            return request;
        }


        public void StampResponseGiven()
        {
            LastResponseGivenTime = DateTime.Now;
        }

        public bool CanGiveResponseNow()
        {
            // KHC: TODO Revisit, response time seemed more like minute fractions than totalseconds for whole struct
            // could be
            DateTime thisResponseTime = DateTime.Now;
            TimeSpan TSP = thisResponseTime.Subtract(LastResponseGivenTime);
            double timedelay = Math.Abs(TSP.TotalSeconds);
            double secPerChat = (60 / (double)MaxRespondToChatPerMinute);
            return (timedelay >
                // ReSharper disable PossibleLossOfFraction
                    secPerChat
                   );
            // ReSharper restore PossibleLossOfFraction
        }

        public void Enter(ConversationScopeHolder srai)
        {
            //depth++;
        }

        public void Exit(ConversationScopeHolder srai)
        {
            //depth--;
            if (false)
            {
                SituationInConversation sraiContextScope = srai.ContextScope;
                if (sraiContextScope != null)
                {
                    var query = sraiContextScope.CurrentQuery;
                    if (query != null) query.PurgeTagHandlers();
                }
            }
        }

        public string grabSettingNoDebug(string settingName)
        {
            return Predicates.grabSetting(settingName);
        }

        public User Value
        {
            get { return this; }
        }

        public UserStaticModel StaticModel
        {
            get { return this; }
        }

        public UserConversationScope ConversationScope
        {
            get { return this; }
        }

        public UserConversationScope DuringProcessing
        {
            get { return this; }
        }

        public void DisposeObject()
        {
            IsValid = false;
            Dispose();
        }
        /// <summary>
        /// the value of the "topic" predicate
        /// </summary>
        public string TopicString
        {
            get
            {
                return GetValueOr("topic", "*");
            }
        }

        private string GetValueOr(string varname, string or)
        {
            var t = this.Predicates.grabSetting(varname);
            if (string.IsNullOrEmpty(t)) return or;
            return t;
        }

        /// <summary>
        /// the value of the "state" predicate
        /// </summary>
        public string State
        {
            get
            {
                return GetValueOr("state", "*");
            }
        }


        /// <summary>
        /// Returns the string to use for the next that part of a subsequent path
        /// </summary>
        /// <returns>the string to use for that</returns>
        public IEnumerable<string> getThats()
        {
            List<string> outputs = new List<string>();
            if (blackBoardThat.Length > 0)
            {
                outputs.Add(blackBoardThat);
            }
            int cnt = 0;
            foreach (Result result in Results)
            {

                string s = result.LastSentence;
                s = s.TrimStart(" .,".ToCharArray());
                if (!string.IsNullOrEmpty(s))
                {
                    if (!outputs.Contains(s))
                    {
                        outputs.Add(s);
                        cnt++;
                    }
                }
                s = result.BestSentence;
                s = s.TrimStart(" .,".ToCharArray());
                if (!string.IsNullOrEmpty(s))
                {
                    if (!outputs.Contains(s))
                    {
                        outputs.Add(s);
                        cnt++;
                    }
                }
                if (cnt > 0) break;
            }
            if (outputs.Count == 0) return new[] { "*" };
            return outputs.ToArray();
        }

        public IEnumerable<string> getTopics()
        {
            return new[] { (string)Topic };
        }
        public IEnumerable<string> getPreStates()
        {
            return new[] { State };
        }
        public IEnumerable<string> getPostStates()
        {
            return new[] { State };
        }

        public void setUserID(string id)
        {
            this.id = id;
            this.UserName = id;
        }
    }

    public interface ConversationScopeHolder
    {
        SituationInConversation ContextScope { get; }
    }
}
/*
namespace AltAIMLbotFOO
{
    /// <summary>
    /// Encapsulates information and history of a user who has interacted with the bot
    /// </summary>
    [Serializable]
    public partial class UserFoo
    {
        #region Attributes

        /// <summary>
        /// The local instance of the GUID that identifies this user to the bot
        /// </summary>
        private string id;

        /// <summary>
        /// The bot this user is using
        /// </summary>
        public AltBot bot;

        /// <summary>
        /// The GUID that identifies this user to the bot
        /// </summary>
        public string UserID
        {
            get { return this.id; }
        }
        public void setUserID(string uid)
        {
            id = uid;
        }
        /// <summary>
        /// A collection of all the result objects returned to the user in this session
        /// </summary>
        private List<Result> Results = new List<Result>();

        /// <summary>
        /// the value of the "topic" predicate
        /// </summary>
        public string Topic
        {
            get
            {
                return GetValueOr("topic", "*");
            }
        }

        private string GetValueOr(string varname, string or)
        {
            var t = this.Predicates.grabSetting(varname);
            if (string.IsNullOrEmpty(t)) return or;
            return t;
        }

        /// <summary>
        /// the value of the "state" predicate
        /// </summary>
        public string State
        {
            get
            {
                return GetValueOr("state", "*");
            }
        }

        /// <summary>
        /// List of possible non-determinstic "states". When present will select the one with 
        /// the highest score
        /// </summary>
        public Dictionary<string, double> Qstate = new Dictionary<string, double>();

        /// <summary>
        /// the value of the "that" on the blackboard predicate
        /// </summary>
        public string blackBoardThat = "";

        /// <summary>
        /// the predicates associated with this particular user
        /// </summary>
        public SettingsDictionary Predicates;

        /// <summary>
        /// The most recent result to be returned by the bot
        /// </summary>
        public Result LastResult
        {
            get
            {
                if (this.Results.Count > 0)
                {
                    return (Result)this.Results[0];
                }
                else
                {
                    return null;
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="UserID">The GUID of the user</param>
        /// <param name="bot">the bot the user is connected to</param>
        public UserFoo(string UserID, AltBot bot)
        {

            if (UserID.Length > 0)
            {
                this.id = UserID;
                this.bot = bot;
                this.Predicates = new SettingsDictionary(UserID, this.bot, null);
                this.bot.DefaultPredicates.Clone(this.Predicates);
                this.Predicates.bbPrefix = "user";
                this.Predicates.addSetting("topic", "*");
                this.Predicates.addSetting("state", "*");
            }
            else
            {
                throw new Exception("The UserID cannot be empty");
            }
        }

        /// <summary>
        /// Returns the string to use for the next that part of a subsequent path
        /// </summary>
        /// <returns>the string to use for that</returns>
        public string getLastBotOutput()
        {
            if (blackBoardThat.Length > 0)
            {
                return blackBoardThat;
            }
            foreach (Result result in Results)
            {
                string s = result.LastSentence;
                s = s.TrimStart(" .,".ToCharArray());
                if (!string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }
            return "*";

        }

        /// <summary>
        /// Returns the first sentence of the last output from the bot
        /// </summary>
        /// <returns>the first sentence of the last output from the bot</returns>
        public string getThat()
        {
            return this.getThat(0, 0);
        }

        /// <summary>
        /// Returns the first sentence of the output "n" steps ago from the bot
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <returns>the first sentence of the output "n" steps ago from the bot</returns>
        public string getThat(int n)
        {
            return this.getThat(n, 0);
        }

        public int SailentResultCount
        {
            get
            {
                lock (Results)
                {
                    return Results.Count;
                }
            }
        }

        /// <summary>
        /// Returns the sentence numbered by "sentence" of the output "n" steps ago from the bot
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <param name="sentence">the sentence number to get</param>
        /// <returns>the sentence numbered by "sentence" of the output "n" steps ago from the bot</returns>
        public string getThat(int n, int sentence)
        {
            Result historicResult = GetResult(n);
            if (historicResult != null)
            {
                return historicResult.GetOutputSentence(sentence);
            }
            return string.Empty;
        }

        public void setOutputSentence(int n, int sent, string data)
        {
            if (n >= this.Results.Count)
            {
                this.Results[n] = null;// new Result(this, this.bot, new Request("", this, this.bot));
            }
            Result historicResult = (Result)this.Results[n];
            historicResult.OutputSentences[sent] = data;

        }
        /// <summary>
        /// Returns the first sentence of the last output from the bot
        /// </summary>
        /// <returns>the first sentence of the last output from the bot</returns>
        public string getInputSentence()
        {
            return this.getInputSentence(0, 0);
        }

        /// <summary>
        /// Returns the first sentence from the output from the bot "n" steps ago
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <returns>the first sentence from the output from the bot "n" steps ago</returns>
        public string getInputSentence(int n)
        {
            return this.getInputSentence(n, 0);
        }

        /// <summary>
        /// Returns the identified sentence number from the output from the bot "n" steps ago
        /// </summary>
        /// <param name="n">the number of steps back to go</param>
        /// <param name="sentence">the sentence number to return</param>
        /// <returns>the identified sentence number from the output from the bot "n" steps ago</returns>
        public string getInputSentence(int n, int sentence)
        {
            Result historicResult = GetResult(n);
            if (historicResult != null)
            {
                return historicResult.GetInputSentence(sentence);
            }
            return string.Empty;
        }


        public Result GetResult(int i)
        {
            return GetResult(i, false);
        }
        public Result GetResult(int i, bool mustBeSalient)
        {
            return GetResult(i, mustBeSalient, null);
        }
        public Result GetResult(int i, bool mustBeSalient, UserConversationScope responder)
        {
            bool mustBeResponder = responder != null;
            //if (i == -1) return CurrentRequest.CurrentResult;
            lock (Results)
            {
                if (i >= Results.Count)
                {
                    return null;
                }
                {
                    foreach (var r in Results)
                    {
                        //if (r.Responder == this) continue;
                        //if (mustBeResponder) if (r.Responder != responder) continue;
                        //if (mustBeSalient && !r.IsSalient) continue;
                        if (i == 0) return r;
                        i--;
                    }
                    return null;
                }
            }
        }


        /// <summary>
        /// Adds the latest result from the bot to the Results collection
        /// </summary>
        /// <param name="latestResult">the latest result from the bot</param>
        public void addResult(Result latestResult)
        {
            this.Results.Insert(0, latestResult);
        }
        public void setResult(int n, Result desiredResult)
        {
            this.Results[n] = desiredResult;
        }
        #endregion

        public IEnumerable<string> getThats()
        {
            return new[] { getLastBotOutput() };
        }
        public IEnumerable<string> getTopics()
        {
            return new[] { Topic };
        }
        public IEnumerable<string> getPreStates()
        {
            return new[] { State };
        }
        public IEnumerable<string> getPostStates()
        {
            return new[] { State };
        }
    }
}
*/