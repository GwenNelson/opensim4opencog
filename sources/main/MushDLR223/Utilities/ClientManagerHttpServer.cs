#define USE_HTTPSERVER_DLL
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web;
using MushDLR223.ScriptEngines;


#if USE_HTTPSERVER_DLL
using HttpServer;
using HttpServer.FormDecoders;
#else
using IHttpResponse = System.Web.HttpResponse;
using IHttpRequest = System.Web.HttpRequest;
using IHttpClientContext = System.Web.HttpContext;
#endif

#if (COGBOT_LIBOMV || USE_STHREADS || true)
using ThreadPoolUtil;
using Thread = ThreadPoolUtil.Thread;
using ThreadPool = ThreadPoolUtil.ThreadPool;
using Monitor = ThreadPoolUtil.Monitor;
#endif

namespace MushDLR223.Utilities
{
    //.. tonight i am writing them a webserver in .net 
    internal class WriteLineToResponse
    {
        internal IHttpResponse response;
        private JobGiver Server;
        internal WriteLineToResponse(JobGiver server, IHttpResponse r)
        {
            response = r;
            Server = server;
        }
        internal void WriteLine(string str, params object[] args)
        {
            try
            {

                string s = DLRConsole.SafeFormat(str, args);
#if USE_HTTPSERVER_DLL
                if (response != null)
                {
                    response.AddToBody(s + Environment.NewLine);
                }
                else
                {
                    Server.LogInfo("no respnse object for " + s);
                }
#endif
            }
            catch (Exception e)
            {
                DLRConsole.DebugWriteLine("" + e);
                Server.LogInfo("WriteLine exception" + e);
            }
        }
    }

    public class ClientManagerHttpServer : JobGiver, IDisposable
    {
        private ScriptExecutorGetter getter;
        HttpServer.HttpListener _listener;
        public static bool ShowExecTime = true;
        private int _port;
        private ScriptExecutorGetter clientManager;
        private string defaultUser = "null";

        public ClientManagerHttpServer(ScriptExecutorGetter bc, int port)
        {
            clientManager = bc;
            _port = port;
            Init();
        }
        internal void Init()
        {            
#if USE_HTTPSERVER_DLL
            _listener = HttpServer.HttpListener.Create(new CHLogger(this), IPAddress.Any, _port);
            _listener.Accepted += _listener_Accepted;
            _listener.Set404Handler(_listener_404);
#endif
            HttpServerUtil.workArroundReuse(_port);
            try
            {
                _listener.Start(10);
                LogInfo("Ready for HTTPD port " + _port);
                new SystemHttpServer(clientManager, _port + 10, "first_robot_name");
                WriteLine("Ready for HTTPD port " + _port);
            }
            catch (Exception e)
            {
                WriteLine("NOT OK for HTTPD port " + _port + "\n" + e);
            }
        }

        public void WriteLine(string s)
        {
            clientManager.WriteLine(s);
        }

        readonly internal static object HttpLock = new object();
        internal readonly List<HttpJob> HttpJobs = new List<HttpJob>(100);
        internal TimeSpan waitForEachTime = TimeSpan.FromSeconds(5);

        internal void _listener_404(IHttpClientContext context, IHttpRequest request, IHttpResponse response)
        {
            HttpJob httpJob = new HttpJob(this, context, request, response);

            lock (HttpJobs)
            {
                HttpJobs.Add(httpJob);
            }
            // 5 second wait
            if (Monitor.TryEnter(HttpLock, waitForEachTime))
            {
                DoJob(httpJob);
                Monitor.Exit(HttpLock);
            }
            else
            {
                LogInfo("ERROR Waiting for prevoius request more than " +
                        TaskQueueHandler.GetTimeString(waitForEachTime));
                httpJob.OutOfLock = true;
                DoJob(httpJob);
            } 
        }
        internal void DoJob(HttpJob httpJob)
        {
            try
            {
                httpJob.DoWork();
            }
            catch (Exception exception)
            {
                httpJob.Error = exception;
                LogInfo("Listener exception: " + exception);
            }
            finally
            {
                httpJob.EndTimeOrDateTimeMax = DateTime.Now;
                //httpJob.EndTimeOrDateTimeMax = DateTime.Now;
                lock (HttpJobs)
                {
                    HttpJobs.Remove(httpJob);
                }
            }
        }

