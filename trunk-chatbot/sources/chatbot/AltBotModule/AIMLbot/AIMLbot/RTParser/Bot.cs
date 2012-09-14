﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Mail;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using AIMLbot;
using LAIR.ResourceAPIs.WordNet;
using MushDLR223.ScriptEngines;
using MushDLR223.Utilities;
using MushDLR223.Virtualization;
using org.opencyc.api;
#if USE_SWIPROLOG
using PrologScriptEngine;
#endif
using RTParser.AIMLTagHandlers;
using RTParser.Database;
using RTParser.Prolog;
using RTParser.Utils;
using RTParser.Variables;
using RTParser.Web;
using Console=System.Console;
using UPath = RTParser.Unifiable;
using UList = System.Collections.Generic.List<RTParser.Utils.TemplateInfo>;
using AltAIMLbot;

namespace RTParser
{
    /// <summary>
    /// Return a Response object
    /// </summary>
    /// <param name="cmd"></param>
    /// <param name="requestOrNull"></param>
    /// <returns></returns>
    public delegate object SystemExecHandler(string cmd, Request requestOrNull);

    /// <summary>
    /// Encapsulates a Proccessor. If no settings.xml file is found or referenced the Proccessor will try to
    /// default to safe settings.
    /// </summary>
    public partial class RTPBot : StaticAIMLUtils, IChatterBot
    {
        public static bool IncludeMeNeValue;
        public static Dictionary<string, RTPBot> Robots = new Dictionary<string, RTPBot>();

        public static RTPBot FindOrCreateRobot(string text)
        {
            RTPBot robot;
            lock (Robots)
            {
                if (TryGetValueLocked(Robots, Robots, text, out robot))
                {
                    return robot;
                }
                Robots[text] = robot = new RTPBot();
            }
            robot.SetName(text);
            return robot;
        }

        private readonly List<XmlNodeEvaluator> XmlNodeEvaluators = new List<XmlNodeEvaluator>();
        private TestCaseRunner testCaseRunner;

        private static int skipMany;
        public static bool UseBreakpointOnError;

        public bool ListeningToSelf
        {
            get
            {
                return true;
                if (GlobalSettings != null)
                {
                    Unifiable lts = GlobalSettings.grabSettingNoDebug("ListeningToSelf");
                    if (IsUnknown(lts)) return false;
                    if (IsFalse(lts)) return false;
                    if (IsTrue(lts)) return true;
                    return true;
                }

                return false;
            }
        }

        public bool ProcessHeardPreds
        {
            get
            {
                return true;
                if (GlobalSettings != null)
                {
                    Unifiable lts = GlobalSettings.grabSettingNoDebug("ProcessHeardPreds");
                    if (IsUnknown(lts)) return false;
                    if (IsFalse(lts)) return false;
                    if (IsTrue(lts)) return true;
                    return true;
                }

                return false;
            }
        }

        public override string ToString()
        {
            string s = GetType().Name;
            if (!string.IsNullOrEmpty(NameAsSet)) return s + " nameAsSet=" + NameAsSet;
            if (GlobalSettings != null)
            {
                s += " name=" + GlobalSettings.grabSettingNoDebug("name") + " (" + NamePath + ")";
            }
            if (!string.IsNullOrEmpty(NamePath)) return s + " NamePath=" + NamePath;
            return s;
        }

        /// <summary>
        /// Will ensure the same loader options are used between loaders
        /// </summary>
        public bool StaticLoader = true;

        public User BotAsUser;
        public User ExemplarUser;
        public string NamePath;
        public string NameAsSet;


        //public Request BotAsRequestUsed = null;
        public Request GetBotRequest(string s)
        {
            var botAsUser1 = BotAsUser ?? LastUser;
            s = Trim(s);
            if (!s.StartsWith("<")) s = "<!-- " + s.Replace("<!--", "<#").Replace("-->", "#>") + " -->";
            var r = new AIMLbot.MasterRequest(s, botAsUser1, Unifiable.EnglishNothing, botAsUser1, this, null,
                                              DefaultStartGraph);
            //r.ChatOutput.RawText = s;
            r.writeToLog = writeToLog;
            //Result res = new AIMLbot.MasterRequest(s, botAsUser1, this, r, null, null);            
            //r.CurrentQuery = new SubQuery(s, res, r);
            OnBotCreated(() =>
                             {
                                 User BotAsUser1 = this.BotAsUser;
                                 ((Request)r).SetSpeakerAndResponder(BotAsUser1, BotAsUser1);
                             });
            r.IsTraced = this.IsTraced;
            r.depth = 0;
            // times out in 15 minutes
            r.TimeOutFromNow = TimeSpan.FromMinutes(15);
            return r;
        }

        private AIMLLoader _loader;
        private AIMLLoader _loaderOnceLeast;
        public AIMLLoader Loader
        {
            set
            {
                _loader = value;
                if (value == null)
                {
                    _loaderOnceLeast = value;
                }
            }
            get
            {
                return _loader ?? _loaderOnceLeast;
            }
        }

        #region Attributes

        public List<CrossAppDomainDelegate> ReloadHooks = new List<CrossAppDomainDelegate>();

        /// <summary>
        /// A dictionary object that looks after all the settings associated with this Proccessor
        /// </summary>
        public SettingsDictionary GlobalSettings;

        public SettingsDictionary SharedGlobalSettings;

        #endregion

        /// <summary>
        /// A dictionary of all the gender based substitutions used by this Proccessor
        /// </summary>
        public SettingsDictionary GenderSubstitutions;

        /// <summary>
        /// A dictionary of all the first person to second person (and back) substitutions
        /// </summary>
        public SettingsDictionary Person2Substitutions;

        /// <summary>
        /// A dictionary of first / third person substitutions
        /// </summary>
        public SettingsDictionary PersonSubstitutions;

        /// <summary>
        /// Generic substitutions that take place during the normalization process
        /// </summary>
        public SettingsDictionary InputSubstitutions;

        /// <summary>
        /// Output substitutions that take place before the bot speaks
        /// </summary>
        static public SettingsDictionary OutputSubstitutions;

        /// <summary>
        /// The default predicates to set up for a user
        /// </summary>
        public SettingsDictionary DefaultPredicates;

        /// <summary>
        /// A weak name/value association list of what has happened in dialog  
        /// </summary>
        public SettingsDictionary HeardPredicates;

        /// <summary>
        /// A name+prop/value association list of things like  look.set-return, look.format-whword,
        /// look.format-assert, look.format-query, look.format-etc,
        /// </summary>
        public SettingsDictionary RelationMetaProps;

        /// <summary>
        /// When a tag has no name like <icecream/> it is transformed to <bot name="icecream"></bot>
        /// </summary>
        public static bool UnknownTagsAreBotVars = true;

        /// <summary>
        ///  Substitution blocks for graphmasters
        /// </summary>
        public Dictionary<string, ISettingsDictionary> AllDictionaries = new Dictionary<string, ISettingsDictionary>();

        /// <summary>
        /// An List<> containing the tokens used to split the input into sentences during the 
        /// normalization process
        /// </summary>
        static public List<string> Splitters = new List<string>();

        /// <summary>
        /// Flag to show if the Proccessor is willing to accept user input
        /// </summary>
        public bool isAcceptingUserInput = true;

        /// <summary>
        /// A dictionary of all inherited settings betten users
        /// </summary>
        public SettingsDictionary AllUserPreds;

        /// <summary>
        /// A dictionary of all settings from anyone .. just a fallback
        /// </summary>
        public SettingsDictionary EnginePreds;

        readonly public TagHandlerProcessor TagHandling = new TagHandlerProcessor();
        /// <summary>
        /// A buffer to hold log messages to be written out to the log file when a max size is reached
        /// </summary>
        private readonly List<string> LogBuffer = new List<string>();

        /// <summary>
        /// A list of Topic states that are set currently (for use of guarding content)
        /// </summary>
        public List<Unifiable> CurrentStates = new List<Unifiable>();

        /// <summary>
        /// How big to let the log buffer get before writing to disk
        /// </summary>
        private int MaxLogBufferSize
        {
            get { return Convert.ToInt32(GlobalSettings.grabSetting("maxlogbuffersize")); }
        }

        /// <summary>
        /// The message to show if a user tries to use the Proccessor whilst it is set to not process user input
        /// </summary>
        private Unifiable NotAcceptingUserInputMessage
        {
            get { return GlobalSettings.grabSettingNoDebug("notacceptinguserinputmessage"); }
        }

        /// <summary>
        /// The maximum amount of time a request should take (in milliseconds)
        /// </summary>
        public double TimeOut
        {
            get
            {
                return 7000;
                if (GlobalSettings == null || !GlobalSettings.containsSettingCalled("timeout"))
                {
                    return 2000000;
                }
                String s = GlobalSettings.grabSettingNoDebug("timeout").ToValue(null);
                return Convert.ToDouble(s);
            }
        }

        /// <summary>
        /// The message to display in the event of a timeout
        /// </summary>
        public Unifiable TimeOutMessage
        {
            get { return GlobalSettings.grabSetting("timeoutmessage"); }
        }

        /// <summary>
        /// The locale of the Proccessor as a CultureInfo object
        /// </summary>
        public CultureInfo Locale
        {
            get { return new CultureInfo(GlobalSettings.grabSetting("culture")); }
        }

        /// <summary>
        /// Will match all the illegal characters that might be inputted by the user
        /// </summary>
        public Regex Strippers
        {
            get
            {
                return new Regex(GlobalSettings.grabSettingNoDebug("stripperregex"),
                                 RegexOptions.IgnorePatternWhitespace);
            }
        }

