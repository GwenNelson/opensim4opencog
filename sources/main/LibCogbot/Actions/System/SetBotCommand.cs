using System;
using System.Collections.Generic;
using System.Text;
using OpenMetaverse;

using MushDLR223.ScriptEngines;

namespace cogbot.Actions.System
{

        public class SetBotCommand : Command, SystemApplicationCommand
        {
            public SetBotCommand(BotClient testClient)
            {
                Name = "setbot";
                Description = "Sets the current bot for subsequent botcmd commands.";
                Usage =
                    Htmlize.Usage("setmasterkey <name>", "Sets the bot by user name") +
                    Htmlize.Example(@"
... log on two bots, Ima Bot and Another Bot
/setbot Ima Bot
/say hi, I am Ima Bot
... Ima Bot says hi, I am Ima Bot in chat
/setbot Another Bot
/say hi, I'm not Ima
... Another Bot says I'm not Ima  in chat
", "first one bot, then a different one, chats");
                Parameters = NamedParam.CreateParams("bot", typeof(AgentSpec), "name of bot");
                Category = CommandCategory.BotClient;
            }

            public override CmdResult Execute(string[] args, UUID fromAgentID, OutputDelegate WriteLine)
            {
                string botname = String.Join(" ",args).Trim();
                TheBotClient.ClientManager.SetOnlyOneCurrentBotClient(botname);
                // This is a dummy command. Calls to it should be intercepted and handled specially
                return Success("SetOnlyOneCurrentBotClient=" + TheBotClient.ClientManager.OnlyOneCurrentBotClient);
            }
        }
    
}