        public void JobFinished(HttpJob httpJob)
        {
            lock (HttpJobs)
            {
                HttpJobs.Remove(httpJob);
            }
        }

#if USE_HTTPSERVER_DLL
        public void BlockingHandler(IHttpClientContext context0, IHttpRequest request, IHttpResponse response)
        {
            //  UUID capsID;
            bool success;

            string path = request.Uri.PathAndQuery;//.TrimEnd('/');
            if (path.Contains("5580"))
            {
                path = path.Replace("?:5580/", "");
            }
            string pathd = HttpUtility.UrlDecode(path);//.TrimEnd('/');
            LogInfo("_listener " + path + " from " + request.RemoteEndPoint);
            if (request.UriPath.EndsWith(".ico"))
            {
                response.Status = HttpStatusCode.NotFound;
                response.Send();
            }
            var wrresp = new WriteLineToResponse(this, response);

            string botname = GetVariable(request, "bot", GetVariable(request, "botid", null));

            ScriptExecutor _botClient = clientManager.GetScriptExecuter(botname);
            if (_botClient == null)
            {
                response.Status = HttpStatusCode.ServiceUnavailable;
                response.Send();
                return;
            }

            // Micro-posterboard
            if (pathd.StartsWith("/posterboard"))
            {
                string slot = path;
                string value = "";
                value = _botClient.getPosterBoard(slot) as string;
                if (value != null)
                    if (value.Length > 0) { LogInfo(String.Format(" board response: {0} = {1}", slot, value)); }
                AddToBody(response, "<xml>");
                AddToBody(response, "<slot>");
                AddToBody(response, "<path>" + path + "</path>");
                AddToBody(response, "<value>" + (value ?? "") + "</value>");
                AddToBody(response, "</slot>");
                AddToBody(response, "</xml>");

                wrresp.response = null;
                response.Status = HttpStatusCode.OK;
                response.Send();
                return;
            }

            bool useHtml = false;
            if (request.Method == "POST")
            {
                var fdp = new FormDecoderProvider();
                fdp.Add(new MultipartDecoder());
                fdp.Add(new UrlDecoder());
                request.DecodeBody(fdp);
            }

            if (path.StartsWith("/?") || path.StartsWith("/test"))
            {
                useHtml = true;
            }
            if (DLRConsole.IsDougsMachine)
            {
                useHtml = true;
            }
            try
            {
                if (OverrideHandlers(request, response)) return;

                if (useHtml)
                {
                    AddToBody(response, "<html>");
                    AddToBody(response, "<head>");
                    botname = GetVariable(request, "bot", _botClient.GetName());

                    AddToBody(response, "<title>" + botname + "</title>");
                    AddToBody(response, "</head>");
                    AddToBody(response, "<body>");
                    AddToBody(response, "<pre>");
                    foreach (var p in request.Param.AsEnumerable())
                    {
                        foreach (var item in p.Values)
                        {
                            AddToBody(response, "" + p.Name + " = " + item);
                        }
                    }
                    AddToBody(response, "</pre>");
                    AddToBody(response, "<a href='" + request.Uri.PathAndQuery + "'>"
                                        + request.Uri.PathAndQuery + "</a>");
                    AddToBody(response, "<pre>");
                }


                string cmd = GetVariable(request, "cmd", "MeNe");

                CmdResult res;
                // this is our default handler
                if (cmd != "MeNe")
                {
                    res = _botClient.ExecuteXmlCommand(cmd + " " + GetVariable(request, "args", ""), request,
                                                       wrresp.WriteLine);

                }
                else
                {
                    try
                    {
                        InvokeAsMene(request, response, _botClient, pathd, wrresp);
                    }
                    catch (Exception exception)
                    {
                        LogInfo("InvokeAsMene exception: " + exception);
                    }
                }
                if (useHtml)
                {
                    AddToBody(response, "</pre>");
                    AddToBody(response, "</body>");
                    AddToBody(response, "</html>");
                }
            }
            finally
            {
                wrresp.response = null;
                response.Status = HttpStatusCode.OK;
                try
                {
                    response.Send();
                }
                catch (Exception e)
                {
                    LogInfo("Exception sening respose: " + e);
                }
            }
        }

