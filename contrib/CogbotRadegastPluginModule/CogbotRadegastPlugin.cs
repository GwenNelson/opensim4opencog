using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
using cogbot;
using OpenMetaverse;
using Radegast;
//using RadegastTab = Radegast.SleekTab;

namespace CogbotRadegastPluginModule
{
    public class CogbotRadegastPlugin : IRadegastPlugin
    {
        public CogbotRadegastPlugin()
        {
        }

        public RadegastInstance RadegastInstance;
        public CogbotContextMenuListener CogbotContextMenuListener;
        private CogbotTabWindow chatConsole;
        private SimObjectsConsole _simObjectsConsole;
        private RadegastTab tab;
        private ClientManager clientManager;
        private CogbotRadegastInterpreter cogbotRadegastInterpreter;
        private CogbotNotificationListener CogbotNoticeuListener;
        private CommandContextAction _commandContextAction;
        private AspectContextAction _aspectContextAction;

        public void StartPlugin(RadegastInstance inst)
        {
            RadegastInstance = inst;
            try
            {
                // inst.MainForm.Invoke(new MethodInvoker(() => StartPlugin0(inst)));               
                StartPlugin0(RadegastInstance);
            }
            catch (Exception ex)
            {
                Logger.Log("[COGBOT PLUGIN] exception " + ex, Helpers.LogLevel.Error, ex);
            }
        }

        public void StartPlugin0(RadegastInstance inst)
        {
            RadegastInstance = inst;
            CogbotContextMenuListener = new CogbotContextMenuListener();
            CogbotNoticeuListener = new CogbotNotificationListener();
            if (ClientManager.UsingRadgastFromCogbot)
            {
                // just unregister events for now
                inst.Netcom.Dispose();
                clientManager = ClientManager.SingleInstance;
            }
            else
            {
                ClientManager.UsingCogbotFromRadgast = true;
                clientManager = new ClientManager();
            }
            cogbotRadegastInterpreter = new CogbotRadegastInterpreter(clientManager);
            RadegastInstance.CommandsManager.LoadInterpreter(cogbotRadegastInterpreter);
            _commandContextAction = new CommandContextAction(inst);
            inst.TabConsole.RegisterContextAction(_commandContextAction);
            _aspectContextAction = new AspectContextAction(inst);
            inst.TabConsole.RegisterContextAction(_aspectContextAction);

            if (ClientManager.UsingRadgastFromCogbot) return;
            inst.Client.Settings.MULTIPLE_SIMS = true;
            clientManager.outputDelegate = WriteLine;
            clientManager.StartUpLisp();
            chatConsole = new CogbotTabWindow(inst, clientManager)
                              {
                                  Dock = DockStyle.Fill,
                                  Visible = false
                              };
            tab = inst.TabConsole.AddTab("cogbot", "Cogbot", chatConsole);
            tab.AllowClose = false;
            tab.AllowDetach = true;

            _simObjectsConsole = new SimObjectsConsole(inst)
            {
                Dock = DockStyle.Fill,
               // Visible = false
            };
            tab = inst.TabConsole.AddTab("simobjects", "SimObjects", _simObjectsConsole);
            tab.AllowClose = false;
            tab.AllowDetach = true;

            RadegastTab tab1 = RadegastInstance.TabConsole.GetTab("chat");
            tab1.AllowDetach = true;
            RadegastTab tab2 = RadegastInstance.TabConsole.GetTab("login");
            tab2.AllowDetach = true;
        }

        private void WriteLine(string str, object[] args)
        {
            if (args == null || args.Length == 0)
            {
                args = new object[] { str };
                str = "{0}";
            }
            if (chatConsole == null)
            {
                Console.WriteLine(str,args);
            }
            else
            {
                chatConsole.WriteLine(str, args);                
            }
        }

        public void StopPlugin(RadegastInstance inst)
        {
        }

    }
}