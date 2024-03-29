#if MONO
#else
//#define MICROSOFT
#endif
using System;
using System.Windows.Forms;
using Cogbot;
using java.net;
using MushDLR223.ScriptEngines;
using MushDLR223.Utilities;

namespace CycWorldModule.DotCYC
{
    using org.opencyc.api;
    using System.Reflection;
    public partial class CycConnectionForm //: Form
    {
        //  WinformREPL.REPLForm replForm = null;
        static private CycAccess m_cycAccess = null;
        public CycAccess cycAccess
        {
            get
            {
                return getCycAccess();
            }
        }

        public bool IsDisposed
        {
            get { return false; }
        }

        public bool wasConnected = false;
        public CycConnectionForm()
        {
#if GUI
            InitializeComponent();
#endif
#if MICROSOFT
            // add this line to the form's constructor after InitializeComponent() 
          hMenu = GetSystemMenu(this.Handle, false);
#endif
            // replForm = new WinformREPL.REPLForm();
            // replForm.Show(); 
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
#if GUI
    try { btnConnect.Enabled = false; }
            catch (Exception) { }
#endif
            if (!IsConnected())
                connect();
            else
                disconnect();
#if GUI
            try { btnConnect.Enabled = true; }
            catch (Exception) { }
#endif
        }

        private bool IsConnected()
        {
            if (m_cycAccess != null) wasConnected = !m_cycAccess.isClosed();
            else wasConnected = false;
#if GUI
            if (wasConnected) btnConnect.Text = "Disconnect";
            else btnConnect.Text = "Connect";
#endif
            return wasConnected;
        }
#if GUI
        private void btnEval_Click(object sender, EventArgs e)
        {
            try
            {
                txtCycOutput.Text = "" + cycAccess.converseObject(txtEvalString.Text);
            }
            catch (Exception ee)
            {
                txtCycOutput.Text = ee.ToString();
            }
        }
#endif

        public CycAccess getCycAccess()
        {
            if (!IsConnected()) connect();
            return m_cycAccess;
        }
        object cycConnectLock = new object();
        private void connect()
        {
            lock (cycConnectLock)
            {
                connect0();
            }
        }
        private void connect0()
        {
            if (IsConnected()) return;
            try
            {
                m_cycAccess = new CycAccess(cycServerAddress, Int16.Parse(cycBasePort));
                m_cycAccess.getCycConnection().traceOn();
                m_cycAccess.find("isa");
                //   m_cycAccess.getCycConnection().converse("()");
                m_cycAccess.getCycConnection().traceOff();
                //  CycAccess.genls.ToString();
            }
            catch (Exception ee)
            {
                DLRConsole.DebugWriteLine("" + ee);
                SimCyclifier.Trace();
#if GUI
                txtCycOutput.Text = ee.ToString();
#endif
            }
            if (m_cycAccess != null)
            {
                ClientManager.addSetting("cycHostName", m_cycAccess.getHostName());
                ClientManager.addSetting("cycBasePort", "" + m_cycAccess.getBasePort());
            }
            wasConnected = IsConnected();
        }

        [ConfigSetting(Description="Base port for the cyc server")]
        public static string cycBasePort = "3600";
        [ConfigSetting(Description = "IP address for the cyc server. The default is a public opencyc instance maintained by a Cogbot contributor which you are free to use")]
        public static string cycServerAddress = "opencyc.kqml.org";

        private void disconnect()
        {
            if (m_cycAccess != null)
            {
                m_cycAccess.getCycConnection().close();
                m_cycAccess = null;
            }
            wasConnected = IsConnected();
        }

        private void CycConnectionForm_Load(object sender, EventArgs e)
        {
            wasConnected = IsConnected();
        }

#if MICROSOFT
        private const uint SC_CLOSE = 0xf060;
        private const uint MF_GRAYED = 0x01;
        private IntPtr hMenu;

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll")]
        private static extern int EnableMenuItem(IntPtr hMenu, uint wIDEnableItem, uint wEnable);

#endif
        private void CycConnectionForm_NoClose(object sender, EventArgs e)
        {
#if MICROSOFT
            EnableMenuItem(hMenu, SC_CLOSE, MF_GRAYED);
#endif
        }

        public void Reactivate()
        {
#if GUI
            this.Show();
            //if (this.WindowState == FormWindowState.Minimized)              
            this.WindowState = FormWindowState.Normal;
            this.Visible = true;
            this.Activate();
#endif
        }

        private void txtCycOutput_TextChanged(object sender, EventArgs e)
        {

        }

        private String objToStrimg(object o)
        {
            Type t = o.GetType();
            if (t.IsEnum)
            {
                //System.Reflection.MemberInfo[] mi = t.GetMembers();
                return Enum.GetName(t, o);
            }
            if (t.IsValueType)
            {
                return o.ToString();
                //
                //return ValueType GetName(t, o);
            }
            System.Reflection.MemberInfo[] mi = t.GetMembers();
            for (int i = 0; i < mi.Length; i++)
            {
                System.Reflection.MemberInfo m = mi[i];
                if (m.MemberType == MemberTypes.Field)
                {
                    t.GetField(m.Name).GetValue(o);

                }
            }
            return o.ToString();
        }
    }
}