        public static List<OverrideHandlerDelegate> OverrideHandlerList = new List<OverrideHandlerDelegate>();
        public delegate bool OverrideHandlerDelegate(IHttpRequest request, IHttpResponse response);
        public bool OverrideHandlers(IHttpRequest request, IHttpResponse response)
        {
            foreach (OverrideHandlerDelegate hand in OverrideHandlerList)
            {
                if (hand(request, response)) return true;
            }
            return false;
        }

        private void InvokeAsMene(IHttpRequest request, IHttpResponse response, ScriptExecutor _botClient, string pathd, WriteLineToResponse wrresp)
        {
            {
                {
                    CmdResult res;
                    AddToBody(response, "<xml>");
                    AddToBody(response, "\n<!-- Begin Response !-->");
                    // this is our MeNe handler
                    string username = GetVariable(request, "username", GetVariable(request, "ident", null));
                    string saytext = GetVariable(request, "saytext", "missed the post");
                    string text = GetVariable(request, "text", GetVariable(request, "entry", pathd.TrimStart('/')));
                    if (text.Contains("<sapi>"))
                    {
                        // example fragment
                        // <sapi> <silence msec="100" /> <bookmark mark="anim:hello.csv"/> Hi there </sapi>
                        text = text.Replace("<sapi>", " ");
                        text = text.Replace("</sapi>", " ").Trim();
                        while (text.Contains("<"))
                        {
                            int p1 = text.IndexOf("<");
                            int p2 = text.IndexOf(">", p1);
                            if (p2 > p1)
                            {
                                string fragment = text.Substring(p1, (p2 + 1) - p1);
                                text = text.Replace(fragment, " ");
                            }
                        }

                    }

                    if (String.IsNullOrEmpty(username))
                    {
                        //res = _botClient.ExecuteCommand(cmd + " " + text, wrresp.WriteLine);
                        res = _botClient.ExecuteCommand("aiml @withuser " + defaultUser + " - " + text, request,
                                                        wrresp.WriteLine, CMDFLAGS.Foregrounded);
                    }
                    else
                    {
                        res = _botClient.ExecuteCommand("aiml @withuser " + username + " - " + text, request,
                                                        wrresp.WriteLine, CMDFLAGS.Foregrounded);
                    }
                    AddToBody(response, "");
                    AddToBody(response, "\n<!-- End Response !-->");
                    AddToBody(response, "</xml>");
                }
            }
        }
#endif

        public void LogInfo(string s)
        {
            // Console.WriteLine("[HTTP SERVER] " + s);
        }

        static internal void AddToBody(IHttpResponse response, string text)
        {
#if USE_HTTPSERVER_DLL 
            response.AddToBody(text + Environment.NewLine);            
#endif
        }
        static internal string GetVariable(IHttpRequest request, string varName, string defaultValue)
        {
            var ret = GetVariable0(request, varName, defaultValue);
            if (ret==null) return ret;
            if (ret.Contains("5580"))
            {
                ret = ret.Replace("?:5580/", "");
            }
            return ret;
        }

        static internal string GetVariable0(IHttpRequest request, string varName, string defaultValue)
        {
#if USE_HTTPSERVER_DLL
            if (request.Param.Contains(varName))
            {
                var single = request.Param[varName].Value;
                if (!String.IsNullOrEmpty(single)) return single;
                var values = request.Param[varName].Values;
                if (values.Count > 0) return values[0];
            }
            if (request.QueryString.Contains(varName))
            {
                return HttpUtility.UrlDecode(request.QueryString[varName].Value);
            }
#endif
            return HttpUtility.UrlDecode(defaultValue);
        }

#if USE_HTTPSERVER_DLL
        private void _listener_Accepted(object sender, ClientAcceptedEventArgs e)
        {
            LogInfo("_listener_Accepted " + e.Socket);
        }

        //#region Implementation of ILogWriter

