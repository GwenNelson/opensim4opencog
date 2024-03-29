using System;
using System.Collections.Generic;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.Packets;
using MushDLR223.ScriptEngines;

namespace Cogbot.Actions.System
{
    public class DilationCommand : Command, RegionMasterCommand, AsynchronousCommand
    {
        public DilationCommand(BotClient testClient)
        {
            Name = "dilation";
        }

        public override void MakeInfo()
        {
            Description = "Shows time dilation for current sim.";
            Category = CommandCategory.Simulator;
            Parameters = CreateParams();
        }

        public override CmdResult ExecuteRequest(CmdRequest args)
        {
            int argsUsed;
            Simulator CurSim = TryGetSim(args, out argsUsed) ?? Client.Network.CurrentSim;
            return Success("Dilation is " + CurSim.Stats.Dilation.ToString());
        }
    }
}