        /// <summary>
        /// The email address of the botmaster to be used if WillCallHome is set to true
        /// </summary>
        public string AdminEmail
        {
            get { return GlobalSettings.grabSetting("adminemail"); }
            set
            {
                if (value.Length > 0)
                {
                    // check that the email is valid
                    Unifiable patternStrict = @"^(([^<>()[\]\\.,;:\s@\""]+"
                                              + @"(\.[^<>()[\]\\.,;:\s@\""]+)*)|(\"".+\""))@"
                                              + @"((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}"
                                              + @"\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+"
                                              + @"[a-zA-Z]{2,}))$";
                    Regex reStrict = new Regex(patternStrict);

                    if (reStrict.IsMatch(value))
                    {
                        // update the settings
                        GlobalSettings.addSetting("adminemail", value);
                    }
                    else
                    {
                        throw (new Exception("The AdminEmail is not a valid email address"));
                    }
                }
                else
                {
                    GlobalSettings.addSetting("adminemail", Unifiable.Empty);
                }
            }
        }

        /// <summary>
        /// Flag to denote if the Proccessor is writing messages to its logs
        /// </summary>
        public bool IsLogging
        {
            get
            {
                // otherwse we use up too much ram
                if (true) return false;
                if (!GlobalSettings.containsSettingCalled("islogging")) return false;
                Unifiable islogging = GlobalSettings.grabSettingNoDebug("islogging");
                if (IsTrue(islogging))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Flag to denote if the Proccessor will email the botmaster using the AdminEmail setting should an error
        /// occur
        /// </summary>
        public bool WillCallHome
        {
            get
            {
                Unifiable willcallhome = GlobalSettings.grabSetting("willcallhome");
                return (IsTrue(willcallhome));
            }
        }

        /// <summary>
        /// When the RTPBot was initialised
        /// </summary>
        public DateTime StartedOn = DateTime.Now;

        /// <summary>
        /// The supposed sex of the Proccessor
        /// </summary>
        public Gender Sex
        {
            get
            {
                int sex = Convert.ToInt32(GlobalSettings.grabSetting("gender"));
                Gender result;
                switch (sex)
                {
                    case -1:
                        result = Gender.Unknown;
                        break;
                    case 0:
                        result = Gender.Female;
                        break;
                    case 1:
                        result = Gender.Male;
                        break;
                    default:
                        result = Gender.Unknown;
                        break;
                }
                return result;
            }
        }

        private string _PathToUserFiles;

        public string PathToUserDir
        {
            get
            {
                if (_PathToUserFiles != null) return _PathToUserFiles;
                if (GlobalSettings.containsSettingCalled("userdirectory"))
                {
                    Unifiable dir = GlobalSettings.grabSettingNoDebug("userdirectory");
                    HostSystem.CreateDirectory(dir);
                    _PathToUserFiles = dir;
                    return HostSystem.ToRelativePath(dir, RuntimeDirectory);
                }
                foreach (string s in new[] { PersonalAiml, PathToAIML, PathToConfigFiles, RuntimeDirectory })
                {
                    if (s == null) continue;
                    string exists = HostSystem.Combine(s, "users");
                    if (HostSystem.DirExists(exists))
                    {
                        exists = HostSystem.ToRelativePath(exists, RuntimeDirectory);
                        _PathToUserFiles = exists;
                        return exists;
                    }
                }
                string tryplace = HostSystem.Combine(PathToAIML, "users");
                HostSystem.CreateDirectory(tryplace);
                _PathToUserFiles = tryplace;
                return tryplace;
            }
        }

        private string _PathToBotPersonalFiles;

        protected string PersonalAiml
        {
            get { return _PathToBotPersonalFiles; }
            set
            {
                lock (_RuntimeDirectories)
                {
                    if (_PathToUserFiles != null) _RuntimeDirectories.Remove(_PathToUserFiles);
                    _PathToUserFiles = value;
                    _RuntimeDirectories.Remove(value);
                    _RuntimeDirectories.Insert(0, value);
                }
            }
        }

        /// <summary>
        /// The directory to look in for the AIML files
        /// </summary>
        public string PathToAIML
        {
            get { return GetPathSetting("aimldirectory", "aiml"); }
        }

        private readonly object RuntimeDirectoriesLock = new object();

        public List<string> RuntimeDirectories
        {
            get { lock (RuntimeDirectoriesLock) return new List<string>(_RuntimeDirectories); }
        }

        private string _dataDir = Environment.CurrentDirectory;

        protected string RuntimeDirectory
        {
            get { return _dataDir ?? Environment.CurrentDirectory; }
            set { _dataDir = value; }
        }

        /// <summary>
        /// The directory to look in for the various XML configuration files
        /// </summary>
        public string PathToConfigFiles
        {
            get { return GetPathSetting("configdirectory", "config"); }
        }

        /// <summary>
        /// The directory into which the various log files will be written
        /// </summary>
        public string PathToLogs
        {
            get { return GetPathSetting("logdirectory", null); }
        }

        /// <summary>
        /// If set to false the input from AIML files will undergo the same normalization process that
        /// user input goes through. If true the Proccessor will assume the AIML is correct. Defaults to true.
        /// </summary>
        public bool TrustAIML = true;

        /// <summary>
        /// The maximum number of characters a "that" element of a path is allowed to be. Anything above
        /// this length will cause "that" to be "*". This is to avoid having the graphmaster process
        /// huge "that" elements in the path that might have been caused by the Proccessor reporting third party
        /// data.
        /// </summary>
        public int MaxThatSize = 256;

        //#endregion

        #region Delegates

        public delegate void LogMessageDelegate();

        #endregion

        #region Events

        public event LogMessageDelegate WrittenToLog;

        #endregion

        public static int BotNumberCreated;


        public static readonly Dictionary<string, string[]> SettingsAliases = new Dictionary<string, string[]>();

        public bool IsTraced
        {
            get { return qsbase.IsTraced; }
            set { qsbase.IsTraced = value; }
        }

        private readonly QuerySettings qsbase;
        public QuerySettings GetQuerySettings()
        {
            return qsbase;
        }

        public Servitor servitor = null;
        public bool useServitor = false;
        public void sayConsole(string message)
        {
            //Default output
            Console.WriteLine("SERVITOR SAYS:{0}", message);
        }
        public void reloadServitor()
        {
            string rapDir = GlobalSettings.grabSetting("rapstore");
            servitor.rapStoreDirectory = rapDir;

            string servRoot = GlobalSettings.grabSetting("serverRoot");
            if ((servRoot != null) && (servRoot.Length > 7))
            {
                WebServitor.serverRoot = servRoot;
            }

            string rapstorSL = GlobalSettings.grabSetting("rapstoreslices");
            if ((rapstorSL != null))
            {
                servitor.rapStoreSlices = int.Parse(rapstorSL); 
            }
            string rapstorTL = GlobalSettings.grabSetting("rapstoretrunklevel");
            if ((rapstorTL != null))
            {
                servitor.rapStoreTrunkLevel = int.Parse(rapstorTL);
            }


            string behaviorcache = GlobalSettings.grabSetting("behaviorcache");
            if ((behaviorcache != null) && (behaviorcache.Length > 0))
            {
                servitor.curBot.myBehaviors.persistantDirectory=behaviorcache;
            }

            if (servitor.skiploading) return;

            string graphcache = GlobalSettings.grabSetting("graphcache");
            if (File.Exists(graphcache))
            {
                try
                {
                    bool localCritical = servitor.curBot.inCritical;
                    servitor.curBot.inCritical = true;
                    servitor.loadAIMLFromFile(graphcache);
                    servitor.curBot.inCritical = localCritical;
                }
                catch (Exception e)
                {
                    Console.WriteLine("***** ERR reloadServitor():{0} ERR ******", e.Message );
                }
                servitor.skiploading = true;
                Console.WriteLine("***** reloadServitor():{0} COMPLETE ******", graphcache);
                return;
            }
            else
            {
                Console.WriteLine("No file exists for reloadServitor(graphcache)");
            }

            string servitorbin = GlobalSettings.grabSetting("servitorbin");
            if (File.Exists(servitorbin))
            {
                servitor.loadFromBinaryFile(servitorbin);
                servitor.skiploading = true;
            }
            else
            {
                Console.WriteLine("No file exists for reloadServitor()");
            }
        }
        public void saveServitor()
        {
            try
            {
                saveServitor0();
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: saveServitor()" + e);
            }
        }
        public void saveServitor0()
        {
            if (servitor == null) return;
            string rapDir = GlobalSettings.grabSetting("rapstore");
            servitor.rapStoreDirectory = rapDir;

            List<string> allPaths = new List<string>();
            List<string> allCrons = new List<string>();
            List<string> allBehaviors = new List<string>();
            //servitor.curBot.Graphmaster.collectPaths("",allPaths);
            //File.WriteAllLines(@"./aiml/graphmap.txt", allPaths.ToArray());
            string graphcache = GlobalSettings.grabSetting("graphcache");
            if (File.Exists(graphcache))
            {
                Console.WriteLine("***** saveServitor():{0} SKIPPING ******", graphcache);
                if (servitor != null) servitor.skiploading = true;
                return;
            }
            string[] header = { "<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "<aiml version=\"1.0\">", " <state name=\"*\">" };
            string[] footer = { " </state>", "</aiml>" };
            servitor.curBot.Graphmaster.collectFullPaths("", allPaths);
            allCrons = servitor.curBot.myCron.cronXmlList();
            allBehaviors = servitor.curBot.myBehaviors.behaviorXmlList();
            //StreamWriter sw = File.CreateText(@"./aiml/servitorgraphmap.aiml");
            StreamWriter sw = File.CreateText(graphcache);
            foreach (string line in header)
            {
                sw.WriteLine(line);
            }
            foreach (string line in allCrons)
            {
                sw.WriteLine(line);
            }
            foreach (string line in allPaths.ToArray())
            {
                sw.WriteLine(line);
            }
            foreach (string line in allBehaviors)
            {
            //    sw.WriteLine(line);
            }

            foreach( string line in footer)
            {
                sw.WriteLine(line);
            }
            sw.Flush();
            sw.Close();

            string servitorbin = GlobalSettings.grabSetting("servitorbin");
            if (!File.Exists(servitorbin))
            {
                //servitor.saveToBinaryFile(servitorbin);
                servitor.skiploading = true;
            }
            else
            {
                Console.WriteLine("Skipping saveServitor(): already exists!!!");
            }

        }
        public void updateServitor2RTP(User activeUser)
        {
            if (useServitor == false) return;
            if (servitor == null)
            {
                servitor = new Servitor(this.UserID, null);
                servitor.curBot.sayProcessor = new sayProcessorDelegate(sayConsole);
                servitor.curBot.wordNetEngine = wordNetEngine;
                reloadServitor();
            }
            updateServitor2RTP();
            //User specific code (ALTBOT USER->RTPUSER  )
            try
            {
                if (activeUser.Predicates != null)
                    foreach (string key in servitor.curUser.Predicates.Keys)
                    {
                        string v = servitor.curUser.Predicates.grabSetting(key);
                        activeUser.Predicates.updateSetting(key, v);
                        Console.WriteLine("ALT->RTP Predicates[{0}] = {1}", key, v);
                    }

                int rcount = servitor.curUser.SailentResultCount;
                for (int n = 0; n < rcount; n++)
                {

                    AltAIMLbot.Result historicResult = servitor.curUser.GetResult(n);
                    if (historicResult == null) continue;
                    for (int sent = 0; sent < historicResult.OutputSentences.Count; sent++)
                    {
                        string data = historicResult.OutputSentences[sent];
                        activeUser.setOutputSentence(n, sent, data);
                        Console.WriteLine("ALT->RTP setOutputSentence[{0},{1}] = {2}",n,sent,data);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(" **** ERR : {0} {1} " , e.Message , e.StackTrace);
            }
        }
        public void updateServitor2RTP()
        {
            if (useServitor == false) return;
            if (servitor == null)
            {
                servitor = new Servitor(this.UserID, null);
                servitor.curBot.sayProcessor = new sayProcessorDelegate(sayConsole);
                servitor.curBot.wordNetEngine = wordNetEngine;
                reloadServitor();
            }

        }
        public void updateRTP2Sevitor(User activeUser)
        {
            if (useServitor == false) return;
            if (servitor == null)
            {
                servitor = new Servitor(this.UserID, null);
                servitor.curBot.sayProcessor = new sayProcessorDelegate(sayConsole);
                servitor.curBot.wordNetEngine = wordNetEngine;
                reloadServitor();
            }
            updateRTP2Sevitor();
            try
            {
                //User specific code (RTPUSER -> ALTBOT USER)
                string that = activeUser.getThat(activeUser);
                servitor.curUser.setUserID(activeUser.UserID);
                if (activeUser.Predicates != null)
                    foreach (string key in activeUser.Predicates.Keys)
                    {
                        string v = activeUser.Predicates[key];
                        servitor.curUser.Predicates.updateSetting(key, v);
                        Console.WriteLine("RTP->ALT Predicates[{0}] = {1}", key, v);
                    }

                int rcount = activeUser.SailentResultCount;
                for (int n = 0; n < rcount; n++)
                {

                    Result historicResult = activeUser.GetResult(n, true, null);
                    if (historicResult == null) continue;
                    for (int sent = 0; sent < historicResult.OutputSentenceCount; sent++)
                    {
                        string data = historicResult.GetOutputSentence(sent);
                        servitor.curUser.setOutputSentence(n, sent, data);
                        Console.WriteLine("RTP->ALT setOutputSentence[{0},{1}] = {2}", n, sent, data);
                    }
                }
                // An alternate way is at the request level
                //   Result historicResult = activeUser.GetResult(n, true, activeUser);
                //   AltAIMLbot .Result duplicateResult = new AltAIMLbot.Result (servitor.curUser, servitor .curBot
                //                                              ,servitor.curBot.LastRequest);
            }
            catch (Exception e)
            {
                Console.WriteLine(" **** ERR : {0} {1} ", e.Message , e.StackTrace);
            }

        }
        public void updateRTP2Sevitor()
        {
            if (useServitor == false) return;
            if (servitor==null)
            {
                servitor = new Servitor(this.UserID, null);
                servitor.curBot.sayProcessor = new sayProcessorDelegate(sayConsole);
                servitor.curBot.wordNetEngine = wordNetEngine;
                reloadServitor();


            }
            // fill in the blanks
            servitor.curBot.AdminEmail = this.AdminEmail;
            servitor.curBot.conversationStack = this.conversationStack;
            servitor.curBot.isAcceptingUserInput = this.isAcceptingUserInput;
            servitor.curBot.LastLogMessage = this.LastLogMessage;
            servitor.curBot.MaxThatSize = this.MaxThatSize;
            servitor.curBot.StartedOn = this.StartedOn;
            servitor.curBot.TrustAIML = this.TrustAIML;
            servitor.curBot.StartedOn = this.StartedOn;
            servitor.curBot.GlobalSettings.updateSetting("aimldirectory", PathToAIML);

            if (SharedGlobalSettings !=null) 
            foreach (string key in SharedGlobalSettings.Keys)
            {
                string v = SharedGlobalSettings[key];
                servitor.curBot.GlobalSettings.updateSetting(key, v);
                servitor.setBBHash(key, v);
            }

            if (GlobalSettings != null)
            foreach (string key in GlobalSettings.Keys)
            {
                string v = GlobalSettings[key];
                servitor.curBot.GlobalSettings.updateSetting(key, v);
                servitor.setBBHash(key, v);
            }

            if ((GenderSubstitutions != null) 
                && (servitor.curBot.GenderSubstitutions.Count != GenderSubstitutions.Count))
            foreach (string key in GenderSubstitutions.Keys)
            {
                string v = GenderSubstitutions[key];
                servitor.curBot.GenderSubstitutions.updateSetting(key, v);
            }

            if ((Person2Substitutions != null)
                && (servitor.curBot.Person2Substitutions.Count != Person2Substitutions.Count))
                foreach (string key in Person2Substitutions.Keys)
            {
                string v = Person2Substitutions[key];
                servitor.curBot.Person2Substitutions.updateSetting(key, v);
            }

            if ((PersonSubstitutions != null)
                && (servitor.curBot.PersonSubstitutions.Count != PersonSubstitutions.Count))
                foreach (string key in PersonSubstitutions.Keys)
            {
                string v = PersonSubstitutions[key];
                servitor.curBot.PersonSubstitutions.updateSetting(key, v);
            }

            if ((InputSubstitutions != null)
                && (servitor.curBot.InputSubstitutions.Count != InputSubstitutions.Count))
                foreach (string key in InputSubstitutions.Keys)
            {
                string v = InputSubstitutions[key];
                servitor.curBot.InputSubstitutions.updateSetting(key, v);
            }

            if ((DefaultPredicates != null)
                && (servitor.curBot.DefaultPredicates.Count != DefaultPredicates.Count))
                foreach (string key in DefaultPredicates.Keys)
            {
                string v = DefaultPredicates[key];
                servitor.curBot.DefaultPredicates.updateSetting(key, v);
            }

            
        }
        /// <summary>
        /// Ctor
        /// </summary>
        public RTPBot()
            : base()
        {
            rtpbotcommands = new RTPBotCommands(this);
            qsbase = QuerySettings.CogbotDefaults;
            _RuntimeDirectories = new List<string>();
            PushSearchPath(HostSystem.GetAbsolutePath(AppDomain.CurrentDomain.RelativeSearchPath));
            PushSearchPath(HostSystem.GetAbsolutePath(AppDomain.CurrentDomain.DynamicDirectory));
            PushSearchPath(HostSystem.GetAbsolutePath(AppDomain.CurrentDomain.BaseDirectory));
            PushSearchPath(HostSystem.GetAbsolutePath(Environment.CurrentDirectory));
            PushSearchPath(HostSystem.GetAbsolutePath(_dataDir));
            PushSearchPath(HostSystem.GetAbsolutePath(RuntimeDirectory));
            _dataDir = PushSearchPath(RuntimeDirectory);
            lock (OneAtATime)
            {
                EnsureStaticInit();
                BotNumberCreated++;
                EnsureBotInit(BotNumberCreated == 1);
            }
        }

        public string PopSearchPath(string directory)
        {
            if (directory == null) return null;
            directory = Trim(directory);
            if (directory.Length == 0)
            {
                directory = ".";
            }
            directory = HostSystem.ToCanonicalDirectory(directory);
            lock (_RuntimeDirectories)
            {
                string e = _RuntimeDirectories[0];
                if (e == directory)
                {
                    _RuntimeDirectories.RemoveAt(0);
                    return e;
                }
                bool found = _RuntimeDirectories.Remove(directory);
                return found ? directory : null;
            }
        }

        public string PushSearchPath(string directory)
        {
            if (directory == null) return null;
            directory = Trim(directory);
            if (directory.Length == 0)
            {
                directory = ".";
            }
            directory = HostSystem.ToCanonicalDirectory(directory);
            lock (_RuntimeDirectories)
            {
                bool found = false; // _RuntimeDirectories.Remove(directory);
                _RuntimeDirectories.Insert(0, directory);
                // ReSharper disable ConditionIsAlwaysTrueOrFalse
                return found ? directory : null;
                // ReSharper restore ConditionIsAlwaysTrueOrFalse
            }
        }

        public void EnsureBotInit(bool wasFirst)
        {
            //LocalGraphsByName["default"] =
            //EnsureLocalGraphs();
            TheNLKB = new NatLangDb(this);
            //            BotAsRequestUsed = new AIMLbot.Request("-bank-input-", BotAsUser, this, null);
            AddExcuteHandler("aiml", EvalAIMLHandler);
            AddExcuteHandler("bot", LightWeigthBotDirective);

            testCaseRunner = new TestCaseRunner(null);
            XmlNodeEvaluators.Add(testCaseRunner);

            if (TheCyc == null) TheCyc = new CycDatabase(this);
            CycAccess v = TheCyc.GetCycAccess;


            setup();
            GlobalSettings.IsTraced = true;
        }

        protected bool IsMonoRuntime
        {
            get { return true; }
        }


#if !(NOT_FAKE_LISTENERS)
        public Dictionary<string, object> listeners = new Dictionary<string, object>();

        public RTPBot MyBot
        {
            get { return this; }
        }
#endif

        #region Settings methods

        /// <summary>
        /// Loads AIML from .aiml files into the graphmaster "brain" of the Proccessor
        /// </summary>
        public void loadAIMLFromDefaults()
        {
            if (useServitor)
            {
               // servitor.curBot.loadAIMLFromDefaults();
                return;
            }

        }

        public void loadAIMLFromDefaults0()
        {
            loadConfigs(this, PathToConfigFiles, GetBotRequest("-loadAimlFromDefaults-"));
            loadAIMLAndSettings(HostSystem.Combine(PathToAIML, "shared_aiml"));
        }

        /// <summary>
        /// Loads AIML from .aiml files into the graphmaster "brain" of the Proccessor
        /// </summary>
        public void loadAIMLFromURI(string path, Request request)
        {

            bool prev = request.GraphsAcceptingUserInput;
            LoaderOptions savedOptions = request.LoadOptions;
            try
            {
                request.GraphsAcceptingUserInput = false;
                request.Filename = path;
                LoaderOptions options = request.LoadOptions;
                request.Loader.loadAIMLURI(path, options);
                request.Loader.DumpErrors(DLRConsole.DebugWriteLine, false);
                ReloadHooks.Add(() => request.Loader.loadAIMLURI(path, options));
            }
            finally
            {
                request.GraphsAcceptingUserInput = prev;
                request.LoadOptions = savedOptions;
            }
            if (useServitor)
            {
                if (HostSystem.FileExists(path))
                {
                    servitor.loadAIMLFromFiles(path);
                }
                return;
            }
        }


        /// <summary>
        /// Loads AIML from .aiml files into the graphmaster "brain" of the Proccessor
        /// </summary>
        public void loadAIMLAndSettings(string path)
        {
            Request request = GetBotRequest("-loadAIMLAndSettings-" + path + "-");
            request.LoadingFrom = null;
            bool prev = request.GraphsAcceptingUserInput;
            try
            {
                request.GraphsAcceptingUserInput = false;
                // maybe loads settings files if they are there
                string settings = HostSystem.Combine(path, "Settings.xml");
                if (HostSystem.FileExists(settings)) loadSettingsFile(settings, request);
                if (useServitor)
                {
                    if (HostSystem.FileExists(settings))
                    {
                        servitor.curBot.loadSettings(settings);
                    }
                }
                //loading settings first
                loadConfigs(this, path, request);

                loadAIMLFromURI(path, request);
                if (useServitor)
                {
                    if (HostSystem.FileExists(path))
                    {

                        servitor.loadAIMLFromFiles(path);
                        //return;
                    }
                }
            }
            finally
            {
                request.GraphsAcceptingUserInput = prev;
            }
        }


        internal AIMLLoader GetLoader(Request request)
        {
            RTPBot bot = this;
            AIMLLoader loader = bot.Loader;
            if (!bot.StaticLoader || loader == null)
            {
                loader = new AIMLLoader(bot, request);
            }
            bot.Loader = loader;
            return loader;
        }

        /// <summary>
        /// Allows the Proccessor to load a new XML version of some AIML
        /// </summary>
        /// <param name="newAIML">The XML document containing the AIML</param>
        /// <param name="filename">The originator of the XML document</param>
        public void loadAIMLFromXML(XmlDocument newAIML, LoaderOptions filename, Request request)
        {
            if (useServitor)
            {
                if (HostSystem.FileExists(filename.ToString()))
                {
                    servitor.curBot.loadAIMLFromXML(newAIML, filename.ToString());
                    return;
                }
            }
            bool prev = request.GraphsAcceptingUserInput;
            try
            {
                request.GraphsAcceptingUserInput = false;
                AIMLLoader loader = GetLoader(request);
                loader.loadAIMLNode(newAIML.DocumentElement, filename, request);
            }
            finally
            {
                request.GraphsAcceptingUserInput = prev;
            }
        }

        public SettingsDictionary GetRelationMetaProps()
        {
            return RelationMetaProps;
        }

        /// <summary>
        /// Instantiates the dictionary objects and collections associated with this class
        /// </summary>
        private void setup()
        {
            bool prev = isAcceptingUserInput;
            try
            {
                //isAcceptingUserInput = false;
                RelationMetaProps = new SettingsDictionary("chat.relationprops", this, null);
                RegisterDictionary("meta", RelationMetaProps);
                RegisterDictionary("metaprops", RelationMetaProps);

                GlobalSettings = new SettingsDictionary("bot.globalsettings", this, null);
                GlobalSettings.InsertMetaProvider(GetRelationMetaProps);

                GenderSubstitutions = new SettingsDictionary("nl.substitutions.gender", this, null);
                RegisterSubstitutions("gender", GenderSubstitutions);
                Person2Substitutions = new SettingsDictionary("nl.substitutions.person2", this, null);
                RegisterSubstitutions("person2", Person2Substitutions);
                PersonSubstitutions = new SettingsDictionary("nl.substitutions.person", this, null);
                RegisterSubstitutions("person", PersonSubstitutions);
                InputSubstitutions = new SettingsDictionary("nl.substitutions.input", this, null);
                InputSubstitutions.IsSubsts = true;
                InputSubstitutions.IsTraced = true;
                RegisterSubstitutions("input", InputSubstitutions);
                OutputSubstitutions = new SettingsDictionary("nl.substitutions.output", this, null);
                RegisterSubstitutions("output", OutputSubstitutions);


                //ParentProvider provider = new ParentProvider(() => GlobalSettings);
                DefaultPredicates = new SettingsDictionary("bot.defaultpredicates", this, null);
                DefaultPredicates = new SettingsDictionary("defaults", this, null);
                DefaultPredicates.InsertMetaProvider(GetRelationMetaProps);
                HeardPredicates = new SettingsDictionary("chat.heardpredicates", this, null);
                RegisterDictionary("heard", HeardPredicates);
                AllUserPreds = new SettingsDictionary("bot.alluserpred", this, null);
                RegisterDictionary("predicates", AllUserPreds);
                EnginePreds = AllUserPreds;
                RegisterDictionary("enginepreds", EnginePreds);

                AllUserPreds.InsertMetaProvider(GetRelationMetaProps);


                User guser = ExemplarUser = LastUser = new MasterUser("globalPreds", this);
                lock (microBotUsersLock)
                {
                    BotUsers["globalpreds"] = guser;
                }
                guser.IsRoleAcct = true;
                guser.Predicates.clearSettings();
                guser.Predicates.clearHierarchy();
                guser.Predicates.InsertFallback(() => HeardPredicates);
                guser.Predicates.maskSetting("name");
                ///guser.Predicates.maskSetting("currentaction");
                guser.Predicates.maskSetting("id");

                // try a safe default setting for the settings xml file
                // Checks for some important default settings
                GlobalSettings.IsIdentityReadOnly = false;
                SetSaneGlobals(GlobalSettings);
                string pathToSettings = HostSystem.Combine(RuntimeDirectory,
                                                           HostSystem.Combine("config", "Settings.xml"));
                Request request = GetBotRequest("<!-- Loads settings from: '" + pathToSettings + "' -->");
                loadSettingsFile(pathToSettings, request);
                // RE-Checks for some important default settings
                SetSaneGlobals(GlobalSettings);
                SetupConveration();
                GlobalSettings.IsIdentityReadOnly = true;
            }
            finally
            {
                isAcceptingUserInput = prev;
            }
        }

        /// <summary>
        /// Loads settings based upon the default location of the Settings.xml file
        /// </summary>
        public void loadGlobalBotSettings()
        {
        }

        public void ReloadAll()
        {
            // Setup creates too many things from scratch andis uneeded for refreshing
            //setup();
            var todo = new List<CrossAppDomainDelegate>(ReloadHooks);
            ReloadHooks.Clear();
            foreach (CrossAppDomainDelegate list in todo)
            {
                try
                {
                    list();
                }
                catch (Exception e)
                {
                    writeToLog(e);
                    writeToLog("ReloadAll: " + e);
                }
            }
        }

        // Load the dictionaries for this RTPBot from the various configuration files
        public static void loadConfigs(RTPBot thiz, string pathToSettings, Request request)
        {
            if (!HostSystem.DirExists(pathToSettings))
            {
                thiz.writeToLog("Not loading configs from non-existent dir: " + pathToSettings);
                return;
            }

            var files = new List<string>(HostSystem.GetFiles(pathToSettings, "*.xml"));

            var HostSystemCombine = new Func<string, string, string>((arg1, arg2) =>
                                                                         {
                                                                             if (arg2 == null) return null;
                                                                             string s = HostSystem.Combine(arg1, arg2);
                                                                             int i =
                                                                                 files.RemoveAll(
                                                                                     obj =>
                                                                                     obj.ToLower().Replace("\\", "/").
                                                                                         EndsWith("/" + arg2.ToLower()));
                                                                             if (i == 0)
                                                                             {
                                                                                 return null;
                                                                             }
                                                                             if (i == 1)
                                                                             {
                                                                                 //good
                                                                                 return s;
                                                                             }
                                                                             //not so good
                                                                             return s;
                                                                         });

            SettingsDictionary GlobalSettings = thiz.GlobalSettings;
            GlobalSettings.IsTraced = true;

            if (request == null) request = thiz.GetBotRequest("<!- Loads Configs from: '" + pathToSettings + "' -->");

            // Checks for some important default settings
            GlobalSettings.loadSettings(HostSystemCombine(pathToSettings, "settings.xml"), request);
            GlobalSettings.loadSettings(HostSystemCombine(pathToSettings, "core.xml"), request);
            GlobalSettings.loadSettings(
                HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("programd.startup-file-path")), request);
            thiz.SetSaneGlobals(GlobalSettings);

            // these are ignores
            HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("programd.conf-location.plugins"));
            HostSystemCombine(pathToSettings, "listeners.xml");
            HostSystemCombine(pathToSettings, "log4j.xml");

            thiz.DefaultPredicates.loadSettings(HostSystemCombine(pathToSettings, "predicates.xml"), request);
            thiz.DefaultPredicates.loadSettings(HostSystemCombine(pathToSettings, "properties.xml"), request);


            thiz.Person2Substitutions.loadSettings(
                HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("person2substitutionsfile")), request);
            thiz.PersonSubstitutions.loadSettings(
                HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("personsubstitutionsfile")), request);
            thiz.GenderSubstitutions.loadSettings(
                HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("gendersubstitutionsfile")), request);
            thiz.InputSubstitutions.loadSettings(
                HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("substitutionsfile")), request);
            thiz.Person2Substitutions.IsTraced =
                thiz.PersonSubstitutions.IsTraced =
                thiz.GenderSubstitutions.IsTraced = thiz.InputSubstitutions.IsTraced = false;

            thiz.DefaultPredicates.loadSettings(
                HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("defaultpredicates")), request);

            thiz.InputSubstitutions.loadSettings(
                HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("substitutions")), request);
            thiz.InputSubstitutions.loadSettings(HostSystemCombine(pathToSettings, "substitutions.xml"), request);
            thiz.InputSubstitutions.IsTraced = true;

            // Grab the splitters for this Proccessor
            thiz.loadSplitters(HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("splittersfile")));
            thiz.loadSplitters(HostSystemCombine(pathToSettings, GlobalSettings.grabSetting("sentence-splitters")));
            thiz.loadSplitters(HostSystemCombine(pathToSettings, "sentence-splitters.xml"));

            // genformat.xml
            thiz.RelationMetaProps.loadSettings(HostSystemCombine(pathToSettings, "genformat.xml"), request);


            User guser = thiz.FindUser("globalPreds");
            SettingsDictionary.loadSettings(guser.Predicates, HostSystemCombine(pathToSettings, "globalpreds.xml"),
                                            true, false, request);
            thiz.writeToLog("Files left to process = " + files.Count);
            foreach (string list in files)
            {
                writeDebugLine("AIMLLOADER: loadSettings " + list);
                GlobalSettings.IsTraced = true;
                GlobalSettings.loadSettings(list, request);
            }
        }

        /// <summary>
        /// Loads settings and configuration info from various xml files referenced in the settings file passed in the args. 
        /// Also generates some default values if such values have not been set by the settings file.
        /// </summary>
        /// <param name="pathToSettings">Path to the settings xml file</param>
        public void loadSettingsFile(string pathToSettings, Request request)
        {
            if (request == null) request = GetBotRequest("<!-- Loads settings from: '" + pathToSettings + "' -->");
            ReloadHooks.Add(() => loadSettingsFile(pathToSettings, request));
            GlobalSettings.loadSettings(pathToSettings, request);
        }

        private void SetSaneGlobals(ISettingsDictionary settings)
        {
            SaneLocalSettings(settings, "notopic", Unifiable.EnglishNothing);
            SaneLocalSettings(settings, "version", Environment.Version.ToString());
            SaneLocalSettings(settings, "name", "Unknown");
            SaneLocalSettings(settings, "botmaster", "Unknown");
            SaneLocalSettings(settings, "author", "Nicholas H.Tollervey");
            SaneLocalSettings(settings, "location", "Unknown");
            SaneLocalSettings(settings, "gender", "-1");
            SaneLocalSettings(settings, "birthday", "2006/11/08");
            SaneLocalSettings(settings, "birthplace", "Towcester, Northamptonshire, UK");
            SaneLocalSettings(settings, "website", "http://sourceforge.net/projects/aimlbot");
            AdminEmail = SaneLocalSettings(settings, "adminemail", "");
            SaneLocalSettings(settings, "islogging", "False");
            SaneLocalSettings(settings, "willcallhome", "False");
            SaneLocalSettings(settings, "timeout", "5000");
            SaneLocalSettings(settings, "timeoutmessage", "ERROR: The request has timed out.");
            SaneLocalSettings(settings, "culture", "en-US");
            SaneLocalSettings(settings, "splittersfile", "Splitters.xml");
            SaneLocalSettings(settings, "person2substitutionsfile", "Person2Substitutions.xml");
            SaneLocalSettings(settings, "personsubstitutionsfile", "PersonSubstitutions.xml");
            SaneLocalSettings(settings, "gendersubstitutionsfile", "GenderSubstitutions.xml");
            SaneLocalSettings(settings, "defaultpredicates", "DefaultPredicates.xml");
            SaneLocalSettings(settings, "substitutionsfile", "Substitutions.xml");
            SaneLocalSettings(settings, "aimldirectory", "aiml");
            SaneLocalSettings(settings, "configdirectory", "config");
            SaneLocalSettings(settings, "logdirectory", "logs");
            SaneLocalSettings(settings, "maxlogbuffersize", "64");
            SaneLocalSettings(settings, "notacceptinguserinputmessage",
                              "This Proccessor is currently set to not accept user input.");
            SaneLocalSettings(settings, "stripperregex", "[^0-9a-zA-Z]");

            SaneLocalSettings(settings, "systemlang", "bot");
            SaneLocalSettings(settings, "interp", "cloj");
        }

        internal static Unifiable SaneLocalSettings(ISettingsDictionary settings, string name, object value)
        {
            if (!settings.containsLocalCalled(name))
            {
                Unifiable sane = Unifiable.Create(value);
                settings.addSetting(name, sane);
                return sane;
            }
            Unifiable res = settings.grabSetting(name);
            return res;
        }

        /// <summary>
        /// Loads the splitters for this Proccessor from the supplied config file (or sets up some safe defaults)
        /// </summary>
        /// <param name="pathToSplitters">Path to the config file</param>
        private void loadSplitters(string pathToSplitters)
        {
            if (DontUseSplitters) return;
            if (HostSystem.FileExists(pathToSplitters))
            {
                XmlDocumentLineInfo splittersXmlDoc = new XmlDocumentLineInfo(pathToSplitters, true);
                Stream stream = HostSystem.OpenRead(pathToSplitters);
                try
                {
                    splittersXmlDoc.Load(stream);
                }
                finally
                {
                    HostSystem.Close(stream);
                }

                // the XML should have an XML declaration like this:
                // <?xml version="1.0" encoding="utf-8" ?> 
                // followed by a <root> tag with children of the form:
                // <item value="value"/>
                if (splittersXmlDoc.ChildNodes.Count == 2)
                {
                    if (splittersXmlDoc.LastChild.HasChildNodes)
                    {
                        foreach (XmlNode myNode in splittersXmlDoc.LastChild.ChildNodes)
                        {
                            if ((myNode.Name == "item") & (myNode.Attributes.Count == 1))
                            {
                                Unifiable value = Unifiable.Create(myNode.Attributes["value"].Value);
                                Splitters.Add(value);
                            }
                        }
                    }
                }
            }
            if (Splitters.Count == 0)
            {
                // if we process lisp and other things
                if (true) return;
                // we don't have any splitters, so lets make do with these...
                Splitters.Add(".");
                Splitters.Add("!");
                //this.Splitters.Add("?");
                Splitters.Add(";");
            }
        }

        #endregion

        // Persistent user tracking
        public readonly Dictionary<string, User> BotUsers = new Dictionary<string, User>();

        public void SetChatOnOff(string username, bool value)
        {
            lock (microBotUsersLock)
            {
                foreach (User u in BotUsers.Values)
                {
                    if (u.UserID.Contains(username) || username.Contains(u.UserID))
                        u.RespondToChat = value;
                }
            }
        }

        public ICollection<User> SetOfUsers
        {
            get
            {
                List<User> list = new List<User>();
                lock (BotUsers) foreach (var user in BotUsers.Values)
                    {
                        if (list.Contains(user)) continue;
                        list.Add(user);
                    }
                return list;
            }
        }

        public void AddAiml(string aimlText)
        {
            AddAiml(DefaultStartGraph, aimlText);
        }

        public void AddAiml(GraphMaster graph, string aimlText)
        {
            AddAiml(graph, aimlText, GetBotRequest("AddAiml into '" + graph + "' '" + aimlText + "'"));
        }

        public void AddAiml(GraphMaster graph, string aimlText, Request request)
        {
            GraphMaster prev = request.Graph;
            try
            {
                request.Graph = graph;
                LoaderOptions loader = request.LoadOptions.Value; // LoaderOptions.GetDefault(request);
                loader.CtxGraph = graph;
                loader.Loading0 = "from_text";
                string s = string.Format("<aiml graph=\"{0}\">{1}</aiml>", graph.ScriptingName, aimlText);
                request.Loader.loadAIMLString(s, loader);
            }
            catch (Exception e)
            {
                writeDebugLine("" + e);
                writeChatTrace("" + e);
                writeToLog(e);
                throw e;
            }
            finally
            {
                request.Graph = prev;
            }
        }


        public IEnumerable<XmlNode> EvalAiml(XmlNode currentNode, Request request, OutputDelegate del)
        {
            var nodes = new HashSet<XmlNode>();
            bool evaledNode = false;
            del = del ?? request.WriteLine;
            var getEvaluators = GetEvaluators(currentNode);
            foreach (XmlNodeEval funct in getEvaluators)
            {
                evaledNode = true;
                var newNode = funct(currentNode, request, del);
                if (newNode != null)
                {
                    evaledNode = true;
                    foreach (XmlNode node in newNode)
                    {
                        nodes.Add(node);
                    }
                }
            }
            if (evaledNode)
            {
                del("evaledNode=" + evaledNode);
                del("nodes.Count=" + nodes.Count);
                int nc = 1;
                foreach (XmlNode n in nodes)
                {
                    del("node {0}:{1}", nc, n);
                    nc++;
                }
                return nodes;
            }
            return XmlNodeEvaluatorImpl.NO_XmlNode;
        }

        #region Serialization

        /// <summary>
        /// Loads a dump of all graphmaster into memory so avoiding processing the AIML files again
        /// </summary>
        /// <param name="path">the path to the dump file</param>
        public void ScanAndLoadGraphs()
        {
            loadFromBinaryFile(GraphsSaveDir);
        }
        /// <summary>
        /// Saves the graphmaster node (and children) to a binary file to avoid processing the AIML each time the 
        /// Proccessor starts
        /// </summary>
        /// <param name="path">the path to the file for saving</param>
        public void SaveLoadedGraphs()
        {
            saveToBinaryFile(GraphsSaveDir);
        }

        private static string GraphsSaveDir
        {
            get { return "graphbins"; }
        }
        /// <summary>
        /// Saves the graphmaster node (and children) to a binary file to avoid processing the AIML each time the 
        /// Proccessor starts
        /// </summary>
        /// <param name="path">the path to the file for saving</param>
        public void saveToBinaryFile(string path)
        {
            BinaryFormatter bf = Unifiable.GetBinaryFormatter();
            string binext = ".gfxbin";
            string localdir = Path.Combine(path, NamePath);
            Unifiable.SaveUnifiables(Path.Combine(path, "unifiables"), bf);
            foreach (var name in SetOfGraphs)
            {
                bf = Unifiable.GetBinaryFormatter();
                name.saveToBinaryFile(Path.Combine(path, name.ScriptingName + binext), bf);
            }
            if (!Directory.Exists(localdir)) Directory.CreateDirectory(localdir);
            foreach (var name in SetOfLocalGraphs)
            {
                name.saveToBinaryFile(Path.Combine(localdir, name.ScriptingName + binext), bf);
            }
        }

        /// <summary>
        /// Loads a dump of the graphmaster into memory so avoiding processing the AIML files again
        /// </summary>
        /// <param name="path">the path to the dump file</param>
        public void loadFromBinaryFile(string path)
        {
            BinaryFormatter bf = Unifiable.GetBinaryFormatter();
            string binext = ".gfxbin";
            string localdir = Path.Combine(path, NamePath);
            Unifiable.LoadUnifiables(Path.Combine(path, "unifiables"), bf);
            foreach (string s in Directory.GetFiles(path, "*" + binext))
            {
                var graphname = Path.GetFileNameWithoutExtension(s).ToLower();
                var G = GetGraph(graphname, null);
                G.loadFromBinaryFile(s, bf);
                foreach (string gn in G.GraphNames)
                {
                    GraphsByName[gn] = G;
                }
            }
            if (Directory.Exists(localdir))
            {
                foreach (string s in Directory.GetFiles(localdir, "*" + binext))
                {
                    var graphname = Path.GetFileNameWithoutExtension(s).ToLower();
                    var G = GetGraph(graphname, null);
                    G.loadFromBinaryFile(s, bf);
                    foreach (string gn in G.GraphNames)
                    {
                        LocalGraphsByName[gn] = G;
                    }
                }
            }
        }

        #endregion


        #region Phone Home

        /// <summary>
        /// Attempts to send an email to the botmaster at the AdminEmail address setting with error messages
        /// resulting from a query to the Proccessor
        /// </summary>
        /// <param name="errorMessage">the resulting error message</param>
        /// <param name="request">the request object that encapsulates all sorts of useful information</param>
        public void phoneHome(Unifiable errorMessage, Request request)
        {
            if (AdminEmail == "")
            {
                return;
            }
            MailMessage msg = new MailMessage("donotreply@aimlbot.com", AdminEmail);
            msg.Subject = "WARNING! AIMLBot has encountered a problem...";
            string message =
                @"Dear Botmaster,

This is an automatically generated email to report errors with your Proccessor.

At *TIME* the Proccessor encountered the following error:

""*MESSAGE*""

whilst processing the following input:

""*RAWINPUT*""

from the user with an id of: *USER*

The normalized paths generated by the raw input were as follows:

*PATHS*

Please check your AIML!

Regards,

The AIMLbot program.
";
            message = message.Replace("*TIME*", DateTime.Now.ToString());
            message = message.Replace("*MESSAGE*", errorMessage);
            message = message.Replace("*RAWINPUT*", request.rawInput);
            message = message.Replace("*USER*", request.Requester.UserID);
            StringAppendableUnifiableImpl paths = Unifiable.CreateAppendable();
            foreach (Unifiable path in request.CurrentResult.InputPaths)
            {
                paths.Append(path.LegacyPath + Environment.NewLine);
            }
            message = message.Replace("*PATHS*", Unifiable.ToVMString(paths));
            msg.Body = message;
            msg.IsBodyHtml = false;
            try
            {
                if (msg.To.Count > 0)
                {
                    SmtpClient client = new SmtpClient();
                    client.Send(msg);
                }
            }
            catch
            {
                // if we get here then we can't really do much more
            }
        }

        #endregion


        internal readonly Dictionary<string, SystemExecHandler> ExecuteHandlers =
            new Dictionary<string, SystemExecHandler>();

        public void AddExcuteHandler(string lang, SystemExecHandler handler)
        {
            lang = ToLower(Trim(lang));
            lock (ExecuteHandlers) ExecuteHandlers[lang] = handler;
        }


        /// <summary>
        /// Returns the value of a setting given the name of the setting
        /// </summary>
        /// <param name="name">the name of the setting whose value we're interested in</param>
        /// <returns>the value of the setting</returns>
        public Unifiable GetBotSetting(Unifiable name)
        {
            return GlobalSettings.grabSetting(name);
        }

        public Unifiable NOTOPIC
        {
            get
            {
                if (!GlobalSettings.containsSettingCalled("notopic")) return Unifiable.EnglishNothing;
                return GlobalSettings.grabSettingNoDebug("notopic");
            }
        }


        static public IEnumerable<GraphMaster> SetOfGraphs
        {
            get
            {
                lock (RTPBot.GraphsByName) return new ListAsSet<GraphMaster>(GraphMaster.CopyOf(RTPBot.GraphsByName).Values);
            }
        }

        public IEnumerable<GraphMaster> SetOfLocalGraphs
        {
            get
            {
                lock (LocalGraphsByName) return new ListAsSet<GraphMaster>(GraphMaster.CopyOf(LocalGraphsByName).Values);
            }
        }


        public static Dictionary<string, GraphMaster> GraphsByName = new Dictionary<string, GraphMaster>();
        public Dictionary<string, GraphMaster> LocalGraphsByName = new Dictionary<string, GraphMaster>();
        public CycDatabase TheCyc;
        public NatLangDb TheNLKB;
        public bool UseInlineThat = true;

        public bool CycEnabled
        {
            get { return TheCyc.CycEnabled; }
            set { TheCyc.CycEnabled = value; }
        }

        public static bool SaveProofs;

        public GraphMaster GetUserGraph(string graphPath)
        {
            //graphPath = "default";
            if (!graphPath.Contains("_to_"))
            {
                graphPath = ToLower(ConsolidSpaces(Trim(graphPath + "_to_" + this.NamePath)));
            }
            GraphMaster g;
            lock (GraphsByName)
            {
                if (LocalGraphsByName.TryGetValue(graphPath, out g))
                {
                    return g;
                }
                g = GraphsByName[graphPath] = GraphMaster.FindOrCreate(graphPath);
                GraphMaster dtob = Utils.GraphMaster.FindOrCreate("default_to_" + this.NamePath);
                g.AddGenlMT(dtob, writeToLog);
                //ㄴdtob.AddGenlMT(Utils.GraphMaster.FindOrCreate("default"), writeToLog);
            } 
            return g;
        }

        static public GraphMaster FindGraph(string graphPath)
        {
            GraphMaster g;
            lock (GraphsByName) GraphsByName.TryGetValue(graphPath, out g);
            return g;
        }

        public GraphMaster GetGraph(string graphPath, GraphMaster current)
        {
            GraphMaster g = FindGraph(graphPath, current);
            if (g != null) return g;
            if (graphPath == null)
            {
                if (current == null)
                {
                }
                return current;
            }

            string lower = graphPath.ToLower();
            int graphPathLength = graphPath.IndexOf(".");
            if (graphPathLength > 0)
            {
                string sv = graphPath.Substring(0, graphPathLength);
                string left = graphPath.Substring(graphPathLength + 1);
                var vg = GetGraph(sv, current);
                return GetGraph(left, vg);
            }

            graphPath = ToScriptableName(graphPath);
            lock (GraphsByName)
            {
                if (LocalGraphsByName.TryGetValue(graphPath, out g))
                {
                    return g;
                }
                if (!GraphsByName.TryGetValue(graphPath, out g))
                {
                    g = GraphsByName[graphPath] = GraphMaster.FindOrCreate(graphPath);
                }
            }
            return g;
        }

        public GraphMaster FindGraph(string graphPath, GraphMaster current)
        {
            if (graphPath == null)
            {
                return current;
            }

            string lower = graphPath.ToLower();
            int graphPathLength = graphPath.IndexOf(".");
            if (graphPathLength > 0)
            {
                string sv = graphPath.Substring(0, graphPathLength);
                string left = graphPath.Substring(graphPathLength + 1);
                var vg = FindGraph(sv, current);
                return FindGraph(left, vg);
            }

            graphPath = ToScriptableName(graphPath);

            if (graphPath == "current" || graphPath == "")
            {
                return current;
            }

            if (true)
            {
                if (_g != null && graphPath == "default")
                {
                    return DefaultStartGraph;
                }

                if (_h != null && graphPath == "heardselfsay")
                {
                    return DefaultHeardSelfSayGraph;
                }
            }
            if (graphPath == "parent" || graphPath == "parallel")
            {
                if (current == null) return null;
                return current.Parallel;
            }

            GraphMaster g;
            lock (GraphsByName)
            {
                if (LocalGraphsByName.TryGetValue(graphPath, out g))
                {
                    return g;
                }
                if (!GraphsByName.TryGetValue(graphPath, out g))
                {
                    return null;
                }
            }
            return g;
        }

        public static string ToScriptableName(string path)
        {
            string sk = "";
            if (path.StartsWith("is_")) path = path.Substring(3);
            if (path.StartsWith("was_")) path = path.Substring(4);
            foreach (char s in path)
            {
                if (IsOkForNameChar(s))
                    sk += s;
            }
            path = OlderReference(path, sk);
            return NoSpaceLowerCaseName(path);
        }

        public static int DivideString(string args, string sep, out string left, out string right)
        {
            if (args == null)
            {
                left = "";
                right = null;
                return 0;
            }
            args = args.Trim();
            if (args.Length == 0)
            {
                left = args;
                right = null;
                return 1;
            }
            int lastIndex = args.IndexOf(sep);
            if (lastIndex == -1)
            {
                left = args;
                right = null;
                return 1;
            }
            int seplen = sep.Length;
            left = Trim(args.Substring(0, lastIndex));
            right = Trim(args.Substring(lastIndex + seplen));
            if (right.Length == 0) return 1;
            return 2;
        }

        public string GetUserMt(User user, SubQuery subquery)
        {
            Unifiable ret = user.Predicates.grabSettingNoDebug("mt");
            if (!IsNullOrEmpty(ret))
            {
                string v = ret.ToValue(subquery);
                if (v != null && v.Length > 1) return TheCyc.Cyclify(v);
            }
            //GetAttribValue("mt","");
            return "#$BaseKB";
        }

        public void WriteConfig()
        {
            lock (BotUsers) ///lock (OnBotCreatedHooks)
            {
                TheCyc.WriteConfig();
                DefaultStartGraph.WriteConfig();
                writeDebugLine("Bot loaded");
                saveServitor();
            }
        }

        public string LoadPersonalDirectory(string myName)
        {
            return LoadPersonalDirectory0(myName);
            //return UserOper(() => LoadPersonalDirectory0(myName), QuietLogger);
        }

        private string LoadPersonalDirectory0(string myName)
        {
            ReloadHooks.Add(() => LoadPersonalDirectory(myName));
            string loaded = null;

            // this is the personal "config file" only.. aiml stored elsewhere
            string file = HostSystem.Combine("config", myName);
            Request request = GetBotRequest("loading personal directory " + myName);
            if (HostSystem.DirExists(file))
            {
                loaded = file;
                loadSettingsFileAndDir(HostSystem.Combine(file, "Settings.xml"), request);
            }

            file = HostSystem.Combine("aiml", myName);
            if (HostSystem.DirExists(file))
            {
                UsePersonalDir(file);
                ;
                loaded = file;
            }

            // this is the personal "config file" only.. aiml stored elsewhere
            file = HostSystem.Combine(myName, "config");
            if (HostSystem.DirExists(file))
            {
                loaded = file;
                loadSettingsFileAndDir(HostSystem.Combine(file, "Settings.xml"), request);
            }

            file = HostSystem.Combine(myName, "aiml");
            if (HostSystem.DirExists(file))
            {
                UsePersonalDir(file);
                ;
                loaded = file;
            }
            return loaded;
        }

        private void loadSettingsFileAndDir(string file, Request request)
        {
            writeToLog("LoadPersonalDirectories: '{0}'", file);
            loadSettingsFile(HostSystem.Combine(file, "Settings.xml"), request);
            loadConfigs(this, file, request);
        }

        public void UsePersonalDir(string file)
        {
            //lock (BotUsers) lock (OnBotCreatedHooks) 
            UsePersonalDir0(file);
        }

        private void UsePersonalDir0(string file)
        {
            if (!HostSystem.DirExists(file))
            {
                writeToLog("ERROR - cannot use non existent personal dir = " + file);
                return;
            }
            PushSearchPath(file);
            _PathToBotPersonalFiles = file;
            string s = string.Format("-LoadPersonalDirectories: '{0}'-", file);
            Request request = GetBotRequest(s);
            request.LoadingFrom = file;
            writeToLog(s);
            bool prev = request.GraphsAcceptingUserInput;
            try
            {
                // loading of personal configs must be done before and after the AIML files
                loadConfigs(this, file, request);
                request.GraphsAcceptingUserInput = false;
                loadAIMLFromURI(file, request);
                foreach (string s1 in HostSystem.GetFiles(file, "Settings*.xml"))
                {
                    loadSettingsFile(s1, request);
                }
                loadConfigs(this, file, request);
                lock (RuntimeDirectoriesLock)
                {
                    _RuntimeDirectories = RuntimeDirectories;
                }
            }
            finally
            {
                request.GraphsAcceptingUserInput = prev;
            }
            if (useServitor)
            {
                saveServitor();
            }
        }

        public string SetName(string myName)
        {
            lock (OnBotCreatedHooks)
            {
                return SetName0(myName);
                //return UserOper(() => SetName0(myName), writeDebugLine);
            }
        }

        private string SetName0(string myName)
        {
            //char s1 = myName[1];
            Robots[myName] = this;
            NameAsSet = myName;
            //new AIMLbot.User("heardselfsay", this)
            var thisBotAsUser = FindOrCreateUser(myName);
            this.BotAsUser = thisBotAsUser;
            if (useServitor) { updateRTP2Sevitor(); }

            ExternalIntern("BotAsUser", thisBotAsUser);
            thisBotAsUser.IsRoleAcct = true;
            SharedGlobalSettings = this.GlobalSettings;
            thisBotAsUser.Predicates = new SettingsDictionary(myName, this, () => SharedGlobalSettings);
            thisBotAsUser.Predicates.InsertFallback(() => AllUserPreds);
            AllUserPreds.InsertFallback(() => SharedGlobalSettings);

            GlobalSettings.IsTraced = true;
            GlobalSettings = thisBotAsUser.Predicates;
            //BotAsUser.UserDirectory = "aiml/users/heardselfsay";
            //BotAsUser.UserID = "heardselfsay";
            //BotAsUser.UserName = "heardselfsay";
            //BotUsers["heardselfsay"] = BotAsUser;            
            thisBotAsUser.UserName = myName;
            AllDictionaries["bot"] = thisBotAsUser.Predicates;
            thisBotAsUser.removeSetting("userdir");
            NamePath = ToScriptableName(NameAsSet);
            thisBotAsUser.UserID = NamePath;
            this.StartHttpServer();
            SetupExecHandlers();

            //var OnTaskAtATimeHandler = HeardSelfSayQueue = thisBotAsUser.OnTaskAtATimeHandler;
            //OnTaskAtATimeHandler.Name = "TaskQueue For " + myName;

            //thisBotAsUser.SaveDirectory(thisBotAsUser.UserDirectory);
            string dgn = "default_to_" + NamePath;
            string n2n = NamePath + "_to_" + NamePath;
            string hgn = "heardselfsay_to_" + NamePath;
            lock (GraphsByName)
            {
                if (String.IsNullOrEmpty(NamePath))
                {
                    throw new NullReferenceException("SetName! = " + myName);
                }
                if (_g == null)
                {
                    GraphMaster od;
                    GraphsByName.TryGetValue("default", out od);
                    _g = GraphMaster.FindOrCreate(dgn);
                    if (od == null) GraphsByName["default"] = _g;
                    else _g.AddGenlMT(od, writeToLog);
                    _h //= TheUserListernerGraph 
                        = new GraphMaster(hgn);
                    GraphsByName[n2n] = _h;
                    _h.AddGenlMT(GraphsByName["heardselfsay"], writeToLog);
                    _h.AddGenlMT(GraphsByName["listener"], writeToLog);
                    GraphsByName[dgn] = _g;
                    GraphsByName[hgn] = _h;
                }

                GraphsByName[n2n].RemoveGenlMT(GraphsByName[dgn], writeToLog);
            }
            GraphMaster listeningGraph = DefaultHeardSelfSayGraph;
            if (listeningGraph != null) BotAsUser.HeardSelfSayGraph = listeningGraph;
            lock (OnBotCreatedHooks)
            {
                foreach (Action list in OnBotCreatedHooks)
                {
                    try
                    {
                        list();
                    }
                    catch (Exception e)
                    {
                        writeDebugLine("OnBotCreatedHooks ERROR: " + list);
                    }
                }
                OnBotCreatedHooks.Clear();
            }
            loadAIMLFromDefaults0();
            EnsureDefaultUsers();
            string official = LoadPersonalDirectories(myName);
            thisBotAsUser.SaveDirectory(thisBotAsUser.UserDirectory);
            AddExcuteHandler(NamePath, ChatWithThisBot);
            return official ?? thisBotAsUser.UserDirectory;
        }

        public static bool StaticInitStarted;
        public static object OneAtATime = new object();

        private static void EnsureStaticInit()
        {
            lock (OneAtATime)
            {
                var tc = DLRConsole.TransparentCallers;
                lock (tc)
                {
                    tc.Add(typeof(RTPBot));
                    tc.Add(typeof(AIMLbot.MasterRequest));
                    // ReSharper disable AssignNullToNotNullAttribute
                    tc.Add(typeof(MasterResult).BaseType);
                    // ReSharper restore AssignNullToNotNullAttribute
                    tc.Add(typeof(Request));
                }

                TagHandlerProcessor.InitTagHandlers();

                if (StaticInitStarted) return;
                StaticInitStarted = true;
                GraphsByName["listener"] = TheUserListenerGraph = GraphMaster.FindOrCreate("listener");
                TheUserListenerGraph.SilentTagsInPutParallel = false;
                // var defaultGraph = GraphsByName["default"] = GraphMaster.FindOrCreate("default");
                // defaultGraph.RemovePreviousTemplatesFromNodes = false;
                GraphsByName["heardselfsay"] = TheUserListenerGraph;////new GraphMaster("heardselfsay");
                AddSettingsAliases("lastuserid", "you");
                AddSettingsAliases("lastusername", "you");
                AddSettingsAliases("you", "lastusername");
                AddSettingsAliases("he", "him");
                AddSettingsAliases("she", "her");
            }
        }

        private static void AddSettingsAliases(string real, string aliases)
        {
            SettingsAliases.Add(real, aliases.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries));
        }

        public string LoadPersonalDirectories(string myName)
        {
            return LoadPersonalDirectories0(myName);
        }

        public string LoadPersonalDirectories0(string myName)
        {
            string loaded = LoadPersonalDirectory(myName);
            if (string.IsNullOrEmpty(loaded))
            {
                myName = ToScriptableName(myName);
                loaded = LoadPersonalDirectory(myName);
            }
            if (string.IsNullOrEmpty(loaded))
            {
                myName = "default_bot";
                loaded = LoadPersonalDirectory(myName);
            }
            if (string.IsNullOrEmpty(loaded))
            {
                writeToLog("Didnt find personal directories with stem: '{0}'", myName);
            }
            return loaded;
        }

        readonly public static OutputDelegate writeDebugLine = writeDebugLine_0_;
        internal static void writeDebugLine_0_(string message, params object[] args)
        {
            bool printIt = false;
            lock (LoggedWords)
            {
                printIt = LoggedWords.writeDebugLine(DLRConsole.SystemWriteLine, message, args);
            }
            //
            {
                try
                {
                    bool wasStopped = true;
                    string real = SafeFormat(message, args);
                    message = real.ToUpper();
                    if (message.Contains("ERROR") && !message.Contains("TIMEOUTMESSAGE"))
                    {
                        wasStopped = Breakpoint(real);
                    }
                    else if (message.Contains("EXCEPTION"))
                    {
                        wasStopped = Breakpoint(real);
                    }
                    if (!printIt)
                    {
                        if (!wasStopped)
                        {
                            DLRConsole.DebugWriteLine(real);
                            return;
                        }
                        UnseenWriteline(real);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        public static bool Breakpoint(string err)
        {
            if (skipMany > 0)
            {
                skipMany--;
                return false;
            }
            DLRConsole.SystemWriteLine("" + err);
            if (!UseBreakpointOnError)
            {
                return false;
            }
            DLRConsole.SystemWriteLine("press enter of enter a number to skip breakpoints");
            string p = DLRConsole.ReadLine();
            int skipNext;
            if (int.TryParse(p, out skipNext))
            {
                skipMany = skipNext;
            }
            return true;
        }

        public bool SameUser(string old, string next)
        {
            old = old ?? "";
            next = next ?? "";
            old = Trim(ToLower(old));
            next = Trim(ToLower(next));
            return FindUser0(old) == FindUser0(next);
        }

        private List<string> _RuntimeDirectories;
        ICollectionRequester _objr;
        private readonly List<Action> PostObjectRequesterSet = new List<Action>();
        public ICollectionRequester ObjectRequester
        {
            get
            {
                return _objr;
            }
            set
            {
                _objr = value;
                if (value != null)
                {
                   lock(PostObjectRequesterSet)
                   {
                       foreach (var set in PostObjectRequesterSet)
                       {
                           set();
                       }
                       PostObjectRequesterSet.Clear();
                   }
                }
            }
        }

        #region Overrides of QuerySettings

        /*
        /// <summary>
        /// The Graph to start the query on
        /// </summary>
        public override string GraphName
        {
            get { return GraphMaster.ScriptingName; }
            set { throw new NotImplementedException(); }
        }
        */
        public string UserID
        {
            get
            {
                if (BotAsUser != null) return BotAsUser.UserID;
                SettingsDictionary dict = GlobalSettings;
                if (dict != null)
                {
                    Unifiable botid = dict.grabSettingNoDebug("id");
                    return botid;
                }
                return null;
            }
        }

        public string BotID
        {
            get
            {
                if (BotAsUser != null) return BotAsUser.UserID;
                return UserID ?? "-BOT-ID-NULL-";
            }
            set { throw new NotImplementedException(); }
        }

        public ISettingsDictionary Predicates
        {
            get { return GlobalSettings; }
        }

        #endregion

        public ISettingsDictionary GetDictionary(string name)
        {
            var idict = GetDictionary0(name);
            if (idict!=null) return idict;
            var rtpbotobjCol = ScriptManager.ResolveToObject(this, name);
            if (rtpbotobjCol == null || rtpbotobjCol.Count == 0)
            {
                return null;
            }
            //if (tr)
            foreach (object o in rtpbotobjCol)
            {
                ParentProvider pp = o as ParentProvider;
                ISettingsDictionary pi = o as ISettingsDictionary;
                User pu = o as User;
                if (pp != null)
                {
                    pi = pp();
                }
                if (pi != null)
                {
                    return pi;
                }
                if (pu != null)
                {
                    return pu;
                }
            }
            return null;
        }

        public ISettingsDictionary GetDictionary0(string named)
        {
            Func<ISettingsDictionary, SettingsDictionary> SDCAST = SettingsDictionary.ToSettingsDictionary;
            //dict = FindDict(type, query, dict);
            if (named == null) return null;
            string key = named.ToLower().Trim();
            if (key == "") return null;
            lock (AllDictionaries)
            {
                ISettingsDictionary dict;
                if (AllDictionaries.TryGetValue(key, out dict))
                {
                    return dict;
                }
            }
            if (key == "predicates")
            {
                return SDCAST(this.AllUserPreds);
            }
            // try to use a global blackboard predicate
            User gUser = ExemplarUser;
            if (key == "globalpreds") return SDCAST(gUser);
            if (key == "allusers") return SDCAST(AllUserPreds);
            var path = named.Split(new[] { '.' });
            if (path.Length == 1)
            {
                User user = FindUser(key);
                if (user != null) return user;
            }
            else
            {
                if (path[0] == "bot" || path[0] == "users" || path[0] == "char" || path[0] == "nl")
                {
                    ISettingsDictionary f = GetDictionary(string.Join(".", path, 1, path.Length - 1));
                    if (f != null) return SDCAST(f);
                }
                if (path[0] == "substitutions")
                {
                    ISettingsDictionary f = GetDictionary(string.Join(".", path, 1, path.Length - 1), "substitutions",
                                                          true);
                    if (f != null) return SDCAST(f);
                }
                else
                {
                    ISettingsDictionary f = GetDictionary(path[0]);
                    if (f != null)
                    {
                        SettingsDictionary sd = SDCAST(f);
                        ParentProvider pp = sd.FindDictionary(string.Join(".", path, 1, path.Length - 1), null);
                        if (pp != null)
                        {
                            ISettingsDictionary pi = pp();
                            if (pi != null) return SDCAST(pi);
                        }
                    }
                }
            }
            return null;
        }

        public ISettingsDictionary GetDictionary(string named, string type, bool createIfMissing)
        {
            lock (AllDictionaries)
            {
                string key = (type + "." + named).ToLower();
                ISettingsDictionary dict;
                if (!AllDictionaries.TryGetValue(key, out dict))
                {
                    ISettingsDictionary sdict = GetDictionary(named);
                    if (sdict != null) return sdict;
                    if (createIfMissing)
                    {
                        dict = AllDictionaries[key] = AllDictionaries[named] = new SettingsDictionary(named, this, null);
                        User user = ExemplarUser ?? BotAsUser;
                        Request r = //user.CurrentRequest ??
                                    user.CreateRequest(
                                        "@echo <!-- loadDictionary '" + named + "' from '" + type + "' -->", Unifiable.EnglishNothing, BotAsUser);
                        loadDictionary(dict, named, type, r);
                    }
                }
                return dict;
            }
        }

        private void loadDictionary(ISettingsDictionary dictionary, string path, string type, Request r0)
        {
            User user = LastUser ?? 
                ExemplarUser ?? BotAsUser;
            Request r = r0 ??
                        //user.CurrentRequest ??
                        user.CreateRequest(
                            "@echo <!-- loadDictionary '" + dictionary + "' from '" + type + "' -->", Unifiable.EnglishNothing,
                            BotAsUser);
            int loaded = 0;
            foreach (string p in GetSearchRoots(r))
            {
                foreach (string s0 in new[] { "", type, type + "s", })
                {
                    foreach (string s1 in new[] { "", "." + type, ".xml", ".subst", ".properties", })
                    {
                        string named = HostSystem.Combine(p, path + s0 + s1);
                        if (HostSystem.FileExists(named))
                        {
                            try
                            {
                                SettingsDictionary.loadSettings(dictionary, named, true, false, r);
                                loaded++;
                                break;
                            }
                            catch (Exception e)
                            {
                                writeToLog("ERROR {0}", e);
                                //continue;
                                throw;
                            }
                        }
                    }
                }
                if (loaded > 0) return;
            }
            if (loaded == 0)
            {
                writeToLog("WARNING: Cannot find " + path + " for " + type);
            }
        }

        public void RegisterDictionary(ISettingsDictionary dict)
        {
            RegisterDictionary(dict.NameSpace, dict);
        }
        public void RegisterDictionary(string named, ISettingsDictionary dict)
        {
            named = named.ToLower().Trim().Replace("  ", " ");
            string key = named.Replace(" ", "_");
            RegisterDictionary(named, dict, true);
        }

        public void RegisterDictionary(string key, ISettingsDictionary dict, bool always)
        {
            Action needsExit = LockInfo.MonitorTryEnter("RegisterDictionary " + key, AllDictionaries, MaxWaitTryEnter);
            try
            {
                var path = key.Split(new[] { '.' });
                if (always || !AllDictionaries.ContainsKey(key)) AllDictionaries[key] = dict;
                if (path.Length > 1)
                {
                    if (path[0] == "bot" || path[0] == "users" || path[0] == "char" || path[0] == "nl")
                    {
                        string join = string.Join(".", path, 1, path.Length - 1);
                        RegisterDictionary(join, dict, false);
                    }
                }
            }
            finally
            {
                needsExit();
            }
        }

        private void RegisterSubstitutions(string named, ISettingsDictionary dict)
        {
            dict.IsTraced = false;
            RegisterDictionary("substitutions" + "." + named, dict);
        }

        protected IEnumerable GetSearchRoots(Request request)
        {
            lock (RuntimeDirectoriesLock)
            {
                var searchWas = RuntimeDirectories;

                PushSearchPath(PathToUserDir);
                PushSearchPath(PathToConfigFiles);
                PushSearchPath(RuntimeDirectory);
                PushSearchPath(PathToAIML);
                PushSearchPath(_PathToBotPersonalFiles);
                PushSearchPath(GetUserDir(request.Requester.UserID));

                var searchAt = RuntimeDirectories;
                _RuntimeDirectories = searchWas;
                return searchAt;
            }
        }


        public IEnumerable<XmlNodeEval> GetEvaluators(XmlNode node)
        {
            var nodes = new List<XmlNodeEval>();
            foreach (XmlNodeEvaluator xmlNodeEvaluator in XmlNodeEvaluators)
            {
                var nodeE = xmlNodeEvaluator.GetEvaluators(node);
                nodes.AddRange(nodeE);
            }
            return nodes;
        }

        #region IChatterBot Members

        public SystemExecHandler ChatWithHandler(string userName)
        {
            User theUser = FindOrCreateUser(userName);
            return (txt, req) =>
                       {
                           req.SetSpeakerAndResponder(theUser, BotAsUser);
                           var ret = ChatWithThisBot(txt, req);
                           return ret;
                       };
        }
        #endregion

        public long RunLowMemHooks()
        {
            long total = Unifiable.LowMemExpireUnifiableCaches();
            foreach (GraphMaster graph in SetOfGraphs)
            {
                total += graph.RunLowMemHooks();
            }
            return total;
        }
    }
}