        internal void Write(object source, LogPrio priority, string message)
        {
            WriteLine(priority + " " + message);
        }
#endif
       // #endregion
        public void Dispose()
        {
            
        }
    }
    #if USE_HTTPSERVER_DLL 
    internal class CHLogger : ILogWriter
    {
        private ClientManagerHttpServer Logger;
        internal CHLogger(ClientManagerHttpServer hd)
        {
            Logger = hd;
        }
        public void Write(object source, LogPrio priority, string message)
        {
            Logger.Write(source, priority, message);
        }
    }
     #endif
    public class HttpJob
    {
        private static long serialNum = 0;
        internal long Serial = ++serialNum;
        internal JobGiver Server;
        private string Name;
        internal IHttpClientContext Context;
        internal IHttpRequest Request;
        internal IHttpResponse Response;
        internal Thread Thread;
        internal DateTime StarTime =  DateTime.Now;
        internal DateTime EndTimeOrDateTimeMax = DateTime.MaxValue;
        internal bool OutOfLock;
        internal bool ActuallyStarted = false;
        internal Exception Error;
        internal static bool DoWorkInThread = false;
        internal Thread WorkerThread;

        internal TimeSpan Runtime
        {
            get { return EndTimeOrDateTimeMax - StarTime; }
        }

        internal void KillIt()
        {
            Thread w = WorkerThread;
            if (w != null && w.IsAlive)
            {
                w.Abort();
            }
        }

        internal void DoWork()
        {
            WorkerThread = new Thread(DoWorkNow, 0)
                               {
                                   Name = "Worker for " + GetName(),
                               };
            WorkerThread.Start();
            if (!DoWorkInThread) WorkerThread.Join();
        }

        internal string GetName()
        {
            return Name;
        }

        internal void DoWorkNow()
        {
            try
            {
                ActuallyStarted = true;      
#if USE_HTTPSERVER_DLL
                Server.BlockingHandler(Context, Request, Response);
#endif
            }
            catch (ThreadAbortException e)
            {
                WriteLine("ABORT: " + e);
                if (WorkerThread == Thread.CurrentThread)
                {
                    Thread.ResetAbort();
                }
                else
                {
                    WriteLine("WRONG THREAD: " + WorkerThread);
                    throw;
                }
            }
            catch (Exception e)
            {
                WriteLine("ERROR: " + e);
                Error = e;
            }
            finally
            {
                EndTimeOrDateTimeMax = DateTime.Now;
                TimeSpan usedTime = EndTimeOrDateTimeMax - StarTime;
                WriteLine("Finished in " + usedTime);
                Server.JobFinished(this);
            }
        }

        internal void WriteLine(string e)
        {
            Server.WriteLine("JOB-" + e + " named " + Name);
        }

        internal HttpJob(JobGiver server, IHttpClientContext context, IHttpRequest request, IHttpResponse response)
        {
            this.Server = server;
            this.Context = context;
            this.Request = request;
            this.Response = response;
            Thread = Thread.CurrentThread;            
#if USE_HTTPSERVER_DLL
            Name = request.UriPath;
#endif
        }
    }

    internal interface JobGiver
    {
        void WriteLine(string s);
        void JobFinished(HttpJob httpJob);     
#if USE_HTTPSERVER_DLL
        void BlockingHandler(IHttpClientContext context, IHttpRequest request, IHttpResponse response);
#else
        #endif
        void LogInfo(string p0);
    }

    public interface ScriptExecutorGetter
    {
        ScriptExecutor GetScriptExecuter(object o);
        void WriteLine(string s, params object[] args);
    }

    public interface ScriptExecutor
    {
        CmdResult ExecuteCommand(string s, object session, OutputDelegate outputDelegate, CMDFLAGS needResult);
        CmdResult ExecuteXmlCommand(string s, object session, OutputDelegate outputDelegate);
        string GetName();
        object getPosterBoard(object slot);
    }
    public static class HttpServerUtil
    {
        public static IDisposable CreateHttpServer(ScriptExecutorGetter clientManager, int port, string robotName)
        {
#if USE_HTTPSERVER_DLL
            return new ClientManagerHttpServer(clientManager, port);
#else
            return new SystemHttpServer(clientManager, port, robotName);
#endif
        }

        static public void workArroundReuse(int port)
        {
            return;
            try
            {
                TcpClient client = new TcpClient();
                client.Connect("localhost", port);            
                client.Close();
            }
            catch (Exception exception)
            {
               // DLRConsole.DebugWriteLine("Listener workarround: " + exception.Message);
            }
        }
    }
}


